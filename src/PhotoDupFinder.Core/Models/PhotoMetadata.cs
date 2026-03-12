namespace PhotoDupFinder.Core.Models;

public sealed record PhotoMetadata(
  string Format,
  int Width,
  int Height,
  int NormalizedWidth,
  int NormalizedHeight,
  int Orientation,
  DateTimeOffset? CaptureDate,
  int MetadataScore);
