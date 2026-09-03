# Quickstart: Validação de Roteamento de Canais & Seleção de Dispositivo

## Pré-requisitos

- Windows 10/11 com .NET 8 Runtime (ou SDK, para build local).
- Interface M-Audio AIR 192|4 conectada via USB, com sinal disponível em pelo menos o Input 1 (ex.
  microfone) para validar o cenário principal (US1).
- Idealmente, pelo menos um outro dispositivo de captura Windows disponível (ex. microfone
  embutido do notebook) para validar o seletor de dispositivo (US3) sem depender de um segundo
  M-Audio físico.
- Fones de ouvido ou alto-falantes em um dispositivo de saída Windows, como na feature 001.

## Setup

```bash
dotnet restore
dotnet build
```

## Rodando o app

```bash
dotnet run --project src/AirControl.App
```

Ao abrir, o app deve:
1. Auto-selecionar o M-Audio AIR 192|4 como dispositivo de entrada ativo, se conectado (FR-008).
2. Aplicar Stereo como modo de roteamento padrão, se nenhuma preferência tiver sido salva ainda
   (FR-004).
3. Restaurar o modo de roteamento e o dispositivo de entrada salvos de uma sessão anterior, se
   existirem e ainda forem válidos (FR-004, FR-011).

## Rodando os testes automatizados

```bash
dotnet test
```

Os testes de `RoutingModeTests.cs` (unitários, mapeamento/soma/validação de canais) rodam sem
hardware; os de `RoutingIntegrationTests.cs` e `DeviceSelectionIntegrationTests.cs` usam os fakes
de `IAudioEngine`/`IAudioDeviceProvider` já estabelecidos pela feature 001.

## Cenários de validação manual (golden path + edge cases)

Espelham as Acceptance Scenarios do spec.md; conferir manualmente com hardware real antes de
considerar a feature completa.

1. **Mic centralizado (US1, golden path)**: com microfone só no Input 1, selecione "Input 1 as
   Mono" → o sinal é audível/medido igualmente em ambos os canais de saída, sem nada só à
   esquerda.
2. **Input 2 Mono (US1)**: repita com sinal só no Input 2 e o modo "Input 2 as Mono".
3. **Persistência de modo (US1)**: selecione um modo mono, feche e reabra o app → o mesmo modo é
   restaurado automaticamente.
4. **Stereo tradicional (US2, golden path)**: com sinal em ambos os inputs, selecione Stereo →
   Input 1 só à esquerda, Input 2 só à direita.
5. **Combined Mono (US2)**: com sinal em ambos os inputs, selecione Combined Mono → o sinal
   combinado aparece igual nos dois canais de saída, sem clipping introduzido pela própria soma
   (compare o pico combinado ao de cada input individual — não deve ultrapassar ~o maior dos dois
   antes da soma).
6. **Troca sem glitch (US2)**: alterne entre os 4 modos com sinal ativo → sem pop/glitch audível
   perceptível, efeito em até 100ms (SC-002).
7. **Trim/mute/solo sob roteamento (edge case, FR-006)**: em Combined Mono, mute o Input 2 → o
   resultado deixa de incluir o Input 2 na soma (equivalente a Input 1 Mono em nível), confirmando
   que mute/solo continuam válidos por cima do roteamento.
8. **M-Audio como padrão (US3, golden path)**: com o AIR 192|4 e outro dispositivo conectados,
   abra o app → o AIR é selecionado automaticamente, sem prompt.
9. **Sem M-Audio (US3, edge case)**: desconecte o AIR e abra o app com apenas outro dispositivo
   disponível → o app mostra um estado/prompt claro de seleção, sem assumir um dispositivo
   silenciosamente.
10. **Troca manual de dispositivo (US3)**: com o app rodando, abra o seletor e escolha outro
    dispositivo → o app passa a capturar/medir/rotear esse dispositivo em menos de 15s (SC-005),
    sem reiniciar.
11. **Persistência de seleção manual (US3)**: com uma seleção manual feita, feche e reabra o app
    (dispositivo ainda conectado) → a mesma seleção manual é restaurada, não o M-Audio.
12. **Fallback de dispositivo desconectado (US3, edge case)**: com uma seleção manual salva,
    desconecte esse dispositivo e reabra o app → o app cai para auto-detectar o M-Audio (se
    presente) ou mostra o prompt de seleção (se não).
13. **Fallback de modo por canal único (edge case, FR-005)**: selecione um dispositivo de entrada
    com apenas 1 canal → os modos Stereo/Input 2 Mono/Combined Mono ficam ocultos ou desabilitados,
    e o modo ativo cai automaticamente para "Input 1 as Mono" se um modo incompatível estava
    selecionado.
14. **Revalidação ao trocar de dispositivo (edge case, FR-005)**: com Combined Mono ativo, troque
    para um dispositivo de 1 canal → o modo cai automaticamente para "Input 1 as Mono" sem exigir
    ação extra do usuário.

## Critério de sucesso da validação

Todos os 14 cenários acima devem passar antes de considerar a feature "002" pronta para revisão,
em linha com os Success Criteria SC-001 a SC-005 do spec.md.
