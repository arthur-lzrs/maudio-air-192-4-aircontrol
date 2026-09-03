using System.Collections.Concurrent;
using AirControl.Core;

namespace AirControl.Integration.Tests.Fakes;

/// <summary>
/// Uma "thread da UI" de mentira: uma thread dedicada com um <see cref="SynchronizationContext"/>
/// de fila, para verificar o marshalling de eventos (research.md §4 / R2) sem precisar de um
/// <c>Dispatcher</c> do WPF — o mesmo papel que <see cref="WpfUiDispatcher"/> cumpre no app real.
/// </summary>
public sealed class UiThreadHarness : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();

    public IUiDispatcher Dispatcher { get; private set; } = null!;

    public int UiThreadId { get; private set; }

    public UiThreadHarness()
    {
        _thread = new Thread(Pump) { IsBackground = true, Name = "fake-ui-thread" };
        _thread.Start();
        _ready.Wait(Timeout);
    }

    private void Pump()
    {
        var context = new QueueSynchronizationContext(_queue);
        SynchronizationContext.SetSynchronizationContext(context);
        UiThreadId = Environment.CurrentManagedThreadId;
        Dispatcher = new SynchronizationContextUiDispatcher(context, UiThreadId);
        _ready.Set();

        foreach (var action in _queue.GetConsumingEnumerable())
        {
            action();
        }
    }

    /// <summary>Executa <paramref name="action"/> na thread da UI e espera concluir.</summary>
    public void RunOnUiThread(Action action) => Dispatcher.Send(action);

    /// <summary>Executa <paramref name="action"/> em uma thread de trabalho (simula COM/captura) e espera concluir.</summary>
    public static void RunOnWorkerThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        })
        { IsBackground = true };

        thread.Start();
        thread.Join(Timeout);

        if (error is not null)
        {
            throw error;
        }
    }

    /// <summary>Espera a fila da thread da UI drenar tudo o que já foi enfileirado.</summary>
    public void Drain()
    {
        using var drained = new ManualResetEventSlim();
        _queue.Add(() => drained.Set());
        drained.Wait(Timeout);
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(Timeout);
        _ready.Dispose();
        _queue.Dispose();
    }

    private sealed class QueueSynchronizationContext : SynchronizationContext
    {
        private readonly BlockingCollection<Action> _queue;

        public QueueSynchronizationContext(BlockingCollection<Action> queue) => _queue = queue;

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add(() => d(state));

        public override void Send(SendOrPostCallback d, object? state)
        {
            using var done = new ManualResetEventSlim();
            Exception? error = null;

            _queue.Add(() =>
            {
                try
                {
                    d(state);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            done.Wait(Timeout);

            if (error is not null)
            {
                throw error;
            }
        }
    }
}
