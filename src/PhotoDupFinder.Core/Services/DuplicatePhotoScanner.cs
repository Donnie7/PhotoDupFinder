using System.Collections.Concurrent;
using PhotoDupFinder.Core.Abstractions;
using PhotoDupFinder.Core.Models;

namespace PhotoDupFinder.Core.Services;

public sealed class DuplicatePhotoScanner
{
  private readonly IPhotoMetadataReader _metadataReader;
  private readonly IPixelFingerprintService _fingerprintService;

  public DuplicatePhotoScanner(
    IPhotoMetadataReader? metadataReader = null,
    IPixelFingerprintService? fingerprintService = null)
  {
    _metadataReader = metadataReader ?? new PhotoMetadataReader();
    _fingerprintService = fingerprintService ?? new PixelFingerprintService();
  }

  public async Task<ScanReport> ScanAsync(
    ScanOptions options,
    IProgress<ScanProgressUpdate>? progress = null,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(options);

    if (!Directory.Exists(options.RootPath))
    {
      throw new DirectoryNotFoundException($"Directory '{options.RootPath}' was not found.");
    }

    var startedAt = DateTimeOffset.UtcNow;
    var supportedExtensions = SupportedPhotoFormats.CreateSet(options.Extensions);
    var searchOption = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
    var files = Directory
      .EnumerateFiles(options.RootPath, "*.*", searchOption)
      .Where(path => SupportedPhotoFormats.IsSupported(path, supportedExtensions))
      .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
      .ToArray();

    progress?.Report(new ScanProgressUpdate(
      ScanStage.DiscoveringFiles,
      files.Length,
      files.Length,
      $"Discovered {files.Length} supported photo files."));

    var issues = new ConcurrentBag<ScanIssue>();
    var candidates = await ReadMetadataAsync(files, options, issues, progress, cancellationToken)
      .ConfigureAwait(false);

    await GenerateFingerprintsAsync(candidates, options, issues, progress, cancellationToken)
      .ConfigureAwait(false);

    var report = BuildReport(options.RootPath, startedAt, candidates, issues);
    progress?.Report(new ScanProgressUpdate(
      ScanStage.Completed,
      report.FilesDiscovered,
      report.FilesDiscovered,
      $"Completed scan with {report.DuplicateGroupCount} duplicate groups."));

    return report;
  }

  private async Task<List<PhotoScanCandidate>> ReadMetadataAsync(
    IReadOnlyList<string> files,
    ScanOptions options,
    ConcurrentBag<ScanIssue> issues,
    IProgress<ScanProgressUpdate>? progress,
    CancellationToken cancellationToken)
  {
    var candidates = new ConcurrentBag<PhotoScanCandidate>();
    var processed = 0;

    await Parallel.ForEachAsync(files, CreateParallelOptions(options, cancellationToken), (path, token) =>
    {
      var fileInfo = new FileInfo(path);

      try
      {
        var metadata = _metadataReader.Read(path);
        candidates.Add(PhotoScanCandidate.CreatePending(fileInfo, metadata));
      }
      catch (Exception ex)
      {
        candidates.Add(PhotoScanCandidate.CreateUnverified(
          fileInfo,
          Path.GetExtension(path),
          statusMessage: ex.Message));

        issues.Add(new ScanIssue(path, nameof(ScanStage.ReadingMetadata), ex.Message));
      }

      var current = Interlocked.Increment(ref processed);
      progress?.Report(new ScanProgressUpdate(
        ScanStage.ReadingMetadata,
        current,
        files.Count,
        $"Read metadata for {current}/{files.Count} files."));

      return ValueTask.CompletedTask;
    }).ConfigureAwait(false);

    return candidates
      .OrderBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  private async Task GenerateFingerprintsAsync(
    IReadOnlyList<PhotoScanCandidate> candidates,
    ScanOptions options,
    ConcurrentBag<ScanIssue> issues,
    IProgress<ScanProgressUpdate>? progress,
    CancellationToken cancellationToken)
  {
    var fingerprintTargets = candidates
      .Where(candidate => candidate.Status == PhotoVerificationStatus.Verified && candidate.Metadata is not null)
      .ToArray();

    var total = fingerprintTargets.Length;
    var processed = 0;

    await Parallel.ForEachAsync(fingerprintTargets, CreateParallelOptions(options, cancellationToken), (candidate, token) =>
    {
      try
      {
        candidate.Fingerprint = _fingerprintService.Create(candidate.Path);
      }
      catch (Exception ex)
      {
        candidate.Status = PhotoVerificationStatus.Unverified;
        candidate.StatusMessage = ex.Message;
        issues.Add(new ScanIssue(candidate.Path, nameof(ScanStage.Fingerprinting), ex.Message));
      }

      var current = Interlocked.Increment(ref processed);
      progress?.Report(new ScanProgressUpdate(
        ScanStage.Fingerprinting,
        current,
        total,
        $"Fingerprinting {current}/{total} files."));

      return ValueTask.CompletedTask;
    }).ConfigureAwait(false);

    progress?.Report(new ScanProgressUpdate(
      ScanStage.Grouping,
      1,
      1,
      "Grouping verified files by normalized fingerprint."));
  }

  private static ParallelOptions CreateParallelOptions(ScanOptions options, CancellationToken cancellationToken) =>
    new()
    {
      CancellationToken = cancellationToken,
      MaxDegreeOfParallelism = options.MaxDegreeOfParallelism > 0 ?
        options.MaxDegreeOfParallelism :
        Environment.ProcessorCount,
    };

  private static ScanReport BuildReport(
    string rootPath,
    DateTimeOffset startedAt,
    IReadOnlyList<PhotoScanCandidate> candidates,
    ConcurrentBag<ScanIssue> issues)
  {
    var groupedByFingerprint = candidates
      .Where(candidate => candidate.Status == PhotoVerificationStatus.Verified && candidate.Fingerprint is not null)
      .GroupBy(candidate => candidate.Fingerprint!.Sha256, StringComparer.Ordinal)
      .Where(group => group.Count() > 1)
      .OrderByDescending(group => group.Count())
      .ThenBy(group => group.Key, StringComparer.Ordinal)
      .ToArray();

    var duplicateGroups = new List<DuplicateGroup>(groupedByFingerprint.Length);
    var groupAssignments = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var keepReasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    var groupId = 1;
    foreach (var group in groupedByFingerprint)
    {
      var ordered = group.OrderByDescending(candidate => candidate.PixelCount)
        .ThenByDescending(candidate => GetFormatRank(candidate.Metadata?.Format))
        .ThenByDescending(candidate => candidate.Metadata?.MetadataScore ?? 0)
        .ThenByDescending(candidate => candidate.FileSizeBytes)
        .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
        .ToArray();

      var suggestedKeep = ordered[0];
      var keepReason = BuildKeepReason(suggestedKeep, ordered);
      keepReasons[suggestedKeep.Path] = keepReason;

      foreach (var candidate in ordered)
      {
        groupAssignments[candidate.Path] = groupId;
      }

      var fileRows = ordered.Select(candidate => ToRow(
          candidate,
          groupId,
          string.Equals(candidate.Path, suggestedKeep.Path, StringComparison.OrdinalIgnoreCase),
          string.Equals(candidate.Path, suggestedKeep.Path, StringComparison.OrdinalIgnoreCase) ? keepReason : "Duplicate candidate"))
        .ToArray();

      var reclaimableBytes = ordered.Sum(candidate => candidate.FileSizeBytes) - suggestedKeep.FileSizeBytes;

      duplicateGroups.Add(new DuplicateGroup(
        groupId,
        group.Key,
        fileRows[0],
        fileRows,
        reclaimableBytes));

      groupId++;
    }

    var allFiles = candidates
      .OrderBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
      .Select(candidate =>
      {
        var isKeep = keepReasons.ContainsKey(candidate.Path);
        return ToRow(
          candidate,
          groupAssignments.TryGetValue(candidate.Path, out var assignedGroupId) ? assignedGroupId : null,
          isKeep,
          isKeep ? keepReasons[candidate.Path] : null);
      })
      .ToArray();

    var verifiedFiles = allFiles.Count(file => file.Status == PhotoVerificationStatus.Verified);
    var unverifiedFiles = allFiles.Length - verifiedFiles;

    return new ScanReport(
      rootPath,
      startedAt,
      DateTimeOffset.UtcNow - startedAt,
      allFiles.Length,
      verifiedFiles,
      unverifiedFiles,
      duplicateGroups.Count,
      duplicateGroups.Sum(group => group.ReclaimableBytes),
      duplicateGroups,
      allFiles,
      issues
        .OrderBy(issue => issue.Path, StringComparer.OrdinalIgnoreCase)
        .ToArray());
  }

  private static PhotoReportRow ToRow(
    PhotoScanCandidate candidate,
    int? groupId,
    bool suggestedKeep,
    string? keepReason)
  {
    return new PhotoReportRow(
      candidate.Path,
      candidate.Extension,
      candidate.Metadata?.Format ?? "UNKNOWN",
      candidate.FileSizeBytes,
      candidate.Metadata?.NormalizedWidth ?? 0,
      candidate.Metadata?.NormalizedHeight ?? 0,
      candidate.Metadata?.CaptureDate,
      candidate.Status,
      candidate.StatusMessage,
      candidate.Fingerprint?.Sha256,
      candidate.Metadata?.MetadataScore ?? 0,
      suggestedKeep,
      keepReason,
      groupId);
  }

  private static string BuildKeepReason(PhotoScanCandidate winner, IReadOnlyList<PhotoScanCandidate> group)
  {
    var reasons = new List<string>();
    if (winner.PixelCount == group.Max(item => item.PixelCount))
    {
      reasons.Add("highest resolution");
    }

    if (GetFormatRank(winner.Metadata?.Format) == group.Max(item => GetFormatRank(item.Metadata?.Format)))
    {
      reasons.Add("preferred archival format");
    }

    if ((winner.Metadata?.MetadataScore ?? 0) == group.Max(item => item.Metadata?.MetadataScore ?? 0))
    {
      reasons.Add("richest metadata");
    }

    if (winner.FileSizeBytes == group.Max(item => item.FileSizeBytes))
    {
      reasons.Add("largest source file");
    }

    return reasons.Count == 0 ?
      "Stable path tiebreaker" :
      $"Suggested keep because it has the {string.Join(", ", reasons)}.";
  }

  private static int GetFormatRank(string? format) => format?.ToUpperInvariant() switch
  {
    "DNG" => 6,
    "TIFF" => 5,
    "TIF" => 5,
    "PNG" => 4,
    "HEIC" => 3,
    "HEIF" => 3,
    "JPG" => 2,
    "JPEG" => 2,
    "WEBP" => 2,
    "BMP" => 1,
    "GIF" => 0,
    _ => 0,
  };

  private sealed class PhotoScanCandidate
  {
    public required string Path { get; init; }

    public required string Extension { get; init; }

    public required long FileSizeBytes { get; init; }

    public PhotoMetadata? Metadata { get; init; }

    public PixelFingerprint? Fingerprint { get; set; }

    public PhotoVerificationStatus Status { get; set; }

    public string? StatusMessage { get; set; }

    public long PixelCount => (long)(Metadata?.NormalizedWidth ?? 0) * (Metadata?.NormalizedHeight ?? 0);

    public static PhotoScanCandidate CreatePending(FileInfo fileInfo, PhotoMetadata metadata)
    {
      return new PhotoScanCandidate
      {
        Path = fileInfo.FullName,
        Extension = SupportedPhotoFormats.Normalize(fileInfo.Extension),
        FileSizeBytes = fileInfo.Length,
        Metadata = metadata,
        Status = PhotoVerificationStatus.Verified,
      };
    }

    public static PhotoScanCandidate CreateUnverified(FileInfo fileInfo, string extension, string statusMessage)
    {
      return new PhotoScanCandidate
      {
        Path = fileInfo.FullName,
        Extension = SupportedPhotoFormats.Normalize(extension),
        FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
        Status = PhotoVerificationStatus.Unverified,
        StatusMessage = statusMessage,
      };
    }
  }
}
