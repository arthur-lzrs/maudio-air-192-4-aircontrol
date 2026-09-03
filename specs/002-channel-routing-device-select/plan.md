# Implementation Plan: Channel Routing & Device Selection

**Branch**: `002-channel-routing-device-select` | **Date**: 2026-09-02 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-channel-routing-device-select/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Adiciona ao AIR Control um seletor de modo de roteamento (Stereo, Input 1 Mono, Input 2 Mono,
Combined Mono com compensação de -6dB) aplicado em tempo real entre o estágio de trim/mute/solo já
existente e a saída/meters, e um seletor de dispositivo de entrada que substitui a busca fixa por
"AIR 192" por uma lista de dispositivos de captura Windows detectados, com auto-seleção do
M-Audio AIR quando presente e fallback explícito para seleção manual. Ambas as escolhas persistem
entre sessões no mesmo arquivo JSON já usado para trim/mute/solo, com revalidação automática do
modo de roteamento sempre que o dispositivo ativo muda ou é reconectado.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (LTS) — mesmo runtime da feature 001

**Primary Dependencies**: WPF (UI), NAudio (captura/reprodução WASAPI, enumeração de dispositivos
via `MMDeviceEnumerator`), System.Text.Json (persistência de configurações) — sem novas dependências
externas

**Storage**: Mesmo arquivo JSON local em `%AppData%\AirControl\channel-settings.json`, estendido
com `RoutingMode` e `InputDeviceId` (seleção manual, pode ser nulo)

**Testing**: xUnit — testes unitários para a lógica pura de roteamento (mapeamento de canais,
soma com compensação de -6dB, validação de modo vs. contagem de canais, fallback) em
`AirControl.Core.Tests`; testes de integração contra `IAudioEngine`/`IAudioDeviceProvider`
reais/fakes para troca de dispositivo, persistência e o orçamento de 100ms em
`AirControl.Integration.Tests`, seguindo o padrão de `PerformanceBudgetTests.cs`

**Target Platform**: Windows 10/11 desktop (x64)

**Project Type**: Desktop app (single project, mesma estrutura de 3 assemblies da feature 001)

**Performance Goals**: Troca de modo de roteamento ou de dispositivo reflete em áudio/meters em
até 100ms (SC-002), consistente com o orçamento já estabelecido para trim/mute/solo em
`PerformanceBudgetTests.cs`; sem glitch/pop audível maior que o já tolerado para mudanças de
controle na feature 001

**Constraints**: Roteamento é aplicado por cima do trim/mute/solo existente, não o substitui
(FR-006); modos de roteamento que exigem mais canais do que o dispositivo ativo expõe devem ser
ocultados/desabilitados (FR-005); troca de dispositivo ativo deve revalidar o modo de roteamento
selecionado (edge case em spec.md); apenas um dispositivo de entrada ativo por vez, sem uso
simultâneo de múltiplos dispositivos (mesma limitação da feature 001)

**Scale/Scope**: Um único usuário; N dispositivos de captura Windows detectáveis (tipicamente 1-3);
4 modos de roteamento fixos definidos nesta iteração

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Code Quality**: A lógica de roteamento (mapeamento de canais + soma com compensação) fica
  isolada em um tipo puro em `AirControl.Core` (sem dependência de NAudio/WPF), seguindo o padrão já
  usado por `TrimCalculator`/`ChannelToggleTracker`; `IAudioDeviceProvider` ganha um método para
  enumerar dispositivos de entrada, mantendo responsabilidade única (enumeração) separada da
  aplicação do roteamento (`IAudioEngine`). PASS.
- **II. Testing Standards**: A lógica de mapeamento/soma/validação de canais é 100% testável sem
  hardware (unit tests); a troca de dispositivo ativo e a revalidação de modo são cobertas por
  testes de integração com fakes, seguindo o padrão de `DeviceConnectionIntegrationTests.cs` e
  `TrimIntegrationTests.cs`. PASS.
- **III. User Experience Consistency**: O seletor de modo de roteamento e o seletor de dispositivo
  de entrada reutilizam os padrões visuais já estabelecidos pelo `OutputDeviceSelectorView`
  existente (mesmo tipo de combo/lista, mesmos `AutomationProperties`); golden path (troca de modo
  com sinal ativo) e edge cases (dispositivo com 1 canal, dispositivo desconectado, nenhum M-Audio
  presente) são cobertos no quickstart.md. PASS.
- **IV. Performance Requirements**: Orçamento de 100ms definido para troca de modo/dispositivo
  (SC-002), reaproveitando a mesma infraestrutura de medição de `PerformanceBudgetTests.cs`. PASS.

Nenhuma violação identificada; Complexity Tracking não é necessário.

## Project Structure

### Documentation (this feature)

```text
specs/002-channel-routing-device-select/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   └── audio-engine-contract.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── AirControl.App/                      # WPF UI
│   ├── Views/
│   │   ├── RoutingModeSelectorView.xaml       # NEW — seletor de modo de roteamento
│   │   └── InputDeviceSelectorView.xaml       # NEW — seletor de dispositivo de entrada
│   ├── ViewModels/
│   │   ├── RoutingModeSelectorViewModel.cs    # NEW
│   │   ├── InputDeviceSelectorViewModel.cs    # NEW
│   │   └── MainWindowViewModel.cs             # MODIFIED — compõe os novos view-models
│   └── App.xaml / App.xaml.cs
├── AirControl.Audio/                    # Implementação real (NAudio/WASAPI)
│   ├── AudioEngine.cs                   # MODIFIED — Start(inputDeviceId, outputDeviceId),
│   │                                     #   aplica RoutingMode no pipeline de amostras
│   └── AudioDeviceProvider.cs           # MODIFIED — GetAvailableInputDevices(), detecção de
│                                         #   M-Audio AIR entre múltiplos dispositivos
└── AirControl.Core/                     # Contratos + lógica de domínio pura
    ├── RoutingMode.cs                   # NEW — enum + RoutingModeApplier (mapeamento/soma)
    ├── AudioInputDeviceInfo.cs          # NEW — id, nome, contagem de canais, é-M-Audio
    ├── IAudioDeviceProvider.cs          # MODIFIED — + GetAvailableInputDevices()
    ├── IAudioEngine.cs                  # MODIFIED — Start(inputDeviceId, outputDeviceId),
    │                                     #   SetRoutingMode/GetRoutingMode
    └── ChannelSettings.cs               # MODIFIED — ChannelSettingsProfile + RoutingMode,
                                          #   InputDeviceId

tests/
├── AirControl.Core.Tests/
│   └── RoutingModeTests.cs              # NEW — mapeamento por modo, soma com -6dB, validação
│                                         #   de canais, fallback de modo
└── AirControl.Integration.Tests/
    ├── RoutingIntegrationTests.cs       # NEW — troca de modo -> LevelsChanged dentro do orçamento
    ├── DeviceSelectionIntegrationTests.cs # NEW — auto-detecção M-Audio, seleção manual,
    │                                     #   persistência, fallback em desconexão
    └── PerformanceBudgetTests.cs        # MODIFIED — + orçamento para troca de modo/dispositivo
```

**Structure Decision**: Mantém a mesma divisão em três assemblies da feature 001
(`AirControl.Core` para domínio puro e testável, `AirControl.Audio` para I/O real via NAudio,
`AirControl.App` para WPF). Roteamento e seleção de dispositivo são extensões dos contratos e
implementações existentes (`IAudioEngine`, `IAudioDeviceProvider`, `ChannelSettingsProfile`), não
novos assemblies — o escopo é pequeno o suficiente para não justificar uma nova fronteira de
projeto.

## Complexity Tracking

*Nenhuma violação da constituição identificada — seção não aplicável.*
