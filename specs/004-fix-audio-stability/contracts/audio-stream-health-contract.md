# Contract: Audio Stream Health

**Feature**: `004-fix-audio-stability` | **Surface**: `IAudioEngine` (extensão) + `AudioStreamHealth` (`AirControl.Core`)

Contrato que torna a saúde do fluxo de captura/reprodução observável, para que um congelamento
nunca seja silencioso (FR-006, FR-007, FR-009; SC-002).

## Extensão de `IAudioEngine`

```csharp
/// Estado de saúde atual do fluxo de áudio. Delivering antes de qualquer problema.
AudioStreamHealth Health { get; }

/// Disparado quando Health.State muda. Entregue SEMPRE na thread da UI (marshalling na
/// borda de AirControl.Audio) — os assinantes são view-models ligados ao WPF.
event EventHandler<AudioStreamHealthChangedEventArgs>? StreamHealthChanged;
```

## Estados e garantias

| Estado | Significado | Garantia exigida |
|--------|-------------|------------------|
| `Delivering` | Dados chegando; medidores refletem o sinal | `LastDataReceivedAt` atualizado a cada buffer |
| `Stalled` | Sem dados > limiar OU evento de parada do NAudio | Sinalizado em ≤ 5s do último dado (SC-002); dispara recuperação automática limitada |
| `Faulted` | Recuperação automática esgotada | `FaultReason` acionável exposto à UI; terminal até ação do usuário |

## Regras de comportamento

1. `OnDataAvailable` MUST atualizar `LastDataReceivedAt` e, se estava `Stalled`/`Faulted` e os
   dados voltaram, transitar para `Delivering` e zerar `RecoveryAttempts`.
2. O watchdog MUST rodar na thread da UI (`DispatcherTimer`) e MUST NOT fazer polling do driver —
   só compara `agora - LastDataReceivedAt` (FR-015b/SC-004b).
3. Assinar `WasapiCapture.RecordingStopped` e `WasapiOut.PlaybackStopped`: um evento de parada com
   exceção MUST levar a `Stalled` (não engolir).
4. Recuperação automática: no máximo **2** tentativas de `Stop`+`Start`; falhando, `Faulted`
   com mensagem acionável — nunca laço infinito (FR-007).
5. `StreamHealthChanged` MUST ser entregue na thread da UI.
6. A avaliação de staleness MUST ser uma função pura testável (`(now, lastData, threshold) → bool`).

## Contra-exemplos (o que NÃO pode acontecer)

- Medidor mostrando valor congelado de um instante anterior sem que `State` tenha saído de
  `Delivering` (viola FR-006/SC-002).
- Recuperação reiniciando a captura em laço a cada evento de dispositivo irrelevante
  (viola a estabilidade; ver research.md §2 alternativas rejeitadas).
- `Faulted` sem `FaultReason` acionável (viola FR-003/Constitution III).

## Cobertura de teste

- Unit (`AudioStreamHealthTests`): transições `Delivering→Stalled→Faulted→Delivering`; staleness
  pura; teto de tentativas.
- Integração (`StreamHealthIntegrationTests`): parada simulada via fake → `Stalled` → recuperação
  ou `Faulted`; `StreamHealthChanged` entregue na thread esperada; regressão de S2.
