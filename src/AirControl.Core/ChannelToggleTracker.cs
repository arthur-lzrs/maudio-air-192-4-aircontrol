namespace AirControl.Core;

/// <summary>
/// Estado mutável de mute/solo por canal com a máquina de estados de solo (data-model.md):
/// snapshot de IsMuted ao entrar em solo, "todos soloed" equivale a nenhum soloed, e
/// restauração do snapshot ao sair do único solo ativo.
/// </summary>
public class ChannelToggleTracker
{
    private readonly Dictionary<InputChannelId, bool> _isMuted;
    private readonly Dictionary<InputChannelId, bool> _isSoloed;
    private readonly Dictionary<InputChannelId, bool> _preSoloMuteState;

    public ChannelToggleTracker(IEnumerable<InputChannelId> channels)
    {
        var channelList = channels.ToList();
        _isMuted = channelList.ToDictionary(c => c, _ => false);
        _isSoloed = channelList.ToDictionary(c => c, _ => false);
        _preSoloMuteState = channelList.ToDictionary(c => c, _ => false);
    }

    public bool IsMuted(InputChannelId channel) => _isMuted[channel];

    public bool IsSoloed(InputChannelId channel) => _isSoloed[channel];

    public void SetMute(InputChannelId channel, bool isMuted) => _isMuted[channel] = isMuted;

    public void SetSolo(InputChannelId channel, bool isSoloed)
    {
        var wasAnySoloed = _isSoloed.Values.Any(v => v);
        if (isSoloed && !wasAnySoloed)
        {
            foreach (var key in _isMuted.Keys.ToList())
            {
                _preSoloMuteState[key] = _isMuted[key];
            }
        }

        _isSoloed[channel] = isSoloed;

        var isAnySoloed = _isSoloed.Values.Any(v => v);
        if (!isAnySoloed)
        {
            foreach (var key in _isMuted.Keys.ToList())
            {
                _isMuted[key] = _preSoloMuteState[key];
            }
        }
    }

    public bool IsEffectivelyAudible(InputChannelId channel)
    {
        var snapshot = _isMuted.Keys.ToDictionary(
            key => key,
            key => new ChannelToggleState(_isMuted[key], _isSoloed[key]));

        return EffectiveAudibilityResolver.Resolve(channel, snapshot);
    }
}
