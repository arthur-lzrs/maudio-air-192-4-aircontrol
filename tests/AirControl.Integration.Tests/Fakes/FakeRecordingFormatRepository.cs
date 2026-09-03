using AirControl.Core;

namespace AirControl.Integration.Tests.Fakes;

/// <summary>In-memory <see cref="IRecordingFormatRepository"/>, sem tocar o disco (mesmo padrão de FakeAudioEngine/FakeAudioDeviceProvider).</summary>
public class FakeRecordingFormatRepository : IRecordingFormatRepository
{
    private readonly Dictionary<string, RecordingFormat> _formats = new();

    public RecordingFormat? Load(string deviceId) => _formats.TryGetValue(deviceId, out var format) ? format : null;

    public void Save(string deviceId, RecordingFormat format) => _formats[deviceId] = format;
}
