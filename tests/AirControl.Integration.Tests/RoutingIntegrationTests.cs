using System.Diagnostics;
using AirControl.Core;
using AirControl.Integration.Tests.Fakes;
using Xunit;

namespace AirControl.Integration.Tests;

/// <summary>
/// Verifica que os modos de roteamento afetam o monitoramento audível e os meters de forma
/// consistente (FR-002), dentro do orçamento de 100ms (SC-002), e que trim/mute/solo continuam
/// se aplicando antes do roteamento (FR-006).
/// </summary>
public class RoutingIntegrationTests
{
    private const long LevelsChangedBudgetMs = 100;

    /// <summary>
    /// Em Input1Mono, o roteamento duplica o Input 1 para as duas saídas físicas, mas os
    /// meters (research.md §1) continuam refletindo o nível real de cada entrada física — não
    /// devem mostrar o mesmo valor duplicado, o que esconderia o silêncio real do Input 2.
    /// </summary>
    [Fact]
    public void Input1Mono_KeepsMetersReflectingRealPerChannelLevel()
    {
        var engine = new FakeAudioEngine();
        engine.Start(null, "fake-output");
        engine.SetRoutingMode(RoutingMode.Input1Mono);

        var received = new List<ChannelLevelsChangedEventArgs>();
        engine.LevelsChanged += (_, args) => received.Add(args);

        var stopwatch = Stopwatch.StartNew();
        engine.PushRoutedSamples(new float[] { 0.8f }, new float[] { 0f });
        stopwatch.Stop();

        var leftEvent = Assert.Single(received, e => e.Channel == InputChannelId.Input1);
        var rightEvent = Assert.Single(received, e => e.Channel == InputChannelId.Input2);

        Assert.True(leftEvent.PeakDb > LevelMetering.SilenceFloorDb);
        Assert.Equal(LevelMetering.SilenceFloorDb, rightEvent.PeakDb);
        Assert.True(stopwatch.ElapsedMilliseconds < LevelsChangedBudgetMs);
    }

    [Fact]
    public void Input2Mono_KeepsMetersReflectingRealPerChannelLevel()
    {
        var engine = new FakeAudioEngine();
        engine.Start(null, "fake-output");
        engine.SetRoutingMode(RoutingMode.Input2Mono);

        var received = new List<ChannelLevelsChangedEventArgs>();
        engine.LevelsChanged += (_, args) => received.Add(args);

        engine.PushRoutedSamples(new float[] { 0f }, new float[] { 0.8f });

        var leftEvent = Assert.Single(received, e => e.Channel == InputChannelId.Input1);
        var rightEvent = Assert.Single(received, e => e.Channel == InputChannelId.Input2);

        Assert.Equal(LevelMetering.SilenceFloorDb, leftEvent.PeakDb);
        Assert.True(rightEvent.PeakDb > LevelMetering.SilenceFloorDb);
    }

    [Fact]
    public void Stereo_ReportsInput1OnLeftOnlyAndInput2OnRightOnly()
    {
        var engine = new FakeAudioEngine();
        engine.Start(null, "fake-output");
        engine.SetRoutingMode(RoutingMode.Stereo);

        var received = new List<ChannelLevelsChangedEventArgs>();
        engine.LevelsChanged += (_, args) => received.Add(args);

        engine.PushRoutedSamples(new float[] { 0.7f }, new float[] { 0f });

        var leftEvent = Assert.Single(received, e => e.Channel == InputChannelId.Input1);
        var rightEvent = Assert.Single(received, e => e.Channel == InputChannelId.Input2);

        Assert.True(leftEvent.PeakDb > LevelMetering.SilenceFloorDb);
        Assert.Equal(LevelMetering.SilenceFloorDb, rightEvent.PeakDb);
    }

    [Fact]
    public void CombinedMono_ReportsCompensatedSummedLevelEquallyOnBothChannels()
    {
        var engine = new FakeAudioEngine();
        engine.Start(null, "fake-output");
        engine.SetRoutingMode(RoutingMode.CombinedMono);

        var received = new List<ChannelLevelsChangedEventArgs>();
        engine.LevelsChanged += (_, args) => received.Add(args);

        engine.PushRoutedSamples(new float[] { 0.8f }, new float[] { 0.8f });

        var leftEvent = Assert.Single(received, e => e.Channel == InputChannelId.Input1);
        var rightEvent = Assert.Single(received, e => e.Channel == InputChannelId.Input2);

        Assert.Equal(leftEvent.PeakDb, rightEvent.PeakDb, precision: 3);
        Assert.False(leftEvent.IsClipping);
        Assert.False(rightEvent.IsClipping);
    }

    [Fact]
    public void SwitchingBetweenModes_WithActiveSignal_CompletesWithinBudget()
    {
        var engine = new FakeAudioEngine();
        engine.Start(null, "fake-output");
        engine.SetRoutingMode(RoutingMode.Stereo);
        engine.PushRoutedSamples(new float[] { 0.5f }, new float[] { 0.5f });

        var stopwatch = Stopwatch.StartNew();
        engine.SetRoutingMode(RoutingMode.CombinedMono);
        engine.PushRoutedSamples(new float[] { 0.5f }, new float[] { 0.5f });
        stopwatch.Stop();

        Assert.Equal(RoutingMode.CombinedMono, engine.RoutingMode);
        Assert.True(stopwatch.ElapsedMilliseconds < LevelsChangedBudgetMs);
    }

    [Fact]
    public void CombinedMono_WithInput2Muted_CombinedOutputExcludesInput2FromTheSum()
    {
        var mutedEngine = new FakeAudioEngine();
        mutedEngine.Start(null, "fake-output");
        mutedEngine.SetRoutingMode(RoutingMode.CombinedMono);
        mutedEngine.SetMute(InputChannelId.Input2, true);

        var mutedReceived = new List<ChannelLevelsChangedEventArgs>();
        mutedEngine.LevelsChanged += (_, args) => mutedReceived.Add(args);
        mutedEngine.PushRoutedSamples(new float[] { 0.6f }, new float[] { 0.6f });
        var mutedLeftEvent = Assert.Single(mutedReceived, e => e.Channel == InputChannelId.Input1);

        var bothMutedEngine = new FakeAudioEngine();
        bothMutedEngine.Start(null, "fake-output");
        bothMutedEngine.SetRoutingMode(RoutingMode.CombinedMono);
        bothMutedEngine.SetMute(InputChannelId.Input2, true);

        var bothMutedReceived = new List<ChannelLevelsChangedEventArgs>();
        bothMutedEngine.LevelsChanged += (_, args) => bothMutedReceived.Add(args);
        bothMutedEngine.PushRoutedSamples(new float[] { 0.6f }, new float[] { 0.9f });
        var bothMutedLeftEvent = Assert.Single(bothMutedReceived, e => e.Channel == InputChannelId.Input1);

        // Input 2 sendo mutado significa que seu valor não entra mais na soma compensada —
        // mudar o valor de Input 2 (0.6 -> 0.9) enquanto mutado não altera o resultado (FR-006).
        Assert.Equal(mutedLeftEvent.PeakDb, bothMutedLeftEvent.PeakDb, precision: 3);
    }
}
