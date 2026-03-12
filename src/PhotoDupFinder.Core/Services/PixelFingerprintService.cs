using ImageMagick;
using PhotoDupFinder.Core.Abstractions;
using PhotoDupFinder.Core.Models;

namespace PhotoDupFinder.Core.Services;

public sealed class PixelFingerprintService : IPixelFingerprintService
{
  static PixelFingerprintService()
  {
    OpenCL.IsEnabled = false;
    ResourceLimits.Thread = 1;
  }

  public PixelFingerprint Create(string path)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);

    using var image = new MagickImage(path);
    image.AutoOrient();
    image.ColorSpace = ColorSpace.sRGB;
    image.BackgroundColor = MagickColors.White;
    image.Alpha(AlphaOption.Remove);

    var digest = image.Signature;
    if (string.IsNullOrWhiteSpace(digest))
    {
      throw new InvalidOperationException($"Failed to compute a normalized fingerprint for '{path}'.");
    }

    return new PixelFingerprint(
      digest,
      checked((int)image.Width),
      checked((int)image.Height));
  }
}
