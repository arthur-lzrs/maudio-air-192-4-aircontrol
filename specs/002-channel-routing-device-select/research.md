# Phase 0 Research: Channel Routing & Device Selection

## 1. Onde aplicar o mapeamento de roteamento no pipeline de áudio

**Decision**: Aplicar o roteamento em `AudioEngine.OnDataAvailable`, como uma etapa explícita
depois do cálculo de `leftOut`/`rightOut` (trim + mute/solo já resolvidos) e antes de
`SampleFormatIO.WriteSample`/`RaiseLevels`. A etapa recebe os dois sinais pós-trim/mute/solo
(`input1Out`, `input2Out`) e o modo ativo, e devolve o par `(left, right)` já roteado, que é o que
é escrito no buffer de saída **e** o que alimenta os meters.

**Rationale**: FR-002 exige que roteamento afete monitoramento audível e meters de forma
consistente — aplicar antes de `WriteSample`/`RaiseLevels` garante que os dois usem o mesmo valor
roteado, sem duplicar lógica. FR-006 exige que trim/mute/solo continuem se aplicando aos sinais
"antes" do roteamento — colocar o roteamento depois do cálculo de audibilidade efetiva preserva
essa ordem sem mudar `TrimCalculator`/`ChannelToggleTracker`.

**Alternatives considered**:
- Aplicar roteamento nos dados brutos de captura (antes do trim): rejeitado porque violaria FR-006
  (trim/mute/solo deixariam de refletir corretamente o canal físico de origem quando, por exemplo,
  Combined Mono já tivesse somado os dois antes do trim individual ser aplicável).
- Aplicar roteamento só nos meters, mantendo a saída de áudio sempre 1:1: rejeitado porque o
  problema relatado pelo usuário é justamente o áudio saindo só à esquerda — a saída audível
  precisa mudar, não só o metering.

## 2. Compensação de ganho no Combined Mono

**Decision**: `combined = (input1Out + input2Out) * 0.5` (equivalente a -6.02dB), aplicado à soma
já pós-trim/mute/solo, e o resultado é escrito igualmente em Left e Right.

**Rationale**: Já decidido na clarificação da spec ("Somar com compensação de ganho (ex.:
(In1+In2)/2, ou -6dB)"). Multiplicar por 0.5 é a forma mais simples e numericamente estável de
implementar essa compensação em ponto flutuante, e mantém o mesmo tipo de clipping-detection já
usado por `LevelMetering` sem necessidade de lógica adicional — o valor combinado passa pelo mesmo
`CalculatePeakDb`/`IsClipping` que os outros modos.

**Alternatives considered**: Soma em nível cheio sem compensação — rejeitada explicitamente pela
clarificação da spec (risco de clipping introduzido pela própria soma, não pelo sinal original).

## 3. Enumeração de dispositivos de entrada e contagem de canais

**Decision**: Estender `IAudioDeviceProvider` com
`IReadOnlyList<AudioInputDeviceInfo> GetAvailableInputDevices()`, implementado em
`AudioDeviceProvider` via `_enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)`
(mesma API já usada para detectar o AIR). A contagem de canais de cada dispositivo é lida de
`device.AudioClient.MixFormat.Channels` (mesma fonte de formato que `AudioEngine` já consulta
via `_capture.WaveFormat` após `StartRecording`, mas aqui lida sem precisar iniciar a captura).
`AudioInputDeviceInfo` inclui `IsAirDevice` (nome contém "AIR 192", case-insensitive — mesmo
fragmento hoje hardcoded em `AudioEngine`/`AudioDeviceProvider`) para que a lógica de
auto-seleção não precise duplicar essa checagem.

**Rationale**: Reaproveita a mesma API COM (`MMDeviceEnumerator`) já usada e testada pela feature
001; `MixFormat.Channels` é a mesma propriedade de onde `AudioEngine` já deriva o formato de
captura hoje, então não há risco de valores inconsistentes entre "quantos canais o seletor
mostra" e "quantos canais a captura realmente usa".

**Alternatives considered**: Abrir uma captura temporária de cada dispositivo só para ler o
formato — rejeitado por custo (I/O real) e por risco de interferir com outro processo já usando o
dispositivo; `MixFormat` é suficiente e não exige abrir o stream.

## 4. Contrato de `IAudioEngine.Start` com dispositivo de entrada selecionável

**Decision**: Mudar a assinatura de `Start(string outputDeviceId)` para
`Start(string? inputDeviceId, string outputDeviceId)`. Quando `inputDeviceId` é `null` ou não
corresponde a um dispositivo ativo, o engine cai para a mesma lógica de auto-detecção do AIR que
já existe hoje (busca por fragmento "AIR 192"); se nem isso for encontrado, propaga a mesma
exceção `InvalidOperationException` já lançada hoje, que a camada de app já trata como "prompt de
seleção de dispositivo" (consistente com FR-009).

**Rationale**: Minimiza a mudança de contrato — reaproveita o `try/resolve/fallback` já existente
para o dispositivo de saída (`ResolveOutputDevice`) como padrão para o de entrada, em vez de
introduzir um mecanismo novo. A troca de dispositivo em runtime (FR-010) reusa `Stop()` + `Start()`
já existentes, que já são idempotentes e seguros por `try/catch` em `Start`.

**Alternatives considered**: Método separado `SwitchInputDevice(string id)` independente de
`Start`: rejeitado — duplicaria a lógica de resolução/captura/output-init já presente em `Start`,
e a troca de dispositivo já precisa parar e reiniciar a captura de qualquer forma (novo
`WaveFormat`, nova contagem de canais).

## 5. Persistência de `RoutingMode` e `InputDeviceId`

**Decision**: Estender `ChannelSettingsProfile` (record) com dois campos novos:
`RoutingMode RoutingMode` (default `Stereo`) e `string? InputDeviceId` (default `null` = "sem
seleção manual, usar auto-detecção"). Mesma serialização JSON via `System.Text.Json` já usada por
`SettingsRepository`, sem migração especial — `System.Text.Json` preenche campos ausentes de um
JSON antigo com os defaults do record ao desserializar.

**Rationale**: Mesma mecânica de persistência já validada por `SettingsRepositoryTests.cs`;
adicionar campos a um `record` com valores default é compatível com arquivos salvos pela feature
001 sem exigir versionamento de schema.

**Alternatives considered**: Arquivo de configuração separado para roteamento/dispositivo —
rejeitado pela Assumption já registrada na spec ("Routing mode and device selection preferences
are stored using the same persistence mechanism already established for trim/mute/solo
settings").

## 6. Validação e fallback de modo de roteamento por contagem de canais

**Decision**: `RoutingModeApplier` (ou tipo equivalente em `AirControl.Core`) expõe
`bool IsSupported(RoutingMode mode, int channelCount)` e
`RoutingMode ResolveFallback(RoutingMode requested, int channelCount)`. Regra: `Input1Mono`
precisa de 1 canal; `Stereo`, `Input2Mono`, `CombinedMono` precisam de 2. Se `channelCount == 1`,
o fallback é sempre `Input1Mono`; caso contrário (>= 2), quando o modo pedido não é suportado, cai
para `Stereo`. Essa função é pura e testável sem hardware, chamada tanto no primeiro `Start`
quanto sempre que o dispositivo ativo muda.

**Rationale**: Implementa diretamente FR-005 e os edge cases da spec ("fallback para o
primeiro/modo mais simples compatível", "Stereo se o device tiver 2 canais; Input 1 as Mono se
expuser só 1 canal"). Mantendo a validação em `AirControl.Core`, ela é testável em
`RoutingModeTests.cs` sem precisar de um dispositivo real, seguindo o mesmo padrão de
`TrimCalculator.Clamp`/`ChannelToggleTracker`.

**Alternatives considered**: Deixar a UI decidir o fallback (esconder opções inválidas e não
mudar o modo persistido): rejeitado porque FR-005 exige que o app efetivamente troque para um modo
válido quando o atual deixa de ser suportado, não apenas ocultar a opção inválida na lista.
