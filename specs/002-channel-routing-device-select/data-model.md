# Data Model: Channel Routing & Device Selection

## RoutingMode (enum, `AirControl.Core`)

Identificador do modo de mapeamento ativo dos dois canais de entrada do dispositivo para os dois
canais de saída/monitoramento (Left/Right).

| Valor          | Left recebe                              | Right recebe                             | Canais de entrada exigidos |
|----------------|-------------------------------------------|--------------------------------------------|-----------------------------|
| `Stereo`       | Input 1                                   | Input 2                                    | 2                           |
| `Input1Mono`   | Input 1                                   | Input 1                                    | 1                           |
| `Input2Mono`   | Input 2                                   | Input 2                                    | 2                           |
| `CombinedMono` | (Input 1 + Input 2) × 0.5 (-6dB)          | (Input 1 + Input 2) × 0.5 (-6dB)           | 2                           |

- Default de primeiro uso (nenhuma preferência persistida ainda): `Stereo` (FR-004).
- É sempre resolvido/validado contra a contagem de canais do dispositivo ativo antes de ser
  aplicado (ver `RoutingModeApplier` abaixo e research.md §6).

## RoutingModeApplier (tipo estático/puro, `AirControl.Core`)

Lógica pura de mapeamento e validação, sem dependência de NAudio/WPF — testável por unidade.

- `(float Left, float Right) Apply(RoutingMode mode, float input1, float input2)`: aplica o
  mapeamento da tabela acima a uma amostra já processada por trim/mute/solo.
- `bool IsSupported(RoutingMode mode, int channelCount)`: verifica se o modo é válido para a
  contagem de canais do dispositivo ativo.
- `RoutingMode ResolveFallback(RoutingMode requested, int channelCount)`: retorna `requested` se
  suportado; caso contrário, `Input1Mono` quando `channelCount == 1`, senão `Stereo`.

## AudioInputDeviceInfo (record, `AirControl.Core`)

Representa um dispositivo de captura Windows detectado, para exibição no seletor de dispositivo e
para validação de roteamento.

| Campo          | Tipo   | Descrição                                                                 |
|----------------|--------|-----------------------------------------------------------------------------|
| `Id`           | string | Identificador estável do endpoint COM (mesmo formato usado por `AudioOutputDeviceInfo.Id`) |
| `FriendlyName` | string | Nome exibido ao usuário (ex.: "M-Audio AIR 192\|4")                        |
| `ChannelCount` | int    | Canais de entrada expostos pelo `MixFormat` do dispositivo                 |
| `IsAirDevice`  | bool   | `true` se `FriendlyName` contém "AIR 192" (case-insensitive)               |

Não persiste sozinho; é produzido em tempo real por `IAudioDeviceProvider.GetAvailableInputDevices()`
a cada abertura do seletor / mudança de conexão.

## ChannelSettingsProfile (record, `AirControl.Core` — extensão)

Estende o registro já existente (feature 001) com dois campos novos, ambos com default seguro
para compatibilidade com arquivos JSON salvos antes desta feature:

| Campo           | Tipo          | Default   | Descrição                                                                 |
|------------------|--------------|-----------|-----------------------------------------------------------------------------|
| `Input1`         | ChannelSettings | (existente) | Sem mudança                                                             |
| `Input2`         | ChannelSettings | (existente) | Sem mudança                                                             |
| `OutputDeviceId` | string?      | (existente) | Sem mudança                                                             |
| `RoutingMode`    | RoutingMode  | `Stereo`  | Modo de roteamento persistido entre sessões (FR-004)                       |
| `InputDeviceId`  | string?      | `null`    | Seleção manual de dispositivo de entrada; `null` = usar auto-detecção do M-Audio AIR (FR-011) |

### Regras de validação / transição

- Ao carregar (`SettingsRepository.Load`), nenhuma validação de canais é feita aqui — o valor é
  usado como "modo pedido" e revalidado contra o dispositivo ativo assim que ele é resolvido no
  `AudioEngine.Start` (ver research.md §6). Isso evita duplicar a lógica de fallback na camada de
  persistência.
- `InputDeviceId` só é atualizado quando o usuário faz uma seleção manual explícita (FR-010); a
  auto-seleção do M-Audio AIR (FR-008) **não** grava `InputDeviceId`, para que continuar
  auto-detectando funcione mesmo que o ID físico do AIR mude entre reconexões.

## AudioEngine — estado em runtime (não persistido)

Adições ao estado interno de `AudioEngine` (implementação `AirControl.Audio`, fora do modelo de
domínio público):

- `RoutingMode` ativo (mutável via `SetRoutingMode`, lido via `GetRoutingMode` — expostos em
  `IAudioEngine`).
- `int ActiveChannelCount`, derivado do `WaveFormat` da captura ativa, usado por quem chama o
  engine para decidir quais modos habilitar no seletor (`RoutingModeApplier.IsSupported`).

## Relações

```text
AudioInputDeviceInfo (N, efêmero) ──selecionado por──> InputDeviceId (ChannelSettingsProfile)
                                                              │
                                                              ▼
                                                        AudioEngine.Start(inputDeviceId, outputDeviceId)
                                                              │
                                                              ▼
                                          ActiveChannelCount ──valida──> RoutingMode (ChannelSettingsProfile)
                                                              │                 │
                                                              ▼                 ▼
                                                  RoutingModeApplier.ResolveFallback (se inválido)
```
