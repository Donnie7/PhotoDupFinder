namespace PhotoDupFinder.Core.Models;

public sealed record ScanReport(
  string RootPath,
  DateTimeOffset StartedAtUtc,
  TimeSpan Duration,
  int FilesDiscovered,
  int VerifiedFiles,
  int UnverifiedFiles,
  int DuplicateGroupCount,
  long ReclaimableBytes,
  IReadOnlyList<DuplicateGroup> DuplicateGroups,
  IReadOnlyList<PhotoReportRow> AllFiles,
  IReadOnlyList<ScanIssue> Issues);
