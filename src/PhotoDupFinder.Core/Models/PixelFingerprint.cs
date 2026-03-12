namespace PhotoDupFinder.Core.Models;

public sealed record PixelFingerprint(
  string Sha256,
  int Width,
  int Height);
