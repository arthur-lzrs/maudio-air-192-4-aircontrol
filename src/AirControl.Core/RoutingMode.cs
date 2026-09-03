namespace AirControl.Core;

public enum RoutingMode
{
    Stereo,
    Input1Mono,
    Input2Mono,
    CombinedMono,
}

/// <summary>
/// Lógica pura de mapeamento e validação de roteamento entre os dois canais de entrada e os dois
/// canais de saída (Left/Right), sem dependência de NAudio/WPF (data-model.md, research.md §1-§2/§6).
/// </summary>
public static class RoutingModeApplier
{
    public static (float Left, float Right) Apply(RoutingMode mode, float input1, float input2) => mode switch
    {
        RoutingMode.Stereo => (input1, input2),
        RoutingMode.Input1Mono => (input1, input1),
        RoutingMode.Input2Mono => (input2, input2),
        RoutingMode.CombinedMono => CombineMono(input1, input2),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private static (float Left, float Right) CombineMono(float input1, float input2)
    {
        var combined = (input1 + input2) * 0.5f;
        return (combined, combined);
    }

    public static bool IsSupported(RoutingMode mode, int channelCount) => mode switch
    {
        RoutingMode.Input1Mono => channelCount >= 1,
        RoutingMode.Stereo or RoutingMode.Input2Mono or RoutingMode.CombinedMono => channelCount >= 2,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public static RoutingMode ResolveFallback(RoutingMode requested, int channelCount)
    {
        if (IsSupported(requested, channelCount))
        {
            return requested;
        }

        return channelCount == 1 ? RoutingMode.Input1Mono : RoutingMode.Stereo;
    }
}
