using System.Text.Json;
using System.Text.Json.Serialization;

namespace AirControl.Core;

public class SettingsRepository : ISettingsRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

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
            var profile = JsonSerializer.Deserialize<ChannelSettingsProfile>(json, SerializerOptions);
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

        var json = JsonSerializer.Serialize(profile, SerializerOptions);
        File.WriteAllText(_filePath, json);
    }
}
