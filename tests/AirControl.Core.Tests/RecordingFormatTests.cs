using AirControl.Core;
using Xunit;

namespace AirControl.Core.Tests;

public class RecordingFormatTests
{
    [Fact]
    public void Default_Is48000Hz32Bit()
    {
        Assert.Equal(48000, RecordingFormat.Default.SampleRate);
        Assert.Equal(32, RecordingFormat.Default.BitDepth);
    }

    [Fact]
    public void Format_IsSupported_WhenPresentInSupportedList()
    {
        var supported = new[] { new RecordingFormat(48000, 24), new RecordingFormat(48000, 32) };

        Assert.Contains(new RecordingFormat(48000, 32), supported);
    }

    [Fact]
    public void Format_IsNotSupported_WhenAbsentFromSupportedList()
    {
        var supported = new[] { new RecordingFormat(44100, 16) };

        Assert.DoesNotContain(RecordingFormat.Default, supported);
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        Assert.Equal(new RecordingFormat(48000, 32), new RecordingFormat(48000, 32));
        Assert.NotEqual(new RecordingFormat(48000, 32), new RecordingFormat(48000, 24));
    }
}
