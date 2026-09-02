---

description: "Task list template for feature implementation"
---

# Tasks: Input Monitoring & Control Panel for AIR 192|4

**Input**: Documentos de design de `/specs/001-air-192-4-input-control/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/audio-engine-contract.md, quickstart.md

**Tests**: A constituição do projeto (Testing Standards) exige testes automatizados para toda
funcionalidade nova — unitários para lógica de negócio e de integração para qualquer coisa que
cruze a fronteira UI↔camada de áudio. As tasks de teste abaixo são obrigatórias, não opcionais.

**Organização**: Tarefas agrupadas por user story (spec.md) para permitir implementação e teste
independentes de cada uma.

## Formato: `[ID] [P?] [Story] Descrição`

- **[P]**: Pode rodar em paralelo (arquivos diferentes, sem dependências pendentes)
- **[Story]**: A qual user story a tarefa pertence (US1, US2, US3, US4)
- Caminhos de arquivo exatos estão incluídos em cada descrição

## Path Conventions

Projeto único (desktop), três assemblies conforme plan.md:

```text
src/AirControl.Core/    # Contratos + lógica de domínio pura
src/AirControl.Audio/   # Implementação real: NAudio, WASAPI, Core Audio notifications
src/AirControl.App/     # WPF UI: janelas, views, view-models
tests/AirControl.Core.Tests/
tests/AirControl.Integration.Tests/
```

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Inicialização do projeto e estrutura básica de solução

- [ ] T001 Criar a estrutura de diretórios e arquivos `.csproj` para `src/AirControl.Core/`,
      `src/AirControl.Audio/`, `src/AirControl.App/`, `tests/AirControl.Core.Tests/` e
      `tests/AirControl.Integration.Tests/` conforme plan.md § Project Structure
- [ ] T002 Criar `AirControl.sln` na raiz do repositório referenciando os 5 projetos e configurar
      as referências entre projetos (`AirControl.App` → `AirControl.Audio` → `AirControl.Core`;
      `AirControl.Core` sem dependência de WPF/NAudio); adicionar pacotes NuGet `NAudio` em
      `AirControl.Audio` e `xunit`/`xunit.runner.visualstudio` nos dois projetos de teste
- [ ] T003 [P] Configurar `.editorconfig`, `<Nullable>enable</Nullable>` e
      `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` em todos os `.csproj` (Constitution:
      Code Quality — zero warnings não justificados)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Infraestrutura central que TODAS as user stories dependem (contratos de
`contracts/audio-engine-contract.md`, persistência, detecção de dispositivo, instância única,
seleção de saída de áudio)

**⚠️ CRITICAL**: Nenhuma user story pode começar antes desta fase estar completa

- [ ] T004 Criar os records/enums de domínio em `src/AirControl.Core/`: `InputChannelId`,
      `ChannelState`, `ChannelLevelsChangedEventArgs`, `DeviceConnectionChangedEventArgs`,
      `AudioOutputDeviceInfo`, `ChannelSettings`, `ChannelSettingsProfile` (ver data-model.md e
      contracts/audio-engine-contract.md)
- [ ] T005 [P] Criar as interfaces `IAudioEngine`, `IAudioDeviceProvider`, `ISettingsRepository`,
      `ISingleInstanceGuard` em `src/AirControl.Core/` exatamente como definidas em
      contracts/audio-engine-contract.md
- [ ] T006 [P] Implementar `SettingsRepository` (JSON via `System.Text.Json`) em
      `src/AirControl.Core/SettingsRepository.cs`, lendo/escrevendo
      `%AppData%\AirControl\channel-settings.json`; retorna `ChannelSettingsProfile` com defaults
      (`TrimDb=0`, `IsMuted=false`, `IsSoloed=false`, `OutputDeviceId=null`) se o arquivo estiver
      ausente ou corrompido (FR-014)
- [ ] T007 [P] Testes unitários de `SettingsRepository` em
      `tests/AirControl.Core.Tests/SettingsRepositoryTests.cs`: defaults quando arquivo ausente,
      defaults quando arquivo corrompido, round-trip `Save`→`Load` retorna profile idêntico
      (SC-004)
- [ ] T008 Implementar `AudioDeviceProvider` em `src/AirControl.Audio/AudioDeviceProvider.cs`
      usando `MMDeviceEnumerator`/`IMMNotificationClient` para detectar conexão/desconexão do AIR
      192|4 (evento `ConnectionChanged` em até 3s, FR-001/FR-015/FR-016/SC-005) e para enumerar
      dispositivos de saída via `GetAvailableOutputDevices()` (FR-019)
- [ ] T009 Implementar `SingleInstanceGuard` em `src/AirControl.Audio/SingleInstanceGuard.cs` com
      `Mutex` nomeado; se já existir instância, focar a janela existente via
      `RegisterWindowMessage`/`PostMessage` e retornar `false` em `TryAcquire()` (FR-017)
- [ ] T010 [P] Criar `FakeAudioEngine` e `FakeAudioDeviceProvider` em
      `tests/AirControl.Integration.Tests/Fakes/` simulando buffers de áudio e eventos de
      conexão/desconexão, para permitir todos os testes de integração das próximas fases sem
      hardware físico (research.md §6)
- [ ] T011 Criar `App.xaml`/`App.xaml.cs` em `src/AirControl.App/` com o composition root
      (instanciando `AudioDeviceProvider`, `SettingsRepository`, `SingleInstanceGuard`) e o fluxo
      de startup: se `SingleInstanceGuard.TryAcquire()` retornar `false`, encerrar o processo sem
      inicializar `IAudioEngine` (FR-017)
- [ ] T012 [P] Criar infraestrutura base de ViewModel (`ObservableObject`/`RelayCommand` ou
      `CommunityToolkit.Mvvm`) em `src/AirControl.App/ViewModels/ViewModelBase.cs`
- [ ] T013 Implementar `OutputDeviceSelectorViewModel` e diálogo/seção de seleção em
      `src/AirControl.App/ViewModels/OutputDeviceSelectorViewModel.cs` e
      `src/AirControl.App/Views/OutputDeviceSelectorView.xaml`: ao iniciar, se
      `ChannelSettingsProfile.OutputDeviceId` for nulo, solicitar ao usuário que escolha um
      dispositivo de saída (via `GetAvailableOutputDevices()`) e salvar a escolha via
      `ISettingsRepository` (FR-019)

**Checkpoint**: Fundação pronta — implementação das user stories pode começar

---

## Phase 3: User Story 1 - Watch real-time input levels (Priority: P1) 🎯 MVP

**Goal**: Exibir meters de peak+RMS em tempo real para os dois inputs do AIR 192|4, distinguindo
silêncio, atividade normal e clipping, e mostrando claramente o status de conexão do dispositivo

**Independent Test**: Conectar o AIR 192|4, alimentar sinal no Input 1 e depois no Input 2, e
confirmar que cada meter se move independentemente e reflete presença/nível/clipping em tempo real

### Tests for User Story 1

> **NOTE: Escrever estes testes PRIMEIRO, garantir que FALHAM antes da implementação**

- [ ] T014 [P] [US1] Testes unitários de cálculo de peak/RMS e do limiar de clipping (0 dBFS) em
      `tests/AirControl.Core.Tests/LevelMeteringTests.cs`
- [ ] T015 [P] [US1] Teste de integração: `LevelsChanged` reflete níveis independentes por canal
      (sinal só no Input 1 não afeta Input 2) usando `FakeAudioEngine` em
      `tests/AirControl.Integration.Tests/MeteringIntegrationTests.cs`
- [ ] T016 [P] [US1] Teste de integração: `ConnectionChanged` de `FakeAudioDeviceProvider`
      dispara estado "não conectado"/"conectado" e o app não exibe atividade falsa quando
      desconectado, em `tests/AirControl.Integration.Tests/DeviceConnectionIntegrationTests.cs`

### Implementation for User Story 1

- [ ] T017 [US1] Implementar captura em `AudioEngine` (`src/AirControl.Audio/AudioEngine.cs`):
      `Start(outputDeviceId)`/`Stop()` capturando o stream estéreo dos 2 inputs via WASAPI
      (NAudio), calculando peak/RMS por buffer e disparando `LevelsChanged` (FR-003, FR-004,
      FR-005a)
- [ ] T018 [US1] Criar `DeviceStatusViewModel` em
      `src/AirControl.App/ViewModels/DeviceStatusViewModel.cs` vinculado a
      `IAudioDeviceProvider.ConnectionChanged`, expondo texto "Conectado"/"Não conectado" com
      `AutomationProperties` (FR-002)
- [ ] T019 [US1] Criar `ChannelMeterViewModel` (uma instância por `InputChannelId`) em
      `src/AirControl.App/ViewModels/ChannelMeterViewModel.cs` expondo `PeakDb`, `RmsDb`,
      `IsClipping` e estado de repouso quando o dispositivo está desconectado (FR-003, FR-004,
      FR-005, FR-015)
- [ ] T020 [US1] Criar `MeterControl` (custom control ou composição de `ProgressBar`) em
      `src/AirControl.App/Views/MeterControl.xaml` exibindo peak e RMS no mesmo meter, com
      indicação visual de clipping distinta e `AutomationProperties.Name`/`LiveSetting=Polite`
      para leitores de tela (FR-005a, FR-020)
- [ ] T021 [US1] Montar `MainWindow` em `src/AirControl.App/Views/MainWindow.xaml` com o status de
      conexão e dois `MeterControl` (Input 1 e Input 2) ligados aos respectivos ViewModels
- [ ] T022 [US1] Conectar o ciclo de vida do `AudioEngine` aos eventos de conexão em
      `src/AirControl.App/App.xaml.cs`/`MainWindowViewModel`: chamar `Start(outputDeviceId)`
      quando o dispositivo conecta (usando o `OutputDeviceId` salvo/selecionado), `Stop()` e
      reset dos meters para o estado de repouso quando desconecta (FR-015, FR-016)

**Checkpoint**: User Story 1 totalmente funcional e testável de forma independente (MVP)

---

## Phase 4: User Story 2 - Adjust each input's trim from the app (Priority: P2)

**Goal**: Fornecer um controle de trim digital por input (-12 dB a +12 dB) que ajusta o nível
monitorado daquele canal sem afetar o outro, com persistência entre sessões

**Independent Test**: Mover o trim do Input 1 para cima e para baixo com sinal constante e
confirmar que só o Input 1 responde; repetir para o Input 2; fechar/reabrir o app e confirmar que
o trim salvo é restaurado

### Tests for User Story 2

> **NOTE: Escrever estes testes PRIMEIRO, garantir que FALHAM antes da implementação**

- [ ] T023 [P] [US2] Testes unitários do clamp de `TrimDb` para o range [-12.0, +12.0] em
      `tests/AirControl.Core.Tests/TrimTests.cs`
- [ ] T024 [P] [US2] Teste de integração: `SetTrim` reflete em `LevelsChanged` dentro de 100ms
      (SC-002) usando `FakeAudioEngine`, em
      `tests/AirControl.Integration.Tests/TrimIntegrationTests.cs`
- [ ] T025 [P] [US2] Teste de integração: `TrimDb` salvo via `ISettingsRepository` é restaurado
      corretamente ao recarregar o profile (SC-004), em
      `tests/AirControl.Integration.Tests/TrimPersistenceIntegrationTests.cs`

### Implementation for User Story 2

- [ ] T026 [US2] Implementar `SetTrim(channel, trimDb)` em
      `src/AirControl.Audio/AudioEngine.cs`, clampeando para [-12, +12] e aplicando ganho digital
      ao buffer já capturado do canal correspondente, sem afetar o outro canal (FR-006, FR-007,
      FR-008)
- [ ] T027 [US2] Criar `TrimControlViewModel` por canal em
      `src/AirControl.App/ViewModels/TrimControlViewModel.cs`, vinculado a um `Slider` de -12 a
      +12 dB, chamando `IAudioEngine.SetTrim` a cada mudança
- [ ] T028 [US2] Adicionar `Slider` de trim por canal em `src/AirControl.App/Views/MainWindow.xaml`
      com `AutomationProperties.Name`/`HelpText` legíveis por leitor de tela (FR-020)
- [ ] T029 [US2] Carregar `ChannelSettingsProfile.Input{1,2}.TrimDb` no startup e aplicar via
      `SetTrim`; salvar o valor (debounced, ex. ao soltar o slider) via `ISettingsRepository` em
      `src/AirControl.App/ViewModels/TrimControlViewModel.cs` (FR-014)

**Checkpoint**: User Stories 1 E 2 funcionam de forma independente

---

## Phase 5: User Story 3 - Mute an individual input (Priority: P3)

**Goal**: Fornecer um controle de Mute por input que silencia a reprodução/monitoramento daquele
canal sem afetar o outro, com estado persistente e sempre visível

**Independent Test**: Alimentar sinal no Input 1, engajar Mute no Input 1 e confirmar que o
Input 1 silencia enquanto o Input 2 (com sinal próprio) permanece inalterado

### Tests for User Story 3

> **NOTE: Escrever estes testes PRIMEIRO, garantir que FALHAM antes da implementação**

- [ ] T030 [P] [US3] Testes unitários da resolução `EffectivelyAudible` no caso sem solo
      (`EffectivelyAudible(ch) = !ch.IsMuted`) em
      `tests/AirControl.Core.Tests/MuteSoloResolutionTests.cs`
- [ ] T031 [P] [US3] Teste de integração: `SetMute` silencia apenas o canal alvo no
      monitoramento/levels, canal não-mutado permanece ativo, usando `FakeAudioEngine` em
      `tests/AirControl.Integration.Tests/MuteIntegrationTests.cs`

### Implementation for User Story 3

- [ ] T032 [US3] Implementar `SetMute(channel, isMuted)` e a resolução de `IsEffectivelyAudible`
      para o caminho sem solo em `src/AirControl.Audio/AudioEngine.cs`, aplicando o silenciamento
      ao playthrough de saída (FR-009, FR-018, FR-019)
- [ ] T033 [US3] Criar `MuteButtonViewModel` por canal em
      `src/AirControl.App/ViewModels/MuteButtonViewModel.cs` vinculado a um `ToggleButton`,
      chamando `IAudioEngine.SetMute`
- [ ] T034 [US3] Adicionar `ToggleButton` de Mute por canal em
      `src/AirControl.App/Views/MainWindow.xaml` com estado visual persistente (não só no
      instante do toggle) e `AutomationProperties`/`LiveSetting` anunciando a mudança (FR-013,
      FR-020)
- [ ] T035 [US3] Persistir `IsMuted` via `ISettingsRepository` a cada toggle e restaurar no
      startup em `src/AirControl.App/ViewModels/MuteButtonViewModel.cs` (FR-014)

**Checkpoint**: User Stories 1, 2 E 3 funcionam de forma independente

---

## Phase 6: User Story 4 - Solo an individual input (Priority: P4)

**Goal**: Fornecer um controle de Solo por input que isola aquele canal no monitoramento,
sobrepondo o mute do canal soloed e silenciando os demais, com resolução bem definida para
"todos soloed" e restauração do estado prévio ao sair do solo

**Independent Test**: Alimentar sinais independentes nos dois inputs, engajar Solo no Input 1 e
confirmar que só o Input 1 permanece ativo (independentemente do mute do Input 2); liberar o solo
e confirmar que ambos retornam ao estado de mute/trim anterior

### Tests for User Story 4

> **NOTE: Escrever estes testes PRIMEIRO, garantir que FALHAM antes da implementação**

- [ ] T036 [P] [US4] Testes unitários da máquina de estados de solo em
      `tests/AirControl.Core.Tests/MuteSoloResolutionTests.cs`: solo único isola o canal
      independentemente do mute (FR-010, solo sobrepõe mute), ambos soloed simultaneamente
      equivale a nenhum soloed (FR-012), e liberar solo restaura o `PreSoloMuteState` de cada
      canal (FR-011)
- [ ] T037 [P] [US4] Teste de integração: engajar Solo no Input 1 silencia o Input 2
      independentemente do seu mute; liberar o solo restaura o mute/trim anterior de ambos os
      canais, usando `FakeAudioEngine`, em
      `tests/AirControl.Integration.Tests/SoloIntegrationTests.cs`

### Implementation for User Story 4

- [ ] T038 [US4] Implementar `SetSolo(channel, isSoloed)` em
      `src/AirControl.Audio/AudioEngine.cs`: snapshot de `IsMuted` de todos os canais ao entrar em
      solo (`PreSoloMuteState`), resolução `AllSoloed` equivalente a nenhum soloed (FR-012), e
      restauração do `PreSoloMuteState` ao desengajar o único canal soloed (FR-011)
- [ ] T039 [US4] Criar `SoloButtonViewModel` por canal em
      `src/AirControl.App/ViewModels/SoloButtonViewModel.cs` vinculado a um `ToggleButton`,
      chamando `IAudioEngine.SetSolo`
- [ ] T040 [US4] Adicionar `ToggleButton` de Solo por canal em
      `src/AirControl.App/Views/MainWindow.xaml` com estado visual persistente e
      `AutomationProperties`/`LiveSetting` anunciando a mudança (FR-013, FR-020)
- [ ] T041 [US4] Persistir `IsSoloed` via `ISettingsRepository` a cada toggle e restaurar no
      startup em `src/AirControl.App/ViewModels/SoloButtonViewModel.cs` (FR-014)

**Checkpoint**: Todas as user stories (US1-US4) funcionam de forma independente

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Melhorias que afetam múltiplas user stories

- [ ] T042 [P] Atualizar `README.md` na raiz do repositório com instruções de setup/build/run
      (espelhando quickstart.md)
- [ ] T043 Passo de limpeza de código: revisar nomes, remover código morto/comentado, garantir
      zero warnings em todos os projetos (Constitution: Code Quality)
- [ ] T044 [P] Testes unitários adicionais para lógica de domínio não coberta (ex.: buffers vazios
      ou silenciosos no cálculo de peak/RMS) em `tests/AirControl.Core.Tests/`
- [ ] T045 Verificar os orçamentos de performance de plan.md: adicionar/rodar teste de integração
      medindo a latência `SetTrim`/`SetMute`/`SetSolo` → `LevelsChanged` (< 100ms, SC-002) e a
      detecção de conexão/desconexão (< 3s, SC-005) em
      `tests/AirControl.Integration.Tests/PerformanceBudgetTests.cs` (Constitution: Performance
      Requirements)
- [ ] T046 [P] Verificar consistência de UX e acessibilidade: navegação completa por teclado em
      todos os controles (meters, trim, mute, solo), contraste adequado nos estados visuais
      (incluindo clipping/mute/solo) e anúncios corretos por leitor de tela (FR-020, Constitution:
      User Experience Consistency)
- [ ] T047 Executar a validação manual completa de quickstart.md (os 14 cenários) com o hardware
      AIR 192|4 real antes de considerar a feature 001 pronta para revisão

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Sem dependências — pode começar imediatamente
- **Foundational (Phase 2)**: Depende da conclusão do Setup — BLOQUEIA todas as user stories
- **User Stories (Phase 3-6)**: Todas dependem da conclusão da fase Foundational
  - Podem prosseguir em paralelo (se houver equipe) ou sequencialmente por prioridade
    (US1 → US2 → US3 → US4)
- **Polish (Phase 7)**: Depende da conclusão de todas as user stories desejadas

### User Story Dependencies

- **US1 (P1)**: Pode começar após a fase Foundational — sem dependência de outras stories
- **US2 (P2)**: Pode começar após a fase Foundational — reutiliza o `AudioEngine`/meters de US1
  mas é testável independentemente (T023-T025 não dependem de US1 estar "completa")
- **US3 (P3)**: Pode começar após a fase Foundational — mesma observação de US2
- **US4 (P4)**: Pode começar após a fase Foundational — depende conceitualmente do `IsMuted`
  existir (US3, `PreSoloMuteState` referencia o mute), mas seus próprios testes/implementação
  usam `FakeAudioEngine`/`ChannelState` e podem ser desenvolvidos em paralelo; a integração final
  em `AudioEngine.cs` reaproveita o campo `IsMuted` já modelado na Fase 2

### Within Each User Story

- Testes MUST ser escritos e FALHAR antes da implementação
- Lógica de `AudioEngine` (Core/Audio) antes dos ViewModels
- ViewModels antes das Views/binding
- História completa antes de avançar para a próxima prioridade

### Parallel Opportunities

- Todas as tasks [P] de Setup podem rodar em paralelo
- Todas as tasks [P] de Foundational podem rodar em paralelo (dentro da Fase 2)
- Após a Foundational, todas as user stories podem começar em paralelo (se houver capacidade)
- Todos os testes [P] de uma user story podem rodar em paralelo entre si
- Diferentes user stories podem ser trabalhadas em paralelo por diferentes desenvolvedores

---

## Parallel Example: User Story 1

```bash
# Lançar os testes de User Story 1 juntos:
Task: "Testes unitários de cálculo de peak/RMS e clipping em tests/AirControl.Core.Tests/LevelMeteringTests.cs"
Task: "Teste de integração LevelsChanged independente por canal em tests/AirControl.Integration.Tests/MeteringIntegrationTests.cs"
Task: "Teste de integração de status de conexão em tests/AirControl.Integration.Tests/DeviceConnectionIntegrationTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 apenas)

1. Completar Phase 1: Setup
2. Completar Phase 2: Foundational (CRÍTICO — bloqueia todas as stories)
3. Completar Phase 3: User Story 1
4. **PARAR e VALIDAR**: testar User Story 1 de forma independente (cenários 1-3 de quickstart.md)
5. Demonstrar/entregar se estiver pronto

### Incremental Delivery

1. Completar Setup + Foundational → Fundação pronta
2. Adicionar US1 → Testar independentemente → Demo (MVP!)
3. Adicionar US2 → Testar independentemente → Demo
4. Adicionar US3 → Testar independentemente → Demo
5. Adicionar US4 → Testar independentemente → Demo
6. Cada story adiciona valor sem quebrar as anteriores

### Parallel Team Strategy

Com múltiplos desenvolvedores:

1. Equipe completa Setup + Foundational junto
2. Após Foundational pronta:
   - Dev A: User Story 1
   - Dev B: User Story 2
   - Dev C: User Story 3
   - Dev D: User Story 4
3. Stories completam e integram de forma independente na camada `AudioEngine`

---

## Notes

- [P] = arquivos diferentes, sem dependências pendentes
- [Story] mapeia a task à user story correspondente para rastreabilidade
- Cada user story deve ser completável e testável de forma independente
- Verificar que os testes falham antes de implementar
- Fazer commit após cada task ou grupo lógico
- Parar em qualquer checkpoint para validar a story isoladamente
- Evitar: tasks vagas, conflitos no mesmo arquivo, dependências entre stories que quebrem a
  independência
