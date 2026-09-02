# Contract: Camada de Áudio (Audio Engine)

Este projeto é um aplicativo desktop, não expõe API HTTP. O "contrato" relevante aqui é a
interface interna entre a UI (WPF) e a camada de áudio, que deve ser abstraída para permitir
testes automatizados sem hardware físico (ver research.md §6).

## IAudioDeviceProvider

Responsável por detectar o AIR 192|4 e enumerar dispositivos de saída.

```csharp
public interface IAudioDeviceProvider
{
    // Dispara quando o AIR 192|4 é conectado ou desconectado.
    event EventHandler<DeviceConnectionChangedEventArgs> ConnectionChanged;

    bool IsAirDeviceConnected { get; }

    IReadOnlyList<AudioOutputDeviceInfo> GetAvailableOutputDevices();
}

public record DeviceConnectionChangedEventArgs(bool IsConnected, string? DeviceId);
public record AudioOutputDeviceInfo(string Id, string FriendlyName, bool IsDefault);
```

**Pré-condições**: nenhuma. **Pós-condições**: `ConnectionChanged` é disparado dentro de 3s
(SC-005) após uma mudança física de conexão.

## IAudioEngine

Responsável por capturar os dois inputs, aplicar trim/mute/solo, calcular peak/RMS, e reproduzir
no dispositivo de saída selecionado.

```csharp
public interface IAudioEngine
{
    void Start(string outputDeviceId);
    void Stop();

    // Disparado a cada buffer processado (esperado a cada poucos ms, suficiente para SC-002).
    event EventHandler<ChannelLevelsChangedEventArgs> LevelsChanged;

    void SetTrim(InputChannelId channel, double trimDb);       // clamp [-12, +12]
    void SetMute(InputChannelId channel, bool isMuted);
    void SetSolo(InputChannelId channel, bool isSoloed);

    ChannelState GetState(InputChannelId channel);
}

public enum InputChannelId { Input1, Input2 }

public record ChannelLevelsChangedEventArgs(
    InputChannelId Channel, double PeakDb, double RmsDb, bool IsClipping);

public record ChannelState(
    double TrimDb, bool IsMuted, bool IsSoloed, bool IsEffectivelyAudible);
```

**Pré-condições**: `Start` só deve ser chamado quando `IAudioDeviceProvider.IsAirDeviceConnected`
é `true`.

**Pós-condições**:
- `SetTrim` fora do range [-12, +12] é clampeado, nunca lança exceção (FR-006).
- `SetSolo(X, true)` quando outro canal já está soloed resulta em `AllSoloed` (FR-012): ambos
  `IsEffectivelyAudible` passam a refletir apenas o próprio `IsMuted`.
- `SetSolo(X, false)` quando `X` era o único soloed restaura `IsMuted` de todos os canais ao valor
  anterior ao início do solo (FR-011).
- Mudança em `TrimDb`, `IsMuted` ou `IsSoloed` reflete em `LevelsChanged`/`IsEffectivelyAudible`
  dentro de 100ms (SC-002).

## ISettingsRepository

Persistência do `ChannelSettingsProfile` (ver data-model.md).

```csharp
public interface ISettingsRepository
{
    ChannelSettingsProfile Load();     // retorna defaults se arquivo ausente/corrompido
    void Save(ChannelSettingsProfile profile);
}

public record ChannelSettingsProfile(
    ChannelSettings Input1,
    ChannelSettings Input2,
    string? OutputDeviceId);

public record ChannelSettings(double TrimDb, bool IsMuted, bool IsSoloed);
```

**Pós-condições**: `Load()` após um `Save(profile)` anterior retorna um `ChannelSettingsProfile`
igual (SC-004 — restauração 100% confiável).

## Single-instance contract

```csharp
public interface ISingleInstanceGuard
{
    // true se esta é a única instância; false se já existe outra (e a existente foi focada).
    bool TryAcquire();
}
```

**Pós-condições**: se `TryAcquire()` retorna `false`, a instância existente é trazida ao foco
(FR-017) e o processo atual deve encerrar sem inicializar `IAudioEngine`.
