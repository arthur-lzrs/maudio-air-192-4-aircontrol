namespace AirControl.Core;

public record RecordingFormat(int SampleRate, int BitDepth)
{
    public static RecordingFormat Default { get; } = new(SampleRate: 48000, BitDepth: 32);

    public override string ToString() => $"{SampleRate}Hz / {BitDepth}-bit";
}
