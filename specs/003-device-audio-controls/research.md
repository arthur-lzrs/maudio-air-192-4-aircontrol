# Phase 0 Research: Device & Monitoring Audio Controls

## 1. Fonte do sinal de metering (FR-001, FR-002)

**Decision**: Os meters passam a ler os arrays `input1`/`input2` já calculados em
`AudioEngine.OnDataAvailable` — sinal após trim, antes do gate de audibilidade
(`leftAudible && _monitoringEnabled`) e antes de `RoutingModeApplier.Apply`. O gate de
audibilidade e o roteamento continuam sendo aplicados apenas ao par escrito no
`_outputBuffer` (o que sai fisicamente pela saída).

**Rationale**: Hoje `RaiseLevels` recebe `routedLeft`/`routedRight`, que já passaram tanto
pelo gate de mute/solo/monitoramento quanto pelo `RoutingModeApplier`. Isso causa dois
problemas: (1) o bug relatado — desativar monitoramento ou mutar/soloar zera a entrada do
gate e portanto zera o meter também, já que o gate roda antes do cálculo de nível; (2) um
acoplamento latente não pedido por esta feature — em `Input1Mono`/`Input2Mono`/`CombinedMono`
os dois meters passariam a mostrar o mesmo valor, escondendo o nível real de cada entrada
física. Usar o par pré-gate/pré-roteamento resolve os dois de uma vez com uma mudança mínima
(trocar os dois argumentos passados a `RaiseLevels`), sem alterar `LevelMetering` nem os
contratos de `IAudioEngine`/eventos.

**Alternatives considered**:
- Manter o roteamento no caminho do meter mas remover só o gate de audibilidade: rejeitado
  porque ainda esconderia o nível real de uma entrada nos modos mono/combinado, contrariando
  a leitura mais natural de FR-001 ("nível real do sinal de entrada").
- Calcular os níveis duas vezes (uma pré-gate para o meter, outra pós-roteamento só para
  depuração): descartado por complexidade desnecessária — nenhum requisito pede o nível do
  sinal já roteado.

## 2. Faixa de trim e silêncio digital (FR-011, FR-012)

**Decision**: `TrimCalculator.MinDb` passa de `-12.0` para `double.NegativeInfinity`;
`MaxDb` passa de `12.0` para `10.0`. `ToLinearGain` continua sendo
`Math.Pow(10, Clamp(trimDb) / 20.0)`, que já retorna exatamente `0` quando o expoente é
`double.NegativeInfinity` — nenhuma ramificação especial é necessária para o "ganho zero
exato" pedido por FR-011. `Clamp` com `Math.Clamp(trimDb, double.NegativeInfinity, 10.0)`
funciona normalmente (inclusive para `NaN`-safe já que `trimDb` nunca é `NaN` neste domínio).

O slider de trim na UI (`TrimControlViewModel`/`MainWindow.xaml`) precisa de um mínimo
finito para funcionar como controle contínuo. Introduz-se uma constante de UI
`TrimControlViewModel.SliderFloorDb = -60.0` (mesma ordem de grandeza do `SilenceFloorDb`
de `LevelMetering`, mas um conceito separado — um é piso de exibição de medidor, o outro é
piso do controle de trim): o slider varre `[-60, +10]`; ao chegar no piso, o ViewModel grava
`TrimDb = double.NegativeInfinity` (snap) em vez de `-60`; ao mover o slider para cima a
partir do piso, volta a um valor finito. Exibição usa um conversor que mostra `"-∞ dB"`
quando o valor é `double.NegativeInfinity` e `"{0:0.0} dB"` caso contrário.

**Rationale**: `double.NegativeInfinity` é o único valor que satisfaz literalmente "ganho
exatamente zero, igual ao mute" sem introduzir um caminho de código condicional extra em
`OnDataAvailable`/`TrimCalculator` — a mesma fórmula de conversão dB→linear já usada para
todos os outros valores continua válida. Um slider WPF (`Slider.Minimum`/`Maximum`) não
aceita `NegativeInfinity` como limite, daí a necessidade de um piso finito só para fins de
interação, sem afetar o domínio (`AirControl.Core`) que continua usando `NegativeInfinity`
como o mínimo real.

**Alternatives considered**:
- Usar um sentinel numérico finito (ex.: `-96.0` = "mudo") e checar `trimDb <= -96.0` para
  forçar ganho zero: rejeitado — exige uma ramificação extra em `ToLinearGain`, duplica o
  conceito de "piso" (o mesmo -96 já é usado por `LevelMetering.SilenceFloorDb` para outro
  propósito) e não é literalmente "zero" armazenado, só um valor que produz zero por
  arredondamento de ponto flutuante (risco de não ser bit-exato).
- Manter apenas atenuação forte (ex.: -60dB) sem chegar a zero exato: rejeitado, contraria
  FR-011 explicitamente (a clarificação da sessão confirma "silêncio digital absoluto").

## 3. Persistência de `double.NegativeInfinity` em JSON (suporte a FR-011/FR-012)

**Decision**: `SettingsRepository` passa a serializar/desserializar `ChannelSettingsProfile`
com `JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals }`
além do `WriteIndented` já usado.

**Rationale**: `System.Text.Json` rejeita `double.NegativeInfinity` por padrão
(`JsonException: Number... 'NegativeInfinity' is not a finite number`). A flag
`AllowNamedFloatingPointLiterals` (disponível desde .NET Core 3.0, sem dependência nova) faz
o serializer emitir/aceitar os literais `"-Infinity"`/`"Infinity"`/`"NaN"` como strings JSON
para propriedades `double`, preservando o restante do arquivo como está (compatível com
perfis salvos por versões anteriores, que só continham valores finitos).

**Alternatives considered**: converter `TrimDb` para uma DTO própria de persistência
(ex.: `double?` com `null` representando `-∞`): rejeitado por exigir um tipo intermediário e
lógica de mapeamento adicional só para contornar uma limitação já resolvida pela flag nativa.

## 4. Clamp de valores salvos fora da nova faixa (FR-012)

**Decision**: `TrimControlViewModel`, ao carregar `savedSettings.TrimDb` no construtor,
aplica `TrimCalculator.Clamp(savedSettings.TrimDb)` antes de atribuir a `_trimDb` e antes de
chamar `audioEngine.SetTrim`. `AudioEngine.SetTrim` já chama `TrimCalculator.Clamp`
internamente (linha existente), então o motor já está protegido; o ajuste fica só na
ViewModel para que o valor exibido no slider também reflita o clamp (hoje ela atribui
`savedSettings.TrimDb` diretamente, sem clamp, então um perfil antigo com +12dB apareceria
fora da faixa do slider).

**Rationale**: Um perfil salvo antes desta mudança pode conter `+12.0` (antigo máximo), que
excede o novo `MaxDb = 10.0`. `Math.Clamp` já trata esse caso corretamente assim que
`MinDb`/`MaxDb` mudam — não é necessário nenhum código de migração de schema, só garantir
que o clamp seja aplicado no ponto de leitura usado pela UI, não só pelo motor de áudio.

## 5. Controle do "Default Format" do Windows para o dispositivo de gravação (FR-003–FR-006)

**Decision**: Introduzir uma nova abstração em `AirControl.Core`,
`IRecordingFormatController`, com:
- `RecordingFormat? GetCurrentFormat(string deviceId)`
- `IReadOnlyList<RecordingFormat> GetSupportedFormats(string deviceId)`
- `bool TrySetFormat(string deviceId, RecordingFormat format, out string? error)`

`RecordingFormat` é um `record RecordingFormat(int SampleRate, int BitDepth)` em
`AirControl.Core`, com `Default => new(48000, 32)`.

A implementação real (`AirControl.Audio.WindowsRecordingFormatController`) usa o mesmo
`MMDevice` já obtido via `MMDeviceEnumerator` e:
- Lê o formato atual e testa suporte via `IAudioClient::IsFormatSupported` em modo
  compartilhado (já exposto indiretamente por `NAudio.CoreAudioApi.AudioClient`), varrendo
  uma lista fixa de combinações candidatas (`{44100,16}`, `{44100,24}`, `{48000,16}`,
  `{48000,24}`, `{48000,32}`, `{96000,24}`, `{96000,32}`) — a mesma lista que o Windows
  oferece na aba "Formato Padrão" do Painel de Som para a maioria dos dispositivos WASAPI.
- Grava o novo formato escrevendo a chave de propriedade `PKEY_AudioEngine_DeviceFormat`
  (`{f19f064d-082c-4e27-bc73-6882a1bb8e4c}, 0`) no `IPropertyStore` do endpoint, aberto com
  `STGM_READWRITE` — o mesmo mecanismo que o próprio Painel de Controle de Som do Windows
  usa internamente para persistir a escolha em
  `HKCU\...\MMDevices\Audio\Capture\{id}\Properties`. `NAudio.CoreAudioApi.MMDevice` já expõe
  leitura de propriedades (`Properties`), mas não escrita nessa chave específica; a escrita
  exige uma interop direta com `IPropertyStore`/`PROPVARIANT` (P/Invoke sobre o mesmo
  `IPropertyStore` COM que `NAudio` já usa internamente), isolada dentro de
  `WindowsRecordingFormatController` para não vazar COM/interop para `AirControl.Core` ou
  `AirControl.App`.
- Após uma escrita bem-sucedida, `AudioEngine` deve reiniciar a captura (`Stop`+`Start`) para
  que o novo mix format seja renegociado — o dispositivo já ativo não relê o "Default Format"
  sozinho até o próximo `IAudioClient::Initialize`.

**Rationale**: O `IPropertyStore`/`PKEY_AudioEngine_DeviceFormat` é o mecanismo documentado
(via cabeçalhos do Windows SDK, `mmdeviceapi.h`/`devicetopology`) usado pelo próprio Painel
de Controle de Som para persistir o "Formato Padrão"; é a única forma pública, não invasiva
(sem reinstalar driver, sem tocar registro fora do padrão do próprio Windows) de alcançar
FR-003. Encapsular atrás de `IRecordingFormatController` mantém `AirControl.Core` livre de
COM/interop (Constitution I — responsabilidade única) e torna a lógica de
validação/fallback 100% testável com um fake, sem hardware real (Constitution II).

**Alternatives considered**:
- Usar apenas `AudioClient.MixFormat` (leitura) e assumir que o app pode forçar um formato
  específico no próprio `WasapiCapture` sem tocar a configuração do Windows: rejeitado — isso
  mudaria só o formato que a *própria* AIR Control pede ao abrir o stream, não o "Default
  Format" do dispositivo visível no Painel de Som (violaria a User Story 2, que pede
  explicitamente equivalência com o painel nativo).
- Editar o registro diretamente sem passar pelo `IPropertyStore` COM: rejeitado — mais frágil
  (layout de registro não é contrato público estável) quando a API COM padrão já cobre o caso.

## 6. Viabilidade de controlar sample rate/buffer size do driver M-Audio (FR-007–FR-009)

**Decision**: Nesta iteração, **não existe um caminho de integração suportado e não invasivo
suficiente para justificar FR-008** (controle inline, com valores lidos/gravados
diretamente pelo AIR Control). A conclusão da investigação (que satisfaz SC-004) é:

- O AIR 192|4 da M-Audio expõe sample rate/buffer size através de um driver ASIO próprio
  (o "AIR Control"/painel M-Audio já mencionado pelo usuário), não através de uma API pública
  documentada independente do ASIO SDK.
- O ASIO SDK define, como único mecanismo padrão para expor configurações do driver ao
  usuário, `IASIO::controlPanel()` — que abre a UI própria do fabricante; não há um método
  ASIO padrão para *gravar* buffer size diretamente (o tamanho de buffer é negociado via
  `ASIOGetBufferSize`/`ASIOCreateBuffers` no momento em que um host abre um stream ASIO, não
  é uma preferência persistida que terceiros possam sobrescrever de fora de um host ASIO).
  Sample rate tem, em alguns drivers, `ASIOSetSampleRate`, mas isso exige que o AIR Control
  se torne um **host ASIO completo**, o que implica adotar o ASIO SDK da Steinberg — cuja
  licença exige aceite de termos e não é redistribuível livremente, uma mudança de escopo e
  de dependências que este plano não assume sem uma decisão explícita do usuário.
- Portanto, FR-008 fica fora do escopo desta iteração; aplica-se FR-009: o app expõe uma
  seção "Driver M-Audio" (visível apenas com o M-Audio ativo, mesma regra de FR-003) que (a)
  mostra, quando disponível via WASAPI, os valores atuais que o driver está reportando ao
  Windows como informação diagnóstica, e (b) apresenta um botão "Abrir painel M-Audio" que
  localiza e inicia o executável do painel de controle do fabricante (processo externo,
  descoberto por nome de aplicativo/atalho conhecido do instalador M-Audio, não por
  integração COM/ASIO), deixando claro que a alteração real deve ser feita lá.

**Rationale**: SC-004 pede uma resposta clara e documentada antes de qualquer UI de controle
de driver ser construída — esta seção é essa resposta. Adotar um host ASIO completo
introduziria uma dependência de licenciamento de terceiros e uma superfície de risco (um bug
de host ASIO pode travar o driver do dispositivo para outros aplicativos) desproporcional ao
valor desta iteração, especialmente por ser a User Story de menor prioridade (P3) e o próprio
spec já prever esse desfecho como aceitável (Acceptance Scenario 4 da User Story 3).

**Alternatives considered**:
- Implementar um host ASIO mínimo (ex.: via um wrapper gerenciado de ASIO) só para
  `getClockSource`/`setSampleRate`: rejeitado por licenciamento e escopo — reavaliar apenas
  se o usuário confirmar que aceita a dependência do ASIO SDK.
- Automatizar cliques na UI do painel M-Audio (ex.: UI Automation) para simular a alteração:
  rejeitado — frágil (depende da versão exata do instalador do fabricante), viola o espírito
  de "não invasivo" e não é testável de forma confiável.

## 7. Escopo de visibilidade "M-Audio é o dispositivo ativo" (clarificação de sessão)

**Decision**: Reutiliza-se a mesma fonte de verdade já existente de feature 002 —
`InputDeviceSelectorViewModel.SelectedDevice`/`AudioInputDeviceInfo.IsAirDevice` — para
decidir se os novos controles de formato (User Story 2) e de driver (User Story 3) ficam
visíveis/habilitados. Nenhuma nova forma de detecção é necessária.

**Rationale**: A clarificação da sessão de 2026-09-03 amarra explicitamente a visibilidade
desses controles ao dispositivo *ativo no app*, não a "M-Audio conectado em algum lugar do
Windows" (que já é `IAudioDeviceProvider.IsAirDeviceConnected`, um conceito diferente e já
usado por `DeviceStatusViewModel`). Reaproveitar `AudioInputDeviceInfo.IsAirDevice` do
dispositivo atualmente selecionado evita duplicar a lógica de detecção por nome
("AIR 192"/`AirDeviceNameFragment`) que já existe em `AudioDeviceProvider`.
