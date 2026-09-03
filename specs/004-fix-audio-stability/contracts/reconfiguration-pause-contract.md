# Contract: Reconfiguration Pause

**Feature**: `004-fix-audio-stability` | **Surface**: `ReconfigurationPause` (`AirControl.Core`) + call sites em `AirControl.App`

Contrato que unifica os três pontos `Stop → mutar dispositivo → Start` de hoje
(`RecordingFormatSelectorViewModel.OnSelectedFormatChanged`,
`DriverSettingsViewModel.OnSelectedSampleRateChanged`, consulta ASIO para filtrar formatos) em uma
única operação deliberada, limitada e sinalizada — distinta do congelamento (FR-015a–d; SC-004a/b).

## Forma da operação

```csharp
/// Executa uma mutação de dispositivo dentro de uma janela controlada:
///   Stop captura → (mutação) → Start captura, sempre restabelecendo no finally.
/// Só pode ser chamada em resposta a um gatilho discreto (enum Trigger).
ReconfigurationResult RunPause(
    ReconfigurationTrigger trigger,
    Action mutateDevice,          // a mutação real (TrySetFormat, TrySetSampleRate, GetCurrentSampleRate…)
    TimeSpan deadline);           // teto de duração; default 2s (SC-004a)
```

`ReconfigurationResult = Completed | Faulted(reason)`.
`Phase` (`InProgress`/`Completed`/`Faulted`) é observável para a UI durante toda a operação.

## Gatilhos permitidos (FR-015b)

`OpenFormatList`, `ChangeDriverSampleRate`, `ChangeActiveDevice`, `Startup`. **Nenhum outro.**
Não existe caminho periódico/especulativo que dispare uma pausa (SC-004b: 30 min sem tocar em
formato/driver → zero pausas).

## Regras de comportamento

1. `InProgress` MUST ser sinalizado por um estado transitório visível ("Reconfigurando…") durante
   toda a pausa (FR-015c) — para nunca ser confundido com o congelamento da US2.
2. A captura MUST ser restabelecida em **todos** os caminhos, inclusive quando `mutateDevice`
   lança (bloco `finally` — corrige S5 em research.md §1) (FR-015a).
3. Se a captura não for restabelecida dentro de `deadline`, o resultado MUST ser `Faulted` com
   estado de erro acionável (FR-015d) — nunca pausa silenciosa.
4. A consulta ao sample rate do driver (US3) MUST acontecer **dentro** de uma pausa (captura
   parada), nunca com a captura ativa (substitui o `FilterByAsioSampleRate` atual).
5. Ao final de `Completed`, `AudioStreamHealth` volta a `Delivering` em ≤ 2s (SC-004a) e ≤ 3s após
   qualquer alteração do usuário (SC-003).

## Contra-exemplos

- Trocar formato/sample rate e a captura ficar parada porque `Start` lançou fora de um `finally`
  (o bug S5 de hoje).
- Consultar o ASIO com a captura ativa (o `FilterByAsioSampleRate` de hoje) — perturba a
  negociação WASAPI e zera os canais (R3/S3).
- Pausa disparada por polling ou sem indicador visível (viola FR-015b/c).

## Cobertura de teste

- Unit (`ReconfigurationPauseTests`): `deadline` excedido → `Faulted`; `mutateDevice` que lança →
  captura ainda restabelecida; só gatilhos válidos aceitos.
- Integração (`ReconfigurationPauseIntegrationTests`): troca de formato e de sample rate
  restabelecem a captura dentro do teto (SC-004a); 30 min sem ação → zero pausas (SC-004b);
  regressão de S3/S5.
