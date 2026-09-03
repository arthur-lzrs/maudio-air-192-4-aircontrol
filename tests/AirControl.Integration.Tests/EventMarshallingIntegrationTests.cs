using AirControl.App.ViewModels;
using AirControl.Core;
using AirControl.Integration.Tests.Fakes;
using Xunit;

namespace AirControl.Integration.Tests;

/// <summary>
/// Regressão de R2/S4/S6 (research.md §4): os callbacks <c>IMMNotificationClient</c> (thread COM) e
/// o <c>DataAvailable</c> do NAudio (thread de captura) terminavam escrevendo propriedades ligadas
/// ao WPF sem marshalling. Aqui a "thread da UI" é a <see cref="UiThreadHarness"/> e os eventos são
/// levantados de threads de trabalho — exatamente como no app real.
/// </summary>
public class EventMarshallingIntegrationTests
{
    [Fact]
    public void LevelsChanged_RaisedFromWorkerThread_IsDeliveredOnTheUiThread()
    {
        using var ui = new UiThreadHarness();
        var engine = new FakeAudioEngine(ui.Dispatcher);
        var deliveryThreadIds = new List<int>();
        engine.LevelsChanged += (_, _) => deliveryThreadIds.Add(Environment.CurrentManagedThreadId);

        UiThreadHarness.RunOnWorkerThread(() => engine.PushRoutedSamples(new float[] { 0.5f }, new float[] { 0.5f }));
        ui.Drain();

        Assert.NotEmpty(deliveryThreadIds);
        Assert.All(deliveryThreadIds, id => Assert.Equal(ui.UiThreadId, id));
    }

    [Fact]
    public void ConnectionChanged_RaisedFromWorkerThread_IsDeliveredOnTheUiThread()
    {
        using var ui = new UiThreadHarness();
        var deviceProvider = new FakeAudioDeviceProvider { UiDispatcher = ui.Dispatcher };
        var deliveryThreadIds = new List<int>();
        deviceProvider.ConnectionChanged += (_, _) => deliveryThreadIds.Add(Environment.CurrentManagedThreadId);

        UiThreadHarness.RunOnWorkerThread(() => deviceProvider.SimulateConnection(true));
        ui.Drain();

        Assert.Single(deliveryThreadIds);
        Assert.Equal(ui.UiThreadId, deliveryThreadIds[0]);
    }

    [Fact]
    public void InputDevicesChanged_RaisedFromWorkerThread_IsDeliveredOnTheUiThread()
    {
        using var ui = new UiThreadHarness();
        var deviceProvider = new FakeAudioDeviceProvider { UiDispatcher = ui.Dispatcher };
        var deliveryThreadIds = new List<int>();
        deviceProvider.InputDevicesChanged += (_, _) => deliveryThreadIds.Add(Environment.CurrentManagedThreadId);

        UiThreadHarness.RunOnWorkerThread(() => deviceProvider.SimulateInputDevicesChanged());
        ui.Drain();

        Assert.Single(deliveryThreadIds);
        Assert.Equal(ui.UiThreadId, deliveryThreadIds[0]);
    }

    /// <summary>
    /// O consumidor real: um <see cref="ChannelMeterViewModel"/> só pode ter suas propriedades
    /// observáveis escritas na thread da UI, mesmo com os níveis vindo da thread de captura.
    /// </summary>
    [Fact]
    public void ChannelMeterViewModel_UpdatesOnlyOnTheUiThread()
    {
        using var ui = new UiThreadHarness();
        var engine = new FakeAudioEngine(ui.Dispatcher);
        var deviceProvider = new FakeAudioDeviceProvider { UiDispatcher = ui.Dispatcher };

        ChannelMeterViewModel meter = null!;
        ui.RunOnUiThread(() => meter = new ChannelMeterViewModel(InputChannelId.Input1, engine, deviceProvider));

        var propertyChangeThreadIds = new List<int>();
        meter.PropertyChanged += (_, _) => propertyChangeThreadIds.Add(Environment.CurrentManagedThreadId);

        UiThreadHarness.RunOnWorkerThread(() => engine.PushRoutedSamples(new float[] { 0.8f }, new float[] { 0.8f }));
        ui.Drain();

        Assert.NotEmpty(propertyChangeThreadIds);
        Assert.All(propertyChangeThreadIds, id => Assert.Equal(ui.UiThreadId, id));
        Assert.True(meter.PeakDb > LevelMetering.SilenceFloorDb);
    }

    /// <summary>
    /// O dispatcher não pode reenfileirar quando já se está na thread da UI (senão a resolução
    /// síncrona do fim do construtor do MainWindowViewModel deixaria de ser síncrona — S6).
    /// </summary>
    [Fact]
    public void Dispatcher_WhenAlreadyOnUiThread_RunsInline()
    {
        using var ui = new UiThreadHarness();
        var ranInline = false;

        ui.RunOnUiThread(() =>
        {
            Assert.True(ui.Dispatcher.IsOnUiThread);
            ui.Dispatcher.Post(() => ranInline = true);
            Assert.True(ranInline);
        });
    }

    [Fact]
    public void ImmediateUiDispatcher_KeepsPreviousSynchronousBehaviour()
    {
        var executed = false;

        ImmediateUiDispatcher.Instance.Post(() => executed = true);

        Assert.True(executed);
        Assert.True(ImmediateUiDispatcher.Instance.IsOnUiThread);
    }
}
