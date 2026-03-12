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

    var quickFingerprintService = new FakeQuickFingerprintService(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      [first] = "QUICK-MATCH",
      [second] = "QUICK-MATCH",
    });

    var fingerprintService = new FakeFingerprintService(new Dictionary<string, PixelFingerprint>(StringComparer.OrdinalIgnoreCase)
    {
      [first] = new("MATCH", 4000, 3000),
      [second] = new("MATCH", 4000, 3000),
    });

    var scanner = new DuplicatePhotoScanner(metadataReader, quickFingerprintService, fingerprintService);

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
    var other = CreateFile("other.jpg", 41);

    var scanner = new DuplicatePhotoScanner(
      new FakeMetadataReader(new Dictionary<string, PhotoMetadata>(StringComparer.OrdinalIgnoreCase)
      {
        [file] = new("JPEG", 200, 100, 200, 100, 1, null, 0),
        [other] = new("JPEG", 200, 100, 200, 100, 1, null, 0),
      }),
      new FakeQuickFingerprintService(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        [file] = "ONLY",
        [other] = "ONLY",
      }),
      new FakeFingerprintService(
        new Dictionary<string, PixelFingerprint>(StringComparer.OrdinalIgnoreCase)
        {
          [other] = new("OTHER", 200, 100),
        },
        new Dictionary<string, Exception>(StringComparer.OrdinalIgnoreCase)
        {
          [file] = new InvalidOperationException("decode failed"),
        }));

    var report = await scanner.ScanAsync(new ScanOptions(_rootDirectory));

    Assert.Equal(0, report.DuplicateGroupCount);
    Assert.Equal(1, report.VerifiedFiles);
    Assert.Equal(1, report.UnverifiedFiles);
    Assert.Single(report.Issues);
    Assert.Equal(PhotoVerificationStatus.Unverified, report.AllFiles.Single(item => item.Path == file).Status);
  }

  [Fact]
  public async Task ScanAsync_SkipsFingerprinting_WhenDimensionsAreUnique()
  {
    var first = CreateFile("a.jpg", 10);
    var second = CreateFile("b.jpg", 11);

    var quickFingerprintService = new CountingQuickFingerprintService();
    var exactFingerprintService = new CountingExactFingerprintService();

    var scanner = new DuplicatePhotoScanner(
      new FakeMetadataReader(new Dictionary<string, PhotoMetadata>(StringComparer.OrdinalIgnoreCase)
      {
        [first] = new("JPEG", 4000, 3000, 4000, 3000, 1, null, 0),
        [second] = new("JPEG", 3000, 2000, 3000, 2000, 1, null, 0),
      }),
      quickFingerprintService,
      exactFingerprintService);

    var report = await scanner.ScanAsync(new ScanOptions(_rootDirectory));

    Assert.Equal(0, report.DuplicateGroupCount);
    Assert.Equal(0, quickFingerprintService.CallCount);
    Assert.Equal(0, exactFingerprintService.CallCount);
  }

  [Fact]
  public async Task ScanAsync_OnlyExactFingerprints_QuickHashCollisions()
  {
    var first = CreateFile("a.jpg", 10);
    var second = CreateFile("b.jpg", 11);
    var third = CreateFile("c.jpg", 12);

    var quickFingerprintService = new FakeQuickFingerprintService(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      [first] = "Q1",
      [second] = "Q1",
      [third] = "Q2",
    });

    var exactFingerprintService = new CountingExactFingerprintService(new Dictionary<string, PixelFingerprint>(StringComparer.OrdinalIgnoreCase)
    {
      [first] = new("EXACT", 4000, 3000),
      [second] = new("EXACT", 4000, 3000),
    });

    var scanner = new DuplicatePhotoScanner(
      new FakeMetadataReader(new Dictionary<string, PhotoMetadata>(StringComparer.OrdinalIgnoreCase)
      {
        [first] = new("JPEG", 4000, 3000, 4000, 3000, 1, null, 0),
        [second] = new("PNG", 4000, 3000, 4000, 3000, 1, null, 0),
        [third] = new("JPEG", 4000, 3000, 4000, 3000, 1, null, 0),
      }),
      quickFingerprintService,
      exactFingerprintService);

    var report = await scanner.ScanAsync(new ScanOptions(_rootDirectory));

    Assert.Equal(1, report.DuplicateGroupCount);
    Assert.Equal(3, quickFingerprintService.CallCount);
    Assert.Equal(2, exactFingerprintService.CallCount);
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

  private sealed class FakeQuickFingerprintService(Dictionary<string, string> fingerprintsByPath) : IQuickFingerprintService
  {
    private int _callCount;

    public int CallCount => _callCount;

    public string Create(string path)
    {
      Interlocked.Increment(ref _callCount);
      return fingerprintsByPath[path];
    }
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

  private sealed class CountingQuickFingerprintService : IQuickFingerprintService
  {
    private int _callCount;

    public int CallCount => _callCount;

    public string Create(string path)
    {
      Interlocked.Increment(ref _callCount);
      throw new InvalidOperationException("Quick fingerprint should not be created for this test.");
    }
  }

  private sealed class CountingExactFingerprintService(
    Dictionary<string, PixelFingerprint>? fingerprintsByPath = null) : IPixelFingerprintService
  {
    private int _callCount;

    public int CallCount => _callCount;

    public PixelFingerprint Create(string path)
    {
      Interlocked.Increment(ref _callCount);

      if (fingerprintsByPath is null)
      {
        throw new InvalidOperationException("Exact fingerprint should not be created for this test.");
      }

      return fingerprintsByPath[path];
    }
  }
}
