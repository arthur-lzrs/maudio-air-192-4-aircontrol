namespace AirControl.Core;

/// <summary>
/// Ponto único de marshalling para a thread da UI (research.md §4 / R2). Os callbacks do
/// <c>IMMNotificationClient</c> (thread COM) e o <c>DataAvailable</c> do NAudio (thread de captura)
/// terminam escrevendo propriedades ligadas ao WPF; sem um ponto de sincronização isso produz
/// exatamente a intermitência que esta feature ataca ("às vezes o campo abre vazio", "às vezes o
/// medidor congela"). A abstração fica em <c>AirControl.Core</c> — sem tipos WPF — para que os
/// testes possam verificar a thread de entrega sem um <c>Dispatcher</c> real.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>True quando a chamada já está na thread da UI (nenhum marshalling é necessário).</summary>
    bool IsOnUiThread { get; }

    /// <summary>Enfileira <paramref name="action"/> na thread da UI sem bloquear o chamador.</summary>
    void Post(Action action);

    /// <summary>Executa <paramref name="action"/> na thread da UI, bloqueando até concluir.</summary>
    void Send(Action action);
}

/// <summary>
/// Dispatcher de passagem direta: executa tudo na thread do chamador. É o default de
/// <see cref="IAudioEngine"/>/provedores quando nenhum dispatcher é injetado (mantém o
/// comportamento anterior das features 001–003 em testes e cenários sem UI).
/// </summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public static readonly ImmediateUiDispatcher Instance = new();

    public bool IsOnUiThread => true;

    public void Post(Action action) => action();

    public void Send(Action action) => action();
}

/// <summary>
/// Dispatcher baseado em <see cref="SynchronizationContext"/> — usável em testes (ou em qualquer
/// host não-WPF) para verificar que os eventos são entregues na thread capturada.
/// </summary>
public sealed class SynchronizationContextUiDispatcher : IUiDispatcher
{
    private readonly SynchronizationContext _context;
    private readonly int _uiThreadId;

    public SynchronizationContextUiDispatcher(SynchronizationContext context, int uiThreadId)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _uiThreadId = uiThreadId;
    }

    /// <summary>Captura o <see cref="SynchronizationContext"/> e a thread atuais como "a thread da UI".</summary>
    public static SynchronizationContextUiDispatcher Capture() => new(
        SynchronizationContext.Current
            ?? throw new InvalidOperationException("Nenhum SynchronizationContext ativo para capturar."),
        Environment.CurrentManagedThreadId);

    public bool IsOnUiThread => Environment.CurrentManagedThreadId == _uiThreadId;

    public void Post(Action action)
    {
        if (IsOnUiThread)
        {
            action();
            return;
        }

        _context.Post(_ => action(), null);
    }

    public void Send(Action action)
    {
        if (IsOnUiThread)
        {
            action();
            return;
        }

        _context.Send(_ => action(), null);
    }
}
