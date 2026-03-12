namespace PhotoDupFinder.Core.Models;

public sealed record PhotoReportRow(
  string Path,
  string Extension,
  string Format,
  long FileSizeBytes,
  int Width,
  int Height,
  DateTimeOffset? CaptureDate,
  PhotoVerificationStatus Status,
  string? StatusMessage,
  string? Fingerprint,
  int MetadataScore,
  bool SuggestedKeep,
  string? KeepReason,
  int? DuplicateGroupId);
