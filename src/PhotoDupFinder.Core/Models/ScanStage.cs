namespace PhotoDupFinder.Core.Models;

public enum ScanStage
{
  DiscoveringFiles,
  ReadingMetadata,
  Fingerprinting,
  Grouping,
  Completed,
}
