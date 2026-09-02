namespace AirControl.Core;

public record ChannelToggleState(bool IsMuted, bool IsSoloed);

public static class EffectiveAudibilityResolver
{
    /// <summary>
    /// Resolve se um canal deve estar audível dado o estado de mute/solo de todos os canais.
    /// Nenhum ou todos soloed -> depende só do próprio mute. Solo parcial -> só os soloed são audíveis.
    /// </summary>
    public static bool Resolve(InputChannelId channel, IReadOnlyDictionary<InputChannelId, ChannelToggleState> channels)
    {
        var soloedCount = channels.Values.Count(c => c.IsSoloed);
        var noSoloOrAllSoloed = soloedCount == 0 || soloedCount == channels.Count;

        return noSoloOrAllSoloed
            ? !channels[channel].IsMuted
            : channels[channel].IsSoloed;
    }
}
