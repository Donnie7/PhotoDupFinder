namespace PhotoDupFinder.Core.Models;

public sealed record DuplicateSelectionPlan(
  PhotoReportRow Keep,
  IReadOnlyList<PhotoReportRow> Duplicates,
  long ReclaimableBytes);
