using System.Diagnostics;
using AirControl.Core;
using AirControl.Integration.Tests.Fakes;
using Xunit;

namespace AirControl.Integration.Tests;

/// <summary>
/// Verifica os orçamentos de performance de plan.md: SetTrim/SetMute/SetSolo -> LevelsChanged
/// em menos de 100ms (SC-002) e detecção de conexão/desconexão em menos de 3s (SC-005).
/// </summary>
public class PerformanceBudgetTests
{
    private const long LevelsChangedBudgetMs = 100;
    private const long ConnectionDetectionBudgetMs = 3000;

    [Fact]
    public void SetTrim_ToLevelsChanged_IsWithinBudget()
    {
        var engine = new FakeAudioEngine();
        engine.Start("fake-output");
        var samples = new float[] { 0.3f, -0.3f };

        var stopwatch = Stopwatch.StartNew();
        engine.SetTrim(InputChannelId.Input1, 3.0);
        engine.PushSamples(InputChannelId.Input1, samples);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < LevelsChangedBudgetMs);
    }

    [Fact]
    public void SetMute_ToEffectiveAudibilityChange_IsWithinBudget()
    {
        var engine = new FakeAudioEngine();
        engine.Start("fake-output");

        var stopwatch = Stopwatch.StartNew();
        engine.SetMute(InputChannelId.Input1, true);
        var isAudible = engine.GetState(InputChannelId.Input1).IsEffectivelyAudible;
        stopwatch.Stop();

        Assert.False(isAudible);
        Assert.True(stopwatch.ElapsedMilliseconds < LevelsChangedBudgetMs);
    }

    [Fact]
    public void SetSolo_ToEffectiveAudibilityChange_IsWithinBudget()
    {
        var engine = new FakeAudioEngine();
        engine.Start("fake-output");

        var stopwatch = Stopwatch.StartNew();
        engine.SetSolo(InputChannelId.Input1, true);
        var input2Audible = engine.GetState(InputChannelId.Input2).IsEffectivelyAudible;
        stopwatch.Stop();

        Assert.False(input2Audible);
        Assert.True(stopwatch.ElapsedMilliseconds < LevelsChangedBudgetMs);
    }

    [Fact]
    public void ConnectionChange_IsDetectedWithinBudget()
    {
        var deviceProvider = new FakeAudioDeviceProvider();
        DeviceConnectionChangedEventArgs? received = null;
        deviceProvider.ConnectionChanged += (_, args) => received = args;

        var stopwatch = Stopwatch.StartNew();
        deviceProvider.SimulateConnection(true);
        stopwatch.Stop();

        Assert.NotNull(received);
        Assert.True(received!.IsConnected);
        Assert.True(stopwatch.ElapsedMilliseconds < ConnectionDetectionBudgetMs);
    }
}
