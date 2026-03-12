namespace PhotoDupFinder.Core.Models;

public sealed record ScanOptions(
  string RootPath,
  bool Recursive = true,
  int MaxDegreeOfParallelism = 0,
  IReadOnlyCollection<string>? Extensions = null);
