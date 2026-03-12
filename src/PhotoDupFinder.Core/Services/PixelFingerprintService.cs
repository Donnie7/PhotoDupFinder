using System.Security.Cryptography;
using ImageMagick;
using PhotoDupFinder.Core.Abstractions;
using PhotoDupFinder.Core.Models;

namespace PhotoDupFinder.Core.Services;

public sealed class PixelFingerprintService : IPixelFingerprintService
{
  public PixelFingerprint Create(string path)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);

    using var image = new MagickImage(path);
    image.AutoOrient();
    image.ColorSpace = ColorSpace.sRGB;
    image.BackgroundColor = MagickColors.White;
    image.Alpha(AlphaOption.Remove);

    var pixels = image.GetPixels().ToByteArray(PixelMapping.RGB) ?? [];
    var digest = Convert.ToHexString(SHA256.HashData(pixels));

    return new PixelFingerprint(
      digest,
      checked((int)image.Width),
      checked((int)image.Height));
  }
}
