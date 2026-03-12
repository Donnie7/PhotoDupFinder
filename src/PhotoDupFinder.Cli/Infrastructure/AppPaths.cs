namespace PhotoDupFinder.Cli.Infrastructure;

internal static class AppPaths
{
  private static readonly string BaseDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "PhotoDupFinder");

  public static string SettingsFilePath => Path.Combine(BaseDirectory, "settings.json");

  public static string LastReportFilePath => Path.Combine(BaseDirectory, "last-report.json");

  public static void EnsureBaseDirectory() => Directory.CreateDirectory(BaseDirectory);
}
