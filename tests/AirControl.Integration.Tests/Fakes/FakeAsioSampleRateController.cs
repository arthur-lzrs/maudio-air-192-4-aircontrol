using AirControl.Core;

namespace AirControl.Integration.Tests.Fakes;

/// <summary>Simula <see cref="IAsioSampleRateController"/> sem tocar hardware/COM real, mesmo padrão de FakeRecordingFormatController.</summary>
public class FakeAsioSampleRateController : IAsioSampleRateController
{
    private int? _currentSampleRate;
    private IReadOnlyList<int> _supportedSampleRates = Array.Empty<int>();

    public string? ForcedTrySetSampleRateError { get; set; }

    public void SetCurrentSampleRate(int? sampleRate) => _currentSampleRate = sampleRate;

    public void SetSupportedSampleRates(IReadOnlyList<int> sampleRates) => _supportedSampleRates = sampleRates;

    public int? GetCurrentSampleRate() => _currentSampleRate;

    public IReadOnlyList<int> GetSupportedSampleRates() => _supportedSampleRates;

    public bool TrySetSampleRate(int sampleRate, out string? error)
    {
        if (ForcedTrySetSampleRateError is not null)
        {
            error = ForcedTrySetSampleRateError;
            return false;
        }

        if (!_supportedSampleRates.Contains(sampleRate))
        {
            error = $"O driver ASIO não suporta {sampleRate}Hz.";
            return false;
        }

        _currentSampleRate = sampleRate;
        error = null;
        return true;
    }
}
