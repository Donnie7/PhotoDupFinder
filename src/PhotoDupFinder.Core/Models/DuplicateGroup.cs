namespace PhotoDupFinder.Core.Models;

public sealed record DuplicateGroup(
  int GroupId,
  string Fingerprint,
  PhotoReportRow SuggestedKeep,
  IReadOnlyList<PhotoReportRow> Files,
  long ReclaimableBytes);
