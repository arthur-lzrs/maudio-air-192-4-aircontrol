---

description: "Task list template for feature implementation"
---

# Tasks: Device & Monitoring Audio Controls

**Input**: Design documents from `/specs/003-device-audio-controls/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/recording-format-contract.md, quickstart.md

**Tests**: Automated tests are included per the project constitution's Testing Standards principle — unit tests for pure domain logic (`AirControl.Core.Tests`), integration tests with fakes for anything crossing the engine/UI boundary (`AirControl.Integration.Tests`), plus manual verification steps documented in quickstart.md for what depends on real Windows/M-Audio hardware (Constitution II, same pattern as `RealHardwarePerformanceBudgetTests.cs`).

**Organization**: Tasks are grouped by user story (US1–US4, in spec.md priority order) to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- Paths are relative to the repository root (`C:\Users\Arthur\Documents\AIR Control`)

## Path Conventions

Single project, three assemblies (per plan.md, unchanged from features 001/002):
- `src/AirControl.Core/` — pure domain (no NAudio/COM)
- `src/AirControl.Audio/` — real I/O (NAudio/WASAPI/COM)
- `src/AirControl.App/` — WPF UI
- `tests/AirControl.Core.Tests/`, `tests/AirControl.Integration.Tests/`

---

## Phase 1: Setup

No new project/dependency initialization is needed — this feature extends the existing three-assembly solution with no new NuGet packages (plan.md). Skipping straight to Foundational.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared domain types and repository plumbing that multiple user stories build on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T001 [P] Create `RecordingFormat` record with `Default` (48000/32) in [src/AirControl.Core/RecordingFormat.cs](../../src/AirControl.Core/RecordingFormat.cs) (data-model.md)
- [ ] T002 [P] Create `IRecordingFormatController` interface (`GetCurrentFormat`, `GetSupportedFormats`, `TrySetFormat`) in [src/AirControl.Core/IRecordingFormatController.cs](../../src/AirControl.Core/IRecordingFormatController.cs) per [contracts/recording-format-contract.md](./contracts/recording-format-contract.md)
- [ ] T003 [P] Create `IRecordingFormatRepository` (or extend `ISettingsRepository`, per data-model.md's "same pattern as `SettingsRepository`") with `Load(string deviceId)`/`Save(string deviceId, RecordingFormat format)` contract in [src/AirControl.Core/IRecordingFormatRepository.cs](../../src/AirControl.Core/IRecordingFormatRepository.cs)
- [ ] T004 [US-shared] Implement `RecordingFormatRepository` persisting to `%AppData%\AirControl\recording-format.json`, keyed by `deviceId`, in [src/AirControl.Core/RecordingFormatRepository.cs](../../src/AirControl.Core/RecordingFormatRepository.cs) (depends on T003)
- [ ] T005 [P] Create `FakeRecordingFormatController` (configurable current/supported formats, forced `TrySetFormat` failure) in [tests/AirControl.Integration.Tests/Fakes/FakeRecordingFormatController.cs](../../tests/AirControl.Integration.Tests/Fakes/FakeRecordingFormatController.cs) per contracts/recording-format-contract.md (depends on T002)

**Checkpoint**: Domain types/contracts for recording format exist and are testable — user stories can now proceed.

---

## Phase 3: User Story 1 - Meters keep measuring regardless of monitoring/mute/solo (Priority: P1) 🎯 MVP

**Goal**: Meters reflect the real post-trim input signal at all times, independent of mute/solo/monitoring state; only the audible output path is gated.

**Independent Test**: With a live input signal, toggle monitoring off, mute the channel, then solo the other channel — the affected channel's meter keeps moving in every case, while no audio is heard when it should be silenced.

### Tests for User Story 1

- [ ] T006 [P] [US1] Add regression test in [tests/AirControl.Integration.Tests/MeteringIntegrationTests.cs](../../tests/AirControl.Integration.Tests/MeteringIntegrationTests.cs): with monitoring disabled, `LevelsChanged` still reflects the incoming (post-trim) signal for the affected channel
- [ ] T007 [P] [US1] Add regression test in [tests/AirControl.Integration.Tests/MeteringIntegrationTests.cs](../../tests/AirControl.Integration.Tests/MeteringIntegrationTests.cs): muting a channel does not zero its meter reading
- [ ] T008 [P] [US1] Add regression test in [tests/AirControl.Integration.Tests/MeteringIntegrationTests.cs](../../tests/AirControl.Integration.Tests/MeteringIntegrationTests.cs): soloing one channel does not zero the meter of the non-soloed channel
- [ ] T009 [P] [US1] Add regression test in [tests/AirControl.Integration.Tests/MeteringIntegrationTests.cs](../../tests/AirControl.Integration.Tests/MeteringIntegrationTests.cs): clipping indicator still activates while monitoring is off/muted/soloed-out

### Implementation for User Story 1

- [ ] T010 [US1] In [src/AirControl.Audio/AudioEngine.cs](../../src/AirControl.Audio/AudioEngine.cs) `OnDataAvailable`, change the two arguments passed to `RaiseLevels` from `routedLeft`/`routedRight` to the pre-gate/pre-routing `input1`/`input2` pair (research.md §1) — the audible-gate (`leftAudible && _monitoringEnabled`) and `RoutingModeApplier.Apply` continue to feed only `_outputBuffer`
- [ ] T011 [US1] Update [tests/AirControl.Integration.Tests/Fakes/FakeAudioEngine.cs](../../tests/AirControl.Integration.Tests/Fakes/FakeAudioEngine.cs) `PushRoutedSamples` (and/or add a `PushSamples`-based path) so it raises levels from the pre-gate/pre-routing per-channel signal, matching the real engine's new behavior, so integration tests exercise the same contract

**Checkpoint**: User Story 1 is fully functional and independently testable — meters never silence due to mute/solo/monitoring.

---

## Phase 4: User Story 2 - Set the Windows default recording format from the app (Priority: P2)

**Goal**: View/change the Windows "Default Format" (sample rate/bit depth) for the M-Audio recording device from AIR Control, defaulting to 48 kHz/32-bit, visible only while M-Audio is the active device.

**Independent Test**: Open AIR Control's device settings, change format to a supported combination, confirm Windows' own Sound control panel reflects it, and confirm a fresh install defaults to 48 kHz/32-bit.

### Tests for User Story 2

- [ ] T012 [P] [US2] Unit tests in [tests/AirControl.Core.Tests/RecordingFormatTests.cs](../../tests/AirControl.Core.Tests/RecordingFormatTests.cs): `RecordingFormat.Default` is 48000/32; a format is only "supported" when present in a given `GetSupportedFormats` list (pure validation helper if one is introduced) — no hardware required
- [ ] T013 [P] [US2] Integration test in [tests/AirControl.Integration.Tests/RecordingFormatIntegrationTests.cs](../../tests/AirControl.Integration.Tests/RecordingFormatIntegrationTests.cs) (new file): fresh install (no persisted preference) applies `RecordingFormat.Default` via `TrySetFormat` on startup
- [ ] T014 [P] [US2] Integration test in [tests/AirControl.Integration.Tests/RecordingFormatIntegrationTests.cs](../../tests/AirControl.Integration.Tests/RecordingFormatIntegrationTests.cs): selecting a supported format persists it and calls `TrySetFormat`; selecting an unsupported one is rejected with an explanatory error and the previous format stays active (FR-006)
- [ ] T015 [P] [US2] Integration test in [tests/AirControl.Integration.Tests/RecordingFormatIntegrationTests.cs](../../tests/AirControl.Integration.Tests/RecordingFormatIntegrationTests.cs): a persisted preference no longer in `GetSupportedFormats` at startup/reconnect falls back to `RecordingFormat.Default` and surfaces a user-facing message (FR-005)
- [ ] T016 [P] [US2] Integration test in [tests/AirControl.Integration.Tests/RecordingFormatIntegrationTests.cs](../../tests/AirControl.Integration.Tests/RecordingFormatIntegrationTests.cs): the recording-format section is visible/enabled only when `InputDeviceSelectorViewModel.SelectedDevice.IsAirDevice` is true, and hides/disables when switching to a non-M-Audio device (research.md §7)
- [ ] T017 [P] [US2] Extend [tests/AirControl.Integration.Tests/PerformanceBudgetTests.cs](../../tests/AirControl.Integration.Tests/PerformanceBudgetTests.cs): after a successful format change, monitoring/metering recover within the existing 3s reconnection budget (FR-010, SC-005) using `FakeAudioEngine`/`FakeRecordingFormatController` timing

### Implementation for User Story 2

- [ ] T018 [US2] Implement `WindowsRecordingFormatController : IRecordingFormatController` in [src/AirControl.Audio/WindowsRecordingFormatController.cs](../../src/AirControl.Audio/WindowsRecordingFormatController.cs): `GetCurrentFormat`/`GetSupportedFormats` via `IAudioClient::IsFormatSupported` over the fixed candidate list (research.md §5), `TrySetFormat` writing `PKEY_AudioEngine_DeviceFormat` via `IPropertyStore`/`PROPVARIANT` P/Invoke isolated in this class (depends on T002)
- [ ] T019 [US2] Create `RecordingFormatSelectorViewModel` in [src/AirControl.App/ViewModels/RecordingFormatSelectorViewModel.cs](../../src/AirControl.App/ViewModels/RecordingFormatSelectorViewModel.cs): exposes current/available formats gated by `IsAirDeviceActive`, implements the fallback flow from contracts/recording-format-contract.md (load persisted preference → validate against `GetSupportedFormats` → apply `Default` + user message on mismatch → `TrySetFormat` → `IAudioEngine.Stop()`+`Start()` on success) (depends on T004, T018)
- [ ] T020 [US2] Create [src/AirControl.App/Views/RecordingFormatSelectorView.xaml](../../src/AirControl.App/Views/RecordingFormatSelectorView.xaml) + [.xaml.cs](../../src/AirControl.App/Views/RecordingFormatSelectorView.xaml.cs): combo for sample rate/bit depth, visibility bound to `IsAirDeviceActive`, reusing `AutomationProperties`/visual patterns from `InputDeviceSelectorView`/`RoutingModeSelectorView` (depends on T019)
- [ ] T021 [US2] Wire `RecordingFormatSelectorViewModel` into [src/AirControl.App/ViewModels/MainWindowViewModel.cs](../../src/AirControl.App/ViewModels/MainWindowViewModel.cs): instantiate alongside the other child view-models, refresh `IsAirDeviceActive` on `InputDeviceSelector.ActiveDeviceChanged` and device connection/disconnection (depends on T019)
- [ ] T022 [US2] Add `RecordingFormatSelectorView` to [src/AirControl.App/Views/MainWindow.xaml](../../src/AirControl.App/Views/MainWindow.xaml), bound to `MainWindowViewModel.RecordingFormatSelector` (depends on T020, T021)
- [ ] T023 [US2] Register `WindowsRecordingFormatController`/`RecordingFormatRepository` in the app composition root in [src/AirControl.App/App.xaml.cs](../../src/AirControl.App/App.xaml.cs) (depends on T004, T018)

**Checkpoint**: User Stories 1 and 2 both work independently. US2 controls appear only when M-Audio is active and default to 48 kHz/32-bit.

---

## Phase 5: User Story 3 - Control the M-Audio driver's sample rate and buffer size from the app (Priority: P3)

**Goal**: Per research.md §6, no supported non-invasive integration path exists for direct driver control (would require the licensed Steinberg ASIO SDK) — so this story ships as a diagnostic panel plus a shortcut to the manufacturer's own control panel (FR-009), not inline control (FR-008 out of scope this iteration).

**Independent Test**: Open the "Driver M-Audio" section while M-Audio is active; confirm it shows available diagnostic info and a working "Abrir painel M-Audio" button that launches the external control panel, with a clear message if that executable isn't found.

### Tests for User Story 3

- [ ] T024 [P] [US3] Integration test in [tests/AirControl.Integration.Tests/DriverSettingsIntegrationTests.cs](../../tests/AirControl.Integration.Tests/DriverSettingsIntegrationTests.cs) (new file): the driver settings section is visible/enabled only when the active device `IsAirDevice` is true, mirroring US2's visibility rule
- [ ] T025 [P] [US3] Integration test in [tests/AirControl.Integration.Tests/DriverSettingsIntegrationTests.cs](../../tests/AirControl.Integration.Tests/DriverSettingsIntegrationTests.cs): `DiagnosticInfo` reflects `IAudioEngine.CaptureFormatDescription` when available
- [ ] T026 [P] [US3] Integration test in [tests/AirControl.Integration.Tests/DriverSettingsIntegrationTests.cs](../../tests/AirControl.Integration.Tests/DriverSettingsIntegrationTests.cs): when the M-Audio panel executable cannot be located/started, the command surfaces a clear, actionable message instead of failing silently (FR-009, edge case)

### Implementation for User Story 3

- [ ] T027 [US3] Create `DriverSettingsViewModel` in [src/AirControl.App/ViewModels/DriverSettingsViewModel.cs](../../src/AirControl.App/ViewModels/DriverSettingsViewModel.cs): `IsAirDeviceActive`, `DiagnosticInfo` (from `CaptureFormatDescription`), and an "Abrir painel M-Audio" `RelayCommand` using `System.Diagnostics.Process` to locate/launch the manufacturer's panel, catching launch failures into a user-facing message (data-model.md)
- [ ] T028 [US3] Create [src/AirControl.App/Views/DriverSettingsView.xaml](../../src/AirControl.App/Views/DriverSettingsView.xaml) + [.xaml.cs](../../src/AirControl.App/Views/DriverSettingsView.xaml.cs): diagnostic text + button, visibility bound to `IsAirDeviceActive`, same visual/`AutomationProperties` conventions as other device panels (depends on T027)
- [ ] T029 [US3] Wire `DriverSettingsViewModel` into [src/AirControl.App/ViewModels/MainWindowViewModel.cs](../../src/AirControl.App/ViewModels/MainWindowViewModel.cs) and add the view to [src/AirControl.App/Views/MainWindow.xaml](../../src/AirControl.App/Views/MainWindow.xaml) (depends on T027, T028)

**Checkpoint**: User Stories 1–3 all work independently. US3 clearly communicates the "no inline control" outcome per SC-004 rather than presenting a silently-failing control.

---

## Phase 6: User Story 4 - Wider trim range with headroom for boosting quiet sources (Priority: P4)

**Goal**: Change per-channel trim range from -12dB…+12dB to -∞ (bit-exact digital silence)…+10dB; clamp any out-of-range saved value on load.

**Independent Test**: Drag trim to minimum → effectively silent (-∞ dB display, no audible output); drag to maximum → reads +10 dB with signal boosted accordingly; loading a saved +12dB profile clamps to +10dB without failure.

### Tests for User Story 4

- [ ] T030 [P] [US4] Update/add unit tests in [tests/AirControl.Core.Tests/TrimCalculatorTests.cs](../../tests/AirControl.Core.Tests/TrimCalculatorTests.cs) (new file if none exists): `MinDb == double.NegativeInfinity`, `MaxDb == 10.0`, `ToLinearGain(MinDb) == 0f` exactly, `Clamp` pulls an old `+12.0` value down to `10.0`
- [ ] T031 [P] [US4] Extend [tests/AirControl.Integration.Tests/TrimIntegrationTests.cs](../../tests/AirControl.Integration.Tests/TrimIntegrationTests.cs): loading a saved profile with `TrimDb = 12.0` clamps to `10.0` on both `TrimControlViewModel` and `AudioEngine.SetTrim`; setting trim to `double.NegativeInfinity` produces bit-exact silent output samples

### Implementation for User Story 4

- [ ] T032 [P] [US4] Update [src/AirControl.Core/TrimCalculator.cs](../../src/AirControl.Core/TrimCalculator.cs): `MinDb = double.NegativeInfinity` (was `-12.0`), `MaxDb = 10.0` (was `12.0`)
- [ ] T033 [US4] Update [src/AirControl.Core/SettingsRepository.cs](../../src/AirControl.Core/SettingsRepository.cs) `JsonSerializerOptions` (both `Save` and the `Deserialize` call in `Load`) to include `NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals` so `TrimDb = double.NegativeInfinity` round-trips as JSON `"-Infinity"` (research.md §3)
- [ ] T034 [US4] Add `SliderFloorDb = -60.0` constant and clamp-on-load to [src/AirControl.App/ViewModels/TrimControlViewModel.cs](../../src/AirControl.App/ViewModels/TrimControlViewModel.cs): apply `TrimCalculator.Clamp(savedSettings.TrimDb)` before assigning `_trimDb`/calling `SetTrim`; snap slider movement to `SliderFloorDb` down to `double.NegativeInfinity` and back (research.md §2, §4)
- [ ] T035 [P] [US4] Create `TrimDbToDisplayConverter` in [src/AirControl.App/Converters/TrimDbToDisplayConverter.cs](../../src/AirControl.App/Converters/TrimDbToDisplayConverter.cs): `double.NegativeInfinity → "-∞ dB"`, else `"{0:0.0} dB"` (depends on T032)
- [ ] T036 [US4] Update [src/AirControl.App/Views/MainWindow.xaml](../../src/AirControl.App/Views/MainWindow.xaml) trim sliders: bind `Minimum` to `TrimControlViewModel.SliderFloorDb` (not `MinDb`, which is now `-Infinity` and unusable as a WPF `Slider` bound), keep `Maximum` bound to `MaxDb`, and route the trim value `TextBlock`s through `TrimDbToDisplayConverter` instead of the raw `StringFormat` (depends on T034, T035)

**Checkpoint**: All four user stories are independently functional. Trim range and display match FR-011/FR-012.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Improvements spanning multiple user stories.

- [ ] T037 [P] Run `dotnet test` for the full solution and confirm `MeteringIntegrationTests`, `TrimIntegrationTests`, `RecordingFormatIntegrationTests`, `DriverSettingsIntegrationTests`, `PerformanceBudgetTests`, `TrimCalculatorTests`, and `RecordingFormatTests` all pass together (Constitution II)
- [ ] T038 Execute the manual verification steps in [quickstart.md](./quickstart.md) (US1–US4, including the hardware-dependent US2/US3 scenarios that cannot be automated) and record results
- [ ] T039 [P] Verify UX consistency across the new `RecordingFormatSelectorView`/`DriverSettingsView`: shared `AutomationProperties` naming conventions, actionable error messages matching `MainWindowViewModel`'s `"Falha ao ...: {ex.Message}"` pattern (Constitution III)
- [ ] T040 Re-run [tests/AirControl.Integration.Tests/RealHardwarePerformanceBudgetTests.cs](../../tests/AirControl.Integration.Tests/RealHardwarePerformanceBudgetTests.cs) against real M-Audio hardware to confirm the 100ms trim/mute/solo budget and 3s format-change recovery budget both hold (Constitution IV, FR-010)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Foundational (Phase 2)**: No dependencies — start immediately. BLOCKS User Stories 2 (needs `IRecordingFormatController`/repository) but not US1 or US4, which touch unrelated code paths.
- **User Story 1 (Phase 3)**: No dependency on Phase 2 — can start immediately in parallel with Phase 2.
- **User Story 2 (Phase 4)**: Depends on Phase 2 (T001–T005).
- **User Story 3 (Phase 5)**: No dependency on Phase 2 or US2 code, but shares the same `IsAirDevice`-gating pattern introduced by US2's `RecordingFormatSelectorViewModel` visibility wiring — implement after or alongside US2 to reuse the pattern, though it is independently testable.
- **User Story 4 (Phase 6)**: No dependency on Phases 2, 4, or 5 — fully independent, can run in parallel with any other story.
- **Polish (Phase 7)**: Depends on all four user stories being complete.

### User Story Dependencies

- **US1 (P1)**: Independent — touches only `AudioEngine.OnDataAvailable`/`RaiseLevels` and `FakeAudioEngine`.
- **US2 (P2)**: Depends on Foundational (Phase 2). Independent of US1, US3, US4.
- **US3 (P3)**: Independent of US1, US2, US4 in code, but conceptually follows US2 (same visibility gating, same UI area).
- **US4 (P4)**: Fully independent — touches `TrimCalculator`, `SettingsRepository`, `TrimControlViewModel`, `MainWindow.xaml` trim sliders only.

### Within Each User Story

- Tests are written before implementation and should fail first.
- Domain/contract changes (Core) before Audio (I/O) before App (ViewModel) before Views (XAML).
- Story complete and independently verifiable before moving to the next priority.

### Parallel Opportunities

- All Foundational tasks marked [P] (T001, T002, T003, T005) can run in parallel; T004 depends on T003.
- US1's four test tasks (T006–T009) can run in parallel (same file, but independent test methods — coordinate if truly parallelized by multiple people).
- US2's test tasks (T012–T017) can run in parallel with each other.
- US3's test tasks (T024–T026) can run in parallel with each other.
- US4's T030/T031 (tests) and T032/T035 (independent implementation files) can run in parallel.
- **US1, US2 (from Phase 2 completion), US3, and US4 can all be worked in parallel by different people**, since none of their production-code changes touch the same file except all four ultimately touching `MainWindowViewModel.cs`/`MainWindow.xaml` for wiring — coordinate those two files' edits across stories if parallelizing with multiple developers.

---

## Parallel Example: User Story 1

```bash
# Launch all US1 regression tests together:
Task: "Regression test: monitoring off still meters in tests/AirControl.Integration.Tests/MeteringIntegrationTests.cs"
Task: "Regression test: muted channel still meters in tests/AirControl.Integration.Tests/MeteringIntegrationTests.cs"
Task: "Regression test: soloed-out channel still meters in tests/AirControl.Integration.Tests/MeteringIntegrationTests.cs"
Task: "Regression test: clipping indicator still activates in tests/AirControl.Integration.Tests/MeteringIntegrationTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 3 (User Story 1) — it has no dependency on Phase 2.
2. **STOP and VALIDATE**: Run `MeteringIntegrationTests` and manually confirm meters keep moving through mute/solo/monitoring-off per quickstart.md US1.
3. Ship the bug fix independently if desired — it's the smallest, most self-contained change (spec.md).

### Incremental Delivery

1. US1 (P1) → validate → ship (metering bug fix, no UI changes needed).
2. Foundational (Phase 2) → US2 (P2) → validate → ship (Windows recording format control).
3. US3 (P3) → validate → ship (M-Audio driver diagnostic + external panel shortcut).
4. US4 (P4) → validate → ship (wider trim range).
5. Polish (Phase 7) once all four are in.

### Parallel Team Strategy

With multiple developers: one takes US1 immediately; one starts Foundational then US2; one takes US4 immediately (fully independent); US3 follows after Foundational/US2's visibility pattern is established. Coordinate on `MainWindowViewModel.cs` and `MainWindow.xaml` when multiple stories land wiring changes concurrently.

---

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps task to specific user story for traceability.
- FR-007's investigation is already resolved in research.md §6 — no separate "investigation task" exists; US3's tasks implement the documented conclusion (diagnostic + external panel, not inline control).
- Verify tests fail before implementing.
- Commit after each task or logical group.
- Stop at any checkpoint to validate a story independently.
