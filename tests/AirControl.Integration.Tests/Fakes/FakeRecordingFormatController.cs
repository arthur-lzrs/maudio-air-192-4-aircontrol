using AirControl.Core;

namespace AirControl.Integration.Tests.Fakes;

/// <summary>
/// Simula <see cref="IRecordingFormatController"/> sem tocar hardware/COM real, seguindo o
/// mesmo padrão de <see cref="FakeAudioEngine"/>/<see cref="FakeAudioDeviceProvider"/>.
/// </summary>
public class FakeRecordingFormatController : IRecordingFormatController
{
    private readonly Dictionary<string, RecordingFormat> _currentFormats = new();
    private readonly Dictionary<string, IReadOnlyList<RecordingFormat>> _supportedFormats = new();

    public string? ForcedTrySetFormatError { get; set; }

    /// <summary>Quando definida, <see cref="TrySetFormat"/> LANÇA — simula a escrita COM falhando (S5).</summary>
    public Exception? ForcedTrySetFormatException { get; set; }

    /// <summary>Quantas vezes <see cref="TrySetFormat"/> foi chamado — usado para detectar escritas indevidas fora do fluxo pré-Start.</summary>
    public int TrySetFormatCallCount { get; private set; }

    public void SetCurrentFormat(string deviceId, RecordingFormat format) => _currentFormats[deviceId] = format;

    public void SetSupportedFormats(string deviceId, IReadOnlyList<RecordingFormat> formats) => _supportedFormats[deviceId] = formats;

    public RecordingFormat? GetCurrentFormat(string deviceId)
        => _currentFormats.TryGetValue(deviceId, out var format) ? format : null;

    public IReadOnlyList<RecordingFormat> GetSupportedFormats(string deviceId)
        => _supportedFormats.TryGetValue(deviceId, out var formats) ? formats : Array.Empty<RecordingFormat>();

    public bool TrySetFormat(string deviceId, RecordingFormat format, out string? error)
    {
        TrySetFormatCallCount++;

        if (ForcedTrySetFormatException is not null)
        {
            throw ForcedTrySetFormatException;
        }

        if (ForcedTrySetFormatError is not null)
        {
            error = ForcedTrySetFormatError;
            return false;
        }

        if (!GetSupportedFormats(deviceId).Contains(format))
        {
            error = $"Formato {format.SampleRate}Hz/{format.BitDepth}-bit não é suportado por este dispositivo.";
            return false;
        }

        _currentFormats[deviceId] = format;
        error = null;
        return true;
    }
}
