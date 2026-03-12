using System.IO.Compression;
using System.Text.Json;
using PhotoDupFinder.Core.Models;

namespace PhotoDupFinder.Cli.Infrastructure;

internal sealed class ScanReportStore
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = false,
  };

  public async Task SaveAsync(ScanReport report, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(report);

    AppPaths.EnsureBaseDirectory();
    var tempPath = $"{AppPaths.LastReportFilePath}.tmp";

    try
    {
      await using var outputStream = File.Create(tempPath);
      await using var gzipStream = new GZipStream(outputStream, CompressionLevel.SmallestSize);
      await JsonSerializer.SerializeAsync(gzipStream, CreateCachedReport(report), JsonOptions, cancellationToken)
        .ConfigureAwait(false);
    }
    catch
    {
      TryDelete(tempPath);
      throw;
    }

    File.Move(tempPath, AppPaths.LastReportFilePath, overwrite: true);
  }

  public async Task<ScanReport?> LoadAsync(CancellationToken cancellationToken = default)
  {
    AppPaths.EnsureBaseDirectory();
    if (File.Exists(AppPaths.LastReportFilePath))
    {
      try
      {
        await using var compressedStream = File.OpenRead(AppPaths.LastReportFilePath);
        await using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
        return await JsonSerializer.DeserializeAsync<ScanReport>(gzipStream, JsonOptions, cancellationToken)
          .ConfigureAwait(false);
      }
      catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
      {
        TryDelete(AppPaths.LastReportFilePath);
      }
    }

    if (!File.Exists(AppPaths.LegacyLastReportFilePath))
    {
      return null;
    }

    try
    {
      await using var legacyStream = File.OpenRead(AppPaths.LegacyLastReportFilePath);
      return await JsonSerializer.DeserializeAsync<ScanReport>(legacyStream, JsonOptions, cancellationToken)
        .ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is IOException or JsonException)
    {
      TryDelete(AppPaths.LegacyLastReportFilePath);
      return null;
    }
  }

  private static ScanReport CreateCachedReport(ScanReport report)
  {
    // Persist only rows that matter for review so the local cache stays bounded.
    var cachedRows = report.DuplicateGroups
      .SelectMany(group => group.Files)
      .Concat(report.AllFiles.Where(file => file.Status == PhotoVerificationStatus.Unverified))
      .DistinctBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
      .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
      .ToArray();

    return report with
    {
      AllFiles = cachedRows,
    };
  }

  private static void TryDelete(string path)
  {
    try
    {
      if (File.Exists(path))
      {
        File.Delete(path);
      }
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
  }
}
