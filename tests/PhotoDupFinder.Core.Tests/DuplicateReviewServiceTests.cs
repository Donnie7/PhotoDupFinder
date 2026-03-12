using PhotoDupFinder.Core.Models;
using PhotoDupFinder.Core.Services;

namespace PhotoDupFinder.Core.Tests;

public sealed class DuplicateReviewServiceTests
{
  [Fact]
  public void CreatePlan_UsesSelectedKeep_AndCalculatesReclaimableBytes()
  {
    var keep = CreateRow("keep.jpg", 4000, 3000, 400, metadataScore: 5, suggestedKeep: true, keepReason: "Suggested keep");
    var duplicate = CreateRow("dup.jpg", 3000, 2000, 250, metadataScore: 2);
    var group = new DuplicateGroup(1, "hash", keep, [keep, duplicate], 250);

    var plan = DuplicateReviewService.CreatePlan(group, duplicate.Path);

    Assert.Equal(duplicate.Path, plan.Keep.Path);
    Assert.Single(plan.Duplicates);
    Assert.Equal(keep.Path, plan.Duplicates[0].Path);
    Assert.Equal(400, plan.ReclaimableBytes);
  }

  [Fact]
  public void ApplyDeletedPaths_RemovesResolvedGroups_AndRefreshesCounts()
  {
    var keep = CreateRow("keep.jpg", 4000, 3000, 400, metadataScore: 5, suggestedKeep: true, keepReason: "Suggested keep");
    var duplicate = CreateRow("dup.jpg", 3000, 2000, 250, metadataScore: 2);
    var unverified = CreateRow(
      "broken.jpg",
      0,
      0,
      0,
      metadataScore: 0,
      status: PhotoVerificationStatus.Unverified,
      statusMessage: "decode failed");

    var report = new ScanReport(
      RootPath: "C:\\photos",
      StartedAtUtc: DateTimeOffset.UtcNow,
      Duration: TimeSpan.FromMinutes(1),
      FilesDiscovered: 3,
      VerifiedFiles: 2,
      UnverifiedFiles: 1,
      DuplicateGroupCount: 1,
      ReclaimableBytes: 250,
      DuplicateGroups: [new DuplicateGroup(1, "hash", keep, [keep, duplicate], 250)],
      AllFiles: [keep, duplicate, unverified],
      Issues: []);

    var updated = DuplicateReviewService.ApplyDeletedPaths(report, [duplicate.Path]);

    Assert.Equal(2, updated.FilesDiscovered);
    Assert.Equal(1, updated.VerifiedFiles);
    Assert.Equal(1, updated.UnverifiedFiles);
    Assert.Equal(0, updated.DuplicateGroupCount);
    Assert.Equal(0, updated.ReclaimableBytes);
    Assert.Empty(updated.DuplicateGroups);
    Assert.Single(updated.AllFiles);
    Assert.Equal(unverified.Path, updated.AllFiles[0].Path);
  }

  [Fact]
  public void ApplyDeletedPaths_RecomputesSuggestedKeep_WhenOriginalKeepIsDeleted()
  {
    var originalKeep = CreateRow("keep.png", 4000, 3000, 500, metadataScore: 5, suggestedKeep: true, keepReason: "Suggested keep");
    var second = CreateRow("second.jpg", 3500, 2500, 300, metadataScore: 4);
    var third = CreateRow("third.jpg", 3200, 2400, 280, metadataScore: 3);
    var group = new DuplicateGroup(7, "hash", originalKeep, [originalKeep, second, third], 580);
    var report = new ScanReport(
      RootPath: "C:\\photos",
      StartedAtUtc: DateTimeOffset.UtcNow,
      Duration: TimeSpan.FromMinutes(1),
      FilesDiscovered: 3,
      VerifiedFiles: 3,
      UnverifiedFiles: 0,
      DuplicateGroupCount: 1,
      ReclaimableBytes: 580,
      DuplicateGroups: [group],
      AllFiles: [originalKeep, second, third],
      Issues: []);

    var updated = DuplicateReviewService.ApplyDeletedPaths(report, [originalKeep.Path]);

    Assert.Single(updated.DuplicateGroups);
    Assert.Equal(second.Path, updated.DuplicateGroups[0].SuggestedKeep.Path);
    Assert.Equal(280, updated.DuplicateGroups[0].ReclaimableBytes);
    Assert.Contains("highest resolution", updated.DuplicateGroups[0].SuggestedKeep.KeepReason, StringComparison.OrdinalIgnoreCase);
  }

  private static PhotoReportRow CreateRow(
    string path,
    int width,
    int height,
    long fileSizeBytes,
    int metadataScore,
    bool suggestedKeep = false,
    string? keepReason = null,
    PhotoVerificationStatus status = PhotoVerificationStatus.Verified,
    string? statusMessage = null)
  {
    return new PhotoReportRow(
      Path.Combine("C:\\photos", path),
      Path.GetExtension(path),
      "JPEG",
      fileSizeBytes,
      width,
      height,
      null,
      status,
      statusMessage,
      "hash",
      metadataScore,
      suggestedKeep,
      keepReason,
      status == PhotoVerificationStatus.Verified ? 1 : null);
  }
}
