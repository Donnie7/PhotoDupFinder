using System.Security.Cryptography;
using ImageMagick;
using PhotoDupFinder.Core.Abstractions;

namespace PhotoDupFinder.Core.Services;

public sealed class QuickFingerprintService : IQuickFingerprintService
{
  private const uint SampleSize = 32;

  public string Create(string path)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);

    var settings = new MagickReadSettings
    {
      Width = SampleSize,
      Height = SampleSize,
    };

    using var image = new MagickImage(path, settings);
    image.AutoOrient();
    image.ColorSpace = ColorSpace.sRGB;
    image.BackgroundColor = MagickColors.White;
    image.Alpha(AlphaOption.Remove);
    image.Resize(SampleSize, SampleSize);

    var pixels = image.GetPixels().ToByteArray(PixelMapping.RGB) ?? [];
    return Convert.ToHexString(SHA256.HashData(pixels));
  }
}
