# Implementation Plan: Audio Stability & Consistency Fixes

**Branch**: `004-fix-audio-stability` | **Date**: 2026-09-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/004-fix-audio-stability/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Esta feature ataca a intermitência da base atual (WPF/.NET 8 + NAudio/WASAPI, três assemblies
`Core`/`Audio`/`App`) em vez de adicionar funcionalidade. A investigação (research.md, que serve
de documento da User Story 4) identifica três causas-raiz de temporização/ordem que explicam os
sintomas relatados:

1. **Callbacks fora da thread da UI** — os avisos do `IMMNotificationClient`
   (`AudioDeviceProvider`) e o `LevelsChanged` (`AudioEngine.OnDataAvailable`) chegam em threads
   COM/de captura e escrevem propriedades ligadas ao WPF sem marshalling para o `Dispatcher`. É a
   assinatura clássica de bug intermitente e o candidato mais forte para "campo de roteamento
   vazio" e "medidor congela sem motivo".
2. **Sem detecção de parada do fluxo** — não há watchdog nem assinatura de
   `WasapiCapture.RecordingStopped`/`WasapiOut.PlaybackStopped`. Se a captura para (suspensão,
   perda para modo exclusivo, driver reiniciado, dispositivo removido), os medidores congelam com
   o último valor e nada reage (viola FR-006/FR-007/FR-009).
3. **Seletor de roteamento sem estado de fallback** — `RoutingModeSelectorViewModel.RefreshAvailableModes`
   filtra por `ActiveInputChannelCount`; quando esse valor é `0` (Start falhou silenciosamente,
   janela transitória de reconexão, ou a perturbação ASIO já documentada no código), **todos** os
   modos são filtrados e o combobox fica vazio, sem mensagem acionável (viola FR-002/FR-003).

A correção introduz três primitivas de domínio, todas testáveis sem hardware em `AirControl.Core`,
com a I/O real isolada em `AirControl.Audio`:

- **Marshalling de eventos para a UI** — um ponto único de sincronização para eventos de
  dispositivo e de nível, eliminando a corrida cross-thread.
- **Saúde do fluxo de áudio** (`AudioStreamHealth`) — timestamp de "último dado recebido" +
  watchdog + assinatura dos eventos de parada do NAudio, com recuperação automática limitada ou
  estado de erro acionável (FR-007/FR-009, SC-002).
- **Pausa de reconfiguração** (`ReconfigurationPause`) — unifica os três pontos hoje ad-hoc que
  fazem `Stop → mutar dispositivo → Start` (`RecordingFormatSelectorViewModel.OnSelectedFormatChanged`,
  `DriverSettingsViewModel.OnSelectedSampleRateChanged`, e a consulta ASIO para filtrar formatos)
  em uma operação única disparada só por evento discreto, com estado transitório visível, teto de
  duração (SC-004a) e restabelecimento garantido da captura em todos os caminhos (FR-015a–d).

A User Story 3 passa a consultar o sample rate do driver em tempo real **dentro** de uma pausa de
reconfiguração (a captura é parada, o driver consultado, a captura retomada), em vez da consulta
ASIO com a captura ainda ativa que existe hoje. A User Story 5 (revisão de tecnologia) é entregue
primeiro como documento revisável (research.md §7) e, uma vez aprovada, implementada **depois** que
as correções P1 estiverem verdes (FR-020b), com medição antes/depois por item (FR-020c/d).

## Technical Context

**Language/Version**: C# 12 / .NET 8 (LTS) — mesmo runtime das features 001/002/003.

**Primary Dependencies**: WPF + CommunityToolkit.Mvvm (UI), NAudio (WASAPI capture/playback,
`MMDeviceEnumerator`, `WasapiCapture`/`WasapiOut`, `IMMNotificationClient`; host ASIO gerenciado
via `NAudio.Wave.Asio.AsioDriver` para leitura/escrita de sample rate), interop COM direta
isolada (`IPropertyStore`/`PKEY_AudioEngine_DeviceFormat`) para o "Formato Padrão" do Windows.
**Nenhuma dependência NuGet nova é adotada como pré-requisito das correções P1.** Uma eventual
troca da tecnologia de captura (US5) só entra depois de aprovada e só se a revisão a recomendar;
não é pressuposta por este plano.

**Storage**: Sem mudança de schema. Persistência existente permanece:
`%AppData%\AirControl\channel-settings.json` (`ChannelSettingsProfile`) e
`%AppData%\AirControl\recording-format.json` (`RecordingFormat` por `deviceId`). Nenhuma migração
de dados do usuário é necessária (Assumptions da spec).

**Testing**: xUnit. Lógica pura nova (`AudioStreamHealth`, política da `ReconfigurationPause`,
fallback de opções de roteamento) coberta por testes unitários em `AirControl.Core.Tests` sem
hardware. Comportamento de integração (watchdog dispara recuperação; pausa restabelece a captura
dentro do teto; roteamento nunca fica vazio sem mensagem; marshalling na thread correta) coberto
em `AirControl.Integration.Tests` com os fakes existentes estendidos (`FakeAudioEngine`,
`FakeAudioDeviceProvider`, `FakeAsioSampleRateController`). Cada causa-raiz corrigida ganha um
teste de regressão que falha sem a correção (FR-019, SC-006). Validação que exige hardware real
(congelamentos intermitentes não 100% reproduzíveis) fica documentada em quickstart.md, no mesmo
padrão de `RealHardwarePerformanceBudgetTests.cs`.

**Target Platform**: Windows 10/11 desktop (x64).

**Project Type**: Desktop app — mesma divisão em três assemblies (`AirControl.Core` domínio puro,
`AirControl.Audio` I/O real, `AirControl.App` WPF). Nenhum assembly novo é criado pelas correções
P1; um assembly novo só seria considerado se a US5 aprovar uma tecnologia de captura que exija
isolar uma dependência licenciada.

**Performance Goals**:
- Recuperação após qualquer alteração de configuração feita pelo usuário: monitoração operacional
  em ≤ 3s (SC-003), reaproveitando o teto de reconexão de 3s já em `PerformanceBudgetTests.cs`.
- Pausa de reconfiguração: ≤ 2s do início ao restabelecimento da captura, com indicador visível
  durante toda a pausa (SC-004a). Valor inicial a revalidar com hardware real (Assumptions da spec).
- Detecção de congelamento: watchdog sinaliza fluxo parado em ≤ 5s (SC-002).
- Orçamento de 100ms para trim/mute/solo/roteamento refletirem em `LevelsChanged` permanece
  intocado (não pode regredir — FR-021/SC-007).

**Constraints**:
- Nenhuma correção pode alterar o caminho audível nem a fonte de dados dos medidores
  (par pré-gate/pré-roteamento) estabelecida na feature 003 (FR-010/FR-021).
- A pausa de reconfiguração MUST ser disparada apenas por evento discreto (ação do usuário ou
  mudança real de dispositivo); o app MUST NOT consultar o driver por polling (FR-015b, SC-004b).
- A implementação da US5 MUST ocorrer só depois das correções P1 verdes (FR-020b), para manter a
  origem de qualquer regressão atribuível.
- Recuperação e watchdog não podem introduzir laço de reinício infinito: recuperação automática é
  limitada por tentativas antes de cair para o estado de erro acionável.

**Scale/Scope**: Um usuário; um dispositivo M-Audio AIR 192|4 ativo por vez; hardware de
referência único para validação (Assumptions da spec).

### Unknowns / NEEDS CLARIFICATION

As duas maiores incógnitas de escopo foram resolvidas na sessão de clarificação da spec (US5
entrega documento **e** implementação; a consulta de sample rate é em tempo real dentro de uma
janela segura). As incógnitas remanescentes são de parametrização e ficam resolvidas em research.md
com valores default e um plano de revalidação por medição — nenhuma bloqueia o início do Phase 1:

- Limiar de "fluxo parado" do watchdog e número de tentativas de recuperação automática antes do
  estado de erro → **resolvido em research.md §2** (default: 5s de silêncio de dados / 2 tentativas).
- Teto de duração da pausa de reconfiguração → **resolvido em research.md §3** (2s, SC-004a, a
  revalidar com hardware).
- Estratégia de troca de tecnologia de captura (US5) → **deliberadamente em aberto**; é a saída da
  revisão em research.md §7, submetida à aprovação antes de qualquer implementação (FR-020a).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Code Quality**: As três primitivas (`AudioStreamHealth`, política de `ReconfigurationPause`,
  fallback de opções de roteamento) são lógica pura em `AirControl.Core`, sem NAudio/COM, seguindo
  o padrão de `RoutingModeApplier`/`ChannelToggleTracker`. O marshalling para o `Dispatcher` e a
  assinatura de `RecordingStopped`/`PlaybackStopped` ficam em `AirControl.Audio`/`AirControl.App`,
  atrás dos contratos existentes — nenhum tipo de NAudio/threading vaza para `Core`. A unificação
  dos três `Stop→mutar→Start` ad-hoc em uma primitiva **remove** duplicação em vez de adicioná-la.
  Investigação documentada evita "correção que move o problema". PASS.
- **II. Testing Standards**: Cada causa-raiz corrigida ganha um teste de regressão que falha sem a
  correção (FR-019/SC-006): (a) roteamento vazio com `ActiveInputChannelCount == 0` → mensagem
  acionável, não lista vazia; (b) watchdog detecta fluxo parado e dispara recuperação/erro;
  (c) pausa de reconfiguração restabelece a captura mesmo quando a mutação falha; (d) eventos de
  dispositivo/nível são entregues na thread da UI. A suíte existente das features 001/002/003
  permanece verde (FR-021/SC-007) — nenhuma mudança altera contratos públicos existentes. O que
  depende de hardware real fica em quickstart.md. PASS.
- **III. User Experience Consistency**: Todo estado transitório (pausa de reconfiguração) e todo
  estado de erro (fluxo parado, roteamento não populável, formato não determinável) reutiliza o
  padrão de mensagem acionável já em uso (`"Falha ao iniciar monitoração: {ex.Message}"`,
  `StatusMessage`); nenhuma lista vazia sem explicação (FR-003). Um estado de "reconfigurando…"
  visível impede confundir pausa deliberada com congelamento (FR-015c). Golden path + estados de
  erro/transitório validados em quickstart.md. PASS.
- **IV. Performance Requirements**: Orçamentos definidos e medidos — 3s de recuperação
  (reaproveitado), 2s de teto de pausa (SC-004a), 5s de detecção de congelamento (SC-002), 100ms
  de trim/mute/solo (não pode regredir). Cada mudança de tecnologia aprovada na US5 tem medição
  antes/depois obrigatória (FR-020c) e é revertida se não entregar (FR-020d). Benchmarks
  re-executáveis em `PerformanceBudgetTests.cs`/`RealHardwarePerformanceBudgetTests.cs`. PASS.

Nenhuma violação identificada; Complexity Tracking não é necessário.

## Project Structure

### Documentation (this feature)

```text
specs/004-fix-audio-stability/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output — investigação (US4) + revisão de tecnologia (US5)
├── data-model.md        # Phase 1 output — entidades de estabilidade
├── quickstart.md        # Phase 1 output — guia de validação (inclui validação com hardware real)
├── contracts/           # Phase 1 output
│   ├── audio-stream-health-contract.md
│   └── reconfiguration-pause-contract.md
└── tasks.md             # Phase 2 output (/speckit-tasks — NÃO criado por /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── AirControl.App/                              # WPF UI
│   └── ViewModels/
│       ├── RoutingModeSelectorViewModel.cs      # MODIFIED — estado de fallback/mensagem acionável
│       │                                        #   quando ActiveInputChannelCount == 0 (FR-002/003)
│       ├── RecordingFormatSelectorViewModel.cs  # MODIFIED — consulta ASIO dentro da pausa de
│       │                                        #   reconfiguração; sem query com captura ativa (US3)
│       ├── DriverSettingsViewModel.cs           # MODIFIED — Stop→mutar→Start via ReconfigurationPause
│       ├── MainWindowViewModel.cs               # MODIFIED — orquestra saúde do fluxo + estados de erro
│       └── ChannelMeterViewModel.cs             # MODIFIED (se necessário) — consumo de LevelsChanged
│                                                #   marshalado; nunca segura valor congelado
├── AirControl.Audio/                            # Implementação real (NAudio/WASAPI/COM/ASIO)
│   ├── AudioEngine.cs                           # MODIFIED — assina RecordingStopped/PlaybackStopped,
│   │                                            #   registra "último dado recebido", expõe saúde do
│   │                                            #   fluxo; marshalling de LevelsChanged
│   └── AudioDeviceProvider.cs                   # MODIFIED — marshalling dos callbacks IMMNotificationClient
│                                                #   para a thread da UI (elimina corrida cross-thread)
└── AirControl.Core/                             # Contratos + lógica de domínio pura
    ├── AudioStreamHealth.cs                     # NEW — estado de saúde (entregando/parado/erro) +
    │                                            #   política de staleness/recuperação (pura)
    ├── ReconfigurationPause.cs                  # NEW — política pura da pausa (teto, gatilho por
    │                                            #   evento discreto, resultado sucesso/erro)
    ├── IAudioEngine.cs                          # MODIFIED — expõe estado de saúde + evento de
    │                                            #   mudança de saúde (StreamHealthChanged)
    └── RoutingOptions.cs                        # NEW (se necessário) — resolução pura de "opções
                                                 #   disponíveis vs. estado não-determinável"

tests/
├── AirControl.Core.Tests/
│   ├── AudioStreamHealthTests.cs                # NEW — transições entregando/parado/erro, staleness
│   ├── ReconfigurationPauseTests.cs             # NEW — teto excedido → erro; restauração garantida
│   └── RoutingOptionsTests.cs                   # NEW — canais 0/1/2 → opções vs. estado acionável
└── AirControl.Integration.Tests/
    ├── StartupDeterminismIntegrationTests.cs    # NEW — 20 startups → roteamento nunca vazio (SC-001)
    ├── StreamHealthIntegrationTests.cs          # NEW — parada detectada → recuperação/erro (SC-002)
    ├── ReconfigurationPauseIntegrationTests.cs  # NEW — troca de formato/sample rate restabelece
    │                                            #   captura ≤ teto; sem pausa sem ação (SC-004a/b)
    ├── EventMarshallingIntegrationTests.cs      # NEW — eventos de dispositivo/nível na thread da UI
    ├── MeteringIntegrationTests.cs              # (existente) — não pode regredir (FR-010/FR-021)
    └── PerformanceBudgetTests.cs                # MODIFIED — orçamento da pausa (2s) e recuperação (3s)
```

**Structure Decision**: Mantém a divisão em três assemblies das features anteriores. As correções
são extensões/ajustes dos contratos e implementações existentes — nenhuma introduz assembly novo.
A regra de ouro é que toda decisão de estabilidade que possa ser expressa como regra (staleness do
fluxo, teto/gatilho da pausa, opções de roteamento vs. estado não-determinável) vira lógica pura e
testável em `AirControl.Core`, deixando `AirControl.Audio`/`AirControl.App` apenas com a
integração de NAudio/threading. Um eventual assembly novo (`AirControl.Asio`/similar) só surge se a
User Story 5 aprovar uma troca de tecnologia de captura que exija isolar dependência licenciada —
fora do escopo deste plano até a aprovação (FR-020a/b).

## Complexity Tracking

*Nenhuma violação da constituição identificada — seção não aplicável.*
