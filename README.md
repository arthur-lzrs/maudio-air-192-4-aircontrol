# AIR Control

Aplicativo desktop Windows (WPF, .NET 8) para monitorar em tempo real os dois inputs da interface
de áudio M-AUDIO AIR 192|4 e controlar trim digital, mute e solo por canal.

Ver especificação completa em
[specs/001-air-192-4-input-control/spec.md](specs/001-air-192-4-input-control/spec.md).

## Pré-requisitos

- Windows 10/11 (x64).
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- Driver da M-AUDIO AIR 192|4 instalado e a interface conectada via USB (para uso real; os testes
  automatizados não exigem o hardware).

## Setup

```bash
dotnet restore
dotnet build
```

## Rodando o app

```bash
dotnet run --project src/AirControl.App
```

Na primeira execução (ou se nenhum dispositivo de saída estiver salvo), o app solicita a escolha
do dispositivo de saída Windows usado para o monitoramento (playthrough).

## Rodando os testes automatizados

```bash
dotnet test
```

- `tests/AirControl.Core.Tests`: testes unitários de lógica de domínio (trim, mute/solo, peak/RMS)
  — não dependem de hardware nem de WPF.
- `tests/AirControl.Integration.Tests`: testes de integração contra `IAudioEngine`/
  `IAudioDeviceProvider`, usando fakes (`tests/AirControl.Integration.Tests/Fakes/`) para rodar sem
  o AIR 192|4 físico presente.

## Estrutura do projeto

```text
src/
├── AirControl.Core/    # Contratos + lógica de domínio pura (sem WPF/NAudio)
├── AirControl.Audio/   # Implementação real: NAudio (WASAPI), Core Audio notifications
└── AirControl.App/     # UI WPF: janelas, views, view-models

tests/
├── AirControl.Core.Tests/        # Testes unitários
└── AirControl.Integration.Tests/ # Testes de integração (com fakes)
```

## Validação manual com hardware real

Consulte [specs/001-air-192-4-input-control/quickstart.md](specs/001-air-192-4-input-control/quickstart.md)
para os 14 cenários de validação manual (golden path + edge cases) que devem ser conferidos com o
AIR 192|4 físico antes de considerar a feature pronta para revisão.
