using PhotoDupFinder.Core.Models;

namespace PhotoDupFinder.Core.Abstractions;

public interface IPixelFingerprintService
{
  PixelFingerprint Create(string path);
}
