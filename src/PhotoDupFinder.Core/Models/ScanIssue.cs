namespace PhotoDupFinder.Core.Models;

public sealed record ScanIssue(
  string Path,
  string Stage,
  string Message);
