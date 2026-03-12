using System.Globalization;
using PhotoDupFinder.Cli.Infrastructure;
using PhotoDupFinder.Cli.Presentation;
using PhotoDupFinder.Core.Models;
using PhotoDupFinder.Core.Services;
using Spectre.Console;

namespace PhotoDupFinder.Cli;

internal static class Program
{
  private const string MenuRunScan = "Run scan now";
  private const string MenuViewLastReport = "View last report";
  private const string MenuExportLastReport = "Export last report CSV";
  private const string MenuCommandReference = "Command reference";
  private const string MenuSettings = "Settings";
  private const string MenuExit = "Exit";

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
    if (args[0].Equals("start", StringComparison.OrdinalIgnoreCase) ||
        args[0].Equals("menu", StringComparison.OrdinalIgnoreCase))
    {
      await RunInteractiveAsync().ConfigureAwait(false);
      return 0;
    }

    if (args[0].Equals("scan", StringComparison.OrdinalIgnoreCase))
    {
      var command = ParseScanCommand(args);
      var report = await ExecuteScanAsync(command.Options).ConfigureAwait(false);
      report = await RenderReportAsync(report, browseGroups: false).ConfigureAwait(false);

      if (!string.IsNullOrWhiteSpace(command.CsvOutputPath))
      {
        await CsvWriter.WriteAsync(report, command.CsvOutputPath!).ConfigureAwait(false);
        RenderInfo($"CSV report written to {command.CsvOutputPath}");
      }

      return 0;
    }

    if (args[0].Equals("config", StringComparison.OrdinalIgnoreCase))
    {
      var settings = await SettingsStore.LoadAsync().ConfigureAwait(false);
      RenderBanner();
      RenderConfigurationSummary(settings, includeCacheLocation: true);
      return 0;
    }

    if (args[0].Equals("help", StringComparison.OrdinalIgnoreCase) ||
        args[0].Equals("--help", StringComparison.OrdinalIgnoreCase) ||
        args[0].Equals("-h", StringComparison.OrdinalIgnoreCase))
    {
      RenderBanner();
      RenderHelp();
      return 0;
    }

    RenderBanner();
    RenderError($"Unknown command '{args[0]}'.");
    RenderHelp();
    return 1;
  }

  private static async Task RunInteractiveAsync()
  {
    while (true)
    {
      var settings = await SettingsStore.LoadAsync().ConfigureAwait(false);
      AnsiConsole.Clear();
      RenderBanner();
      RenderHomeOverview(settings);

      var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
          .Title($"[{AppTheme.PrimaryMarkup}]Choose an action[/]")
          .HighlightStyle(new Style(foreground: AppTheme.AccentColor, decoration: Decoration.Bold))
          .AddChoices(
            MenuRunScan,
            MenuViewLastReport,
            MenuExportLastReport,
            MenuCommandReference,
            MenuSettings,
            MenuExit));

      switch (choice)
      {
        case MenuRunScan:
          await RunInteractiveScanAsync(settings).ConfigureAwait(false);
          break;
        case MenuViewLastReport:
          await ViewLastReportAsync().ConfigureAwait(false);
          break;
        case MenuExportLastReport:
          await ExportLastReportAsync().ConfigureAwait(false);
          break;
        case MenuCommandReference:
          ShowCommandReference(settings);
          break;
        case MenuSettings:
          await ConfigureSettingsAsync().ConfigureAwait(false);
          break;
        case MenuExit:
          return;
      }
    }
  }

  private static async Task RunInteractiveScanAsync(UserSettings settings)
  {
    var rootPath = AnsiConsole.Prompt(
      new TextPrompt<string>($"[{AppTheme.PrimaryMarkup}]Directory to scan[/]")
        .DefaultValue(settings.DefaultScanRoot ?? Environment.CurrentDirectory)
        .Validate(path => Directory.Exists(path) ?
          ValidationResult.Success() :
          ValidationResult.Error($"[{AppTheme.ErrorMarkup}]Directory not found.[/]")));

    var recursiveChoice = AnsiConsole.Prompt(
      new SelectionPrompt<string>()
        .Title($"[{AppTheme.PrimaryMarkup}]Scan subdirectories too?[/]")
        .AddChoices("Yes", "No"));

    var workerChoice = AnsiConsole.Prompt(
      new SelectionPrompt<string>()
        .Title($"[{AppTheme.PrimaryMarkup}]Fingerprint worker setting[/]")
        .AddChoices(
          $"Use saved configuration ({settings.MaxDegreeOfParallelism} worker{(settings.MaxDegreeOfParallelism == 1 ? string.Empty : "s")})",
          "Override for this scan"));

    var maxDegree = settings.MaxDegreeOfParallelism;
    if (workerChoice == "Override for this scan")
    {
      maxDegree = AnsiConsole.Prompt(
        new TextPrompt<int>($"[{AppTheme.PrimaryMarkup}]Workers for this scan[/]")
          .DefaultValue(settings.MaxDegreeOfParallelism)
          .Validate(value => value > 0 ?
            ValidationResult.Success() :
            ValidationResult.Error($"[{AppTheme.ErrorMarkup}]Value must be positive.[/]")));
    }

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
      Recursive: recursiveChoice == "Yes",
      MaxDegreeOfParallelism: maxDegree);

    var report = await ExecuteScanAsync(scanOptions).ConfigureAwait(false);
    report = await RenderReportAsync(report, browseGroups: true).ConfigureAwait(false);

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

    await RenderReportAsync(report, browseGroups: true).ConfigureAwait(false);
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
    var saveWarning = default(string);

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
          try
          {
            await ReportStore.SaveAsync(report).ConfigureAwait(false);
          }
          catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
          {
            saveWarning = $"Scan completed, but the local cached report could not be written: {ex.Message}";
          }
        }).ConfigureAwait(false);

    if (!string.IsNullOrWhiteSpace(saveWarning))
    {
      RenderWarning(saveWarning);
    }

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

  private static void RenderHomeOverview(UserSettings settings)
  {
    var commandTable = CreateCommandTable();
    var configurationTable = CreateConfigurationTable(settings, includeCacheLocation: false);

    var commandPanel = new Panel(commandTable)
    {
      Header = new PanelHeader($"[{AppTheme.PrimaryMarkup}]Command Shortcuts[/]"),
      Border = BoxBorder.Rounded,
    };

    commandPanel.BorderStyle = new Style(foreground: AppTheme.BorderColor);

    var configurationPanel = new Panel(configurationTable)
    {
      Header = new PanelHeader($"[{AppTheme.AccentMarkup}]Current Configuration[/]"),
      Border = BoxBorder.Rounded,
    };

    configurationPanel.BorderStyle = new Style(foreground: AppTheme.BorderColor);

    var columns = new Columns([commandPanel, configurationPanel])
    {
      Expand = true,
    };

    AnsiConsole.Write(columns);
    AnsiConsole.WriteLine();
  }

  private static void ShowCommandReference(UserSettings settings)
  {
    AnsiConsole.Clear();
    RenderBanner();
    RenderConfigurationSummary(settings, includeCacheLocation: true);
    RenderHelp();
    WaitForKeypress();
  }

  private static async Task<ScanReport> RenderReportAsync(ScanReport report, bool browseGroups)
  {
    ArgumentNullException.ThrowIfNull(report);

    if (!browseGroups)
    {
      RenderReportSections(report);
      return report;
    }

    return await RunDuplicateReviewAsync(report).ConfigureAwait(false);
  }

  private static void RenderReportSections(ScanReport report)
  {
    RenderSummary(report);

    if (report.DuplicateGroups.Count == 0)
    {
      RenderInfo("No duplicates were found in the scanned directory.");
      RenderIssues(report);
      return;
    }

    RenderGroupOverview(report);
    RenderIssues(report);
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

  private static async Task<ScanReport> RunDuplicateReviewAsync(ScanReport report)
  {
    while (true)
    {
      AnsiConsole.Clear();
      RenderBanner();
      RenderReportSections(report);

      if (report.DuplicateGroups.Count == 0)
      {
        WaitForKeypress();
        return report;
      }

      var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
          .Title($"[{AppTheme.PrimaryMarkup}]Choose how to work with the found duplicates[/]")
          .AddChoices(
            "Browse duplicate groups",
            "Delete duplicates using the suggested keep in every group",
            "Copy duplicate candidates to a backup folder",
            "Return"));

      switch (choice)
      {
        case "Browse duplicate groups":
          report = await BrowseGroupsAsync(report).ConfigureAwait(false);
          break;
        case "Delete duplicates using the suggested keep in every group":
          report = await DeleteDuplicatesAsync(
              report,
              report.DuplicateGroups
                .Select(group => new DuplicateGroupReviewRequest(group, DuplicateReviewService.CreateSuggestedPlan(group)))
                .ToArray())
            .ConfigureAwait(false);
          break;
        case "Copy duplicate candidates to a backup folder":
          CopyDuplicatesToBackup(
            report,
            report.DuplicateGroups
              .Select(group => new DuplicateGroupReviewRequest(group, DuplicateReviewService.CreateSuggestedPlan(group)))
              .ToArray());
          break;
        case "Return":
          return report;
      }
    }
  }

  private static async Task<ScanReport> BrowseGroupsAsync(ScanReport report)
  {
    while (true)
    {
      if (report.DuplicateGroups.Count == 0)
      {
        return report;
      }

      AnsiConsole.Clear();
      RenderBanner();
      RenderSummary(report);
      RenderGroupOverview(report);

      var choices = report.DuplicateGroups
        .Select(DuplicateGroupMenuChoice.ForGroup)
        .Append(DuplicateGroupMenuChoice.Return())
        .ToArray();

      var choice = AnsiConsole.Prompt(
        new SelectionPrompt<DuplicateGroupMenuChoice>()
          .Title($"[{AppTheme.PrimaryMarkup}]Browse duplicate groups[/]")
          .PageSize(Math.Min(choices.Length, 10))
          .AddChoices(choices));

      if (choice.Group is null)
      {
        return report;
      }

      report = await ReviewGroupAsync(report, choice.Group).ConfigureAwait(false);
    }
  }

  private static async Task<ScanReport> ReviewGroupAsync(ScanReport report, DuplicateGroup initialGroup)
  {
    var selectedKeepPath = initialGroup.SuggestedKeep.Path;

    while (true)
    {
      var group = report.DuplicateGroups.FirstOrDefault(item => item.GroupId == initialGroup.GroupId);
      if (group is null)
      {
        return report;
      }

      selectedKeepPath = ResolveSelectedKeepPath(group, selectedKeepPath);

      AnsiConsole.Clear();
      RenderBanner();
      RenderGroupDetails(group, selectedKeepPath);

      var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
          .Title($"[{AppTheme.PrimaryMarkup}]Choose an action for group #{group.GroupId}[/]")
          .AddChoices(
            "Select the photo to keep",
            "Delete the other files in this group",
            "Copy the other files in this group to a backup folder",
            "Return"));

      switch (choice)
      {
        case "Select the photo to keep":
          selectedKeepPath = PromptForKeepPath(group, selectedKeepPath);
          break;
        case "Delete the other files in this group":
          report = await DeleteDuplicatesAsync(
              report,
              [new DuplicateGroupReviewRequest(group, DuplicateReviewService.CreatePlan(group, selectedKeepPath))])
            .ConfigureAwait(false);
          return report;
        case "Copy the other files in this group to a backup folder":
          CopyDuplicatesToBackup(
            report,
            [new DuplicateGroupReviewRequest(group, DuplicateReviewService.CreatePlan(group, selectedKeepPath))]);
          break;
        case "Return":
          return report;
      }
    }
  }

  private static void RenderGroupDetails(DuplicateGroup group, string selectedKeepPath)
  {
    selectedKeepPath = ResolveSelectedKeepPath(group, selectedKeepPath);

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
      var isSelectedKeep = string.Equals(file.Path, selectedKeepPath, StringComparison.OrdinalIgnoreCase);
      var isSuggestedKeep = string.Equals(file.Path, group.SuggestedKeep.Path, StringComparison.OrdinalIgnoreCase);
      var decision = isSelectedKeep ?
        $"[{AppTheme.SuccessMarkup}]Keep[/]" :
        isSuggestedKeep ?
          $"[{AppTheme.AccentMarkup}]Suggested[/]" :
          $"[{AppTheme.WarningMarkup}]Duplicate[/]";

      table.AddRow(
        decision,
        file.Format,
        $"{file.Width}x{file.Height}",
        HumanizeBytes(file.FileSizeBytes),
        file.CaptureDate?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "-",
        Markup.Escape(file.Path));
    }

    var keepPanel = new Panel(Markup.Escape(BuildKeepSelectionSummary(group, selectedKeepPath)))
    {
      Header = new PanelHeader($"[{AppTheme.AccentMarkup}]Current keep selection[/]"),
      Border = BoxBorder.Rounded,
    };

    keepPanel.BorderStyle = new Style(foreground: AppTheme.BorderColor);
    AnsiConsole.Write(table);
    AnsiConsole.Write(keepPanel);
    AnsiConsole.WriteLine();
  }

  private static string BuildKeepSelectionSummary(DuplicateGroup group, string selectedKeepPath)
  {
    var selectedKeep = group.Files.First(file =>
      string.Equals(file.Path, selectedKeepPath, StringComparison.OrdinalIgnoreCase));

    if (string.Equals(selectedKeepPath, group.SuggestedKeep.Path, StringComparison.OrdinalIgnoreCase))
    {
      return $"{selectedKeep.Path}{Environment.NewLine}{group.SuggestedKeep.KeepReason ?? "Suggested keep"}";
    }

    return $"{selectedKeep.Path}{Environment.NewLine}Custom keep selected for this group.{Environment.NewLine}Suggested keep: {group.SuggestedKeep.Path}{Environment.NewLine}{group.SuggestedKeep.KeepReason ?? "Suggested keep"}";
  }

  private static string ResolveSelectedKeepPath(DuplicateGroup group, string? selectedKeepPath)
  {
    if (!string.IsNullOrWhiteSpace(selectedKeepPath) &&
        group.Files.Any(file => string.Equals(file.Path, selectedKeepPath, StringComparison.OrdinalIgnoreCase)))
    {
      return selectedKeepPath;
    }

    return group.SuggestedKeep.Path;
  }

  private static string PromptForKeepPath(DuplicateGroup group, string selectedKeepPath)
  {
    var choices = group.Files
      .Select(file => KeepSelectionChoice.ForFile(file, group.SuggestedKeep.Path, selectedKeepPath))
      .ToArray();

    var choice = AnsiConsole.Prompt(
      new SelectionPrompt<KeepSelectionChoice>()
        .Title($"[{AppTheme.PrimaryMarkup}]Choose the file to keep in group #{group.GroupId}[/]")
        .PageSize(Math.Min(choices.Length, 10))
        .AddChoices(choices));

    return choice.Path;
  }

  private static async Task<ScanReport> DeleteDuplicatesAsync(
    ScanReport report,
    IReadOnlyList<DuplicateGroupReviewRequest> requests)
  {
    var duplicateFiles = requests
      .SelectMany(request => request.Plan.Duplicates)
      .DistinctBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
      .ToArray();

    if (duplicateFiles.Length == 0)
    {
      AnsiConsole.Clear();
      RenderBanner();
      RenderInfo("There are no duplicate files left to delete for the current selection.");
      WaitForKeypress();
      return report;
    }

    var reclaimableBytes = duplicateFiles.Sum(file => file.FileSizeBytes);
    var scopeDescription = requests.Count == 1 ?
      "this duplicate group" :
      $"{requests.Count} duplicate groups";

    if (!ConfirmAction(
          $"Delete {duplicateFiles.Length} duplicate file(s) from {scopeDescription}? " +
          $"The selected keep photo in each group will be preserved, and up to {HumanizeBytes(reclaimableBytes)} can be reclaimed."))
    {
      return report;
    }

    var deletedPaths = new List<string>(duplicateFiles.Length);
    var issues = new List<string>();

    foreach (var file in duplicateFiles)
    {
      try
      {
        if (File.Exists(file.Path))
        {
          File.Delete(file.Path);
        }

        deletedPaths.Add(file.Path);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        issues.Add($"{file.Path}: {ex.Message}");
      }
    }

    var updatedReport = deletedPaths.Count > 0 ?
      DuplicateReviewService.ApplyDeletedPaths(report, deletedPaths) :
      report;
    var persistenceWarning = deletedPaths.Count > 0 ?
      await PersistReportAsync(updatedReport).ConfigureAwait(false) :
      null;

    AnsiConsole.Clear();
    RenderBanner();

    if (deletedPaths.Count > 0)
    {
      RenderInfo(
        $"Deleted {deletedPaths.Count} duplicate file(s) from {scopeDescription}. " +
        $"{updatedReport.DuplicateGroupCount} duplicate group(s) remain in the cached report.");
    }
    else
    {
      RenderWarning("No files were deleted.");
    }

    if (issues.Count > 0)
    {
      RenderWarning(BuildIssueSummary("Some files could not be deleted.", issues));
    }

    if (!string.IsNullOrWhiteSpace(persistenceWarning))
    {
      RenderWarning(persistenceWarning);
    }

    WaitForKeypress();
    return updatedReport;
  }

  private static void CopyDuplicatesToBackup(
    ScanReport report,
    IReadOnlyList<DuplicateGroupReviewRequest> requests)
  {
    var duplicateFiles = requests
      .SelectMany(request => request.Plan.Duplicates.Select(file => new DuplicateBackupItem(request.Group.GroupId, file)))
      .DistinctBy(item => item.File.Path, StringComparer.OrdinalIgnoreCase)
      .ToArray();

    if (duplicateFiles.Length == 0)
    {
      AnsiConsole.Clear();
      RenderBanner();
      RenderInfo("There are no duplicate files left to copy for the current selection.");
      WaitForKeypress();
      return;
    }

    var backupRoot = PromptForBackupFolder(report);
    var issues = new List<string>();
    var copiedCount = 0;

    foreach (var item in duplicateFiles)
    {
      try
      {
        if (!File.Exists(item.File.Path))
        {
          issues.Add($"{item.File.Path}: file not found");
          continue;
        }

        var destinationPath = BuildBackupDestinationPath(report.RootPath, backupRoot, item.GroupId, item.File.Path);
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
          Directory.CreateDirectory(destinationDirectory);
        }

        File.Copy(item.File.Path, destinationPath);
        copiedCount++;
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        issues.Add($"{item.File.Path}: {ex.Message}");
      }
    }

    AnsiConsole.Clear();
    RenderBanner();
    RenderInfo($"Copied {copiedCount} duplicate file(s) to {backupRoot}.");

    if (issues.Count > 0)
    {
      RenderWarning(BuildIssueSummary("Some files could not be copied.", issues));
    }

    WaitForKeypress();
  }

  private static bool ConfirmAction(string message)
  {
    var choice = AnsiConsole.Prompt(
      new SelectionPrompt<string>()
        .Title($"[{AppTheme.WarningMarkup}]{Markup.Escape(message)}[/]")
        .AddChoices("No", "Yes"));

    return choice == "Yes";
  }

  private static string PromptForBackupFolder(ScanReport report)
  {
    AppPaths.EnsureBaseDirectory();
    var defaultPath = Path.Combine(
      AppPaths.BackupRootDirectoryPath,
      $"backup-{DateTime.Now:yyyyMMdd-HHmmss}");

    var backupPath = AnsiConsole.Prompt(
      new TextPrompt<string>($"[{AppTheme.PrimaryMarkup}]Backup folder for duplicate copies[/]")
        .DefaultValue(defaultPath)
        .Validate(path => ValidateBackupPath(path, report.RootPath)));

    Directory.CreateDirectory(backupPath);
    return backupPath;
  }

  private static ValidationResult ValidateBackupPath(string path, string scanRoot)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      return ValidationResult.Error($"[{AppTheme.ErrorMarkup}]Choose a backup folder.[/]");
    }

    try
    {
      var fullBackupPath = EnsureTrailingSeparator(Path.GetFullPath(path));
      var fullScanRoot = EnsureTrailingSeparator(Path.GetFullPath(scanRoot));

      if (fullBackupPath.StartsWith(fullScanRoot, StringComparison.OrdinalIgnoreCase))
      {
        return ValidationResult.Error($"[{AppTheme.ErrorMarkup}]Choose a folder outside the scanned directory.[/]");
      }
    }
    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
    {
      return ValidationResult.Error($"[{AppTheme.ErrorMarkup}]Invalid path: {Markup.Escape(ex.Message)}[/]");
    }

    return ValidationResult.Success();
  }

  private static string BuildBackupDestinationPath(
    string rootPath,
    string backupRoot,
    int groupId,
    string sourcePath)
  {
    var relativePath = Path.GetRelativePath(rootPath, sourcePath);
    if (relativePath.StartsWith("..", StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(relativePath))
    {
      relativePath = Path.Combine($"group-{groupId:D4}", Path.GetFileName(sourcePath));
    }

    var destinationPath = Path.Combine(backupRoot, relativePath);
    return EnsureUniquePath(destinationPath);
  }

  private static string EnsureUniquePath(string destinationPath)
  {
    if (!File.Exists(destinationPath))
    {
      return destinationPath;
    }

    var directory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
    var fileName = Path.GetFileNameWithoutExtension(destinationPath);
    var extension = Path.GetExtension(destinationPath);
    var suffix = 1;

    while (true)
    {
      var candidate = Path.Combine(directory, $"{fileName}-copy-{suffix}{extension}");
      if (!File.Exists(candidate))
      {
        return candidate;
      }

      suffix++;
    }
  }

  private static string EnsureTrailingSeparator(string path) =>
    path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
    path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal) ?
      path :
      $"{path}{Path.DirectorySeparatorChar}";

  private static string BuildIssueSummary(string heading, IReadOnlyList<string> issues)
  {
    var visibleIssues = issues.Take(5).ToArray();
    var summary = string.Join(Environment.NewLine, visibleIssues);
    if (issues.Count > visibleIssues.Length)
    {
      summary = $"{summary}{Environment.NewLine}...and {issues.Count - visibleIssues.Length} more.";
    }

    return $"{heading}{Environment.NewLine}{summary}";
  }

  private static async Task<string?> PersistReportAsync(ScanReport report)
  {
    try
    {
      await ReportStore.SaveAsync(report).ConfigureAwait(false);
      return null;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      return $"The cached report could not be updated: {ex.Message}";
    }
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
    var help = CreateCommandTable();

    var panel = new Panel(help)
    {
      Header = new PanelHeader($"[{AppTheme.PrimaryMarkup}]Command Reference[/]"),
      Border = BoxBorder.Rounded,
    };

    panel.BorderStyle = new Style(foreground: AppTheme.BorderColor);
    AnsiConsole.Write(panel);
    AnsiConsole.WriteLine();
  }

  private static void RenderConfigurationSummary(UserSettings settings, bool includeCacheLocation)
  {
    var panel = new Panel(CreateConfigurationTable(settings, includeCacheLocation))
    {
      Header = new PanelHeader($"[{AppTheme.AccentMarkup}]Configuration[/]"),
      Border = BoxBorder.Rounded,
    };

    panel.BorderStyle = new Style(foreground: AppTheme.BorderColor);
    AnsiConsole.Write(panel);
    AnsiConsole.WriteLine();
  }

  private static Table CreateCommandTable()
  {
    var help = new Table()
      .RoundedBorder()
      .BorderColor(AppTheme.BorderColor)
      .AddColumn($"[{AppTheme.SecondaryMarkup}]Command[/]")
      .AddColumn($"[{AppTheme.SecondaryMarkup}]Description[/]");

    help.AddRow("photodupfinder start", "Open the interactive home menu.");
    help.AddRow("photodupfinder menu", "Alias for the home menu.");
    help.AddRow("photodupfinder scan --root <path>", "Run a scan directly from the command line.");
    help.AddRow("--csv <path>", "Export the scan result to CSV.");
    help.AddRow("--max-degree <n>", "Override the worker limit for the scan.");
    help.AddRow("--non-recursive", "Scan only the top directory.");
    help.AddRow("--extensions .jpg,.png", "Override the supported extension list.");
    help.AddRow("photodupfinder config", "Show the current saved configuration.");
    help.AddRow("photodupfinder help", "Show the command reference.");

    return help;
  }

  private static Table CreateConfigurationTable(UserSettings settings, bool includeCacheLocation)
  {
    var table = new Table()
      .RoundedBorder()
      .BorderColor(AppTheme.BorderColor)
      .AddColumn($"[{AppTheme.SecondaryMarkup}]Setting[/]")
      .AddColumn($"[{AppTheme.PrimaryMarkup}]Value[/]");

    table.AddRow("Default scan root", Markup.Escape(settings.DefaultScanRoot ?? Environment.CurrentDirectory));
    table.AddRow("Default CSV folder", Markup.Escape(settings.LastCsvDirectory ?? Path.Combine(Environment.CurrentDirectory, "reports")));
    table.AddRow("Saved worker limit", settings.MaxDegreeOfParallelism.ToString(CultureInfo.InvariantCulture));

    if (includeCacheLocation)
    {
      table.AddRow("Settings file", Markup.Escape(AppPaths.SettingsFilePath));
      table.AddRow("Last report cache", Markup.Escape(AppPaths.LastReportFilePath));
    }

    return table;
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

  private sealed record DuplicateGroupMenuChoice(DuplicateGroup? Group, string Label)
  {
    public static DuplicateGroupMenuChoice ForGroup(DuplicateGroup group) =>
      new(group, $"Group #{group.GroupId} ({group.Files.Count} files, {HumanizeBytes(group.ReclaimableBytes)} reclaimable)");

    public static DuplicateGroupMenuChoice Return() => new(null, "Return");

    public override string ToString() => Label;
  }

  private sealed record KeepSelectionChoice(string Path, string Label)
  {
    public static KeepSelectionChoice ForFile(PhotoReportRow file, string suggestedKeepPath, string selectedKeepPath)
    {
      var tags = new List<string>();
      if (string.Equals(file.Path, selectedKeepPath, StringComparison.OrdinalIgnoreCase))
      {
        tags.Add("current");
      }

      if (string.Equals(file.Path, suggestedKeepPath, StringComparison.OrdinalIgnoreCase))
      {
        tags.Add("suggested");
      }

      var suffix = tags.Count == 0 ? string.Empty : $" ({string.Join(", ", tags)})";
      return new KeepSelectionChoice(
        file.Path,
        $"{System.IO.Path.GetFileName(file.Path)} | {file.Width}x{file.Height} | {HumanizeBytes(file.FileSizeBytes)}{suffix} | {file.Path}");
    }

    public override string ToString() => Label;
  }

  private sealed record DuplicateGroupReviewRequest(DuplicateGroup Group, DuplicateSelectionPlan Plan);

  private sealed record DuplicateBackupItem(int GroupId, PhotoReportRow File);

  private sealed record ScanCommand(ScanOptions Options, string? CsvOutputPath);
}
