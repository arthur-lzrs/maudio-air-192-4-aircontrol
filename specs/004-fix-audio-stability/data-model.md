# Data Model: Audio Stability & Consistency Fixes

**Feature**: `004-fix-audio-stability` | **Date**: 2026-09-03

Estas entidades são de **estado em memória e política pura** — nenhuma altera o schema persistido
(`channel-settings.json`, `recording-format.json` permanecem como na feature 003; sem migração).
Derivam diretamente das Key Entities da spec.

---

## 1. `AudioStreamHealth` (novo — `AirControl.Core`)

Estado de saúde do fluxo de captura/reprodução. Fonte de verdade para "os medidores estão vivos?".

| Campo | Tipo | Regra |
|-------|------|-------|
| `State` | enum `{ Delivering, Stalled, Faulted }` | Deriva de `LastDataReceivedAt` + eventos de parada |
| `LastDataReceivedAt` | timestamp | Atualizado a cada `OnDataAvailable`; base do cálculo de staleness |
| `RecoveryAttempts` | int | Incrementa a cada tentativa automática; teto = 2 antes de `Faulted` |
| `FaultReason` | string? | Mensagem acionável quando `Faulted`; null caso contrário |

**Transições de estado:**

```
Delivering --(sem dados > 5s | RecordingStopped/PlaybackStopped)--> Stalled
Stalled     --(recuperação automática bem-sucedida, dados voltam)--> Delivering  [reset RecoveryAttempts]
Stalled     --(RecoveryAttempts >= 2 sem sucesso)-----------------> Faulted
Faulted     --(ação do usuário: reconectar/trocar dispositivo)----> Delivering  (via Start)
```

**Validação/regras:**
- `Stalled` MUST ser observável (evento) para a UI exibir estado de erro em ≤ 5s (SC-002).
- Nunca laço infinito: `Faulted` é terminal até uma ação explícita do usuário (FR-007).
- A avaliação de staleness é **pura** (recebe "agora" e `LastDataReceivedAt`), testável sem timer.

Deriva de: *Estado de saúde do fluxo de áudio* (spec Key Entities).

---

## 2. `ReconfigurationPause` (novo — `AirControl.Core`)

Interrupção deliberada e limitada da captura, distinta do congelamento.

| Campo | Tipo | Regra |
|-------|------|-------|
| `Trigger` | enum `{ OpenFormatList, ChangeDriverSampleRate, ChangeActiveDevice, Startup }` | Só estes; nenhum polling (FR-015b) |
| `Phase` | enum `{ InProgress, Completed, Faulted }` | `InProgress` MUST ser visível ao usuário (FR-015c) |
| `Deadline` | duration | Teto de 2s (SC-004a); excedê-lo → `Faulted` (FR-015d) |
| `Result` | success \| error(message) | `error` produz estado acionável, nunca pausa silenciosa |

**Ciclo de vida:**

```
(evento discreto) --> InProgress[Stop captura] --> (mutar dispositivo) --> [Start captura]
    --> Completed            (captura restabelecida dentro do Deadline)
    --> Faulted(reason)      (Deadline excedido OU Start falhou) — captura restabelecida é tentada em finally
```

**Validação/regras:**
- MUST terminar sempre com uma tentativa de restabelecer a captura (finally), inclusive quando a
  mutação falha (FR-015a, corrige S5 em research.md §1).
- MUST NOT ser disparável por caminho periódico/especulativo (FR-015b/SC-004b).
- Distinta de `AudioStreamHealth.Stalled`: uma pausa é `InProgress` (esperada, visível, com fim
  previsto); um congelamento é `Stalled` (involuntário, sem fim previsto).

Deriva de: *Pausa de reconfiguração* (spec Key Entities).

---

## 3. `RoutingOptionsState` (novo — `AirControl.Core`)

Resolução das opções de modo de roteamento a partir do estado do dispositivo, com um estado
explícito de "não determinável" para nunca cair em lista vazia sem explicação.

| Campo | Tipo | Regra |
|-------|------|-------|
| `AvailableModes` | lista de `RoutingMode` | Vazia **somente** quando `IsDeterminable == false` |
| `IsDeterminable` | bool | `false` quando `ActiveInputChannelCount == 0` (canais desconhecidos) |
| `Message` | string? | Mensagem acionável quando `!IsDeterminable` (FR-003) |

**Regras (pura, testável):**
- `channelCount >= 2` → todos os modos; `channelCount == 1` → `Input1Mono`; `channelCount == 0` →
  `IsDeterminable = false` + mensagem, **nunca** lista vazia silenciosa (FR-002/FR-003, corrige S1).
- Repopula automaticamente quando um dispositivo válido volta (FR-004).

Deriva de: *Estado do dispositivo ativo* (spec Key Entities) + comportamento do seletor de roteamento.

---

## 4. `InvestigationFinding` (documental — research.md §1)

Não é tipo de código; é a linha da tabela de rastreamento em research.md §1.

| Campo | Regra |
|-------|-------|
| `Symptom` | Relatado ou encontrado (FR-018 exige reportar os não relatados) |
| `RootCause` | Identificada, ou registro explícito de não-reprodução + plano de monitoramento (FR-017) |
| `Fix` | Correção aplicada |
| `RegressionTest` | Teste que falha sem a correção e passa com ela (FR-019/SC-006) |
| `Severity` | alta \| média \| baixa (FR-018) |

Deriva de: *Achado de investigação* (spec Key Entities).

---

## 5. `TechnologyRecommendation` (documental — research.md §7)

Não é tipo de código; é o item da revisão de tecnologia.

| Campo | Regra |
|-------|-------|
| `Capability/Alternative` | Capacidade não aproveitada, config subótima, ou alternativa (FR-020) |
| `Benefit` / `Cost` / `Risk` | Obrigatórios por item (FR-020) |
| `Approval` | Decisão do responsável; só aprovados são aplicados (FR-020a) |
| `BeforeAfterMeasurement` | Medição comparável por item aplicado (FR-020c) |
| `Outcome` | aplicada \| revertida (com motivo) (FR-020d) |

Deriva de: *Recomendação de tecnologia* (spec Key Entities).

---

## 6. Entidades reutilizadas sem alteração de schema

- **Sequência de inicialização** (spec Key Entities) → documentada em research.md §0; codificada
  como comportamento idempotente/isolado, não como novo tipo persistido.
- **Estado do dispositivo ativo** → permanece em `AudioInputDeviceInfo`
  (`Id`, `FriendlyName`, `ChannelCount`, `IsAirDevice`) e `IAudioEngine.ActiveInputChannelCount`;
  fonte de verdade de `RoutingOptionsState` e das seções dependentes do dispositivo.
