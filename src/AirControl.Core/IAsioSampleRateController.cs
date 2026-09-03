namespace AirControl.Core;

/// <summary>
/// Controla o sample rate do driver ASIO do M-Audio diretamente (research.md §6, revisado):
/// diferente do buffer size (que o protocolo ASIO não expõe como preferência persistente
/// gravável por terceiros), o sample rate tem um comando padrão (<c>ASIOSetSampleRate</c>) que
/// qualquer host pode chamar através de um handshake breve com o driver — sem precisar que o
/// AirControl vire um host ASIO permanente nem abrir mão do WASAPI compartilhado para captura.
/// </summary>
public interface IAsioSampleRateController
{
    /// <summary>Sample rate atualmente configurado no driver ASIO, ou null se o driver não estiver disponível.</summary>
    int? GetCurrentSampleRate();

    /// <summary>
    /// Combinações de sample rate candidatas oferecidas na UI. NÃO é uma checagem de capacidade
    /// real: teste ao vivo contra o AIR 192|4 mostrou que <c>ASIOcanSampleRate</c> só retorna
    /// true para o rate JÁ ativo no momento da consulta (o mesmo problema, já documentado, de
    /// <c>IAudioClient::IsFormatSupported</c> em modo compartilhado) — usá-lo para popular a
    /// lista faria o dropdown sempre colapsar para uma única opção. O gate real é a escrita
    /// confirmada em <see cref="TrySetSampleRate"/>.
    /// </summary>
    IReadOnlyList<int> GetSupportedSampleRates();

    /// <summary>
    /// Tenta aplicar o sample rate no driver ASIO. Retorna false com <paramref name="error"/>
    /// preenchido (mensagem acionável) se a escrita falhar ou não for confirmada por releitura.
    /// </summary>
    bool TrySetSampleRate(int sampleRate, out string? error);
}
