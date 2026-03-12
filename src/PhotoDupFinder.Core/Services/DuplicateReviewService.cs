using PhotoDupFinder.Core.Models;

namespace PhotoDupFinder.Core.Services;

public static class DuplicateReviewService
{
  public static DuplicateSelectionPlan CreateSuggestedPlan(DuplicateGroup group)
  {
    ArgumentNullException.ThrowIfNull(group);
    return CreatePlan(group, group.SuggestedKeep.Path);
  }

  public static DuplicateSelectionPlan CreatePlan(DuplicateGroup group, string keepPath)
  {
    ArgumentNullException.ThrowIfNull(group);
    ArgumentException.ThrowIfNullOrWhiteSpace(keepPath);

    var keep = group.Files.FirstOrDefault(file =>
      string.Equals(file.Path, keepPath, StringComparison.OrdinalIgnoreCase));

    if (keep is null)
    {
      throw new ArgumentException("The selected keep path does not exist in this duplicate group.", nameof(keepPath));
    }

    var duplicates = group.Files
      .Where(file => !string.Equals(file.Path, keep.Path, StringComparison.OrdinalIgnoreCase))
      .ToArray();

    return new DuplicateSelectionPlan(
      keep,
      duplicates,
      duplicates.Sum(file => file.FileSizeBytes));
  }

  public static ScanReport ApplyDeletedPaths(ScanReport report, IEnumerable<string> deletedPaths)
  {
    ArgumentNullException.ThrowIfNull(report);
    ArgumentNullException.ThrowIfNull(deletedPaths);

    var deletedPathSet = deletedPaths
      .Where(path => !string.IsNullOrWhiteSpace(path))
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    if (deletedPathSet.Count == 0)
    {
      return report;
    }

    var refreshedGroups = report.DuplicateGroups
      .Select(group => RefreshGroup(group, deletedPathSet))
      .Where(group => group is not null)
      .Cast<DuplicateGroup>()
      .OrderBy(group => group.GroupId)
      .ToArray();

    var unresolvedGroupFiles = refreshedGroups
      .SelectMany(group => group.Files)
      .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase);

    var remainingUnverifiedFiles = report.AllFiles
      .Where(file =>
        file.Status == PhotoVerificationStatus.Unverified &&
        !deletedPathSet.Contains(file.Path))
      .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase);

    var allFiles = unresolvedGroupFiles
      .Concat(remainingUnverifiedFiles)
      .DistinctBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
      .ToArray();

    var removedFiles = report.AllFiles
      .Where(file => deletedPathSet.Contains(file.Path))
      .ToArray();

    var removedVerifiedCount = removedFiles.Count(file => file.Status == PhotoVerificationStatus.Verified);
    var removedUnverifiedCount = removedFiles.Length - removedVerifiedCount;

    return report with
    {
      FilesDiscovered = Math.Max(0, report.FilesDiscovered - removedFiles.Length),
      VerifiedFiles = Math.Max(0, report.VerifiedFiles - removedVerifiedCount),
      UnverifiedFiles = Math.Max(0, report.UnverifiedFiles - removedUnverifiedCount),
      DuplicateGroupCount = refreshedGroups.Length,
      ReclaimableBytes = refreshedGroups.Sum(group => group.ReclaimableBytes),
      DuplicateGroups = refreshedGroups,
      AllFiles = allFiles,
    };
  }

  private static DuplicateGroup? RefreshGroup(DuplicateGroup group, ISet<string> deletedPaths)
  {
    var remainingFiles = group.Files
      .Where(file => !deletedPaths.Contains(file.Path))
      .ToArray();

    if (remainingFiles.Length < 2)
    {
      return null;
    }

    var suggestedKeep = SelectSuggestedKeep(remainingFiles);
    var keepReason = BuildKeepReason(suggestedKeep, remainingFiles);
    var updatedFiles = remainingFiles
      .Select(file => file with
      {
        SuggestedKeep = string.Equals(file.Path, suggestedKeep.Path, StringComparison.OrdinalIgnoreCase),
        KeepReason = string.Equals(file.Path, suggestedKeep.Path, StringComparison.OrdinalIgnoreCase) ?
          keepReason :
          "Duplicate candidate",
        DuplicateGroupId = group.GroupId,
      })
      .OrderByDescending(file => (long)file.Width * file.Height)
      .ThenByDescending(file => GetFormatRank(file.Format))
      .ThenByDescending(file => file.MetadataScore)
      .ThenByDescending(file => file.FileSizeBytes)
      .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
      .ToArray();

    return group with
    {
      SuggestedKeep = updatedFiles[0],
      Files = updatedFiles,
      ReclaimableBytes = updatedFiles.Sum(file => file.FileSizeBytes) - updatedFiles[0].FileSizeBytes,
    };
  }

  private static PhotoReportRow SelectSuggestedKeep(IReadOnlyList<PhotoReportRow> files) =>
    files
      .OrderByDescending(file => (long)file.Width * file.Height)
      .ThenByDescending(file => GetFormatRank(file.Format))
      .ThenByDescending(file => file.MetadataScore)
      .ThenByDescending(file => file.FileSizeBytes)
      .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
      .First();

  private static string BuildKeepReason(PhotoReportRow winner, IReadOnlyList<PhotoReportRow> group)
  {
    var reasons = new List<string>();
    if ((long)winner.Width * winner.Height == group.Max(file => (long)file.Width * file.Height))
    {
      reasons.Add("highest resolution");
    }

    if (GetFormatRank(winner.Format) == group.Max(file => GetFormatRank(file.Format)))
    {
      reasons.Add("preferred archival format");
    }

    if (winner.MetadataScore == group.Max(file => file.MetadataScore))
    {
      reasons.Add("richest metadata");
    }

    if (winner.FileSizeBytes == group.Max(file => file.FileSizeBytes))
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
}
