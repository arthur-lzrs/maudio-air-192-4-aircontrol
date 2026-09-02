# Quickstart: Validação do Painel de Controle AIR 192|4

## Pré-requisitos

- Windows 10/11 com .NET 8 Runtime (ou SDK, para build local).
- Driver da M-AUDIO AIR 192|4 instalado (fora de escopo desta feature — assumido presente).
- Interface AIR 192|4 conectada via USB, com uma fonte de sinal em pelo menos um input (ex.:
  microfone ou linha) para testar os meters.
- Fones de ouvido ou alto-falantes conectados a um dispositivo de saída Windows para validar o
  monitoramento audível (FR-019).

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
1. Detectar automaticamente o AIR 192|4 e mostrar "Conectado" (FR-002).
2. Solicitar a escolha do dispositivo de saída de monitoramento, caso nenhum tenha sido salvo
   ainda (FR-019).

## Rodando os testes automatizados

```bash
dotnet test
```

Os testes de integração usam uma implementação fake de `IAudioEngine`/`IAudioDeviceProvider`
(ver contracts/audio-engine-contract.md), então rodam sem o hardware físico presente.

## Cenários de validação manual (golden path + edge cases)

Estes cenários espelham as Acceptance Scenarios do spec.md e devem ser conferidos manualmente
com o hardware real antes de considerar a feature completa (a constituição exige validação de
golden path + pelo menos um edge case para mudanças de UI):

1. **Meters independentes (US1)**: alimente sinal só no Input 1 → apenas o meter do Input 1 se
   move; o do Input 2 permanece em repouso.
2. **Clipping (US1)**: leve o Input 1 a 0 dBFS → o meter indica clipping visualmente distinto de
   atividade normal.
3. **Dispositivo ausente (US1, edge case)**: abra o app sem o AIR 192|4 conectado → o app mostra
   claramente "não conectado", sem atividade falsa nos meters.
4. **Trim independente (US2)**: suba o trim do Input 1 com sinal constante → o meter do Input 1
   sobe; o Input 2 não é afetado.
5. **Trim mínimo (US2)**: abaixe o trim do Input 1 para -12 dB → o nível cai 12 dB em relação ao
   trim 0 dB.
6. **Persistência de trim (US2)**: feche e reabra o app → o trim salvo é restaurado.
7. **Mute (US3)**: engaje mute no Input 1 com sinal presente → Input 1 silencia; Input 2 continua
   audível/monitorado.
8. **Solo (US4)**: com sinal em ambos os inputs, engaje solo no Input 1 → apenas Input 1 permanece
   ativo, independentemente do mute do Input 2.
9. **Solo sobrepõe mute (US4, edge case)**: com Input 1 já mutado, engaje solo no Input 1 → Input 1
   fica audível (solo vence mute).
10. **Todos soloed (edge case)**: engaje solo em ambos os inputs → ambos voltam a se comportar como
    se nenhum estivesse soloed.
11. **Desconexão a quente (edge case)**: desconecte o cabo USB com o app aberto → em até 3s o app
    indica perda do dispositivo, sem dados de meter congelados (SC-005).
12. **Reconexão a quente (edge case)**: reconecte o cabo → o app retoma os meters e restaura
    trim/mute/solo automaticamente, sem precisar reiniciar.
13. **Segunda instância (edge case)**: abra o app uma segunda vez enquanto a primeira já está
    aberta → a instância existente é focada em vez de abrir uma segunda janela controlando o
    dispositivo.
14. **Acessibilidade (FR-020)**: navegue por todos os controles (meters como indicadores de status,
    trim, mute, solo) apenas com teclado (Tab/setas/Espaço/Enter) e confirme que um leitor de tela
    (ex.: Narrator do Windows) anuncia rótulos e mudanças de estado (mute/solo/clipping).

## Critério de sucesso da validação

Todos os 14 cenários acima devem passar antes de considerar o feature "001" pronto para revisão,
em linha com os Success Criteria SC-001 a SC-005 do spec.md.
