using Spectre.Console;

namespace PhotoDupFinder.Cli.Presentation;

internal static class AppTheme
{
  public const string PrimaryMarkup = "deepskyblue2";
  public const string SecondaryMarkup = "grey62";
  public const string SuccessMarkup = "springgreen2";
  public const string WarningMarkup = "gold3";
  public const string ErrorMarkup = "indianred1";
  public const string AccentMarkup = "cadetblue";

  public static readonly Color PrimaryColor = new(48, 184, 214);
  public static readonly Color BorderColor = new(79, 102, 122);
  public static readonly Color AccentColor = new(100, 149, 237);
}
