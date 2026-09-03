using AirControl.Core;

namespace AirControl.Integration.Tests.Fakes;

/// <summary>Simula <see cref="IAsioSampleRateController"/> sem tocar hardware/COM real, mesmo padrão de FakeRecordingFormatController.</summary>
public class FakeAsioSampleRateController : IAsioSampleRateController
{
    private int? _currentSampleRate;
    private IReadOnlyList<int> _supportedSampleRates = Array.Empty<int>();

    public string? ForcedTrySetSampleRateError { get; set; }

    /// <summary>Quando definida, <see cref="TrySetSampleRate"/> LANÇA — simula o handshake ASIO falhando (S5).</summary>
    public Exception? ForcedTrySetSampleRateException { get; set; }

    /// <summary>Observador chamado a cada consulta em tempo real — usado para verificar que a captura está parada (S3).</summary>
    public Action? OnGetCurrentSampleRate { get; set; }

    public void SetCurrentSampleRate(int? sampleRate) => _currentSampleRate = sampleRate;

    public void SetSupportedSampleRates(IReadOnlyList<int> sampleRates) => _supportedSampleRates = sampleRates;

    /// <summary>Quantas consultas em tempo real ao driver aconteceram — SC-004b/S3 dependem de contar isso.</summary>
    public int GetCurrentSampleRateCallCount { get; private set; }

    public int? GetCurrentSampleRate()
    {
        GetCurrentSampleRateCallCount++;
        OnGetCurrentSampleRate?.Invoke();
        return _currentSampleRate;
    }

    public IReadOnlyList<int> GetSupportedSampleRates() => _supportedSampleRates;

    public bool TrySetSampleRate(int sampleRate, out string? error)
    {
        if (ForcedTrySetSampleRateException is not null)
        {
            throw ForcedTrySetSampleRateException;
        }

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
