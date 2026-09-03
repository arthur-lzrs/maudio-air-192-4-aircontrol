namespace AirControl.Core;

/// <summary>
/// Persiste a preferência de <see cref="RecordingFormat"/> por dispositivo, mesmo padrão de
/// <see cref="ISettingsRepository"/>. Separado por deviceId porque o formato suportado varia
/// por hardware, diferente das demais configurações (globais/por canal).
/// </summary>
public interface IRecordingFormatRepository
{
    RecordingFormat? Load(string deviceId);
    void Save(string deviceId, RecordingFormat format);
}
