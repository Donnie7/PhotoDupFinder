using ImageMagick;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using PhotoDupFinder.Core.Abstractions;
using PhotoDupFinder.Core.Models;

namespace PhotoDupFinder.Core.Services;

public sealed class PhotoMetadataReader : IPhotoMetadataReader
{
  public PhotoMetadata Read(string path)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);

    var directories = ReadDirectories(path);
    var orientation = ReadOrientation(directories);
    var captureDate = ReadCaptureDate(directories);
    var metadataScore = CalculateMetadataScore(directories, captureDate);

    var imageInfo = new MagickImageInfo(path);
    var width = checked((int)imageInfo.Width);
    var height = checked((int)imageInfo.Height);
    var normalizedWidth = RequiresSwap(orientation) ? height : width;
    var normalizedHeight = RequiresSwap(orientation) ? width : height;

    return new PhotoMetadata(
      imageInfo.Format.ToString().ToUpperInvariant(),
      width,
      height,
      normalizedWidth,
      normalizedHeight,
      orientation,
      captureDate,
      metadataScore);
  }

  private static IReadOnlyList<MetadataExtractor.Directory> ReadDirectories(string path)
  {
    try
    {
      return ImageMetadataReader.ReadMetadata(path);
    }
    catch (ImageProcessingException)
    {
      return [];
    }
  }

  private static int ReadOrientation(IReadOnlyList<MetadataExtractor.Directory> directories)
  {
    var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
    if (ifd0 is not null && ifd0.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientation))
    {
      return orientation;
    }

    return 1;
  }

  private static DateTimeOffset? ReadCaptureDate(IReadOnlyList<MetadataExtractor.Directory> directories)
  {
    var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
    if (subIfd is not null &&
        subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var capturedAt))
    {
      var localTime = DateTime.SpecifyKind(capturedAt, DateTimeKind.Local);
      return new DateTimeOffset(localTime);
    }

    return null;
  }

  private static int CalculateMetadataScore(
    IReadOnlyList<MetadataExtractor.Directory> directories,
    DateTimeOffset? captureDate)
  {
    var score = 0;
    if (captureDate is not null)
    {
      score++;
    }

    var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
    if (ifd0 is not null)
    {
      if (ifd0.ContainsTag(ExifDirectoryBase.TagMake))
      {
        score++;
      }

      if (ifd0.ContainsTag(ExifDirectoryBase.TagModel))
      {
        score++;
      }
    }

    return score;
  }

  private static bool RequiresSwap(int orientation) =>
    orientation is 5 or 6 or 7 or 8;
}
