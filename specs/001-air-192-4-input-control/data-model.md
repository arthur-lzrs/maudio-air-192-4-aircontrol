# Data Model: Input Monitoring & Control Panel for AIR 192|4

## Entidades

### Device

Representa a interface M-AUDIO AIR 192|4 como um todo.

| Campo | Tipo | Descrição |
|---|---|---|
| `ConnectionStatus` | enum `{ Disconnected, Connected }` | Estado atual de conexão (FR-002, FR-015, FR-016) |
| `DeviceId` | string | Identificador do dispositivo Core Audio, usado para distinguir o AIR 192|4 de outros dispositivos de áudio |
| `Inputs` | `InputChannel[2]` | Os dois canais de entrada expostos pelo dispositivo |

**Regras**:
- Só existe uma instância de `Device` ativa por processo (FR-017 — instância única).
- Ao transicionar para `Disconnected`, todos os `InputChannel` associados devem exibir nível "sem sinal" e não "silêncio válido" (distinção de UI, não um novo estado de dado).

### InputChannel

Um dos dois inputs físicos do AIR 192|4.

| Campo | Tipo | Descrição |
|---|---|---|
| `Id` | enum `{ Input1, Input2 }` | Identificador do canal |
| `PeakLevelDb` | float (transiente, não persistido) | Nível de pico atual em dBFS, atualizado em tempo real |
| `RmsLevelDb` | float (transiente, não persistido) | Nível médio (RMS) atual em dBFS |
| `IsClipping` | bool (derivado) | `true` quando `PeakLevelDb >= 0` |
| `TrimDb` | float, range **[-12.0, +12.0]** | Ganho digital aplicado ao canal (FR-006, FR-007) |
| `IsMuted` | bool | Estado de mute do canal (FR-009) |
| `IsSoloed` | bool | Estado de solo do canal (FR-010) |
| `PreSoloMuteState` | bool (transiente, não persistido) | Snapshot do `IsMuted` no instante em que QUALQUER canal entrou em solo, usado para restaurar ao sair do solo (FR-011) |

**Regras de validação**:
- `TrimDb` MUST estar sempre dentro de [-12.0, +12.0]; valores fora do range são clampeados na entrada (fader/knob já limita fisicamente esse range).
- `IsClipping` é sempre derivado de `PeakLevelDb >= 0`; nunca definido diretamente pelo usuário.

**Regras de estado (mute/solo)** — máquina de estados de reprodução efetiva (`EffectivelyAudible`):
- Nenhum canal soloed → `EffectivelyAudible(ch) = !ch.IsMuted`.
- Exatamente um canal `X` soloed → `EffectivelyAudible(X) = true`; `EffectivelyAudible(outros) = false`, independentemente do `IsMuted` de cada um (FR-010, FR-012 edge case "ambos os inputs soloed" tratado abaixo).
- Ambos os canais soloed simultaneamente → equivalente a nenhum soloed: `EffectivelyAudible(ch) = !ch.IsMuted` para ambos (FR-012).
- Ao desengajar solo em um canal que estava soloed sozinho: cada canal retorna ao `IsMuted` que tinha antes do solo começar (`PreSoloMuteState`), não a um valor fixo (FR-011).

### ChannelSettingsProfile

Estrutura persistida em disco (JSON), uma por instalação do app.

| Campo | Tipo | Descrição |
|---|---|---|
| `Input1.TrimDb` | float | Trim salvo do Input 1 |
| `Input1.IsMuted` | bool | Mute salvo do Input 1 |
| `Input1.IsSoloed` | bool | Solo salvo do Input 1 |
| `Input2.TrimDb` | float | Trim salvo do Input 2 |
| `Input2.IsMuted` | bool | Mute salvo do Input 2 |
| `Input2.IsSoloed` | bool | Solo salvo do Input 2 |
| `OutputDeviceId` | string | Dispositivo de saída Windows selecionado para monitoramento (FR-019) |

**Persistência** (FR-014, SC-004):
- Gravado a cada mudança de `TrimDb`, `IsMuted`, `IsSoloed` ou `OutputDeviceId` (debounced para trim contínuo, ex.: ao soltar o fader).
- Lido na inicialização do app; se o arquivo não existir ou estiver corrompido, usar valores padrão (`TrimDb=0`, `IsMuted=false`, `IsSoloed=false`, sem output device selecionado — usuário é solicitado a escolher).

### AudioOutputDevice

Representa um dispositivo de saída de áudio do Windows disponível para monitoramento.

| Campo | Tipo | Descrição |
|---|---|---|
| `Id` | string | Identificador Core Audio do dispositivo |
| `FriendlyName` | string | Nome exibido ao usuário no seletor |
| `IsDefault` | bool | Se é o dispositivo de saída padrão do Windows no momento |

## Relacionamentos

```
Device (1) ──has──> (2) InputChannel
InputChannel (persisted subset) ──saved as──> ChannelSettingsProfile
ChannelSettingsProfile (1) ──references──> (1) AudioOutputDevice (by Id)
```

## Transições de estado relevantes

### Conexão do Device
```
Disconnected --(dispositivo detectado)--> Connected
Connected --(dispositivo removido)--> Disconnected
Connected --(reconectado)--> Connected (restaura ChannelSettingsProfile automaticamente)
```

### Solo (por Device, não por canal isolado)
```
NoSolo --(engajar Solo em X)--> SoloActive(X)      [snapshot de IsMuted de todos os canais]
SoloActive(X) --(engajar Solo em Y também)--> AllSoloed  [equivalente a NoSolo, FR-012]
SoloActive(X) --(desengajar Solo em X)--> NoSolo   [restaura IsMuted de todos a partir do snapshot]
AllSoloed --(desengajar Solo em um deles)--> SoloActive(outro)
```
