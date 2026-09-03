---

description: "Task list template for feature implementation"
---

# Tasks: Channel Routing & Device Selection

**Input**: Design documents from `/specs/002-channel-routing-device-select/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/audio-engine-contract.md, quickstart.md

**Tests**: Included per the project constitution's Testing Standards principle and plan.md's
Testing section — unit tests for pure routing logic in `AirControl.Core.Tests`, integration tests
against `IAudioEngine`/`IAudioDeviceProvider` fakes/real implementations in
`AirControl.Integration.Tests`.

**Organization**: Tasks are grouped by user story (US1, US2, US3 from spec.md) to enable
independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- File paths are relative to the repository root

---

## Phase 1: Setup

**Purpose**: Confirm the existing three-assembly structure (from feature 001) needs no new
projects or dependencies for this feature.

- [X] T001 Verify `src/AirControl.Core`, `src/AirControl.Audio`, `src/AirControl.App`,
      `tests/AirControl.Core.Tests`, `tests/AirControl.Integration.Tests` all build cleanly on the
      `002-channel-routing-device-select` branch before starting (`dotnet build`), establishing a
      clean baseline

**Checkpoint**: Baseline build green, no new projects/dependencies required (per plan.md Technical
Context — no new external dependencies).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core domain types and contract changes that every user story (routing modes and
device selection) is built on top of. Must be complete before any user story phase starts.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] Create `RoutingMode` enum (`Stereo`, `Input1Mono`, `Input2Mono`, `CombinedMono`) and
      the pure, static `RoutingModeApplier` type in `src/AirControl.Core/RoutingMode.cs`, with
      `(float Left, float Right) Apply(RoutingMode mode, float input1, float input2)` (Combined
      Mono = `(input1 + input2) * 0.5`, i.e. -6dB compensation per research.md §2),
      `bool IsSupported(RoutingMode mode, int channelCount)`, and
      `RoutingMode ResolveFallback(RoutingMode requested, int channelCount)` (fallback to
      `Input1Mono` when `channelCount == 1`, else `Stereo`, per research.md §6 and data-model.md)
- [X] T003 [P] Create `AudioInputDeviceInfo` record (`Id`, `FriendlyName`, `ChannelCount`,
      `IsAirDevice`) in `src/AirControl.Core/AudioInputDeviceInfo.cs`, mirroring
      `AudioOutputDeviceInfo` (data-model.md)
- [X] T004 Extend `IAudioDeviceProvider` in `src/AirControl.Core/IAudioDeviceProvider.cs` with
      `IReadOnlyList<AudioInputDeviceInfo> GetAvailableInputDevices()` (contracts/audio-engine-contract.md)
- [X] T005 Extend `IAudioEngine` in `src/AirControl.Core/IAudioEngine.cs`: change
      `Start(string outputDeviceId)` to `Start(string? inputDeviceId, string outputDeviceId)`, add
      `RoutingMode RoutingMode { get; }`, `void SetRoutingMode(RoutingMode mode)`, and
      `int ActiveInputChannelCount { get; }` (contracts/audio-engine-contract.md) — depends on T002
- [X] T006 Extend `ChannelSettingsProfile` record in `src/AirControl.Core/ChannelSettings.cs` with
      `RoutingMode RoutingMode` (default `Stereo`) and `string? InputDeviceId` (default `null`),
      keeping backward-compatible JSON deserialization for files saved by feature 001
      (data-model.md, research.md §5) — depends on T002
- [X] T007 Update `AirControl.App` call sites of `IAudioEngine.Start(string)` (e.g.
      `src/AirControl.App/ViewModels/MainWindowViewModel.cs`) to compile against the new
      `Start(string?, string)` signature, passing `null` for input device for now (real
      auto-detect/manual selection wiring happens in US3) — depends on T005
- [X] T008 [P] Update `FakeAudioEngine` in
      `tests/AirControl.Integration.Tests/Fakes/FakeAudioEngine.cs` to implement the new
      `IAudioEngine` members (`Start(string?, string)`, `RoutingMode`, `SetRoutingMode`,
      `ActiveInputChannelCount`) so existing and new integration tests compile — depends on T005
- [X] T009 [P] Update `FakeAudioDeviceProvider` in
      `tests/AirControl.Integration.Tests/Fakes/FakeAudioDeviceProvider.cs` to implement
      `GetAvailableInputDevices()` — depends on T004

**Checkpoint**: Solution builds and existing feature-001 tests still pass; `RoutingMode`,
`RoutingModeApplier`, `AudioInputDeviceInfo`, and the extended contracts exist and compile.
User story implementation can now begin.

---

## Phase 3: User Story 1 - Hear a single microphone centered, not panned to one side (Priority: P1) 🎯 MVP

**Goal**: A user with a mic on Input 1 only can select a mono routing mode and hear/see it
centered on both output channels instead of panned left.

**Independent Test**: Connect a mic to Input 1 only, select "Input 1 as mono" routing mode, and
confirm the monitored/metered signal is audible equally on both left and right.

### Tests for User Story 1

- [X] T010 [P] [US1] Unit tests in `tests/AirControl.Core.Tests/RoutingModeTests.cs` for
      `RoutingModeApplier.Apply` with `Input1Mono` and `Input2Mono` (input duplicated equally to
      Left and Right, other input ignored) and for `IsSupported`/`ResolveFallback` with
      `channelCount == 1` (only `Input1Mono` supported, fallback always `Input1Mono`)
- [X] T011 [P] [US1] Integration test in
      `tests/AirControl.Integration.Tests/RoutingIntegrationTests.cs` asserting that switching to
      `Input1Mono` (or `Input2Mono`) causes `LevelsChanged` to report equal Left/Right levels for
      the routed input within the 100ms budget (SC-002), using `FakeAudioEngine`/real engine per
      existing `PerformanceBudgetTests.cs` pattern

### Implementation for User Story 1

- [X] T012 [US1] In `src/AirControl.Audio/AudioEngine.cs`, apply `RoutingModeApplier.Apply` in
      `OnDataAvailable` after the existing trim/mute/solo resolution (`input1Out`, `input2Out`) and
      before `SampleFormatIO.WriteSample`/`RaiseLevels` (research.md §1), backing the new
      `RoutingMode`/`SetRoutingMode`/`ActiveInputChannelCount` members added to `IAudioEngine` in
      T005; `SetRoutingMode` stores `RoutingModeApplier.ResolveFallback(mode, ActiveInputChannelCount)`
      — depends on T002, T005
- [X] T013 [P] [US1] Create `RoutingModeSelectorViewModel` in
      `src/AirControl.App/ViewModels/RoutingModeSelectorViewModel.cs`, exposing the 4 `RoutingMode`
      values, the currently selected mode (bound to `IAudioEngine.SetRoutingMode`), and persisting
      the choice via `ISettingsRepository`/`ChannelSettingsProfile.RoutingMode` on change — depends
      on T005, T006
- [X] T014 [P] [US1] Create `RoutingModeSelectorView.xaml` (+ code-behind) in
      `src/AirControl.App/Views/`, reusing the combo/list visual pattern and `AutomationProperties`
      already established by `OutputDeviceSelectorView.xaml` (Constitution III) — depends on T013
- [X] T015 [US1] Wire `RoutingModeSelectorViewModel` into
      `src/AirControl.App/ViewModels/MainWindowViewModel.cs`: compose it alongside the existing
      channel/monitoring view-models, load the persisted `RoutingMode` on startup via
      `ChannelSettingsProfile`, and apply it through `IAudioEngine.SetRoutingMode` — depends on
      T012, T013
- [X] T016 [US1] Integration test in
      `tests/AirControl.Integration.Tests/TrimPersistenceIntegrationTests.cs` (or a new
      `RoutingPersistenceIntegrationTests.cs`) asserting a saved `RoutingMode` (e.g. `Input1Mono`)
      is restored automatically on the next `SettingsRepository.Load()` (Acceptance Scenario 3) —
      depends on T006, T015

**Checkpoint**: User Story 1 fully functional and testable independently — mono routing modes
work, apply within budget, and persist across restarts.

---

## Phase 4: User Story 2 - Choose a routing mode from the standard set for a 2-input interface (Priority: P1)

**Goal**: The user can pick from the full set — Stereo, Input 1 Mono, Input 2 Mono, Combined Mono —
and each behaves exactly as described, with glitch-free switching and persistence of any mode.

**Independent Test**: Cycle through each routing mode with known signals on Input 1 and Input 2
and confirm the output channels match that mode's description.

### Tests for User Story 2

- [X] T017 [P] [US2] Unit tests in `tests/AirControl.Core.Tests/RoutingModeTests.cs` for
      `RoutingModeApplier.Apply` with `Stereo` (Input 1 → Left only, Input 2 → Right only) and
      `CombinedMono` (`(input1 + input2) * 0.5` on both Left and Right, verifying no clipping from
      two full-scale inputs summed) and for `IsSupported`/`ResolveFallback` with `channelCount == 2`
      (all 4 modes supported, no fallback needed)
- [X] T018 [P] [US2] Integration test in
      `tests/AirControl.Integration.Tests/RoutingIntegrationTests.cs` asserting: (a) Stereo mode
      reports Input 1 only on Left / Input 2 only on Right via `LevelsChanged`; (b) Combined Mono
      reports the compensated summed level equally on both channels, with `IsClipping` reflecting
      only genuine clipping of the compensated sum (contracts/audio-engine-contract.md); (c)
      switching between any two of the 4 modes with active signal completes within the 100ms
      budget with no dropout longer than the existing control-change tolerance
- [X] T019 [P] [US2] Integration test verifying trim/mute/solo continue to apply before routing
      (FR-006): with `CombinedMono` active, muting Input 2 makes the combined output equal Input 1
      alone (edge case from quickstart.md scenario 7), added to
      `tests/AirControl.Integration.Tests/RoutingIntegrationTests.cs`

### Implementation for User Story 2

- [X] T020 [US2] In `src/AirControl.App/ViewModels/RoutingModeSelectorViewModel.cs`, expose which
      modes are currently enabled/visible by calling `RoutingModeApplier.IsSupported` against
      `IAudioEngine.ActiveInputChannelCount` (FR-005), hiding/disabling `Stereo`, `Input2Mono`, and
      `CombinedMono` when only 1 channel is active — depends on T012, T013
- [X] T021 [US2] Integration test in
      `tests/AirControl.Integration.Tests/RoutingIntegrationTests.cs` (or
      `RoutingPersistenceIntegrationTests.cs`) asserting a saved `RoutingMode` of `Stereo` or
      `CombinedMono` is restored automatically on next load (Acceptance Scenario 4, generalizing
      T016 to the full mode set) — depends on T006, T015

**Checkpoint**: All 4 routing modes are selectable, correctly mapped/metered, switch without
glitches, respect FR-005/FR-006, and persist. Combined with US1, the full routing feature (P1
scope) is complete.

---

## Phase 5: User Story 3 - Choose which audio device the app uses, with the M-Audio interface as the default (Priority: P2)

**Goal**: The app auto-selects the M-Audio AIR interface when present, offers a device selector for
manual choice, persists manual selections, and falls back cleanly on disconnect.

**Independent Test**: With an M-Audio AIR and at least one other input device connected, confirm
auto-selection of the AIR on launch, then use the picker to switch to the other device and confirm
the app starts using it.

### Tests for User Story 3

- [X] T022 [P] [US3] Integration tests in
      `tests/AirControl.Integration.Tests/DeviceSelectionIntegrationTests.cs`: auto-selects the
      M-Audio AIR device when present and no manual selection is stored (FR-008, Acceptance
      Scenario 1); with no AIR and no valid manual selection, exposes a clear "needs selection"
      state instead of guessing (FR-009, Acceptance Scenario 2)
- [X] T023 [P] [US3] Integration tests in
      `tests/AirControl.Integration.Tests/DeviceSelectionIntegrationTests.cs`: manually selecting a
      device switches active channels/meters/routing to it (FR-010, Acceptance Scenario 3); the
      manual selection persists and is restored on restart while the device is still connected
      (FR-011, Acceptance Scenario 4); if the manually selected device is disconnected, the app
      falls back to auto-detecting the AIR or prompts for selection (FR-011/FR-009, Acceptance
      Scenario 5)
- [X] T024 [P] [US3] Integration test in
      `tests/AirControl.Integration.Tests/DeviceSelectionIntegrationTests.cs` for the FR-005 edge
      case: switching to a 1-channel device while `CombinedMono`/`Stereo`/`Input2Mono` is active
      causes `RoutingMode` to fall back to `Input1Mono` automatically (quickstart.md scenarios 13-14)

### Implementation for User Story 3

- [X] T025 [US3] Implement `AudioDeviceProvider.GetAvailableInputDevices()` in
      `src/AirControl.Audio/AudioDeviceProvider.cs` using
      `_enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)` and
      `device.AudioClient.MixFormat.Channels` for `ChannelCount`, with `IsAirDevice` set when
      `FriendlyName` contains "AIR 192" case-insensitively (research.md §3) — depends on T003, T004
- [X] T026 [US3] In `src/AirControl.Audio/AudioEngine.cs`, implement the new
      `Start(string? inputDeviceId, string outputDeviceId)`: resolve `inputDeviceId` to a connected
      device if provided and still present, else fall back to the existing "AIR 192" auto-detect
      logic, else throw `InvalidOperationException` (mirroring `ResolveOutputDevice`,
      research.md §4); after resolving, set `ActiveInputChannelCount` from the capture
      `WaveFormat.Channels` and revalidate the stored `RoutingMode` via
      `RoutingModeApplier.ResolveFallback` — depends on T005, T012
- [X] T027 [P] [US3] Create `InputDeviceSelectorViewModel` in
      `src/AirControl.App/ViewModels/InputDeviceSelectorViewModel.cs`, listing devices from
      `IAudioDeviceProvider.GetAvailableInputDevices()`, reflecting the active device, calling
      `IAudioEngine.Stop()`/`Start(deviceId, outputDeviceId)` on manual selection, and persisting
      the manual choice to `ChannelSettingsProfile.InputDeviceId` (auto-selection does NOT write
      `InputDeviceId`, per data-model.md) — depends on T004, T006, T025, T026
- [X] T028 [P] [US3] Create `InputDeviceSelectorView.xaml` (+ code-behind) in
      `src/AirControl.App/Views/`, reusing the visual pattern and `AutomationProperties` of
      `OutputDeviceSelectorView.xaml` (Constitution III) — depends on T027
- [X] T029 [US3] Wire `InputDeviceSelectorViewModel` into
      `src/AirControl.App/ViewModels/MainWindowViewModel.cs`: on startup, resolve the input device
      per FR-008/FR-009/FR-011 (persisted `InputDeviceId` if still connected → else auto-detect AIR
      → else "needs selection" prompt state), start the engine with the resolved device, and refresh
      `RoutingModeSelectorViewModel`'s enabled modes after any device switch (FR-005 revalidation) —
      depends on T007, T015, T020, T027
- [X] T030 [US3] Extend disconnect handling (reusing the existing pattern from
      `src/AirControl.App/ViewModels/DeviceStatusViewModel.cs` /
      `DeviceConnectionIntegrationTests.cs`) so losing the active input device clearly indicates
      device loss while keeping `InputDeviceSelectorView` available to pick another connected
      device (FR-012) — depends on T029

**Checkpoint**: All 3 user stories work independently and together — device auto-detection,
manual selection, persistence, disconnect fallback, and routing-mode revalidation on device
switch are all functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Improvements spanning multiple user stories.

- [X] T031 [P] Add a routing/device switch performance budget test to
      `tests/AirControl.Integration.Tests/PerformanceBudgetTests.cs`, asserting `SetRoutingMode`
      and device switch (`Stop`+`Start`) reflect in `LevelsChanged` within 100ms (SC-002),
      following the file's existing pattern
- [X] T032 [P] Verify UX consistency (Constitution III): `RoutingModeSelectorView` and
      `InputDeviceSelectorView` share the same combo/list visual style, spacing, and
      `AutomationProperties` naming conventions as `OutputDeviceSelectorView`
- [X] T033 Run the manual validation checklist in
      `specs/002-channel-routing-device-select/quickstart.md` (all 14 scenarios) against real
      M-Audio AIR hardware and confirm SC-001 through SC-005 are met
- [X] T034 Code cleanup pass: remove any now-dead single-device-assumption code paths in
      `src/AirControl.Audio/AudioEngine.cs`/`AudioDeviceProvider.cs` left over from the old
      hardcoded "AIR 192" `Start(string)` behavior (Constitution I)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
- **User Story 1 (Phase 3, P1)**: Depends on Foundational only
- **User Story 2 (Phase 4, P1)**: Depends on Foundational; shares
  `RoutingModeSelectorViewModel`/`View` and the `AudioEngine` routing pipeline built in US1
  (T012-T015), so implement after US1 for the pipeline/UI reuse, though its own tests (T017-T019)
  can be written in parallel with US1
- **User Story 3 (Phase 5, P2)**: Depends on Foundational; its device-switch wiring (T029) also
  depends on US1's `MainWindowViewModel` composition (T015) and US2's channel-count-aware selector
  (T020)
- **Polish (Phase 6)**: Depends on all desired user stories being complete

### Within Each User Story

- Tests before implementation (write and confirm they fail first)
- Core/pure logic (Core project) before Audio implementation before App/UI
- Story complete and checkpointed before moving to the next priority

### Parallel Opportunities

- T002 and T003 (independent new Core files) in parallel
- T008 and T009 (independent fake files) in parallel, after T004/T005
- T010, T011 (US1 tests) in parallel with each other
- T013 and T014 can start once T012 lands, but T014 depends on T013 completing its bindings first
- T017, T018, T019 (US2 tests) in parallel with each other, and in parallel with US1 implementation
- T022, T023, T024 (US3 tests) in parallel with each other
- T027 and T028 in parallel is not possible (View depends on ViewModel bindings) — sequential
- T031 and T032 (Polish) in parallel

---

## Parallel Example: Foundational Phase

```bash
# Launch independent new Core types together:
Task: "Create RoutingMode enum + RoutingModeApplier in src/AirControl.Core/RoutingMode.cs"
Task: "Create AudioInputDeviceInfo record in src/AirControl.Core/AudioInputDeviceInfo.cs"
```

## Parallel Example: User Story 1 Tests

```bash
Task: "Unit tests for RoutingModeApplier mono modes in tests/AirControl.Core.Tests/RoutingModeTests.cs"
Task: "Integration test for mono routing within 100ms budget in tests/AirControl.Integration.Tests/RoutingIntegrationTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (blocks everything)
3. Complete Phase 3: User Story 1 — mono routing fixes the reported panning problem
4. **STOP and VALIDATE**: quickstart.md scenarios 1-3 against real hardware
5. This alone resolves the user's primary complaint and is deployable

### Incremental Delivery

1. Setup + Foundational → foundation ready
2. User Story 1 → validate → mic-centering fix ships (MVP)
3. User Story 2 → validate → full 4-mode routing selector ships
4. User Story 3 → validate → device selection ships
5. Polish → performance budget, UX consistency, full manual validation

## Notes

- [P] tasks touch different files with no unresolved dependency between them
- [Story] label maps each task to spec.md's US1/US2/US3 for traceability
- Combined Mono's -6dB compensation (T002, T017) must be verified against clipping detection
  (`LevelMetering`) already established by feature 001 — no changes needed there, only new tests
- Commit after each task or logical group; stop at any checkpoint to validate independently

---

## Phase 7: Convergence

- [X] T035 In `src/AirControl.App/ViewModels/MainWindowViewModel.cs`, call
      `InputDeviceSelector.ResolveActiveDevice()` (and, if resolution succeeds,
      `RoutingModeSelector.ApplyPersistedMode()` + set `CaptureFormatDescription`) unconditionally
      in the constructor instead of only when `deviceProvider.IsAirDeviceConnected` is already true
      at startup — otherwise a previously manually-selected non-AIR device is never restored on
      launch (FR-011, US3/AC4) and no device is available but no M-Audio AIR is connected either,
      the app never surfaces the "needs selection" state at launch (FR-009, US3/AC2) per FR-008/FR-009/FR-011 (partial)
- [X] T036 [P] Add an integration test in
      `tests/AirControl.Integration.Tests/DeviceSelectionIntegrationTests.cs` constructing
      `MainWindowViewModel` with no M-Audio AIR connected at startup: (a) with a valid persisted
      manual `InputDeviceId` for a still-connected non-AIR device, asserting the engine starts with
      that device (US3/AC4); (b) with no AIR and no valid persisted selection, asserting
      `InputDeviceSelector.NeedsSelection` is `true` and the engine is not started (US3/AC2) per US3/AC2, US3/AC4 (missing)
</content>
