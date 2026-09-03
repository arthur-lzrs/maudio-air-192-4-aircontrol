# Implementation Plan: Device & Monitoring Audio Controls

**Branch**: `003-device-audio-controls` | **Date**: 2026-09-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-device-audio-controls/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Corrige um bug de metering herdado da feature 001 (meters silenciam junto com mute/solo/
monitoramento desativado, em vez de refletirem o sinal real após trim) trocando a fonte dos
níveis calculados em `AudioEngine` para o par pré-gate/pré-roteamento. Adiciona controle, a
partir do AIR Control, do "Formato Padrão" (sample rate/bit depth) do dispositivo de
gravação M-Audio no Windows — mesma configuração da aba "Avançado" do Painel de Som — via
`IPropertyStore`/`PKEY_AudioEngine_DeviceFormat`, com 48kHz/32-bit como padrão de fábrica e
fallback automático caso uma preferência salva deixe de ser suportada. Investiga e documenta
(FR-007) a viabilidade de controlar sample rate/buffer size do próprio driver ASIO M-Audio;
a conclusão desta iteração é que não há caminho não invasivo sem adotar o SDK ASIO da
Steinberg, então a User Story 3 é entregue como um atalho para abrir o painel do fabricante
(FR-009), não como controle inline (FR-008). Por fim, estreita a faixa de trim por canal de
-12dB…+12dB para -∞(silêncio digital exato)…+10dB, com clamp automático de perfis salvos
fora da nova faixa.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (LTS) — mesmo runtime das features 001/002

**Primary Dependencies**: WPF (UI), NAudio (captura/reprodução WASAPI, enumeração de
dispositivos via `MMDeviceEnumerator`, leitura de `AudioClient`/`IsFormatSupported`),
System.Text.Json (persistência, agora com `JsonNumberHandling.AllowNamedFloatingPointLiterals`
para suportar `TrimDb = -Infinity`) — mais uma pequena camada de interop COM direta
(`IPropertyStore`/`PROPVARIANT`, P/Invoke) isolada em `AirControl.Audio` para gravar
`PKEY_AudioEngine_DeviceFormat` (research.md §5); nenhuma dependência de pacote NuGet nova.
A investigação de FR-007 (research.md §6) conclui que controlar o driver ASIO exigiria o
SDK ASIO da Steinberg (licenciado, não redistribuível livremente) — essa dependência **não**
é adotada nesta iteração; User Story 3 usa apenas `System.Diagnostics.Process` para abrir o
painel M-Audio externo.

**Storage**: Mesmo arquivo JSON em `%AppData%\AirControl\channel-settings.json`, sem novos
campos no `ChannelSettingsProfile` para trim (a faixa muda, o schema não); novo arquivo
`%AppData%\AirControl\recording-format.json` (via `IRecordingFormatController`/nova
implementação de repositório, mesmo padrão de `SettingsRepository`) guardando a preferência
`RecordingFormat { SampleRate, BitDepth }` por `deviceId`.

**Testing**: xUnit — testes unitários em `AirControl.Core.Tests` para: `TrimCalculator` com a
nova faixa (`MinDb = -Infinity`, `MaxDb = 10`, ganho exatamente `0` no mínimo), clamp de
valores salvos fora da faixa, e a lógica pura de validação/fallback de
`RecordingFormat` (formato suportado vs. não suportado, fallback para 48kHz/32-bit); testes
de integração em `AirControl.Integration.Tests` com fakes para: metering independente de
mute/solo/monitoramento (`MeteringIntegrationTests.cs`, estendido), aplicação/persistência/
fallback do formato de gravação, e visibilidade dos novos controles atrelada ao dispositivo
M-Audio ativo — seguindo o padrão de `DeviceConnectionIntegrationTests.cs`/
`TrimIntegrationTests.cs`.

**Target Platform**: Windows 10/11 desktop (x64)

**Project Type**: Desktop app (single project, mesma estrutura de 3 assemblies das features
001/002)

**Performance Goals**: Reaproveita o orçamento de 100ms já estabelecido
(`PerformanceBudgetTests.cs`) para mudanças de trim/mute/solo continuarem refletindo em
`LevelsChanged` dentro do mesmo prazo, mesmo com a nova faixa. Uma troca de formato de
gravação (operação mais pesada, exige parar/reiniciar a captura) usa o orçamento já existente
de 3s para reconexão de dispositivo (`ConnectionDetectionBudgetMs`) como teto para
"monitoramento/metering voltam a funcionar sem reiniciar o app" (FR-010, SC-005).

**Constraints**: A mudança de metering (FR-001/FR-002) não pode alterar o caminho de saída
audível — só a fonte de dados usada para calcular peak/RMS/clipping; controles de formato
Windows (US2) e de driver M-Audio (US3) só ficam visíveis/habilitados quando o dispositivo
ativo do app é o M-Audio (clarificação de sessão, research.md §7), reaproveitando
`AudioInputDeviceInfo.IsAirDevice`/`InputDeviceSelectorViewModel.SelectedDevice` já
existentes; qualquer troca de formato deve ser validada contra o que o dispositivo realmente
suporta antes de aplicar (FR-006); a faixa de trim (-∞…+10dB) é um valor de domínio real em
`AirControl.Core`, não só uma restrição de UI — a UI usa um piso finito só para o slider
(research.md §2).

**Scale/Scope**: Um único usuário; um único dispositivo M-Audio ativo por vez (mesma
limitação das features 001/002); lista fixa de ~7 combinações candidatas de sample
rate/bit depth testadas contra o dispositivo (research.md §5).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Code Quality**: A nova lógica pura (validação/fallback de `RecordingFormat`, nova
  faixa de `TrimCalculator`) fica isolada em `AirControl.Core`, sem dependência de NAudio/COM,
  seguindo o padrão de `RoutingModeApplier`/`ChannelToggleTracker`. A interop COM para
  `IPropertyStore` fica inteiramente dentro de `AirControl.Audio`
  (`WindowsRecordingFormatController`), atrás de `IRecordingFormatController` — nenhum tipo
  de interop vaza para `Core`/`App`. A mudança de metering é uma correção pontual e isolada em
  `AudioEngine.OnDataAvailable` (troca dos argumentos passados a `RaiseLevels`), sem introduzir
  responsabilidade nova nessa classe. PASS.
- **II. Testing Standards**: O bug de metering ganha um teste de regressão que falha sem a
  correção (mute/solo/monitoramento desativado + sinal ativo -> meter deve continuar
  refletindo o sinal) em `MeteringIntegrationTests.cs`. Validação/fallback de `RecordingFormat`
  e a nova faixa de trim são 100% testáveis sem hardware (unit tests). A troca real de
  formato via `IPropertyStore` e o fluxo "abrir painel M-Audio" dependem de hardware/SO real
  e ficam cobertos por testes de integração com fakes (`IRecordingFormatController` fake) mais
  verificação manual documentada em quickstart.md, já que não há como automatizar a leitura do
  Painel de Som do Windows em CI. PASS, com a ressalva documentada acima (mesmo padrão já usado
  por `RealHardwarePerformanceBudgetTests.cs` na feature 001 para o que não pode ser
  automatizado).
- **III. User Experience Consistency**: Os novos controles (seção de formato de gravação,
  seção de driver M-Audio) reutilizam os mesmos padrões visuais/`AutomationProperties` já
  estabelecidos por `InputDeviceSelectorView`/`RoutingModeSelectorView`; mensagens de erro de
  formato inválido seguem o mesmo padrão acionável já usado para falha de captura em
  `MainWindowViewModel` (`"Falha ao iniciar monitoração: {ex.Message}"`); o comportamento de
  esconder/desabilitar controles quando o M-Audio não está ativo é validado no golden path e
  no edge case "trocar para outro dispositivo" no quickstart.md. PASS.
- **IV. Performance Requirements**: Orçamentos reaproveitados de `PerformanceBudgetTests.cs`
  (100ms) e do teto de reconexão (3s) cobrem, respectivamente, trim/mute/solo com a nova faixa
  e a recuperação de monitoramento/metering após troca de formato (FR-010). PASS.

Nenhuma violação identificada; Complexity Tracking não é necessário.

## Project Structure

### Documentation (this feature)

```text
specs/003-device-audio-controls/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   └── recording-format-contract.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── AirControl.App/                          # WPF UI
│   ├── Views/
│   │   ├── RecordingFormatSelectorView.xaml      # NEW — combo de sample rate/bit depth
│   │   └── DriverSettingsView.xaml               # NEW — diagnóstico + botão "Abrir painel M-Audio"
│   ├── ViewModels/
│   │   ├── RecordingFormatSelectorViewModel.cs   # NEW
│   │   ├── DriverSettingsViewModel.cs            # NEW
│   │   ├── TrimControlViewModel.cs               # MODIFIED — piso do slider, clamp no load, snap p/ -∞
│   │   └── MainWindowViewModel.cs                # MODIFIED — compõe os novos view-models,
│   │                                               #   gate por IsAirDevice ativo
│   ├── Converters/
│   │   └── TrimDbToDisplayConverter.cs           # NEW — formata -∞ dB vs. "{0:0.0} dB"
│   └── App.xaml / App.xaml.cs
├── AirControl.Audio/                        # Implementação real (NAudio/WASAPI/COM)
│   ├── AudioEngine.cs                       # MODIFIED — RaiseLevels usa par pré-gate/pré-roteamento;
│   │                                         #   Stop+Start após troca de formato aplicada
│   └── WindowsRecordingFormatController.cs  # NEW — IPropertyStore/PKEY_AudioEngine_DeviceFormat,
│                                             #   IsFormatSupported, P/Invoke isolado aqui
└── AirControl.Core/                         # Contratos + lógica de domínio pura
    ├── TrimCalculator.cs                    # MODIFIED — MinDb = NegativeInfinity, MaxDb = 10.0
    ├── RecordingFormat.cs                   # NEW — record + Default (48000/32), validação pura
    ├── IRecordingFormatController.cs        # NEW — GetCurrentFormat/GetSupportedFormats/TrySetFormat
    └── ISettingsRepository.cs               # MODIFIED (se necessário) — persistência de RecordingFormat

tests/
├── AirControl.Core.Tests/
│   ├── RoutingModeTests.cs                  # (existente, sem mudança)
│   ├── TrimCalculatorTests.cs               # NEW/MODIFIED — nova faixa, ganho zero exato no mínimo
│   └── RecordingFormatTests.cs              # NEW — validação de formato suportado, fallback p/ 48k/32-bit
└── AirControl.Integration.Tests/
    ├── MeteringIntegrationTests.cs          # MODIFIED — regressão: mute/solo/monitoramento off
    │                                         #   não pode zerar o meter
    ├── TrimIntegrationTests.cs              # MODIFIED — nova faixa, clamp de perfil salvo fora da faixa
    ├── RecordingFormatIntegrationTests.cs   # NEW — aplicar/persistir/fallback, visibilidade por
    │                                         #   dispositivo ativo, recuperação sem restart
    └── PerformanceBudgetTests.cs            # MODIFIED — orçamento de troca de formato (3s)
```

**Structure Decision**: Mantém a mesma divisão em três assemblies das features 001/002
(`AirControl.Core` para domínio puro/testável, `AirControl.Audio` para I/O real via
NAudio+COM, `AirControl.App` para WPF). O controle de formato Windows e a correção de
metering são extensões dos contratos/implementações existentes
(`IAudioEngine`/`AudioEngine`, nova `IRecordingFormatController`), não novos assemblies. A
User Story 3 (driver M-Audio) fica deliberadamente pequena nesta iteração — um botão que
inicia um processo externo — refletindo a decisão de research.md §6 de não adotar o SDK
ASIO agora; se essa decisão mudar no futuro, um novo assembly (`AirControl.Asio` ou similar)
seria a estrutura natural para isolar a dependência de licenciamento, mas isso está fora do
escopo desta feature.

## Complexity Tracking

*Nenhuma violação da constituição identificada — seção não aplicável.*
