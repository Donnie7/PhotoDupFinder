namespace PhotoDupFinder.Core.Models;

public sealed record ScanProgressUpdate(
  ScanStage Stage,
  int Processed,
  int Total,
  string Message);
