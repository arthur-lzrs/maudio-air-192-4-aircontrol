# Quickstart & Validation Guide: Audio Stability & Consistency Fixes

**Feature**: `004-fix-audio-stability` | **Date**: 2026-09-03

Guia de validação que prova, ponta a ponta, que os sintomas foram corrigidos na causa-raiz.
Combina suíte automatizada (sem hardware) com validação manual com hardware real — obrigatória
porque congelamentos intermitentes não são 100% reproduzíveis com dispositivos simulados
(Assumptions da spec). Referencia contratos e entidades em vez de duplicá-los.

## Pré-requisitos

- Windows 10/11 x64, .NET 8 SDK.
- Para a validação manual: M-Audio AIR 192|4 (mesmo hardware de referência das features anteriores)
  com driver instalado, e uma fonte de sinal ao vivo nas entradas.

## Build & suíte automatizada

```bash
dotnet build AirControl.sln
```

```bash
dotnet test AirControl.sln
```

Espera-se: **toda a suíte verde**, incluindo os testes existentes das features 001/002/003
(FR-021/SC-007 — nada regride) e os novos testes de regressão desta feature.

## Cenários automatizados (mapeados a Success Criteria)

| Cenário | Teste | Prova |
|---------|-------|-------|
| 20 startups → roteamento nunca vazio | `StartupDeterminismIntegrationTests` | SC-001 / FR-002 / S1 |
| Fluxo parado detectado → recuperação ou erro | `StreamHealthIntegrationTests` | SC-002 / FR-007 / S2 |
| Alteração de config → monitoração volta ≤ 3s | `ReconfigurationPauseIntegrationTests` | SC-003 / FR-008 |
| Pausa restabelece captura ≤ teto; sem pausa sem ação | `ReconfigurationPauseIntegrationTests` | SC-004a/b / FR-015a–d / S3 / S5 |
| Eventos de dispositivo/nível na thread da UI | `EventMarshallingIntegrationTests` | R2 / S4 / S6 |
| Opções de roteamento por contagem de canais | `RoutingOptionsTests` | FR-002/003 / S1 |
| Staleness + transições de saúde | `AudioStreamHealthTests` | contrato §health |
| Metering independente de mute/solo/monitoração | `MeteringIntegrationTests` (existente) | FR-010 / não pode regredir |

Cada teste de regressão MUST falhar sem a correção correspondente e passar com ela (FR-019/SC-006).

## Validação manual com hardware real (obrigatória)

Executar com o AIR 192|4 conectado e sinal ao vivo. Registrar resultado de cada item.

### V1 — Determinismo de startup (User Story 1 / SC-001)

1. Abrir e fechar o app **20 vezes** variando condições: dispositivo já conectado; conectado depois
   de abrir; desconectado; outro app usando o dispositivo em modo exclusivo.
2. Esperado: em **100%** dos ciclos, "Modo de roteamento" mostra ≥ 1 opção válida (ou uma mensagem
   acionável quando os canais são indetermináveis — nunca lista vazia silenciosa) e a seleção
   persistida é restaurada.

### V2 — Monitoração nunca congela (User Story 2 / SC-002/SC-003)

1. Manter o app rodando com sinal ao vivo por **≥ 60 min**, provocando perturbações: trocar
   configurações no app; abrir/fechar o painel do fabricante; iniciar outro app de áudio; suspender
   e retomar a máquina; desconectar e reconectar o dispositivo.
2. Esperado: os medidores voltam a refletir o sinal em **cada** caso (recuperação automática), ou
   exibem estado de erro acionável em ≤ 5s — nunca congelam silenciosamente. Após qualquer
   alteração feita por você, a monitoração volta em ≤ 3s sem ação adicional.

### V3 — Formato segue o sample rate do driver (User Story 3 / SC-004a/b)

1. Para cada sample rate suportado pelo driver: abrir a lista de "Formato de gravação (Windows)" e
   confirmar que só as combinações daquele sample rate aparecem.
2. Com a monitoração ativa, ao abrir a lista/alterar o sample rate, confirmar que aparece um
   indicador "Reconfigurando…" durante a pausa, que a pausa dura ≤ 2s, e que os medidores voltam ao
   fim (referência: [reconfiguration-pause-contract.md](./contracts/reconfiguration-pause-contract.md)).
3. Deixar **30 min** sem tocar em nenhum controle de formato/driver e confirmar **zero** pausas de
   reconfiguração (SC-004b).

### V4 — Documento de investigação (User Story 4 / SC-005/SC-006)

1. Ler [research.md](./research.md) §0–§1 e, sem abrir o código, explicar a ordem de inicialização
   e mapear cada sintoma relatado à sua causa-raiz.
2. Confirmar que cada causa-raiz corrigida tem um teste de regressão correspondente na suíte.

### V5 — Revisão de tecnologia (User Story 5 / SC-008/SC-009/SC-010)

> Só após as correções P1 (V1/V2) verdes (FR-020b).

1. Ler [research.md](./research.md) §7 e decidir adotar/não-adotar cada item sem investigação
   adicional.
2. Para cada item aprovado e aplicado, confirmar a medição antes/depois registrada (FR-020c) e que
   V1/V2/V3 continuam passando (SC-010). Item sem melhoria ou com regressão → revertido, motivo
   registrado (FR-020d).

## Critério de conclusão

- Suíte automatizada integralmente verde.
- V1–V4 validados com hardware real e registrados.
- V5 executado após P1 verde, com aprovações e medições registradas para os itens aplicados.
