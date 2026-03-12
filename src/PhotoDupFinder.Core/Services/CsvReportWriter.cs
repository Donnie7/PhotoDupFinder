using System.Globalization;
using System.Text;
using PhotoDupFinder.Core.Models;

namespace PhotoDupFinder.Core.Services;

public sealed class CsvReportWriter
{
  public async Task WriteAsync(ScanReport report, string path, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(report);
    ArgumentException.ThrowIfNullOrWhiteSpace(path);

    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrWhiteSpace(directory))
    {
      Directory.CreateDirectory(directory);
    }

    await using var stream = File.Create(path);
    await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    await writer.WriteLineAsync(
        "GroupId,Path,KeepSuggested,KeepReason,Format,Width,Height,FileSizeBytes,CaptureDate,Status,MetadataScore,Fingerprint")
      .ConfigureAwait(false);

    foreach (var file in report.AllFiles.OrderBy(row => row.DuplicateGroupId).ThenBy(row => row.Path, StringComparer.OrdinalIgnoreCase))
    {
      var values = new[]
      {
        file.DuplicateGroupId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        file.Path,
        file.SuggestedKeep ? "Yes" : "No",
        file.KeepReason ?? string.Empty,
        file.Format,
        file.Width.ToString(CultureInfo.InvariantCulture),
        file.Height.ToString(CultureInfo.InvariantCulture),
        file.FileSizeBytes.ToString(CultureInfo.InvariantCulture),
        file.CaptureDate?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
        file.Status.ToString(),
        file.MetadataScore.ToString(CultureInfo.InvariantCulture),
        file.Fingerprint ?? string.Empty,
      };

      await writer.WriteLineAsync(string.Join(",", values.Select(Escape))).ConfigureAwait(false);
    }
  }

  private static string Escape(string value)
  {
    var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
    return $"\"{escaped}\"";
  }
}
