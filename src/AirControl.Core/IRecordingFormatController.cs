namespace AirControl.Core;

/// <summary>
/// Contrato entre a UI/domínio e a implementação real de I/O (AirControl.Audio) para ler e
/// escrever o "Formato Padrão" (sample rate/bit depth) do Windows para um dispositivo de
/// gravação. Ver contracts/recording-format-contract.md.
/// </summary>
public interface IRecordingFormatController
{
    /// <summary>
    /// Formato atualmente configurado como "Default Format" do Windows para o dispositivo de
    /// gravação identificado, ou null se o dispositivo não existir/não estiver ativo.
    /// </summary>
    RecordingFormat? GetCurrentFormat(string deviceId);

    /// <summary>
    /// Combinações de sample rate/bit depth que o dispositivo aceita em modo compartilhado.
    /// Lista vazia se o dispositivo não existir/não estiver ativo — chamadores devem tratar
    /// isso como "nenhuma alteração possível agora", não como erro.
    /// </summary>
    IReadOnlyList<RecordingFormat> GetSupportedFormats(string deviceId);

    /// <summary>
    /// Tenta aplicar o formato como o novo "Default Format" do Windows para o dispositivo.
    /// Retorna false com <paramref name="error"/> preenchido (mensagem acionável) se o formato
    /// não estiver em <see cref="GetSupportedFormats"/> ou se a escrita falhar. Em caso de
    /// falha, o formato anterior permanece ativo (FR-006). Não reinicia a captura — quem chama
    /// é responsável por Stop()+Start() no IAudioEngine após um retorno true (FR-010).
    /// </summary>
    bool TrySetFormat(string deviceId, RecordingFormat format, out string? error);
}
