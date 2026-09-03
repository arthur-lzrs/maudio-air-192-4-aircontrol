# Contract: Roteamento & Seleção de Dispositivo (extensão do contrato da camada de áudio)

Este documento cobre apenas o que muda ou é adicionado em relação ao contrato já estabelecido em
`specs/001-air-192-4-input-control/contracts/audio-engine-contract.md`. As interfaces
`ISettingsRepository`/`ChannelSettings` e o contrato de single-instance não mudam de forma
relevante aqui (exceto o `ChannelSettingsProfile`, coberto abaixo).

## IAudioDeviceProvider (extensão)

```csharp
public interface IAudioDeviceProvider
{
    event EventHandler<DeviceConnectionChangedEventArgs> ConnectionChanged;

    bool IsAirDeviceConnected { get; }

    IReadOnlyList<AudioOutputDeviceInfo> GetAvailableOutputDevices();

    // NOVO: dispositivos de captura Windows ativos, com contagem de canais e flag M-Audio AIR.
    IReadOnlyList<AudioInputDeviceInfo> GetAvailableInputDevices();
}

public record AudioInputDeviceInfo(
    string Id, string FriendlyName, int ChannelCount, bool IsAirDevice);
```

**Pós-condições**: `GetAvailableInputDevices()` reflete o mesmo conjunto de dispositivos ativos
usado para detectar `IsAirDeviceConnected` — se exatamente um item tem `IsAirDevice == true`, é o
candidato a auto-seleção (FR-008); se nenhum, a UI deve solicitar seleção manual (FR-009); se mais
de um, a auto-seleção usa o primeiro enumerado (edge case da spec).

## IAudioEngine (assinatura alterada + extensão)

```csharp
public interface IAudioEngine
{
    // ALTERADO: aceita o dispositivo de entrada a usar. null = auto-detectar M-Audio AIR
    // (mesmo comportamento da feature 001). Lança InvalidOperationException se nem o id
    // informado nem a auto-detecção resolverem um dispositivo ativo (FR-009).
    void Start(string? inputDeviceId, string outputDeviceId);
    void Stop();

    event EventHandler<ChannelLevelsChangedEventArgs> LevelsChanged;

    void SetTrim(InputChannelId channel, double trimDb);
    void SetMute(InputChannelId channel, bool isMuted);
    void SetSolo(InputChannelId channel, bool isSoloed);

    ChannelState GetState(InputChannelId channel);

    bool IsMonitoringEnabled { get; }
    void SetMonitoringEnabled(bool enabled);

    string? CaptureFormatDescription { get; }

    // NOVO: modo de roteamento ativo. SetRoutingMode aplica RoutingModeApplier.ResolveFallback
    // internamente antes de armazenar — nunca fica em um estado inválido para o dispositivo ativo.
    RoutingMode RoutingMode { get; }
    void SetRoutingMode(RoutingMode mode);

    // NOVO: canais de entrada expostos pelo dispositivo ativo (1 ou 2), usado pela UI para
    // decidir quais opções de RoutingMode habilitar. 0 antes do primeiro Start.
    int ActiveInputChannelCount { get; }
}

public enum RoutingMode { Stereo, Input1Mono, Input2Mono, CombinedMono }
```

**Pós-condições (novas/alteradas)**:
- `Start(inputDeviceId, outputDeviceId)` com `inputDeviceId` apontando para um dispositivo
  desconectado/inexistente cai para auto-detecção do M-Audio AIR, igual ao comportamento já
  existente para `outputDeviceId` inválido (`ResolveOutputDevice`, research.md §4).
- Após `Start`, `ActiveInputChannelCount` reflete `WaveFormat.Channels` da captura resolvida.
- `SetRoutingMode(mode)` armazena `RoutingModeApplier.ResolveFallback(mode, ActiveInputChannelCount)`
  — nunca o valor bruto pedido se ele não for suportado (FR-005).
- Trocar de dispositivo ativo (`Stop()` + `Start()` com outro `inputDeviceId`) revalida o
  `RoutingMode` atualmente armazenado contra o novo `ActiveInputChannelCount`, aplicando o mesmo
  fallback (edge case "troca de dispositivo com roteamento incompatível").
- Mudança em `RoutingMode` reflete em `LevelsChanged` e na saída audível dentro de 100ms (SC-002),
  mesmo orçamento já usado para trim/mute/solo.
- `CombinedMono`: o valor de `PeakDb`/`IsClipping` relatado em `LevelsChanged` para ambos os
  canais é calculado sobre `(input1 + input2) * 0.5` (research.md §2) — clipping só é reportado se
  a soma compensada ultrapassar o teto, não pela soma bruta.

## ISettingsRepository / ChannelSettingsProfile (extensão)

```csharp
public record ChannelSettingsProfile(
    ChannelSettings Input1,
    ChannelSettings Input2,
    string? OutputDeviceId,
    RoutingMode RoutingMode,      // NOVO — default Stereo
    string? InputDeviceId);       // NOVO — default null (auto-detectar M-Audio AIR)
```

**Pós-condições**: `Load()` de um arquivo salvo pela feature 001 (sem os campos novos) retorna
`RoutingMode.Stereo` e `InputDeviceId = null`, sem lançar exceção (compatibilidade retroativa via
defaults do record, research.md §5). `Load()` após `Save(profile)` retorna um profile igual,
incluindo os dois campos novos (SC-004).
