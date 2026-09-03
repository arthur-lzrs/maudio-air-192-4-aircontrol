using System.Windows.Threading;
using AirControl.Core;

namespace AirControl.App;

/// <summary>
/// Implementação WPF de <see cref="IUiDispatcher"/> (research.md §4 / R2). Fica em
/// <c>AirControl.App</c> para que nenhum tipo do WPF vaze para <c>AirControl.Core</c>/
/// <c>AirControl.Audio</c> (Constitution I).
/// </summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public bool IsOnUiThread => _dispatcher.CheckAccess();

    /// <summary>
    /// Usa <see cref="Dispatcher.BeginInvoke(Delegate, object?[])"/> (não bloqueante) de propósito:
    /// os chamadores são callbacks COM/de captura e bloquear essas threads esperando a UI é um
    /// caminho conhecido de deadlock.
    /// </summary>
    public void Post(Action action)
    {
        if (IsOnUiThread)
        {
            action();
            return;
        }

        _dispatcher.BeginInvoke(action);
    }

    public void Send(Action action)
    {
        if (IsOnUiThread)
        {
            action();
            return;
        }

        _dispatcher.Invoke(action);
    }
}
