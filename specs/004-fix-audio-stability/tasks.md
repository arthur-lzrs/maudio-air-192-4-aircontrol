---

description: "Task list for Audio Stability & Consistency Fixes (004-fix-audio-stability)"
---

# Tasks: Audio Stability & Consistency Fixes

**Input**: Design documents from `/specs/004-fix-audio-stability/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Automated tests are REQUIRED per the project constitution (Testing Standards) and by the
feature itself (FR-019 / SC-006: every corrected root cause needs a regression test that fails
without the fix). Test tasks are therefore included and are NOT optional.

**Organization**: Tasks are grouped by user story (from spec.md) to enable independent
implementation and testing. Priority order: US1 (P1), US2 (P1), US3 (P2), US4 (P2), US5 (P3).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story the task belongs to (US1–US5); Setup/Foundational/Polish carry no story label
- Every task includes an exact file path

## Path Conventions

Three-assembly desktop app (unchanged from features 001/002/003):

- `src/AirControl.Core/` — pure domain logic & contracts (no NAudio/COM)
- `src/AirControl.Audio/` — real I/O (NAudio/WASAPI/COM/ASIO)
- `src/AirControl.App/` — WPF UI (ViewModels/Views)
- `tests/AirControl.Core.Tests/` — pure unit tests
- `tests/AirControl.Integration.Tests/` — integration tests with fakes

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the baseline needed before any stability work begins.

- [X] T001 Confirm baseline is green: run `dotnet build AirControl.sln` then `dotnet test AirControl.sln` from repo root and record the current pass count (this is the FR-021/SC-007 regression baseline that must not decrease)
- [X] T002 [P] Capture the current initialization order as executed today by reading `src/AirControl.App/App.xaml.cs` (`OnStartup`) and `src/AirControl.App/ViewModels/MainWindowViewModel.cs` (ctor + `OnConnectionChanged`/`RefreshDeviceDependentSections`), verifying it matches research.md §0 steps 1–6; note any drift as a comment block at the top of research.md §0

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Cross-thread event marshalling — research.md §4 (R2) is the shared root cause behind
the intermittent symptoms (empty routing field, frozen meter). It MUST be fixed before US1/US2
fixes can be relied upon.

**⚠️ CRITICAL**: No user story fix is trustworthy until marshalling is in place.

- [X] T003 Add an injectable UI-thread dispatch abstraction (e.g. `IUiDispatcher` wrapping a `SynchronizationContext`/`Dispatcher`) in `src/AirControl.Core/` so events crossing into the UI can be marshalled and tests can assert delivery thread without WPF
- [X] T004 [P] Implement the WPF-backed dispatcher (wraps `Application.Current.Dispatcher`) in `src/AirControl.App/` and wire it through DI/app composition in `src/AirControl.App/App.xaml.cs`
- [X] T005 Marshal `IMMNotificationClient` callbacks to the UI dispatcher before raising `ConnectionChanged`/`InputDevicesChanged` in `src/AirControl.Audio/AudioDeviceProvider.cs` (research.md §4 / R2)
- [X] T006 Marshal (and coalesce) `LevelsChanged` so subscribers always receive it on the UI thread in `src/AirControl.Audio/AudioEngine.cs` (research.md §4 / R2)

**Checkpoint**: Device and level events are guaranteed on the UI thread — user story fixes can now build on a race-free foundation.

---

## Phase 3: User Story 1 - App initializes correctly on every open (Priority: P1) 🎯 MVP

**Goal**: Every app open reaches the same functional state — routing mode selector always shows a
valid option or an actionable message, never a silent empty list; device-dependent fields
auto-populate when a valid device appears.

**Independent Test**: Open/close the app ≥20 times under varied conditions; in 100% of cycles the
routing mode selector presents ≥1 valid option (or an actionable message when channels are
indeterminable) and the persisted selection is restored (SC-001).

### Tests for User Story 1

- [X] T007 [P] [US1] Unit test `RoutingOptionsState` resolution for channel counts 0/1/2 (0 → not determinable + actionable message, never silent empty; 1 → mono; ≥2 → all modes) in `tests/AirControl.Core.Tests/RoutingOptionsTests.cs` (FR-002/FR-003, S1)
- [X] T008 [P] [US1] Integration test: 20 simulated startups (including `ActiveInputChannelCount == 0` transient and device-arrives-after-open) → routing selector never empty without a message, persisted selection restored, same final state every time in `tests/AirControl.Integration.Tests/StartupDeterminismIntegrationTests.cs` (SC-001, S6)

### Implementation for User Story 1

- [X] T009 [P] [US1] Create pure `RoutingOptionsState` (fields `AvailableModes`, `IsDeterminable`, `Message`; empty list only when `!IsDeterminable`) with resolution logic from channel count in `src/AirControl.Core/RoutingOptions.cs` (data-model §3)
- [X] T010 [US1] Use `RoutingOptionsState` in `src/AirControl.App/ViewModels/RoutingModeSelectorViewModel.cs` so `RefreshAvailableModes` shows an actionable message instead of an empty combobox when `ActiveInputChannelCount == 0`, and repopulates automatically when a valid device returns (FR-002/FR-003/FR-004, S1)
- [X] T011 [US1] Ensure the end-of-ctor device resolution in `src/AirControl.App/ViewModels/MainWindowViewModel.cs` runs after all handlers are wired and is idempotent (a second notification arriving right after produces the same final state) (research.md §5, S6, FR-005)

**Checkpoint**: Routing selector is never silently empty; startup is deterministic across repeats.

---

## Phase 4: User Story 2 - Monitoring & metering never freeze (Priority: P1)

**Goal**: Monitoring and meters keep reflecting the live signal; if the stream stops, the app
auto-recovers (bounded) or shows an actionable error state — never a silent freeze.

**Independent Test**: Run with live signal through provoked disturbances; meters return to the
signal in every case, or an explanatory error state appears within 5s (SC-002); after any config
change, monitoring returns within 3s (SC-003).

### Tests for User Story 2

- [ ] T012 [P] [US2] Unit test `AudioStreamHealth` transitions `Delivering→Stalled→Faulted→Delivering`, pure staleness function `(now, lastData, threshold) → bool`, and recovery-attempt cap (2 → Faulted) in `tests/AirControl.Core.Tests/AudioStreamHealthTests.cs` (contract §Audio Stream Health, S2)
- [ ] T013 [P] [US2] Integration test: simulated stall via fake → `Stalled` → bounded recovery or `Faulted` with actionable reason; `StreamHealthChanged` delivered on the UI thread in `tests/AirControl.Integration.Tests/StreamHealthIntegrationTests.cs` (SC-002/FR-007, S2)
- [ ] T014 [P] [US2] Integration test: device/level events delivered on the UI thread through the dispatcher abstraction in `tests/AirControl.Integration.Tests/EventMarshallingIntegrationTests.cs` (R2/S4/S6)

### Implementation for User Story 2

- [ ] T015 [P] [US2] Create pure `AudioStreamHealth` (`State {Delivering,Stalled,Faulted}`, `LastDataReceivedAt`, `RecoveryAttempts`, `FaultReason`) with staleness/recovery policy (5s threshold, 2 attempts) in `src/AirControl.Core/AudioStreamHealth.cs` (data-model §1)
- [ ] T016 [US2] Extend `IAudioEngine` with `AudioStreamHealth Health { get; }` and `event EventHandler<AudioStreamHealthChangedEventArgs>? StreamHealthChanged` (UI-thread delivery) in `src/AirControl.Core/IAudioEngine.cs`; add `AudioStreamHealthChangedEventArgs` in `src/AirControl.Core/Events.cs` (contract §Extensão de IAudioEngine)
- [ ] T017 [US2] In `src/AirControl.Audio/AudioEngine.cs`: update `LastDataReceivedAt` in `OnDataAvailable`, subscribe to `WasapiCapture.RecordingStopped` and `WasapiOut.PlaybackStopped` (a stop with exception → `Stalled`, never swallowed), run a UI-thread `DispatcherTimer` watchdog (compares `now - LastDataReceivedAt`, no driver polling), attempt bounded auto-recovery (≤2 Stop+Start, ≤500ms backoff) then `Faulted`, and raise `StreamHealthChanged` (FR-006/FR-007/FR-009, contract rules 1–6)
- [ ] T018 [US2] Orchestrate stream-health error/transient states in `src/AirControl.App/ViewModels/MainWindowViewModel.cs` — surface `Faulted` `FaultReason` as an actionable status message (reuse existing `StatusMessage` pattern), recover to normal on `Delivering` (FR-007, Constitution III)
- [ ] T019 [US2] Ensure `src/AirControl.App/ViewModels/ChannelMeterViewModel.cs` consumes marshalled `LevelsChanged` and never holds a frozen value across a `Stalled`/`Faulted` transition (FR-006, contract counter-example)

**Checkpoint**: Freezes become observable states with bounded recovery or an actionable error; metering never regresses (FR-010).

---

## Phase 5: User Story 3 - Recording-format options follow the driver sample rate (Priority: P2)

**Goal**: "Recording format (Windows)" only offers combinations matching the ASIO driver sample
rate; the real-time driver query happens inside a bounded, signalled reconfiguration pause instead
of querying ASIO while capture is active.

**Independent Test**: For each supported driver sample rate, the format list shows only that rate's
combinations (SC-004); with monitoring active the query-induced pause shows a transient indicator,
stays ≤2s, and monitoring returns (SC-004a); 30 min untouched → zero pauses (SC-004b).

### Tests for User Story 3

- [ ] T020 [P] [US3] Unit test `ReconfigurationPause`: deadline exceeded → `Faulted`; `mutateDevice` that throws → capture still re-established (finally); only valid `Trigger` values accepted in `tests/AirControl.Core.Tests/ReconfigurationPauseTests.cs` (contract §Reconfiguration Pause, S5)
- [ ] T021 [P] [US3] Integration test: format change and driver sample-rate change re-establish capture within the deadline (SC-004a); 30 min with no format/driver action → zero pauses (SC-004b); regression of S3/S5 in `tests/AirControl.Integration.Tests/ReconfigurationPauseIntegrationTests.cs`
- [ ] T022 [US3] Extend `tests/AirControl.Integration.Tests/PerformanceBudgetTests.cs` with the reconfiguration-pause budget (≤2s) and post-change recovery budget (≤3s) (SC-004a/SC-003)

### Implementation for User Story 3

- [ ] T023 [P] [US3] Create pure `ReconfigurationPause` policy (`Trigger {OpenFormatList,ChangeDriverSampleRate,ChangeActiveDevice,Startup}`, `Phase {InProgress,Completed,Faulted}`, `Deadline` default 2s, `ReconfigurationResult = Completed|Faulted(reason)`) with the `RunPause(trigger, mutateDevice, deadline)` shape and guaranteed re-establish semantics in `src/AirControl.Core/ReconfigurationPause.cs` (data-model §2, contract §Forma da operação)
- [ ] T024 [US3] Route `OnSelectedFormatChanged`'s Stop→mutate→Start through `ReconfigurationPause` (Start in `finally`) and move the real-time ASIO sample-rate query INSIDE the pause (replacing `FilterByAsioSampleRate` while capture is active) in `src/AirControl.App/ViewModels/RecordingFormatSelectorViewModel.cs` (FR-011/FR-015/FR-015a, S3/S5)
- [ ] T025 [US3] Route `OnSelectedSampleRateChanged`'s Stop→mutate→Start through `ReconfigurationPause` (Start in `finally`), refresh recording-format options immediately, and reconcile + report the applied format when the current format no longer matches the driver rate in `src/AirControl.App/ViewModels/DriverSettingsViewModel.cs` (FR-012/FR-013, S5)
- [ ] T026 [US3] Surface a visible "Reconfigurando…" transient state during any pause and an actionable error when a pause `Faulted` (deadline exceeded / query failed / rate indeterminable), reusing the shared status-message pattern, in `src/AirControl.App/ViewModels/MainWindowViewModel.cs` (FR-014/FR-015c/FR-015d)

**Checkpoint**: Format options always match the driver rate; the pause is bounded, visible, and event-triggered only.

---

## Phase 6: User Story 4 - Documented initialization investigation (Priority: P2)

**Goal**: A written record of the startup sequence, race conditions, each symptom's root cause, and
the regression test that covers each fix — so intermittent bugs are fixed at the root, not masked.

**Independent Test**: A reader can explain the init order and map every reported symptom to a root
cause and its regression test, without reading the code (SC-005/SC-006).

- [ ] T027 [US4] Finalize research.md §0–§1 against the implemented fixes: confirm the init sequence, race points (R1–R3), and the symptom→root-cause→fix→regression-test table (S1–S6) all reference the tests actually written (T007/T008/T012/T013/T014/T020/T021) in `specs/004-fix-audio-stability/research.md` (FR-016/FR-017/FR-018)
- [ ] T028 [US4] Verify FR-019/SC-006 traceability: every corrected root cause (S1–S6) has a named regression test that fails without the fix; add a short "fails-without-fix verified" note per row in `specs/004-fix-audio-stability/research.md` §1

**Checkpoint**: Investigation document is complete and every root cause is test-backed.

---

## Phase 7: User Story 5 - Technology review, decided and applied (Priority: P3)

**Goal**: A reviewable recommendation of the audio-layer/init technology (unused capabilities,
suboptimal config, alternatives) with benefit/cost/risk per item; approved items applied with
before/after measurement, reverted if they don't deliver.

**⚠️ GATE**: Implementation tasks (T031+) MUST NOT start until US1 & US2 fixes are delivered and the
suite is green (FR-020b), and only for items the owner approved (FR-020a).

**Independent Test**: Read research.md §7 and decide adopt/not-adopt per item without further
investigation; with approved changes applied, run the full suite + manual stability validation and
confirm no US1/US2 behavior regressed (SC-008/SC-009/SC-010).

- [ ] T029 [US5] Fill research.md §7 into a decision-ready recommendation: for each candidate (event-driven/exclusive `WasapiCapture`, richer `IMMNotificationClient`/session events, capture-layer alternatives, resampler/latency config) record concrete benefit, cost, risk, and a clear adopt/not-adopt recommendation with "how to measure the improvement" in `specs/004-fix-audio-stability/research.md` (FR-020)
- [ ] T030 [US5] Record the owner's per-item approval decision in research.md §7 (`TechnologyRecommendation.Approval`); only approved items proceed to implementation (FR-020a) in `specs/004-fix-audio-stability/research.md`
- [ ] T031 [US5] For each APPROVED item only: capture a before measurement, apply the change in the relevant `src/AirControl.Audio/`/`src/AirControl.Core/` file, capture the after measurement, and record both in research.md §7 (FR-020c) — gated on US1/US2 green (FR-020b)
- [ ] T032 [US5] For each applied item, re-run the full suite + V1/V2/V3 stability validation; revert any item that fails to deliver its improvement or introduces a regression and record the reason in research.md §7 (FR-020d/SC-010)

**Checkpoint**: Only approved, measured, non-regressing technology changes remain applied.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Final verification across stories.

- [ ] T033 [P] Verify UX consistency: reconfiguration/stall/faulted/indeterminable states all reuse the shared actionable status-message pattern; no silent empty list or silent pause (Constitution III, FR-003/FR-015c) across `src/AirControl.App/ViewModels/`
- [ ] T034 Re-run `dotnet test AirControl.sln` and confirm the full suite (features 001/002/003 + new regression tests) is green with pass count ≥ the T001 baseline (FR-021/SC-007)
- [ ] T035 Execute the real-hardware manual validation V1–V4 (and V5 after P1 green) from `specs/004-fix-audio-stability/quickstart.md` with the AIR 192|4 and record each result (SC-001/SC-002/SC-003/SC-004a/b/SC-010)
- [ ] T036 [P] Code cleanup: remove the now-obsolete `FilterByAsioSampleRate` active-capture query path and any dead ad-hoc Stop→mutate→Start code replaced by `ReconfigurationPause` (Constitution I — no dead code) in `src/AirControl.App/ViewModels/RecordingFormatSelectorViewModel.cs`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories (marshalling is the shared root cause)
- **US1 (Phase 3)**: Depends on Foundational
- **US2 (Phase 4)**: Depends on Foundational
- **US3 (Phase 5)**: Depends on Foundational; `ReconfigurationPause` (T023) is independent of US1/US2 but US3 UI wiring benefits from US2's health orchestration for the "returns to Delivering" checks
- **US4 (Phase 6)**: Depends on US1/US2/US3 fixes existing (documents them and their tests)
- **US5 (Phase 7)**: GATED — implementation only after US1 & US2 are green (FR-020b) and per-item owner approval (FR-020a)
- **Polish (Phase 8)**: Depends on all targeted stories complete

### User Story Dependencies

- **US1 (P1)**: Independent after Foundational
- **US2 (P1)**: Independent after Foundational
- **US3 (P2)**: Independent after Foundational (shares the dispatcher + benefits from US2 health signals for validation)
- **US4 (P2)**: Documentation — consumes the outputs of US1/US2/US3
- **US5 (P3)**: Hard gate on US1+US2 green and owner approval

### Within Each User Story

- Tests written first and MUST fail before implementation (FR-019/SC-006)
- Pure `Core` types before ViewModel wiring; contract extension before `Audio` implementation
- Core → Audio → App order for the health/pause primitives

### Parallel Opportunities

- Setup: T002 [P] alongside T001
- Foundational: T004 [P] alongside T003 (different assemblies)
- US1 tests T007/T008 [P] together; US2 tests T012/T013/T014 [P] together; US3 tests T020/T021 [P] together
- Pure Core types across stories (T009, T015, T023) are [P] once the dispatcher (T003) exists
- After Foundational, US1 and US2 can proceed in parallel by different developers

---

## Parallel Example: User Story 2

```bash
# Launch US2 tests together (write first, ensure they fail):
Task: "Unit test AudioStreamHealth transitions in tests/AirControl.Core.Tests/AudioStreamHealthTests.cs"
Task: "Integration test stall→recovery/faulted in tests/AirControl.Integration.Tests/StreamHealthIntegrationTests.cs"
Task: "Integration test event marshalling in tests/AirControl.Integration.Tests/EventMarshallingIntegrationTests.cs"

# Then the pure Core type (parallel with US1's T009 and US3's T023):
Task: "Create AudioStreamHealth in src/AirControl.Core/AudioStreamHealth.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1: Setup (establish green baseline)
2. Phase 2: Foundational (event marshalling — CRITICAL, unblocks reliability)
3. Phase 3: User Story 1 (routing never silently empty; deterministic startup)
4. **STOP and VALIDATE**: run T008 + manual V1 (20 startups)
5. Demo the MVP

### Incremental Delivery

1. Setup + Foundational → race-free foundation
2. US1 → deterministic startup → validate (SC-001) → demo
3. US2 → no silent freezes → validate (SC-002/SC-003) → demo
4. US3 → format follows driver rate via bounded pause → validate (SC-004a/b) → demo
5. US4 → investigation document finalized and test-backed
6. US5 → GATED on P1 green + approval → apply measured, non-regressing changes only

### Parallel Team Strategy

After Foundational (Phase 2):

- Developer A: US1 (Phase 3)
- Developer B: US2 (Phase 4)
- Developer C: US3 `ReconfigurationPause` Core + tests (T020/T023), integrating UI after US2 health lands

---

## Notes

- [P] = different files, no dependency on incomplete tasks
- Every corrected root cause (S1–S6) has a regression test that must fail without the fix (FR-019/SC-006)
- No fix may alter the audible path or the meter data source from feature 003 (FR-010/FR-021)
- The reconfiguration pause is triggered only by discrete events — never polling (FR-015b/SC-004b)
- US5 implementation is hard-gated on US1/US2 green + owner approval (FR-020a/b)
- Commit after each task or logical group; keep the suite green
