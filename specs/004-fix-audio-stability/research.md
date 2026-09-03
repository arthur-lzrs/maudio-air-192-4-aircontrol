# Research & Investigation: Audio Stability & Consistency Fixes

**Feature**: `004-fix-audio-stability` | **Date**: 2026-09-03

Este documento cumpre duas funções da spec: é o **levantamento documentado da inicialização**
(User Story 4 / FR-016–FR-019) e o **scaffold da revisão de tecnologia** (User Story 5 / FR-020,
§7). Cada seção segue o formato Decisão / Rationale / Alternativas consideradas onde há escolha
técnica; as seções de investigação (§0–§1) rastreiam sintoma → causa-raiz → correção → teste.

---

## §0 — Sequência de inicialização atual (FR-016)

> **Verificação T002 (2026-09-03)** — a ordem abaixo foi reconferida linha a linha contra
> `src/AirControl.App/App.xaml.cs` (`OnStartup`) e
> `src/AirControl.App/ViewModels/MainWindowViewModel.cs` (ctor + `OnConnectionChanged` +
> `RefreshDeviceDependentSections`). **Sem drift**: os 6 passos e os 3 pontos de corrida (R1/R2/R3)
> descrevem exatamente o código executado hoje. Detalhes confirmados: (a) o
> `RegisterEndpointNotificationCallback` acontece no ctor de `AudioDeviceProvider`, chamado no
> passo 2, muito antes de `MainWindowViewModel` fiar `ConnectionChanged`/`InputDevicesChanged`
> (linhas 97–98 do ctor) — R1/S6; (b) `AudioEngine.OnDataAvailable` levanta `LevelsChanged`
> diretamente na thread de captura do NAudio e `AudioDeviceProvider.RefreshAirDeviceState` levanta
> `ConnectionChanged`/`InputDevicesChanged` na thread COM, ambos sem marshalling — R2; (c) a última
> instrução do ctor é `OnConnectionChanged(new DeviceConnectionChangedEventArgs(true, null))`,
> síncrona, passo 5/6.

Ordem observada por leitura de `App.OnStartup` → `MainWindowViewModel` (ctor) → resolução de
dispositivo. Cada passo listado com sua pré-condição e comportamento em caso de falha.

| # | Passo | Onde | Pré-condição | Falha hoje |
|---|-------|------|--------------|------------|
| 1 | Guard de instância única | `App.OnStartup` → `SingleInstanceGuard.TryAcquire` | — | Shutdown limpo se já há instância |
| 2 | Cria repos + `AudioDeviceProvider` + `AudioEngine` | `App.OnStartup` | passo 1 | `AudioDeviceProvider` registra `IMMNotificationClient` **antes** da UI existir |
| 3 | Prompt de dispositivo de saída (1ª vez) | `App.OnStartup` | `OutputDeviceId is null` | Shutdown se cancelado |
| 4 | Constrói todos os view-models | `MainWindowViewModel` ctor | passo 2/3 | Fia eventos `BeforeEngineStart`/`ActiveDeviceChanged`/`ConnectionChanged`/`InputDevicesChanged` |
| 5 | Resolve dispositivo de entrada + Start | ctor → `OnConnectionChanged(new(...true, null))` | passo 4 | **Roda no fim do ctor, síncrono** |
| 6 | Aplica modo persistido + popula seções | `OnConnectionChanged` → `ApplyPersistedMode` + `RefreshDeviceDependentSections` | Start resolveu `ActiveInputChannelCount` | Se Start falhou (StartFailure) ou canais==0, roteamento fica vazio |

**Pontos onde a ordem importa (condições de corrida identificadas):**

- **R1 — Notificações de dispositivo antes da UI (passo 2 vs 4/5).** `AudioDeviceProvider` registra
  o callback COM no construtor (`App.OnStartup`), mas os handlers em `MainWindowViewModel` só são
  fiados no passo 4. Um evento de dispositivo que chegue entre o passo 2 e o 4 é perdido; um que
  chegue depois roda em thread COM (ver R2).
- **R2 — Callbacks cross-thread.** `IMMNotificationClient.OnDeviceStateChanged/Added/Removed` e
  `WasapiCapture.DataAvailable` são invocados em threads que **não** são a thread da UI do WPF.
  `AudioDeviceProvider.RefreshAirDeviceState` e `AudioEngine.OnDataAvailable` disparam eventos que
  terminam escrevendo `[ObservableProperty]` ligadas à UI, sem marshalling para o `Dispatcher`.
  É a causa-raiz mais provável dos sintomas intermitentes (campo vazio, medidor congelado).
- **R3 — Start pode resolver `ActiveInputChannelCount == 0`.** Documentado no próprio código
  (`RecordingFormatSelectorViewModel.FilterByAsioSampleRate` remarks): abrir uma sessão ASIO perto
  da negociação WASAPI perturbou a captura e zerou os canais, esvaziando o seletor de roteamento.

---

## §1 — Rastreamento de sintomas → causa-raiz (FR-017/FR-018)

| ID | Sintoma | Relatado? | Causa-raiz identificada | Correção | Teste de regressão |
|----|---------|-----------|--------------------------|----------|--------------------|
| S1 | "Modo de roteamento" abre vazio | Sim | R3 + `RefreshAvailableModes` filtra tudo quando canais==0, sem fallback nem mensagem | Estado acionável quando canais indetermináveis; repopular na recuperação | `RoutingOptionsTests` + `StartupDeterminismIntegrationTests` |
| S2 | Monitoração/medidores congelam | Sim | Sem watchdog; `RecordingStopped`/`PlaybackStopped` não assinados; parada externa não detectada | `AudioStreamHealth` + watchdog + recuperação limitada/erro | `AudioStreamHealthTests` + `StreamHealthIntegrationTests` |
| S3 | Formato de gravação não segue o sample rate do driver de forma segura | Sim | `FilterByAsioSampleRate` consulta ASIO com a captura **ativa**, sem pausa/teto/sinalização | Consulta dentro da `ReconfigurationPause` | `ReconfigurationPauseIntegrationTests` |
| S4 | Intermitência geral após trocar configurações | Sim | R2 (cross-thread) + três `Stop→mutar→Start` ad-hoc sem garantia de restauração | Marshalling + `ReconfigurationPause` unificada | `EventMarshallingIntegrationTests` + `ReconfigurationPauseIntegrationTests` |
| S5 | (não relatado) Start que lança em `OnSelectedFormatChanged`/`OnSelectedSampleRateChanged` deixa engine parada | Encontrado | `_audioEngine.Start` fora de try/finally nesses setters; exceção sobe ao `DispatcherUnhandledException` com a captura já parada | Restauração garantida na `ReconfigurationPause` (finally) | `ReconfigurationPauseTests` + `ReconfigurationPauseIntegrationTests` |
| S6 | (não relatado) Evento de dispositivo perdido entre passo 2 e passo 4 do startup | Encontrado | Registro do callback COM antes de fiar os handlers | Re-resolver o estado do dispositivo após fiar handlers (já há `OnConnectionChanged(...true,null)` no fim do ctor; garantir idempotência) | `StartupDeterminismIntegrationTests` |

Severidade (FR-018): S1/S2/S4 **alta** (bloqueiam uso); S3 **média** (coerência); S5/S6 **média**
(latentes, agravam intermitência).

### Rastreabilidade FR-019/SC-006 — "falha sem a correção", verificada

Cada linha abaixo nomeia o teste concreto e **como** se sabe que ele falha sem a correção
(observado durante a implementação, na ordem tests-first exigida pela constituição).

| ID | Teste nomeado (arquivo → caso) | Falha-sem-a-correção verificada |
|----|--------------------------------|---------------------------------|
| S1 | `RoutingOptionsTests` (todos os casos) e `StartupDeterminismIntegrationTests.TwentyStartups_WithZeroChannelTransient_NeverLeaveRoutingSilentlyEmpty` | **Sim.** Os testes foram escritos antes do código e falharam: `RoutingOptionsState` não existia (CS0103) e o view-model não expunha `IsDeterminable`/`StatusMessage` (CS1061). Com o comportamento antigo (`AvailableModes` filtrado por `IsSupported`), canais==0 produzia lista vazia **sem** mensagem e `SelectedMode` era reescrito para `Stereo` — exatamente o que o caso assere que não pode acontecer. |
| S2 | `AudioStreamHealthTests` (transições/staleness/teto) e `StreamHealthIntegrationTests.SimulatedStall_WithUnrecoverableStream_FaultsAfterBoundedAttempts` / `ChannelMeter_DoesNotHoldAFrozenValueAcrossAStall` | **Sim.** Escritos antes: `AudioStreamHealth`, `IAudioEngine.Health` e `StreamHealthChanged` não existiam. Sem a correção não há nenhum estado observável de parada — o medidor mantém o último valor indefinidamente, que é o assert central de `ChannelMeter_DoesNotHoldAFrozenValueAcrossAStall`. |
| S3 | `ReconfigurationPauseIntegrationTests.AsioQuery_OnlyHappensInsideAPause` e `RecordingFormatIntegrationTests.SyncDisplayOnly_NeverQueriesTheAsioDriverWithCaptureActive` | **Sim.** Com o `FilterByAsioSampleRate` antigo, `SyncDisplayOnly` chamava `GetCurrentSampleRate()` com a captura ativa: `GetCurrentSampleRateCallCount` era > 0 e `wasStoppedDuringQuery` seria false. O teste antigo `SyncDisplayOnly_LimitsAvailableFormats_ToCurrentAsioSampleRate` (que codificava o comportamento errado) falhou na primeira execução após a correção e foi reescrito para o contrato novo. |
| S4 | `EventMarshallingIntegrationTests` (4 casos de thread) + `ReconfigurationPauseIntegrationTests` | **Sim.** Escritos antes de `IUiDispatcher` existir. Sem marshalling, `LevelsChanged`/`ConnectionChanged`/`InputDevicesChanged` são entregues na thread de trabalho que os levantou, e o assert `Assert.Equal(ui.UiThreadId, id)` falha. |
| S5 | `ReconfigurationPauseTests.RunPause_WithThrowingMutation_StillReestablishesCaptureAndFaults` e `ReconfigurationPauseIntegrationTests.FormatChange_WithThrowingWrite_StillLeavesCaptureRunning` / `DriverSampleRateChange_WithThrowingHandshake_StillLeavesCaptureRunning` | **Sim.** No código antigo, `_audioEngine.Start(...)` era a última instrução dos dois setters, **fora** de qualquer `try/finally`: uma exceção em `TrySetFormat`/`TrySetSampleRate` pulava o Start e deixava `engine.IsStarted == false`, que é justamente o assert dos dois casos de integração. |
| S6 | `StartupDeterminismIntegrationTests.SecondNotificationRightAfterStartup_ProducesIdenticalState` e `DeviceArrivingAfterOpen_RepopulatesRoutingOptions` | **Sim.** Sem o refresh do seletor nos caminhos `NeedsSelection`/desconectado, o estado após uma segunda notificação divergia do estado pós-startup (o seletor mantinha os modos da resolução anterior), quebrando a igualdade de snapshots. |

**Nota de não-reprodução (FR-017/SC-005):** os congelamentos intermitentes com hardware real não são
100% reproduzíveis com dispositivos simulados. As causas de ordem/threading (R1/R2/R3, S5/S6) foram
confirmadas por leitura de código e cobertas por testes determinísticos; a confirmação empírica no
AIR 192|4 fica em quickstart.md (V1–V5, tarefa T035, pendente de execução com o hardware). Enquanto
não executada, o plano de monitoramento é o próprio `AudioStreamHealth`: qualquer parada passa a
gerar um estado observável (`Stalled`/`Faulted` com motivo) em vez de silêncio.

---

## §2 — Saúde do fluxo de áudio e watchdog

**Decisão**: Introduzir `AudioStreamHealth` (lógica pura em `Core`) com três estados —
`Delivering`, `Stalled`, `Faulted` — derivados de um timestamp "último dado recebido" atualizado em
`OnDataAvailable`. `AirControl.Audio` liga isso ao mundo real: assina
`WasapiCapture.RecordingStopped` e `WasapiOut.PlaybackStopped`, e roda um watchdog (`DispatcherTimer`
na thread da UI) que marca `Stalled` quando o silêncio de dados excede o limiar. Ao entrar em
`Stalled`, o app tenta **recuperação automática limitada** (Stop+Start, no máximo 2 tentativas); se
não recuperar, entra em `Faulted` com mensagem acionável (FR-007/FR-009).

**Parâmetros default** (a revalidar com hardware — Assumptions da spec):
- Limiar de staleness: **5s** sem dados (casa com SC-002: nunca congelado > 5s sem estado de erro).
- Tentativas de recuperação automática: **2** antes de `Faulted`.
- Backoff entre tentativas: curto (≤ 500ms) para caber no orçamento de 3s de recuperação (SC-003).

**Rationale**: O congelamento silencioso é a pior falha da spec (US2). Um watchdog com timestamp é
a forma mínima e testável de transformar "sem dados" em um estado observável; assinar os eventos de
parada do NAudio captura os casos em que o driver sinaliza o fim explicitamente (suspensão, perda
para modo exclusivo). Limitar as tentativas evita laço de reinício infinito.

**Como ficou implementado (T015–T019)**: `AudioStreamHealth` + `AudioStreamRecoveryPolicy` em
`AirControl.Core` (puros); `AudioEngine` atualiza `_lastDataTicks` com `Interlocked` em
`OnDataAvailable`, assina `RecordingStopped`/`PlaybackStopped` (desassinando antes de qualquer
parada deliberada, para não contar uma pausa como congelamento) e roda o watchdog a cada 1s.
**Desvio deliberado do texto original**: o watchdog é um `System.Threading.Timer` cujo callback
despacha *todo* o trabalho para a thread da UI via `IUiDispatcher` — equivalente funcional ao
`DispatcherTimer` pedido, sem trazer WPF (`WindowsBase`) para `AirControl.Audio`, que é um
assembly sem `UseWPF` (Constitution I). A mutação do estado de saúde acontece exclusivamente na
thread da UI; a thread de captura só escreve o timestamp — assim nenhum lock é compartilhado com a
thread de captura (um lock ali poderia travar `StopRecording`, que espera essa thread sair).

**Alternativas consideradas**:
- *Polling contínuo do dispositivo para "ping"* — rejeitado: violaria FR-015b/SC-004b (nada de
  polling especulativo do driver) e mascararia a causa em vez de detectá-la.
- *Reinício incondicional a cada evento de dispositivo* — rejeitado: causa exatamente a
  instabilidade que a feature combate (reinicia a captura a cada mudança irrelevante).

---

## §3 — Pausa de reconfiguração unificada

**Decisão**: Extrair `ReconfigurationPause` (política pura em `Core`) que descreve uma operação
`Stop → (mutar dispositivo) → Start` com: gatilho apenas por evento discreto, estado transitório
observável (`InProgress`), teto de duração, e resultado `Completed`/`Faulted`. `AirControl.App`/
`AirControl.Audio` executam a mutação real dentro dessa política. Os três pontos ad-hoc de hoje
—`RecordingFormatSelectorViewModel.OnSelectedFormatChanged`,
`DriverSettingsViewModel.OnSelectedSampleRateChanged`, e a consulta ASIO para filtrar formatos—
passam a usar a mesma primitiva. O `Start` de restauração roda em `finally` (corrige S5).

**Parâmetros default**:
- Teto de duração: **2s** (SC-004a). Se a captura não restabelecer dentro do teto → `Faulted` com
  estado de erro acionável (FR-015d), nunca pausa silenciosa.
- Gatilhos permitidos (FR-015b): abrir a lista de formatos, alterar o sample rate do driver, trocar
  o dispositivo ativo, inicializar. Nenhum outro caminho pode disparar a pausa.

**Rationale**: A spec eleva a pausa de reconfiguração a uma entidade de primeira classe, distinta
do congelamento (data-model). Unificar os três pontos remove duplicação (Code Quality), garante a
sinalização visível (FR-015c) e o restabelecimento em todos os caminhos (FR-015a), e dá um único
lugar para impor o teto (FR-015d) e a regra de "só evento discreto" (FR-015b).

**Alternativas consideradas**:
- *Manter as três implementações separadas, só adicionando try/finally* — rejeitado: repete a
  regra de teto/sinalização em três lugares, alto risco de divergência, contraria Code Quality.
- *Consulta ASIO em cache em vez de tempo real* — rejeitado na clarificação da spec: a decisão é
  consulta em tempo real dentro da janela segura.

---

## §4 — Consistência de threading (marshalling para a UI)

**Decisão**: Centralizar a entrega de eventos que cruzam para a UI. `AudioDeviceProvider` marshala
os callbacks `IMMNotificationClient` para o `Dispatcher` antes de levantar
`ConnectionChanged`/`InputDevicesChanged`; `AudioEngine` marshala (ou coalesce) `LevelsChanged` de
forma que os view-models sempre recebam na thread da UI. O ponto de marshalling é injetável (um
`SynchronizationContext`/`Dispatcher` abstrato) para que os testes de integração possam verificar a
thread de entrega sem WPF real.

**Rationale**: R2 é a assinatura da intermitência. Escritas cross-thread em propriedades ligadas ao
WPF produzem falhas não determinísticas (exceções engolidas, atualizações perdidas, estado
inconsistente) — exatamente "às vezes o campo abre vazio", "às vezes o medidor congela". Resolver o
marshalling é pré-requisito para que as demais correções sejam confiáveis.

**Como ficou implementado (T003–T006)**: `IUiDispatcher` (+ `ImmediateUiDispatcher` e
`SynchronizationContextUiDispatcher`) em `AirControl.Core`; `WpfUiDispatcher` em `AirControl.App`,
criado em `App.OnStartup` **antes** do `AudioDeviceProvider` (que registra o callback COM já no
próprio construtor). `AudioDeviceProvider` marshala `ConnectionChanged`/`InputDevicesChanged`;
`AudioEngine` marshala e **coalesce** `LevelsChanged` (só um despacho pendente por vez, carregando
o último valor de cada canal — `DataAvailable` dispara dezenas de vezes por segundo e inundar a
fila do dispatcher criaria um problema novo). O `Post` usa `BeginInvoke` (não bloqueante) de
propósito: bloquear uma thread COM/de captura esperando a UI é um caminho conhecido de deadlock.

**Alternativas consideradas**:
- *`Dispatcher.Invoke` espalhado em cada handler de view-model* — rejeitado: repete a preocupação
  em todo lugar e é fácil esquecer um; melhor um ponto único na borda de `Audio`.

---

## §5 — Determinismo de inicialização

**Decisão**: Tornar a sequência do §0 idempotente e isolada por etapa (FR-005): cada seção
dependente do dispositivo atualiza de forma independente (uma falha não impede a outra — padrão já
iniciado em `RefreshDeviceDependentSections`), e o seletor de roteamento nunca fica vazio sem
mensagem (FR-002/FR-003). Garantir que a resolução do dispositivo no fim do ctor rode **depois** de
todos os handlers fiados (corrige S6) e que uma segunda notificação chegando logo em seguida
produza o mesmo estado final (SC-001: 20 startups idênticos).

**Rationale**: Os sintomas são intermitentes — assinatura de ordem/temporização, não de lógica
isolada. Idempotência + isolamento por etapa é o que transforma "20 startups → 20 estados
possivelmente diferentes" em "20 startups → 1 estado".

---

## §6 — Confirmação/refutação da hipótese de causa (Assumptions da spec)

A spec assume que os sintomas têm causa em ordem/temporização entre inicialização, negociação de
formato e início da captura, e pede para confirmar ou refutar, não assumir. **Status: confirmada em
parte por leitura de código** (R1/R2/R3, S5/S6 são de ordem/threading), **a confirmar
empiricamente** com hardware real na validação (quickstart.md) — os congelamentos intermitentes não
são 100% reproduzíveis com dispositivos simulados (Assumptions). Qualquer sintoma que não reproduza
recebe registro explícito de não-reprodução + plano de monitoramento (FR-017/SC-005).

---

## §7 — Revisão de tecnologia (User Story 5) — scaffold

> Entregue **primeiro** como recomendação revisável; implementação só depois de aprovada
> (FR-020a) e só depois das correções P1 verdes (FR-020b), com medição antes/depois (FR-020c/d).
> Esta seção lista os itens a avaliar; a recomendação com benefício/custo/risco por item é
> preenchida durante a execução (tasks.md) antes de submeter à aprovação do responsável.
>
> **STATUS (2026-09-03): ainda um scaffold, deliberadamente.** As tarefas T029–T032 NÃO foram
> executadas. FR-020a exige uma decisão de aprovação **do responsável** por item, que não pode ser
> tomada automaticamente; a pré-condição FR-020b (US1 e US2 verdes) já está satisfeita, então a
> seção está pronta para ser preenchida e aprovada por um humano quando ele quiser. Nenhuma troca de
> tecnologia foi aplicada ao código.

Itens candidatos identificados na base atual:

1. **Captura orientada a evento (`WasapiCapture` event-driven / exclusive mode)** vs. o modo
   compartilhado atual — benefício potencial: latência menor e formato sob controle do app (menos
   dessincronia ASIO/Windows); custo/risco: modo exclusivo bloqueia outros apps.
2. **Recuperação nativa de dispositivo do NAudio** (`AudioClient`/`IAudioSessionEvents`,
   `IMMNotificationClient` mais granular) — aproveitar sinais que hoje ignoramos
   (`OnDefaultDeviceChanged`, `OnPropertyValueChanged`) para recuperação mais precisa.
3. **Alternativas à camada de captura** (ex.: WASAPI direto via `Vortice`/`CSCore`, ou host ASIO
   completo) — só se a revisão mostrar ganho concreto que a base atual não entrega; prós/contras/
   risco de migração documentados (FR-020).
4. **Configuração subótima atual** — `MediaFoundationResampler` com `ResamplerQuality = 60` e
   `latency: 50` no `WasapiOut`: medir se ajustes reduzem xruns/latência (FR-020, item de
   configuração com "como medir a melhoria").

Cada item aprovado é aplicado e medido; um que não entregue a melhoria ou introduza regressão é
revertido com o motivo registrado aqui (FR-020d).

---

## Resumo de decisões (todas as NEEDS CLARIFICATION resolvidas)

| Tema | Decisão | Seção |
|------|---------|-------|
| Limiar de watchdog / tentativas de recuperação | 5s / 2 tentativas → `Faulted` | §2 |
| Teto da pausa de reconfiguração | 2s (SC-004a), revalidar com hardware | §3 |
| Threading | Marshalling na borda de `Audio` para o `Dispatcher` (injetável p/ teste) | §4 |
| Determinismo de startup | Idempotência + isolamento por etapa | §5 |
| Consulta de sample rate | Tempo real dentro da pausa (clarificação da spec) | §3 |
| Escopo US5 | Documento + implementação aprovada, pós-P1 | §7 |
| Troca de tecnologia de captura | Em aberto; saída da revisão, sujeita a aprovação | §7 |
