using AirControl.Audio;
using NAudio.Wave;
using Xunit;

namespace AirControl.Integration.Tests;

/// <summary>
/// Regressão para o bug de crosstalk entre canais: assumir 32-bit float quando o mix format real
/// do WASAPI é PCM (16/24/32-bit) desalinha a leitura por amostra e faz um canal "vazar" no
/// outro. Cobre round-trip de escrita/leitura para cada formato suportado.
/// </summary>
public class SampleFormatIOTests
{
    private static readonly WaveFormat Float32Format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    private static readonly WaveFormat Pcm16Format = new(48000, 16, 2);
    private static readonly WaveFormat Pcm24Format = new(48000, 24, 2);
    private static readonly WaveFormat Pcm32Format = new(48000, 32, 2);

    [Theory]
    [MemberData(nameof(FormatsAndTolerances))]
    public void WriteThenRead_RoundTrips_ForEachSupportedFormat(WaveFormat format, double tolerance)
    {
        var buffer = new byte[8];
        const float original = 0.5f;

        SampleFormatIO.WriteSample(buffer, 0, original, format);
        var roundTripped = SampleFormatIO.ReadSample(buffer, 0, format);

        Assert.Equal(original, roundTripped, tolerance);
    }

    public static IEnumerable<object[]> FormatsAndTolerances()
    {
        yield return new object[] { Float32Format, 0.0001 };
        yield return new object[] { Pcm16Format, 0.001 };
        yield return new object[] { Pcm24Format, 0.0001 };
        yield return new object[] { Pcm32Format, 0.0001 };
    }

    [Fact]
    public void ReadSample_DoesNotBleedIntoAdjacentChannel_ForPcm24()
    {
        // Simula um frame estéreo 24-bit: Input1 com sinal, Input2 em silêncio absoluto.
        var buffer = new byte[6];
        SampleFormatIO.WriteSample(buffer, 0, 0.8f, Pcm24Format);
        SampleFormatIO.WriteSample(buffer, 3, 0.0f, Pcm24Format);

        var input1 = SampleFormatIO.ReadSample(buffer, 0, Pcm24Format);
        var input2 = SampleFormatIO.ReadSample(buffer, 3, Pcm24Format);

        Assert.True(input1 > 0.7f);
        Assert.Equal(0.0f, input2);
    }

    [Fact]
    public void ReadSample_UsingWrongStride_WouldMisalignBytes_DemonstratingTheOriginalBug()
    {
        // Regressão do bug real: assumir 4 bytes/amostra (float) num stream PCM 24-bit (3
        // bytes/amostra) desalinha o stride de frame (6 bytes reais vs. 8 assumidos). Ao longo de
        // vários frames essa deriva faz a leitura do "canal 2" vazar bytes do canal 1 (sinal
        // forte), mesmo com o canal 2 fisicamente mudo. Este teste documenta por que o stride
        // deve vir do WaveFormat real, não de uma constante.
        const int frameCount = 8;
        const int correctBytesPerFrame = 6; // PCM 24-bit, 2 canais: 3 bytes cada
        var buffer = new byte[frameCount * correctBytesPerFrame];

        for (var frame = 0; frame < frameCount; frame++)
        {
            var frameOffset = frame * correctBytesPerFrame;
            buffer[frameOffset] = 0xFF; // Input1 (canal 0): sinal forte, todos os bytes não-zero
            buffer[frameOffset + 1] = 0xFF;
            buffer[frameOffset + 2] = 0x7F;
            buffer[frameOffset + 3] = 0x00; // Input2 (canal 1): silêncio absoluto
            buffer[frameOffset + 4] = 0x00;
            buffer[frameOffset + 5] = 0x00;
        }

        const int wronglyAssumedBytesPerSample = 4;
        var misreadAnyNonZero = Enumerable.Range(0, frameCount - 1)
            .Select(frame => frame * 2 * wronglyAssumedBytesPerSample + wronglyAssumedBytesPerSample)
            .Select(wrongChannel2Offset => SampleFormatIO.ReadSample(buffer, wrongChannel2Offset, Pcm24Format))
            .Any(misread => misread != 0f);

        Assert.True(
            misreadAnyNonZero,
            "A leitura com stride errado deveria eventualmente vazar bytes do Input1 (sinal) para o que seria o Input2 (silêncio), demonstrando o bug original.");
    }
}
