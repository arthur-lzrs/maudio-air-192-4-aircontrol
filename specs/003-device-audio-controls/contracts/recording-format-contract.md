# Contract: `IRecordingFormatController` (AirControl.Core)

Interface interna consumida por `AirControl.App` (ViewModels) e implementada por
`AirControl.Audio.WindowsRecordingFormatController`. Não é uma API pública externa — é o
contrato entre as camadas de domínio/UI e a camada de I/O real, no mesmo espírito de
`IAudioEngine`/`IAudioDeviceProvider` das features anteriores.

```csharp
namespace AirControl.Core;

public interface IRecordingFormatController
{
    /// <summary>
    /// Formato atualmente configurado como "Default Format" do Windows para o dispositivo de
    /// gravação identificado, ou null se o dispositivo não existir/não estiver ativo.
    /// </summary>
    RecordingFormat? GetCurrentFormat(string deviceId);

    /// <summary>
    /// Combinações de sample rate/bit depth que o dispositivo aceita em modo compartilhado
    /// (verificadas via IAudioClient::IsFormatSupported), a partir de uma lista fixa de
    /// candidatos comuns (research.md §5). Lista vazia se o dispositivo não existir/não
    /// estiver ativo.
    /// </summary>
    IReadOnlyList<RecordingFormat> GetSupportedFormats(string deviceId);

    /// <summary>
    /// Tenta aplicar o formato como o novo "Default Format" do Windows para o dispositivo.
    /// Retorna false com <paramref name="error"/> preenchido (mensagem acionável, sem stack
    /// trace) se o formato não estiver em <see cref="GetSupportedFormats"/> ou se a escrita
    /// falhar (dispositivo desconectado durante a operação, permissão negada, etc.). Nunca
    /// deixa o dispositivo em um formato parcialmente aplicado — em caso de falha, o formato
    /// anterior permanece ativo (FR-006).
    /// </summary>
    bool TrySetFormat(string deviceId, RecordingFormat format, out string? error);
}
```

## Pré-condições / pós-condições

- `TrySetFormat` **não** reinicia a captura sozinho — quem chama (tipicamente
  `RecordingFormatSelectorViewModel`, via `MainWindowViewModel`/`AudioEngine`) é responsável
  por `Stop()`+`Start()` no `IAudioEngine` após um `true` de retorno, para renegociar o mix
  format (FR-010). Isso mantém `IRecordingFormatController` sem dependência de `IAudioEngine`
  (responsabilidade única — Constitution I).
- `GetSupportedFormats` nunca lança para um `deviceId` inválido/desconectado — retorna lista
  vazia. Chamadores devem tratar lista vazia como "nenhuma alteração possível agora"
  (controle desabilitado), não como erro.
- Todas as strings de `error` devem seguir o padrão de mensagem acionável já usado em
  `MainWindowViewModel` (`"Falha ao ...: {motivo}"`), nunca expor `HRESULT`/exceção crua
  (Constitution III).

## Fake para testes (`AirControl.Integration.Tests.Fakes`)

```csharp
public class FakeRecordingFormatController : IRecordingFormatController
{
    // Permite testes configurarem: formato atual, lista de suportados, e forçar falha de
    // TrySetFormat com uma mensagem específica — sem tocar hardware/COM real, seguindo o
    // mesmo padrão de FakeAudioEngine/FakeAudioDeviceProvider já existentes.
}
```

## Uso pelo fallback de FR-005 (perfil salvo não suportado)

No startup ou na reconexão do dispositivo M-Audio, o fluxo (em `MainWindowViewModel` ou um
novo `RecordingFormatSelectorViewModel`, seguindo o padrão de
`InputDeviceSelector.ResolveActiveDevice()`) é:

1. Ler a preferência persistida para o `deviceId` ativo (novo repositório de
   `RecordingFormat`, mesmo padrão de `SettingsRepository`).
2. Se `GetSupportedFormats(deviceId)` não contiver essa preferência, usar
   `RecordingFormat.Default` (48000/32) e sinalizar ao usuário (data-model.md, seção
   "Fallback (FR-005)").
3. Chamar `TrySetFormat(deviceId, resolvido, out error)`.
4. Se `true`, reiniciar `IAudioEngine` (`Stop`+`Start`) para recuperar monitoramento/metering
   (FR-010) dentro do orçamento de 3s já usado para reconexão de dispositivo.
