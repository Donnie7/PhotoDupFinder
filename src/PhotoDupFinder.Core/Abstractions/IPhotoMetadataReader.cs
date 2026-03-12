using PhotoDupFinder.Core.Models;

namespace PhotoDupFinder.Core.Abstractions;

public interface IPhotoMetadataReader
{
  PhotoMetadata Read(string path);
}
