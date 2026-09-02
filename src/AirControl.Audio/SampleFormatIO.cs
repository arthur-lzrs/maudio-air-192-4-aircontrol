using NAudio.Wave;

namespace AirControl.Audio;

/// <summary>
/// Leitura/escrita de amostras normalizadas em [-1, 1] a partir do buffer bruto do WASAPI,
/// suportando os formatos de mix compartilhado mais comuns (IEEE float 32-bit, PCM 16/24/32-bit).
/// O mix format real de um dispositivo depende da configuração de "Formato Padrão" do Windows
/// para aquele dispositivo (Painel de Controle de Som > Gravar > Propriedades > Avançado) e não
/// pode ser assumido como float apenas porque o WASAPI compartilhado normalmente mixa em float.
/// </summary>
public static class SampleFormatIO
{
    public static float ReadSample(byte[] buffer, int byteOffset, WaveFormat format)
    {
        return format.BitsPerSample switch
        {
            32 when format.Encoding == WaveFormatEncoding.IeeeFloat => BitConverter.ToSingle(buffer, byteOffset),
            16 => BitConverter.ToInt16(buffer, byteOffset) / 32768f,
            24 => Read24BitPcm(buffer, byteOffset) / 8388608f,
            32 => BitConverter.ToInt32(buffer, byteOffset) / 2147483648f,
            _ => throw new NotSupportedException(
                $"Formato de áudio não suportado: {format.BitsPerSample} bits, encoding {format.Encoding}."),
        };
    }

    public static void WriteSample(byte[] buffer, int byteOffset, float value, WaveFormat format)
    {
        switch (format.BitsPerSample)
        {
            case 32 when format.Encoding == WaveFormatEncoding.IeeeFloat:
                BitConverter.GetBytes(value).CopyTo(buffer, byteOffset);
                break;
            case 16:
                var pcm16 = (short)Math.Clamp(value * 32768f, short.MinValue, short.MaxValue);
                BitConverter.GetBytes(pcm16).CopyTo(buffer, byteOffset);
                break;
            case 24:
                Write24BitPcm(buffer, byteOffset, value);
                break;
            case 32:
                var pcm32 = (int)Math.Clamp(value * 2147483648f, int.MinValue, int.MaxValue);
                BitConverter.GetBytes(pcm32).CopyTo(buffer, byteOffset);
                break;
            default:
                throw new NotSupportedException(
                    $"Formato de áudio não suportado: {format.BitsPerSample} bits, encoding {format.Encoding}.");
        }
    }

    private static int Read24BitPcm(byte[] buffer, int byteOffset)
    {
        var value = buffer[byteOffset] | (buffer[byteOffset + 1] << 8) | (buffer[byteOffset + 2] << 16);
        if ((value & 0x800000) != 0)
        {
            value = unchecked((int)(value | 0xFF000000));
        }

        return value;
    }

    private static void Write24BitPcm(byte[] buffer, int byteOffset, float value)
    {
        var clamped = (int)Math.Clamp(value * 8388608f, -8388608, 8388607);
        buffer[byteOffset] = (byte)(clamped & 0xFF);
        buffer[byteOffset + 1] = (byte)((clamped >> 8) & 0xFF);
        buffer[byteOffset + 2] = (byte)((clamped >> 16) & 0xFF);
    }
}
