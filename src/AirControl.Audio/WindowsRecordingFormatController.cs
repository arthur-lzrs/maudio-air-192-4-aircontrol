using System.Runtime.InteropServices;
using AirControl.Core;
using NAudio.CoreAudioApi;

namespace AirControl.Audio;

/// <summary>
/// Implementação real de <see cref="IRecordingFormatController"/> para o "Formato Padrão"
/// (sample rate/bit depth) de um dispositivo de gravação WASAPI — a mesma configuração
/// exposta na aba "Avançado" do Painel de Som do Windows (research.md §5). A escrita exige
/// interop COM direto com <c>IPropertyStore</c>, isolado em <see cref="DeviceFormatPropertyStore"/>
/// para não vazar COM/interop para AirControl.Core/AirControl.App (Constitution I).
/// </summary>
/// <remarks>
/// <c>IAudioClient::IsFormatSupported</c> em modo compartilhado, testado ao vivo contra o AIR
/// 192|4, só retorna <c>true</c> para o formato JÁ ativo no momento da consulta — não reflete a
/// capacidade real do hardware. Usá-la como pré-checagem antes de escrever cria um travamento:
/// uma vez que o dispositivo cai em 44.1kHz, a consulta passa a reportar só 44.1kHz como
/// "suportado", e qualquer tentativa de voltar a 48kHz é recusada antes mesmo de tentar (esse
/// era exatamente o bug relatado pelo usuário). Por isso <see cref="GetSupportedFormats"/>
/// devolve a lista fixa de candidatos (apenas para popular a UI) e o gate real é a escrita em
/// <see cref="TrySetFormat"/>, confirmada por releitura pós-escrita — não uma pré-checagem.
/// A escrita em si (via <see cref="DeviceFormatPropertyStore"/>) funciona de forma confiável
/// desde que o sub-formato peça exatamente o que o driver espera (PCM inteiro, não IEEE float,
/// mesmo em 32-bit neste hardware) — confirmado ao vivo aplicando 48kHz/32-bit depois que o
/// Windows havia derivado sozinho para 44.1kHz, sem intervenção manual. A verificação pós-escrita
/// continua obrigatória mesmo assim: o retorno reflete o que o dispositivo realmente aceitou,
/// nunca apenas "a chamada não lançou".
/// </remarks>
public class WindowsRecordingFormatController : IRecordingFormatController
{
    /// <summary>Lista fixa de combinações candidatas oferecidas na UI (research.md §5) — não é uma checagem de capacidade real, ver remarks da classe.</summary>
    private static readonly (int SampleRate, int BitDepth)[] CandidateFormats =
    {
        (44100, 16), (44100, 24),
        (48000, 16), (48000, 24), (48000, 32),
        (96000, 24), (96000, 32),
    };

    public RecordingFormat? GetCurrentFormat(string deviceId)
    {
        var device = TryGetActiveDevice(deviceId);
        if (device is null)
        {
            return null;
        }

        var mixFormat = device.AudioClient.MixFormat;
        return new RecordingFormat(mixFormat.SampleRate, mixFormat.BitsPerSample);
    }

    public IReadOnlyList<RecordingFormat> GetSupportedFormats(string deviceId)
    {
        var device = TryGetActiveDevice(deviceId);
        return device is null
            ? Array.Empty<RecordingFormat>()
            : CandidateFormats.Select(c => new RecordingFormat(c.SampleRate, c.BitDepth)).ToList();
    }

    public bool TrySetFormat(string deviceId, RecordingFormat format, out string? error)
    {
        if (TryGetActiveDevice(deviceId) is null)
        {
            error = "Dispositivo não encontrado ou inativo.";
            return false;
        }

        try
        {
            DeviceFormatPropertyStore.WriteDefaultFormat(deviceId, format.SampleRate, format.BitDepth);
        }
        catch (Exception ex) when (ex is COMException or ExternalException or InvalidOperationException)
        {
            error = $"Falha ao aplicar o formato: {ex.Message}";
            return false;
        }

        var applied = GetCurrentFormat(deviceId);
        var sampleRateConfirmed = applied?.SampleRate == format.SampleRate;
        // Formatos de 24-bit são gravados em um container de 32 bits (research.md §5); o Windows
        // pode legitimamente reportar de volta 32-bit para um pedido de 24-bit.
        var bitDepthConfirmed = applied is not null
            && (applied.BitDepth == format.BitDepth || (format.BitDepth == 24 && applied.BitDepth == 32));

        if (!sampleRateConfirmed || !bitDepthConfirmed)
        {
            error = $"O Windows não confirmou {format}: o dispositivo continua reportando " +
                    $"{(applied is null ? "estado desconhecido" : applied.ToString())}. " +
                    "Talvez seja necessário ajustar manualmente no Painel de Som do Windows.";
            return false;
        }

        error = null;
        return true;
    }

    private static MMDevice? TryGetActiveDevice(string deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        try
        {
            var device = enumerator.GetDevice(deviceId);
            return device.State == DeviceState.Active ? device : null;
        }
        catch (COMException)
        {
            return null;
        }
    }
}
