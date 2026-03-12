using PhotoDupFinder.Core.Services;

namespace PhotoDupFinder.Core.Tests;

public sealed class SupportedPhotoFormatsTests
{
  [Theory]
  [InlineData("image.JPG")]
  [InlineData("image.jpeg")]
  [InlineData("image.HeIc")]
  [InlineData("image.dng")]
  [InlineData("image.tiff")]
  public void IsSupported_ReturnsTrue_ForKnownExtensions(string fileName)
  {
    var supported = SupportedPhotoFormats.CreateSet();

    var result = SupportedPhotoFormats.IsSupported(fileName, supported);

    Assert.True(result);
  }

  [Fact]
  public void Normalize_AddsDotAndLowercases()
  {
    var result = SupportedPhotoFormats.Normalize("JPG");

    Assert.Equal(".jpg", result);
  }
}
