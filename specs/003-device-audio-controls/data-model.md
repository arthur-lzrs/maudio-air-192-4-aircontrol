# Data Model: Device & Monitoring Audio Controls

## Channel Meter Reading (modificado, sem novo tipo)

Sem mudança de forma — continua `ChannelLevelsChangedEventArgs(Channel, PeakDb, RmsDb,
IsClipping)` (`Events.cs`, inalterado). O que muda é **a origem dos valores**: agora
calculados a partir do sinal pós-trim/pré-gate/pré-roteamento por canal físico
(`InputChannelId.Input1`/`Input2`), nunca zerado por mute, solo ou monitoramento desativado
(FR-001). Ver research.md §1.

## Monitoring State (sem mudança de forma)

`IAudioEngine.IsMonitoringEnabled`/`SetMonitoringEnabled` (inalterados). Continua controlando
apenas o caminho de saída audível; não tem mais nenhuma influência (e nunca deveria ter tido)
sobre o caminho de metering.

## Channel Trim (faixa alterada)

```csharp
public static class TrimCalculator
{
    public const double MinDb = double.NegativeInfinity; // era -12.0
    public const double MaxDb = 10.0;                    // era 12.0

    public static double Clamp(double trimDb) => Math.Clamp(trimDb, MinDb, MaxDb);
    public static float ToLinearGain(double trimDb) => (float)Math.Pow(10, Clamp(trimDb) / 20.0);
}
```

- **Invariante**: `ToLinearGain(MinDb) == 0f` exatamente (não uma aproximação) — decorre de
  `Math.Pow(10, -∞/20) == 0`, sem ramificação especial.
- **Persistência**: `ChannelSettings.TrimDb` (double) agora pode conter
  `double.NegativeInfinity`; `SettingsRepository` usa
  `JsonNumberHandling.AllowNamedFloatingPointLiterals` para (de)serializar isso como o
  literal JSON `"-Infinity"` (research.md §3).
- **Migração/compatibilidade**: um perfil salvo com um valor fora de `[-∞, 10]` (ex.: o antigo
  máximo `+12.0`) é levado para dentro da faixa por `TrimCalculator.Clamp` no ponto de leitura
  usado tanto pelo motor de áudio (`AudioEngine.SetTrim`, já existente) quanto pela ViewModel
  (`TrimControlViewModel`, novo — research.md §4). Nenhuma migração de arquivo é necessária.
- **UI (não é domínio, é apresentação)**: `TrimControlViewModel.SliderFloorDb = -60.0` é o
  mínimo do `Slider` WPF; ao atingir esse piso o valor de domínio gravado é
  `double.NegativeInfinity`, não `-60.0`. `TrimDbToDisplayConverter` mapeia
  `double.NegativeInfinity → "-∞ dB"` e qualquer outro valor para `"{0:0.0} dB"`.

## Recording Device Format (novo)

```csharp
namespace AirControl.Core;

public record RecordingFormat(int SampleRate, int BitDepth)
{
    public static RecordingFormat Default { get; } = new(SampleRate: 48000, BitDepth: 32);
}
```

- **Campos**: `SampleRate` em Hz (ex.: 44100, 48000, 96000); `BitDepth` em bits (ex.: 16, 24,
  32). Ambos inteiros — sem casas decimais, sem enum fechado (a lista de combinações
  candidatas testadas contra o dispositivo vive em `WindowsRecordingFormatController`,
  research.md §5, não no tipo de domínio, para não engessar o modelo a uma lista fixa que
  pode variar por hardware).
- **Validação**: um `RecordingFormat` só é "suportado" para um `deviceId` se
  `IRecordingFormatController.GetSupportedFormats(deviceId)` o contiver — a verificação real
  usa `IAudioClient::IsFormatSupported` (research.md §5), não uma regra estática em `Core`.
- **Persistência**: novo arquivo `%AppData%\AirControl\recording-format.json`, chaveado por
  `deviceId` (um dispositivo M-Audio reconectado com o mesmo `deviceId` do Windows recupera a
  mesma preferência salva). Formato: `{ "<deviceId>": { "SampleRate": 48000, "BitDepth": 32 } }`.
- **Fallback (FR-005)**: se o valor persistido para o `deviceId` ativo não estiver em
  `GetSupportedFormats(deviceId)` no momento em que o app tenta aplicá-lo (reconexão do
  dispositivo ou início do app), o sistema aplica `RecordingFormat.Default` e sinaliza ao
  usuário (mesmo padrão de mensagem acionável de `MainWindowViewModel`, ex.:
  `"O formato salvo (Xhz/Ybit) não é mais suportado por este dispositivo; usando 48kHz/32-bit."`).
- **Relação com dispositivo ativo**: os controles que leem/escrevem `RecordingFormat` só ficam
  visíveis/habilitados quando `InputDeviceSelectorViewModel.SelectedDevice?.IsAirDevice ==
  true` (research.md §7) — não existe `RecordingFormat` para dispositivos não-M-Audio nesta
  feature.

## Driver Configuration (M-Audio, escopo reduzido nesta iteração)

Sem novo tipo de domínio "controlável" — a investigação (research.md §6) conclui que não há
integração de escrita viável sem adotar o SDK ASIO. O que existe:

```csharp
namespace AirControl.App.ViewModels;

public partial class DriverSettingsViewModel : ViewModelBase
{
    public bool IsAirDeviceActive { get; }             // mesma regra de visibilidade de RecordingFormat
    public string? DiagnosticInfo { get; }              // ex.: CaptureFormatDescription já existente
    // Comando "Abrir painel M-Audio": inicia um processo externo (System.Diagnostics.Process),
    // não lê nem escreve nenhum estado de driver.
}
```

Nenhuma persistência associada — não há preferência do app a guardar para algo que o app não
controla.

## Relações entre entidades

```
InputDeviceSelectorViewModel.SelectedDevice (feature 002, existente)
        │  IsAirDevice?
        ├──► RecordingFormatSelectorViewModel (visível/habilitado apenas se true)
        │        │
        │        └──► IRecordingFormatController ──► WindowsRecordingFormatController
        │                                                  │  IPropertyStore / IsFormatSupported
        │                                                  ▼
        │                                          MMDevice (M-Audio, WASAPI)
        │
        └──► DriverSettingsViewModel (visível/habilitado apenas se true)
                 └──► Process.Start(painel M-Audio externo)

AudioEngine.OnDataAvailable
        ├──► input1/input2 (pós-trim, pré-gate, pré-roteamento) ──► RaiseLevels (meters)
        └──► input1Out/input2Out (pós-gate) ──► RoutingModeApplier.Apply ──► saída audível
```
