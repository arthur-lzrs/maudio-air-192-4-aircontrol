using System.Text.Json;

namespace AirControl.Core;

public class RecordingFormatRepository : IRecordingFormatRepository
{
    private readonly string _filePath;

    public RecordingFormatRepository()
        : this(GetDefaultFilePath())
    {
    }

    public RecordingFormatRepository(string filePath)
    {
        _filePath = filePath;
    }

    private static string GetDefaultFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "AirControl", "recording-format.json");
    }

    public RecordingFormat? Load(string deviceId)
    {
        var all = LoadAll();
        return all.TryGetValue(deviceId, out var format) ? format : null;
    }

    public void Save(string deviceId, RecordingFormat format)
    {
        var all = LoadAll();
        all[deviceId] = format;

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    private Dictionary<string, RecordingFormat> LoadAll()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, RecordingFormat>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var all = JsonSerializer.Deserialize<Dictionary<string, RecordingFormat>>(json);
            return all ?? new Dictionary<string, RecordingFormat>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new Dictionary<string, RecordingFormat>();
        }
    }
}
