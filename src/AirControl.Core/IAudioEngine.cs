namespace AirControl.Core;

public interface IAudioEngine
{
    void Start(string outputDeviceId);
    void Stop();

    event EventHandler<ChannelLevelsChangedEventArgs>? LevelsChanged;

    void SetTrim(InputChannelId channel, double trimDb);
    void SetMute(InputChannelId channel, bool isMuted);
    void SetSolo(InputChannelId channel, bool isSoloed);

    ChannelState GetState(InputChannelId channel);
}
