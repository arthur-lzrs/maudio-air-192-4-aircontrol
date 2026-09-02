using System.Text.Json;

namespace AirControl.Core;

public class SettingsRepository : ISettingsRepository
{
    private readonly string _filePath;

    public SettingsRepository()
        : this(GetDefaultFilePath())
    {
    }

    public SettingsRepository(string filePath)
    {
        _filePath = filePath;
    }

    private static string GetDefaultFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "AirControl", "channel-settings.json");
    }

    public ChannelSettingsProfile Load()
    {
        if (!File.Exists(_filePath))
        {
            return ChannelSettingsProfile.Default;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var profile = JsonSerializer.Deserialize<ChannelSettingsProfile>(json);
            return profile ?? ChannelSettingsProfile.Default;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return ChannelSettingsProfile.Default;
        }
    }

    public void Save(ChannelSettingsProfile profile)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
