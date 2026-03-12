using PhotoDupFinder.Core.Models;
using PhotoDupFinder.Core.Services;

namespace PhotoDupFinder.Core.Tests;

public sealed class CsvReportWriterTests
{
  [Fact]
  public async Task WriteAsync_WritesHeaderAndEscapedRows()
  {
    var tempPath = Path.Combine(Path.GetTempPath(), $"photodupfinder-csv-{Guid.NewGuid():N}.csv");
    var writer = new CsvReportWriter();

    var report = new ScanReport(
      RootPath: @"D:\Photos",
      StartedAtUtc: new DateTimeOffset(2026, 3, 12, 16, 0, 0, TimeSpan.Zero),
      Duration: TimeSpan.FromMinutes(2),
      FilesDiscovered: 1,
      VerifiedFiles: 1,
      UnverifiedFiles: 0,
      DuplicateGroupCount: 1,
      ReclaimableBytes: 100,
      DuplicateGroups:
      [
        new DuplicateGroup(
          1,
          "ABC",
          new PhotoReportRow(
            @"D:\Photos\keep""me.jpg",
            ".jpg",
            "JPEG",
            120,
            4000,
            3000,
            null,
            PhotoVerificationStatus.Verified,
            null,
            "ABC",
            2,
            true,
            "highest resolution",
            1),
          Files:
          [
            new PhotoReportRow(
              @"D:\Photos\keep""me.jpg",
              ".jpg",
              "JPEG",
              120,
              4000,
              3000,
              null,
              PhotoVerificationStatus.Verified,
              null,
              "ABC",
              2,
              true,
              "highest resolution",
              1),
          ],
          ReclaimableBytes: 100),
      ],
      AllFiles:
      [
        new PhotoReportRow(
          @"D:\Photos\keep""me.jpg",
          ".jpg",
          "JPEG",
          120,
          4000,
          3000,
          null,
          PhotoVerificationStatus.Verified,
          null,
          "ABC",
          2,
          true,
          "highest resolution",
          1),
      ],
      Issues: []);

    await writer.WriteAsync(report, tempPath);

    var contents = await File.ReadAllTextAsync(tempPath);
    Assert.Contains("GroupId,Path,KeepSuggested", contents, StringComparison.Ordinal);
    Assert.Contains("\"D:\\Photos\\keep\"\"me.jpg\"", contents, StringComparison.Ordinal);

    File.Delete(tempPath);
  }
}
