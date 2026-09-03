# Quickstart: Device & Monitoring Audio Controls

Pré-requisitos: Windows 10/11, .NET 8 SDK, interface M-Audio AIR 192|4 (ou equivalente)
conectada para os cenários que dependem dela; um fone/monitor de saída para confirmar
áudio audível separadamente do que os meters mostram.

```bash
dotnet build
dotnet test
```

## US1 — Meters continuam medindo independente de mute/solo/monitoramento (P1)

1. Rode o app (`dotnet run --project src/AirControl.App`) com sinal ativo no Input 1.
2. Desative "Monitoramento": o meter do Input 1 continua se movendo com o sinal; nenhum áudio
   é ouvido na saída.
3. Reative o monitoramento e mute o Input 1: o meter do Input 1 continua se movendo; Input 1
   fica inaudível.
4. Desmute e ative Solo no Input 2 com sinal também no Input 1: o meter do Input 1 continua
   refletindo seu sinal, mesmo inaudível.
5. Edge case: cause um clipping no Input 1 nas condições acima — o indicador de clipping do
   meter deve continuar acendendo.

Teste automatizado: `tests/AirControl.Integration.Tests/MeteringIntegrationTests.cs`
(regressão adicionada para este bug).

## US2 — Formato padrão de gravação do Windows (P2)

1. Com o M-Audio como dispositivo ativo, abra a seção de formato de gravação no AIR Control.
2. Sem preferência salva (primeira execução), confirme que o valor mostrado é 48kHz/32-bit —
   abra o Painel de Som do Windows (`mmsys.cpl` → Gravar → Propriedades → Avançado) e confirme
   que reflete o mesmo valor.
3. Selecione uma combinação suportada diferente (ex.: 48kHz/24-bit); confirme que o Painel de
   Som do Windows reflete a mudança e que o monitoramento/metering voltam sozinhos, sem
   reiniciar o app (dentro de ~3s).
4. Tente (via teste automatizado com fake, não manualmente) um formato não suportado —
   confirme que o app rejeita com mensagem explicativa e mantém o formato anterior.
5. Edge case: troque o dispositivo ativo para um não-M-Audio — confirme que a seção de formato
   some/desabilita; volte para o M-Audio e confirme que reaparece.

Teste automatizado: `tests/AirControl.Integration.Tests/RecordingFormatIntegrationTests.cs`.

## US3 — Driver M-Audio (sample rate/buffer size) (P3)

1. Com o M-Audio ativo, abra a seção "Driver M-Audio" no AIR Control.
2. Confirme que ela mostra a informação diagnóstica disponível (`CaptureFormatDescription`) e
   um botão "Abrir painel M-Audio".
3. Clique no botão; confirme que o painel de controle do fabricante abre como processo
   externo. Qualquer alteração de buffer size/sample rate feita ali é responsabilidade do
   painel do fabricante, não do AIR Control (research.md §6 documenta por quê).
4. Edge case: com o executável do painel M-Audio não encontrado (não instalado), confirme que
   o app mostra uma mensagem clara em vez de falhar silenciosamente.

## US4 — Faixa de trim -∞…+10dB (P4)

1. Arraste o trim de um canal até o mínimo: o valor exibido é "-∞ dB" e o canal fica
   completamente silencioso (mesmo com monitoramento ativo e sem mute).
2. Arraste até o máximo: o valor exibido é "+10.0 dB" e o sinal é audivelmente mais alto que
   sem trim.
3. Edge case (via teste automatizado, não manual): carregue um perfil salvo com `TrimDb =
   12.0` (valor antigo, agora fora da faixa) e confirme que ele carrega como `+10.0 dB` em vez
   de falhar.

Teste automatizado: `tests/AirControl.Core.Tests/TrimCalculatorTests.cs` e
`tests/AirControl.Integration.Tests/TrimIntegrationTests.cs`.

## Cobertura por contrato/dado

- `IRecordingFormatController`: ver
  [contracts/recording-format-contract.md](./contracts/recording-format-contract.md).
- Formas de dados e invariantes (trim, `RecordingFormat`, relação com dispositivo ativo): ver
  [data-model.md](./data-model.md).
- Decisões e alternativas descartadas: ver [research.md](./research.md).
