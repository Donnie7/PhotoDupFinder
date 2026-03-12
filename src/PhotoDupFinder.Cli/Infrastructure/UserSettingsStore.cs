using System.Text.Json;

namespace PhotoDupFinder.Cli.Infrastructure;

internal sealed class UserSettingsStore
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = true,
  };

  public async Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default)
  {
    AppPaths.EnsureBaseDirectory();
    if (!File.Exists(AppPaths.SettingsFilePath))
    {
      return UserSettings.Default;
    }

    await using var stream = File.OpenRead(AppPaths.SettingsFilePath);
    var settings = await JsonSerializer.DeserializeAsync<UserSettings>(stream, JsonOptions, cancellationToken)
      .ConfigureAwait(false);

    return settings ?? UserSettings.Default;
  }

  public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(settings);

    AppPaths.EnsureBaseDirectory();
    await using var stream = File.Create(AppPaths.SettingsFilePath);
    await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken)
      .ConfigureAwait(false);
  }
}
