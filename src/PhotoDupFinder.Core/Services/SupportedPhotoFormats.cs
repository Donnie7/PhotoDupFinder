namespace PhotoDupFinder.Core.Services;

public static class SupportedPhotoFormats
{
  public static readonly IReadOnlyList<string> DefaultExtensions =
  [
    ".jpg",
    ".jpeg",
    ".png",
    ".bmp",
    ".gif",
    ".tif",
    ".tiff",
    ".heic",
    ".heif",
    ".dng",
    ".webp",
  ];

  public static IReadOnlySet<string> CreateSet(IReadOnlyCollection<string>? extensions = null)
  {
    var values = extensions ?? DefaultExtensions;
    return new HashSet<string>(values.Select(Normalize), StringComparer.OrdinalIgnoreCase);
  }

  public static bool IsSupported(string path, IReadOnlySet<string> supportedExtensions)
  {
    var extension = Normalize(Path.GetExtension(path));
    return supportedExtensions.Contains(extension);
  }

  public static string Normalize(string extension)
  {
    if (string.IsNullOrWhiteSpace(extension))
    {
      return string.Empty;
    }

    return extension.StartsWith(".", StringComparison.Ordinal) ?
      extension.ToLowerInvariant() :
      $".{extension.ToLowerInvariant()}";
  }
}
