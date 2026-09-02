# Implementation Plan: Input Monitoring & Control Panel for AIR 192|4

**Branch**: `001-air-192-4-input-control` | **Date**: 2026-09-01 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-air-192-4-input-control/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

App desktop para Windows que se conecta à interface M-AUDIO AIR 192|4, exibe meters de nível
(peak + RMS) para os dois inputs em tempo real, e oferece trim digital (-12 dB a +12 dB), mute e
solo por canal, com reprodução audível do sinal processado (playthrough) em um dispositivo de
saída Windows selecionável. Configurações de trim/mute/solo persistem entre sessões. Abordagem
técnica: aplicativo WPF em C#/.NET 8, usando WASAPI (via NAudio) para captura/reprodução e Core
Audio notifications para detecção de conexão/desconexão, com a camada de áudio abstraída por
interfaces para permitir testes automatizados sem o hardware físico.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (LTS)

**Primary Dependencies**: WPF (UI), NAudio (captura/reprodução WASAPI, cálculo de peak/RMS),
System.Text.Json (persistência de configurações)

**Storage**: Arquivo JSON local em `%AppData%\AirControl\channel-settings.json` (sem banco de
dados — volume de dados é 2 canais × 3 propriedades + 1 device id de saída)

**Testing**: xUnit para testes unitários (lógica de dB/trim, resolução de conflito mute/solo,
cálculo de peak/RMS) e testes de integração contra `IAudioEngine`/`IAudioDeviceProvider` reais
alternados com fakes (ver contracts/audio-engine-contract.md), permitindo CI sem hardware físico

**Target Platform**: Windows 10/11 desktop (x64)

**Project Type**: Desktop app (single project)

**Performance Goals**: Resposta visível do meter e efeito audível de trim/mute/solo em até 100ms
após o ajuste (SC-002); detecção de desconexão/reconexão do dispositivo em até 3s (SC-005)

**Constraints**: Sem alterar o preamp analógico do hardware (trim é ganho digital pós-captura,
FR-007); não precisa alterar o sinal que outras apps recebem do dispositivo nesta iteração
(FR-018); deve suportar apenas uma instância ativa por vez (FR-017); acessibilidade completa via
teclado e leitor de tela é obrigatória (FR-020)

**Scale/Scope**: Um único usuário, um único dispositivo AIR 192|4 com 2 inputs fixos; sem suporte
a múltiplos dispositivos simultâneos nesta feature

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Code Quality**: A camada de áudio será isolada atrás de interfaces (`IAudioEngine`,
  `IAudioDeviceProvider`, `ISettingsRepository`, ver contracts/) com responsabilidade única cada,
  permitindo nomes descritivos e revisão focada. PASS.
- **II. Testing Standards**: Lógica de negócio (clamp de trim, resolução mute/solo incluindo "todos
  soloed", cálculo de peak/RMS e limiar de clipping) é testável unitariamente sem hardware; a
  fronteira UI↔camada de áudio é coberta por testes de integração com um fake de
  `IAudioEngine`/`IAudioDeviceProvider` (research.md §6). PASS.
- **III. User Experience Consistency**: Todos os controles usam elementos WPF nativos (Slider,
  ToggleButton) com `AutomationProperties` consistentes; golden path e edge cases (dispositivo
  ausente, clipping, mute+solo simultâneos) estão cobertos no quickstart.md. Acessibilidade total
  por teclado e leitor de tela é requisito explícito (FR-020). PASS.
- **IV. Performance Requirements**: Orçamentos de performance definidos e mensuráveis: 100ms para
  resposta de meter/trim/mute/solo (SC-002) e 3s para detecção de conexão (SC-005); testes de
  integração podem medir a latência entre `SetTrim`/`SetMute`/`SetSolo` e o `LevelsChanged`
  correspondente. PASS.

Nenhuma violação identificada; Complexity Tracking não é necessário.

## Project Structure

### Documentation (this feature)

```text
specs/001-air-192-4-input-control/
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
├── AirControl.App/              # WPF UI: janelas, views, view-models, AutomationProperties
│   ├── Views/
│   ├── ViewModels/
│   └── App.xaml / App.xaml.cs
├── AirControl.Audio/            # Implementação real: NAudio, WASAPI, Core Audio notifications
│   ├── AudioEngine.cs
│   ├── AudioDeviceProvider.cs
│   └── SingleInstanceGuard.cs
└── AirControl.Core/             # Contratos + lógica de domínio pura (sem dependência de NAudio/WPF)
    ├── IAudioEngine.cs / IAudioDeviceProvider.cs / ISettingsRepository.cs
    ├── ChannelState.cs / ChannelSettingsProfile.cs
    └── SettingsRepository.cs    # persistência JSON

tests/
├── AirControl.Core.Tests/       # Testes unitários: clamp de trim, mute/solo, peak/RMS, clipping
└── AirControl.Integration.Tests/ # Testes de integração: fake de IAudioEngine/IAudioDeviceProvider,
                                   # medindo latência SetTrim/SetMute/SetSolo -> LevelsChanged
```

**Structure Decision**: Projeto único (desktop app), dividido em três assemblies para manter a
lógica de domínio (`AirControl.Core`) livre de dependência de WPF ou NAudio, tornando-a testável
isoladamente (atende ao gate de Testing Standards). `AirControl.Audio` contém as implementações
concretas de I/O de áudio; `AirControl.App` é a camada de apresentação WPF.

## Complexity Tracking

*Nenhuma violação da constituição identificada — seção não aplicável.*
