namespace AirControl.Core;

public interface ISettingsRepository
{
    ChannelSettingsProfile Load();
    void Save(ChannelSettingsProfile profile);
}
