using System.Globalization;
using PhotoDupFinder.Cli.Infrastructure;
using PhotoDupFinder.Cli.Presentation;
using PhotoDupFinder.Core.Models;
using PhotoDupFinder.Core.Services;
using Spectre.Console;

namespace PhotoDupFinder.Cli;

internal static class Program
{
  private static readonly DuplicatePhotoScanner Scanner = new();
  private static readonly CsvReportWriter CsvWriter = new();
  private static readonly UserSettingsStore SettingsStore = new();
  private static readonly ScanReportStore ReportStore = new();

  public static async Task<int> Main(string[] args)
  {
    try
    {
      if (args.Length > 0)
      {
        return await RunCommandAsync(args).ConfigureAwait(false);
      }

      await RunInteractiveAsync().ConfigureAwait(false);
      return 0;
    }
    catch (Exception exception)
    {
      RenderError($"Unhandled failure: {exception.Message}");
      return 1;
    }
  }

  private static async Task<int> RunCommandAsync(string[] args)
  {
    if (args[0].Equals("scan", StringComparison.OrdinalIgnoreCase))
    {
      var command = ParseScanCommand(args);
      var report = await ExecuteScanAsync(command.Options).ConfigureAwait(false);
      RenderReport(report, browseGroups: false);

      if (!string.IsNullOrWhiteSpace(command.CsvOutputPath))
      {
        await CsvWriter.WriteAsync(report, command.CsvOutputPath!).ConfigureAwait(false);
        RenderInfo($"CSV report written to {command.CsvOutputPath}");
      }

      return 0;
    }

    if (args[0].Equals("help", StringComparison.OrdinalIgnoreCase) ||
        args[0].Equals("--help", StringComparison.OrdinalIgnoreCase) ||
        args[0].Equals("-h", StringComparison.OrdinalIgnoreCase))
    {
      RenderHelp();
      return 0;
    }

    RenderError($"Unknown command '{args[0]}'.");
    RenderHelp();
    return 1;
  }

  private static async Task RunInteractiveAsync()
  {
    while (true)
    {
      AnsiConsole.Clear();
      RenderBanner();

      var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
          .Title($"[{AppTheme.PrimaryMarkup}]Choose an action[/]")
          .HighlightStyle(new Style(foreground: AppTheme.AccentColor, decoration: Decoration.Bold))
          .AddChoices(
            "Scan directory",
            "View last report",
            "Export last report CSV",
            "Settings",
            "Exit"));

      switch (choice)
      {
        case "Scan directory":
          await RunInteractiveScanAsync().ConfigureAwait(false);
          break;
        case "View last report":
          await ViewLastReportAsync().ConfigureAwait(false);
          break;
        case "Export last report CSV":
          await ExportLastReportAsync().ConfigureAwait(false);
          break;
        case "Settings":
          await ConfigureSettingsAsync().ConfigureAwait(false);
          break;
        case "Exit":
          return;
      }
    }
  }

  private static async Task RunInteractiveScanAsync()
  {
    var settings = await SettingsStore.LoadAsync().ConfigureAwait(false);
    var rootPath = AnsiConsole.Prompt(
      new TextPrompt<string>($"[{AppTheme.PrimaryMarkup}]Directory to scan[/]")
        .DefaultValue(settings.DefaultScanRoot ?? Environment.CurrentDirectory)
        .Validate(path => Directory.Exists(path) ?
          ValidationResult.Success() :
          ValidationResult.Error($"[{AppTheme.ErrorMarkup}]Directory not found.[/]")));

    var exportCsv = AnsiConsole.Prompt(
      new SelectionPrompt<string>()
        .Title($"[{AppTheme.PrimaryMarkup}]Export CSV after the scan?[/]")
        .AddChoices("Yes", "No"));

    string? csvPath = null;
    if (exportCsv == "Yes")
    {
      var defaultDirectory = settings.LastCsvDirectory ?? Path.Combine(Environment.CurrentDirectory, "reports");
      Directory.CreateDirectory(defaultDirectory);
      csvPath = AnsiConsole.Prompt(
        new TextPrompt<string>($"[{AppTheme.PrimaryMarkup}]CSV output path[/]")
          .DefaultValue(Path.Combine(defaultDirectory, $"photodupfinder-{DateTime.Now:yyyyMMdd-HHmmss}.csv")));
    }

    var scanOptions = new ScanOptions(
      RootPath: rootPath,
      Recursive: true,
      MaxDegreeOfParallelism: settings.MaxDegreeOfParallelism);

    var report = await ExecuteScanAsync(scanOptions).ConfigureAwait(false);
    RenderReport(report, browseGroups: true);

    if (!string.IsNullOrWhiteSpace(csvPath))
    {
      await CsvWriter.WriteAsync(report, csvPath).ConfigureAwait(false);
      RenderInfo($"CSV report written to {csvPath}");
    }

    var updatedSettings = settings with
    {
      DefaultScanRoot = rootPath,
      LastCsvDirectory = csvPath is null ? settings.LastCsvDirectory : Path.GetDirectoryName(csvPath),
    };

    await SettingsStore.SaveAsync(updatedSettings).ConfigureAwait(false);
    WaitForKeypress();
  }

  private static async Task ViewLastReportAsync()
  {
    var report = await ReportStore.LoadAsync().ConfigureAwait(false);
    if (report is null)
    {
      RenderWarning("No saved report exists yet.");
      WaitForKeypress();
      return;
    }

    AnsiConsole.Clear();
    RenderBanner();
    RenderReport(report, browseGroups: true);
    WaitForKeypress();
  }

  private static async Task ExportLastReportAsync()
  {
    var report = await ReportStore.LoadAsync().ConfigureAwait(false);
    if (report is null)
    {
      RenderWarning("No saved report exists yet.");
      WaitForKeypress();
      return;
    }

    var settings = await SettingsStore.LoadAsync().ConfigureAwait(false);
    var defaultDirectory = settings.LastCsvDirectory ?? Path.Combine(Environment.CurrentDirectory, "reports");
    Directory.CreateDirectory(defaultDirectory);
    var csvPath = AnsiConsole.Prompt(
      new TextPrompt<string>($"[{AppTheme.PrimaryMarkup}]CSV output path[/]")
        .DefaultValue(Path.Combine(defaultDirectory, $"photodupfinder-{DateTime.Now:yyyyMMdd-HHmmss}.csv")));

    await CsvWriter.WriteAsync(report, csvPath).ConfigureAwait(false);
    await SettingsStore.SaveAsync(settings with { LastCsvDirectory = Path.GetDirectoryName(csvPath) })
      .ConfigureAwait(false);

    RenderInfo($"CSV report written to {csvPath}");
    WaitForKeypress();
  }

  private static async Task ConfigureSettingsAsync()
  {
    var settings = await SettingsStore.LoadAsync().ConfigureAwait(false);

    var defaultRoot = AnsiConsole.Prompt(
      new TextPrompt<string>($"[{AppTheme.PrimaryMarkup}]Default scan directory[/]")
        .DefaultValue(settings.DefaultScanRoot ?? Environment.CurrentDirectory));

    var maxDegree = AnsiConsole.Prompt(
      new TextPrompt<int>($"[{AppTheme.PrimaryMarkup}]Max degree of parallelism[/]")
        .DefaultValue(settings.MaxDegreeOfParallelism > 0 ? settings.MaxDegreeOfParallelism : Environment.ProcessorCount)
        .Validate(value => value > 0 ?
          ValidationResult.Success() :
          ValidationResult.Error($"[{AppTheme.ErrorMarkup}]Value must be positive.[/]")));

    await SettingsStore.SaveAsync(settings with
    {
      DefaultScanRoot = defaultRoot,
      MaxDegreeOfParallelism = maxDegree,
    }).ConfigureAwait(false);

    RenderInfo("Settings saved.");
    WaitForKeypress();
  }

  private static async Task<ScanReport> ExecuteScanAsync(ScanOptions options)
  {
    RenderBanner();
    var report = default(ScanReport);

    await AnsiConsole.Status()
      .Spinner(Spinner.Known.Dots)
      .SpinnerStyle(new Style(foreground: AppTheme.PrimaryColor))
      .StartAsync(
        $"[{AppTheme.PrimaryMarkup}]Preparing scan...[/]",
        async context =>
        {
          var progress = new Progress<ScanProgressUpdate>(update =>
          {
            context.Status($"[{AppTheme.PrimaryMarkup}]{Markup.Escape(FormatProgress(update))}[/]");
          });

          report = await Scanner.ScanAsync(options, progress).ConfigureAwait(false);
          await ReportStore.SaveAsync(report).ConfigureAwait(false);
        }).ConfigureAwait(false);

    return report!;
  }

  private static ScanCommand ParseScanCommand(string[] args)
  {
    string? rootPath = null;
    string? csvPath = null;
    var recursive = true;
    var maxDegree = 0;
    IReadOnlyCollection<string>? extensions = null;

    for (var index = 1; index < args.Length; index++)
    {
      switch (args[index])
      {
        case "--root":
          rootPath = ReadRequiredValue(args, ref index, "--root");
          break;
        case "--csv":
          csvPath = ReadRequiredValue(args, ref index, "--csv");
          break;
        case "--max-degree":
          maxDegree = int.Parse(ReadRequiredValue(args, ref index, "--max-degree"), CultureInfo.InvariantCulture);
          break;
        case "--non-recursive":
          recursive = false;
          break;
        case "--extensions":
          extensions = ReadRequiredValue(args, ref index, "--extensions")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
          break;
        default:
          throw new ArgumentException($"Unknown option '{args[index]}'.");
      }
    }

    if (string.IsNullOrWhiteSpace(rootPath))
    {
      throw new ArgumentException("The scan command requires --root <path>.");
    }

    return new ScanCommand(
      new ScanOptions(rootPath, recursive, maxDegree, extensions),
      csvPath);
  }

  private static string ReadRequiredValue(string[] args, ref int index, string optionName)
  {
    if (index + 1 >= args.Length)
    {
      throw new ArgumentException($"Missing value for {optionName}.");
    }

    index++;
    return args[index];
  }

  private static void RenderBanner()
  {
    var banner = new FigletText("PhotoDupFinder")
      .Color(AppTheme.PrimaryColor)
      .LeftJustified();

    AnsiConsole.Write(banner);
    AnsiConsole.Write(new Rule($"[{AppTheme.AccentMarkup}]Metadata + Pixels[/]").RuleStyle(new Style(foreground: AppTheme.BorderColor)));
    AnsiConsole.WriteLine();
  }

  private static void RenderReport(ScanReport report, bool browseGroups)
  {
    ArgumentNullException.ThrowIfNull(report);

    RenderSummary(report);

    if (report.DuplicateGroups.Count == 0)
    {
      RenderInfo("No duplicates were found in the scanned directory.");
      RenderIssues(report);
      return;
    }

    RenderGroupOverview(report);
    RenderIssues(report);

    if (browseGroups)
    {
      BrowseGroups(report);
    }
  }

  private static void RenderSummary(ScanReport report)
  {
    var table = new Table()
      .RoundedBorder()
      .BorderColor(AppTheme.BorderColor)
      .AddColumn($"[{AppTheme.SecondaryMarkup}]Metric[/]")
      .AddColumn($"[{AppTheme.PrimaryMarkup}]Value[/]");

    table.AddRow("Root", Markup.Escape(report.RootPath));
    table.AddRow("Started (UTC)", report.StartedAtUtc.ToString("u"));
    table.AddRow("Duration", report.Duration.ToString("g", CultureInfo.InvariantCulture));
    table.AddRow("Files discovered", report.FilesDiscovered.ToString(CultureInfo.InvariantCulture));
    table.AddRow("Verified files", $"[{AppTheme.SuccessMarkup}]{report.VerifiedFiles}[/]");
    table.AddRow("Unverified files", report.UnverifiedFiles == 0 ?
      $"[{AppTheme.SuccessMarkup}]0[/]" :
      $"[{AppTheme.WarningMarkup}]{report.UnverifiedFiles}[/]");
    table.AddRow("Duplicate groups", report.DuplicateGroupCount == 0 ?
      $"[{AppTheme.SuccessMarkup}]0[/]" :
      $"[{AppTheme.PrimaryMarkup}]{report.DuplicateGroupCount}[/]");
    table.AddRow("Potential reclaim", HumanizeBytes(report.ReclaimableBytes));

    var panel = new Panel(table)
    {
      Header = new PanelHeader($"[{AppTheme.PrimaryMarkup}]Scan Summary[/]"),
      Border = BoxBorder.Double,
    };

    panel.BorderStyle = new Style(foreground: AppTheme.BorderColor);
    AnsiConsole.Write(panel);
    AnsiConsole.WriteLine();
  }

  private static void RenderGroupOverview(ScanReport report)
  {
    var table = new Table()
      .RoundedBorder()
      .BorderColor(AppTheme.BorderColor)
      .AddColumn($"[{AppTheme.SecondaryMarkup}]Group[/]")
      .AddColumn($"[{AppTheme.SecondaryMarkup}]Files[/]")
      .AddColumn($"[{AppTheme.SecondaryMarkup}]Suggested Keep[/]")
      .AddColumn($"[{AppTheme.SecondaryMarkup}]Reclaimable[/]");

    foreach (var group in report.DuplicateGroups)
    {
      table.AddRow(
        $"[{AppTheme.PrimaryMarkup}]#{group.GroupId}[/]",
        group.Files.Count.ToString(CultureInfo.InvariantCulture),
        Markup.Escape(group.SuggestedKeep.Path),
        $"[{AppTheme.WarningMarkup}]{HumanizeBytes(group.ReclaimableBytes)}[/]");
    }

    var panel = new Panel(table)
    {
      Header = new PanelHeader($"[{AppTheme.PrimaryMarkup}]Duplicate Groups[/]"),
      Border = BoxBorder.Rounded,
    };

    panel.BorderStyle = new Style(foreground: AppTheme.BorderColor);
    AnsiConsole.Write(panel);
    AnsiConsole.WriteLine();
  }

  private static void BrowseGroups(ScanReport report)
  {
    while (true)
    {
      var choices = report.DuplicateGroups
        .Select(group => $"Group #{group.GroupId} ({group.Files.Count} files, {HumanizeBytes(group.ReclaimableBytes)} reclaimable)")
        .Append("Return")
        .ToArray();

      var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
          .Title($"[{AppTheme.PrimaryMarkup}]Browse duplicate groups[/]")
          .PageSize(Math.Min(choices.Length, 10))
          .AddChoices(choices));

      if (choice == "Return")
      {
        return;
      }

      var groupId = int.Parse(
        choice.Split('#', ' ', StringSplitOptions.RemoveEmptyEntries)[1],
        CultureInfo.InvariantCulture);
      var group = report.DuplicateGroups.First(item => item.GroupId == groupId);
      RenderGroupDetails(group);
    }
  }

  private static void RenderGroupDetails(DuplicateGroup group)
  {
    AnsiConsole.Write(new Rule($"[{AppTheme.PrimaryMarkup}]Group #{group.GroupId} Details[/]").RuleStyle(new Style(foreground: AppTheme.BorderColor)));

    var table = new Table()
      .RoundedBorder()
      .BorderColor(AppTheme.BorderColor)
      .AddColumn($"[{AppTheme.SecondaryMarkup}]Decision[/]")
      .AddColumn($"[{AppTheme.SecondaryMarkup}]Format[/]")
      .AddColumn($"[{AppTheme.SecondaryMarkup}]Dimensions[/]")
      .AddColumn($"[{AppTheme.SecondaryMarkup}]Size[/]")
      .AddColumn($"[{AppTheme.SecondaryMarkup}]Capture[/]")
      .AddColumn($"[{AppTheme.SecondaryMarkup}]Path[/]");

    foreach (var file in group.Files)
    {
      var decision = file.SuggestedKeep ?
        $"[{AppTheme.SuccessMarkup}]Keep[/]" :
        $"[{AppTheme.WarningMarkup}]Duplicate[/]";

      table.AddRow(
        decision,
        file.Format,
        $"{file.Width}x{file.Height}",
        HumanizeBytes(file.FileSizeBytes),
        file.CaptureDate?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "-",
        Markup.Escape(file.Path));
    }

    var keepPanel = new Panel(Markup.Escape(group.SuggestedKeep.KeepReason ?? "Suggested keep"))
    {
      Header = new PanelHeader($"[{AppTheme.AccentMarkup}]Why keep this file?[/]"),
      Border = BoxBorder.Rounded,
    };

    keepPanel.BorderStyle = new Style(foreground: AppTheme.BorderColor);
    AnsiConsole.Write(table);
    AnsiConsole.Write(keepPanel);
    AnsiConsole.WriteLine();
  }

  private static void RenderIssues(ScanReport report)
  {
    if (report.Issues.Count == 0)
    {
      return;
    }

    var table = new Table()
      .RoundedBorder()
      .BorderColor(AppTheme.BorderColor)
      .AddColumn($"[{AppTheme.SecondaryMarkup}]Stage[/]")
      .AddColumn($"[{AppTheme.SecondaryMarkup}]Path[/]")
      .AddColumn($"[{AppTheme.SecondaryMarkup}]Message[/]");

    foreach (var issue in report.Issues.Take(10))
    {
      table.AddRow(issue.Stage, Markup.Escape(issue.Path), Markup.Escape(issue.Message));
    }

    var panel = new Panel(table)
    {
      Header = new PanelHeader($"[{AppTheme.WarningMarkup}]Scan Issues ({report.Issues.Count})[/]"),
      Border = BoxBorder.Rounded,
    };

    panel.BorderStyle = new Style(foreground: AppTheme.BorderColor);
    AnsiConsole.Write(panel);
    AnsiConsole.WriteLine();
  }

  private static void RenderHelp()
  {
    RenderBanner();
    var help = new Table()
      .RoundedBorder()
      .BorderColor(AppTheme.BorderColor)
      .AddColumn($"[{AppTheme.SecondaryMarkup}]Command[/]")
      .AddColumn($"[{AppTheme.SecondaryMarkup}]Description[/]");

    help.AddRow("scan --root <path>", "Run a scan against a directory.");
    help.AddRow("--csv <path>", "Export the report to CSV after the scan.");
    help.AddRow("--max-degree <n>", "Limit parallel metadata and fingerprint work.");
    help.AddRow("--non-recursive", "Only scan the top directory.");
    help.AddRow("--extensions .jpg,.png", "Override the supported extension list.");

    AnsiConsole.Write(help);
  }

  private static void RenderInfo(string message)
  {
    var panel = new Panel(Markup.Escape(message))
    {
      Border = BoxBorder.Rounded,
      Header = new PanelHeader($"[{AppTheme.SuccessMarkup}]Info[/]"),
    };

    panel.BorderStyle = new Style(foreground: AppTheme.BorderColor);
    AnsiConsole.Write(panel);
  }

  private static void RenderWarning(string message)
  {
    var panel = new Panel(Markup.Escape(message))
    {
      Border = BoxBorder.Rounded,
      Header = new PanelHeader($"[{AppTheme.WarningMarkup}]Warning[/]"),
    };

    panel.BorderStyle = new Style(foreground: AppTheme.BorderColor);
    AnsiConsole.Write(panel);
  }

  private static void RenderError(string message)
  {
    var panel = new Panel(Markup.Escape(message))
    {
      Border = BoxBorder.Double,
      Header = new PanelHeader($"[{AppTheme.ErrorMarkup}]Error[/]"),
    };

    panel.BorderStyle = new Style(foreground: AppTheme.BorderColor);
    AnsiConsole.Write(panel);
  }

  private static string FormatProgress(ScanProgressUpdate update) => update.Stage switch
  {
    ScanStage.DiscoveringFiles => update.Message,
    ScanStage.ReadingMetadata => $"Metadata: {update.Message}",
    ScanStage.Fingerprinting => $"Pixels: {update.Message}",
    ScanStage.Grouping => update.Message,
    ScanStage.Completed => update.Message,
    _ => update.Message,
  };

  private static string HumanizeBytes(long value)
  {
    string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
    double size = value;
    var suffixIndex = 0;

    while (size >= 1024 && suffixIndex < suffixes.Length - 1)
    {
      size /= 1024;
      suffixIndex++;
    }

    return $"{size:0.##} {suffixes[suffixIndex]}";
  }

  private static void WaitForKeypress()
  {
    AnsiConsole.MarkupLine($"[{AppTheme.SecondaryMarkup}]Press any key to continue...[/]");
    Console.ReadKey(intercept: true);
  }

  private sealed record ScanCommand(ScanOptions Options, string? CsvOutputPath);
}
