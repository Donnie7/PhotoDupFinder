using PhotoDupFinder.Core.Abstractions;
using PhotoDupFinder.Core.Models;
using PhotoDupFinder.Core.Services;

namespace PhotoDupFinder.Core.Tests;

public sealed class DuplicatePhotoScannerTests : IDisposable
{
  private readonly string _rootDirectory = Path.Combine(
    Path.GetTempPath(),
    $"photodupfinder-tests-{Guid.NewGuid():N}");

  public DuplicatePhotoScannerTests()
  {
    Directory.CreateDirectory(_rootDirectory);
  }

  [Fact]
  public async Task ScanAsync_GroupsMatchingFingerprints_AndSuggestsBestKeep()
  {
    var first = CreateFile("a.jpg", 100);
    var second = CreateFile("b.png", 150);
    var unique = CreateFile("c.jpg", 75);

    var metadataReader = new FakeMetadataReader(new Dictionary<string, PhotoMetadata>(StringComparer.OrdinalIgnoreCase)
    {
      [first] = new("JPEG", 4000, 3000, 4000, 3000, 1, new DateTimeOffset(2022, 10, 12, 10, 0, 0, TimeSpan.Zero), 1),
      [second] = new("PNG", 4000, 3000, 4000, 3000, 1, new DateTimeOffset(2022, 10, 12, 10, 0, 0, TimeSpan.Zero), 3),
      [unique] = new("JPEG", 1920, 1080, 1920, 1080, 1, null, 0),
    });

    var fingerprintService = new FakeFingerprintService(new Dictionary<string, PixelFingerprint>(StringComparer.OrdinalIgnoreCase)
    {
      [first] = new("MATCH", 4000, 3000),
      [second] = new("MATCH", 4000, 3000),
      [unique] = new("UNIQUE", 1920, 1080),
    });

    var scanner = new DuplicatePhotoScanner(metadataReader, fingerprintService);

    var report = await scanner.ScanAsync(new ScanOptions(_rootDirectory));

    Assert.Equal(3, report.FilesDiscovered);
    Assert.Equal(1, report.DuplicateGroupCount);
    Assert.Equal(100, report.ReclaimableBytes);
    Assert.Single(report.DuplicateGroups);

    var group = report.DuplicateGroups[0];
    Assert.Equal(second, group.SuggestedKeep.Path);
    Assert.Contains("preferred archival format", group.SuggestedKeep.KeepReason, StringComparison.OrdinalIgnoreCase);
    Assert.Contains(group.Files, file => file.Path == first && !file.SuggestedKeep);
    Assert.Contains(group.Files, file => file.Path == second && file.SuggestedKeep);
  }

  [Fact]
  public async Task ScanAsync_TracksUnverifiedFiles_WhenFingerprintingFails()
  {
    var file = CreateFile("broken.jpg", 40);

    var scanner = new DuplicatePhotoScanner(
      new FakeMetadataReader(new Dictionary<string, PhotoMetadata>(StringComparer.OrdinalIgnoreCase)
      {
        [file] = new("JPEG", 200, 100, 200, 100, 1, null, 0),
      }),
      new FakeFingerprintService(
        new Dictionary<string, PixelFingerprint>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, Exception>(StringComparer.OrdinalIgnoreCase)
        {
          [file] = new InvalidOperationException("decode failed"),
        }));

    var report = await scanner.ScanAsync(new ScanOptions(_rootDirectory));

    Assert.Equal(0, report.DuplicateGroupCount);
    Assert.Equal(0, report.VerifiedFiles);
    Assert.Equal(1, report.UnverifiedFiles);
    Assert.Single(report.Issues);
    Assert.Equal(PhotoVerificationStatus.Unverified, report.AllFiles.Single().Status);
  }

  public void Dispose()
  {
    if (Directory.Exists(_rootDirectory))
    {
      Directory.Delete(_rootDirectory, recursive: true);
    }
  }

  private string CreateFile(string name, int size)
  {
    var path = Path.Combine(_rootDirectory, name);
    var content = Enumerable.Repeat((byte)size, size).ToArray();
    File.WriteAllBytes(path, content);
    return path;
  }

  private sealed class FakeMetadataReader(Dictionary<string, PhotoMetadata> metadataByPath) : IPhotoMetadataReader
  {
    public PhotoMetadata Read(string path) => metadataByPath[path];
  }

  private sealed class FakeFingerprintService(
    Dictionary<string, PixelFingerprint> fingerprintsByPath,
    Dictionary<string, Exception>? exceptionsByPath = null) : IPixelFingerprintService
  {
    public PixelFingerprint Create(string path)
    {
      if (exceptionsByPath is not null && exceptionsByPath.TryGetValue(path, out var exception))
      {
        throw exception;
      }

      return fingerprintsByPath[path];
    }
  }
}
