using System.Text.Json;
using PhotoDupFinder.Core.Models;

namespace PhotoDupFinder.Cli.Infrastructure;

internal sealed class ScanReportStore
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = true,
  };

  public async Task SaveAsync(ScanReport report, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(report);

    AppPaths.EnsureBaseDirectory();
    await using var stream = File.Create(AppPaths.LastReportFilePath);
    await JsonSerializer.SerializeAsync(stream, report, JsonOptions, cancellationToken)
      .ConfigureAwait(false);
  }

  public async Task<ScanReport?> LoadAsync(CancellationToken cancellationToken = default)
  {
    AppPaths.EnsureBaseDirectory();
    if (!File.Exists(AppPaths.LastReportFilePath))
    {
      return null;
    }

    await using var stream = File.OpenRead(AppPaths.LastReportFilePath);
    return await JsonSerializer.DeserializeAsync<ScanReport>(stream, JsonOptions, cancellationToken)
      .ConfigureAwait(false);
  }
}
