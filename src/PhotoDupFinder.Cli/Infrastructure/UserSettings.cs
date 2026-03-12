namespace PhotoDupFinder.Cli.Infrastructure;

internal sealed record UserSettings(
  string? DefaultScanRoot,
  string? LastCsvDirectory,
  int MaxDegreeOfParallelism)
{
  public static UserSettings Default => new(
    DefaultScanRoot: Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
    LastCsvDirectory: Path.Combine(Environment.CurrentDirectory, "reports"),
    MaxDegreeOfParallelism: Math.Max(1, Environment.ProcessorCount / 2));
}
