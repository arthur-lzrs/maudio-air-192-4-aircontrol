namespace AirControl.Core;

/// <summary>
/// Resolução pura das opções de modo de roteamento a partir da contagem de canais do dispositivo
/// ativo, com um estado explícito de "não determinável" (data-model.md §3).
/// </summary>
/// <remarks>
/// Corrige S1 (research.md §1): antes, o seletor filtrava os modos por
/// <c>IAudioEngine.ActiveInputChannelCount</c> e, quando esse valor era <c>0</c> (Start que falhou
/// silenciosamente, janela transitória de reconexão, ou a perturbação ASIO documentada em R3),
/// TODOS os modos eram filtrados e o combobox ficava vazio sem nenhuma explicação. A regra passa a
/// ser explícita: lista vazia SÓ existe acompanhada de <see cref="IsDeterminable"/> falso e de uma
/// <see cref="Message"/> acionável (FR-002/FR-003).
/// </remarks>
public sealed record RoutingOptionsState(
    IReadOnlyList<RoutingMode> AvailableModes,
    bool IsDeterminable,
    string? Message)
{
    /// <summary>Mensagem acionável exibida quando os canais do dispositivo não são determináveis (FR-003).</summary>
    public const string IndeterminableMessage =
        "Não foi possível determinar os canais do dispositivo de entrada. "
        + "Reconecte o AIR 192|4 ou use \"Reiniciar conexão\" para repopular os modos de roteamento.";

    public static RoutingOptionsState Resolve(int activeInputChannelCount)
    {
        if (activeInputChannelCount <= 0)
        {
            return new RoutingOptionsState(Array.Empty<RoutingMode>(), IsDeterminable: false, IndeterminableMessage);
        }

        var modes = Enum.GetValues<RoutingMode>()
            .Where(mode => RoutingModeApplier.IsSupported(mode, activeInputChannelCount))
            .ToList();

        return new RoutingOptionsState(modes, IsDeterminable: true, Message: null);
    }
}
