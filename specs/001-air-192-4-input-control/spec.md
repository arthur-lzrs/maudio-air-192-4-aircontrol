# Feature Specification: Input Monitoring & Control Panel for AIR 192|4

**Feature Branch**: `001-air-192-4-input-control`

**Created**: 2026-09-01

**Status**: Draft

**Input**: User description: "este é a feature de abertura do projeto, aqui vamos lançar os fundamentos do que se tornará o desenvolvimento. a ideia é criar um software para Windows que seja capaz de aumentar a usabilidade da interface M-AUDIO AIR 192 | 4. nesta primeira etapa quero que alem de entendermos tudo o que ela tem a oferecer, quero poder gerar dois meters (um para cada input) de sinal que ela tem. além de adicionar um fader ou knob para aumentar e diminuir o trim; botão de mute e solo"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Watch real-time input levels (Priority: P1)

As the owner of an AIR 192|4 interface, I want to see a live level meter for each of the two hardware inputs so that I can tell at a glance whether each source is present, too quiet, or clipping, without relying on the interface's own tiny hardware LEDs or a separate DAW session.

**Why this priority**: Visibility into signal level is the foundation every other control in this feature depends on — trim, mute, and solo are only useful if the user can see their effect. Without this, the app delivers no value.

**Independent Test**: Can be fully tested by connecting the AIR 192|4, feeding a signal into Input 1 and then Input 2, and confirming each channel's meter moves independently and reflects the signal presence and relative loudness in real time.

**Acceptance Scenarios**:

1. **Given** the AIR 192|4 is connected and a signal is present on Input 1 only, **When** the app is open, **Then** Input 1's meter shows activity while Input 2's meter stays at rest (no false activity).
2. **Given** a signal on Input 1 that is being pushed to an unsafe level, **When** the level exceeds the safe maximum, **Then** Input 1's meter clearly indicates clipping (visually distinct from normal activity).
3. **Given** the AIR 192|4 is not connected, **When** the app is open, **Then** the app clearly shows that no device/signal is available instead of displaying misleading meter activity.

---

### User Story 2 - Adjust each input's trim from the app (Priority: P2)

As a user, I want a fader or knob per input that raises or lowers that channel's level so that I can fine-tune levels from my desk without reaching for the physical interface, and without one channel's adjustment affecting the other.

**Why this priority**: Once levels are visible (P1), the next most valuable action is being able to correct them directly from the software.

**Independent Test**: Can be fully tested by moving Input 1's trim control up and down while feeding it a steady signal and confirming only Input 1's meter/level responds, then repeating for Input 2.

**Acceptance Scenarios**:

1. **Given** a steady signal on Input 1, **When** the user raises Input 1's trim control, **Then** Input 1's meter shows a corresponding increase in level and Input 2 is unaffected.
2. **Given** a steady signal on Input 1, **When** the user lowers Input 1's trim control to its minimum (-12 dB), **Then** Input 1's meter shows the signal reduced by 12 dB relative to its unity-gain (0 dB trim) level.
3. **Given** the user sets a trim value, **When** the app is closed and reopened, **Then** the previously set trim value is restored for that input.

---

### User Story 3 - Mute an individual input (Priority: P3)

As a user, I want to mute either input independently so that I can silence a channel I'm not currently using (e.g., an empty mic input) while monitoring the other.

**Why this priority**: Muting is a common, low-complexity control that builds directly on the metering and trim already in place.

**Independent Test**: Can be fully tested by feeding a signal to Input 1, engaging Mute on Input 1, and confirming Input 1's monitored level goes silent while Input 2 (fed a separate signal) is unaffected.

**Acceptance Scenarios**:

1. **Given** a signal on Input 1, **When** the user engages Mute on Input 1, **Then** Input 1 is silenced in the app's monitoring/metering while Input 2 continues unaffected.
2. **Given** Input 1 is muted, **When** the user disengages Mute, **Then** Input 1's signal is audible/monitored again at its previously set trim level.
3. **Given** Input 1 is muted, **Then** the app clearly shows Input 1's muted state at all times (not just at the moment of toggling).

---

### User Story 4 - Solo an individual input (Priority: P4)

As a user, I want to solo either input so that I can listen to/monitor just that one channel in isolation, for example to check one microphone for noise without the other input's signal in the way.

**Why this priority**: Solo is the least critical of the four controls but completes the standard channel-strip toolset (meter, trim, mute, solo) users expect from any mixing surface.

**Independent Test**: Can be fully tested by feeding independent signals to both inputs, engaging Solo on Input 1, and confirming only Input 1 remains active in the app's monitoring while Input 2 is silenced, then releasing solo and confirming both return to their prior mute/trim state.

**Acceptance Scenarios**:

1. **Given** signals on both inputs, **When** the user engages Solo on Input 1, **Then** only Input 1 remains active in the app's monitoring and Input 2 is silenced regardless of Input 2's own mute state.
2. **Given** Input 1 is soloed, **When** the user disengages Solo, **Then** both inputs return to whatever mute/trim state they held before Solo was engaged.
3. **Given** Input 1 is already muted, **When** the user engages Solo on Input 1, **Then** the app resolves the conflict to a single well-defined outcome (Solo overrides Mute for the soloed channel) rather than an ambiguous state.

---

### Edge Cases

- What happens when the AIR 192|4 is unplugged while the app is running? The app MUST detect the disconnection and clearly indicate loss of device instead of freezing or showing stale meter data.
- What happens when the AIR 192|4 is plugged back in? The app MUST detect reconnection and resume showing live meters and previously configured trim/mute/solo state without requiring a restart.
- What happens when both inputs are soloed at the same time? Both remain active and behave as if neither were soloed (soloing all channels is equivalent to soloing none).
- What happens when an input is both muted and soloed? Solo overrides mute for the soloed channel (see User Story 4, Scenario 3); the other (non-soloed) channel is silenced regardless of its own mute state.
- What happens when no signal at all is present on a connected input? The meter MUST show a resting/silent state, clearly distinguishable from a disconnected device.
- What happens if the user tries to open a second instance of the app while one is already running? The app MUST prevent conflicting simultaneous control of the same hardware (e.g., by focusing the existing instance) rather than allowing two instances to fight over the same trim/mute/solo state.

## Clarifications

### Session 2026-09-01

- Q: Quando o app "monitora" um input, isso significa reprodução audível de áudio (playthrough) ou apenas medidores visuais? → A: O app reproduz o sinal dos inputs através da saída de áudio do Windows (fones/alto-falantes); mute/solo/trim afetam esse áudio audível reproduzido, além dos meters.
- Q: Qual nível de sinal deve disparar a indicação de "clipping" no meter? → A: 0 dBFS — clipping quando o sinal atinge o teto digital (nenhuma margem de segurança).
- Q: Qual é a faixa numérica (em dB) do controle de trim digital por canal? → A: -12 dB a +12 dB.
- Q: Qual nível de suporte de acessibilidade é obrigatório para este release? → A: Full — todos os controles (meters, trim, mute, solo) operáveis via teclado, com contraste adequado e rótulos legíveis por leitor de tela.
- Q: O meter de nível deve exibir peak, RMS/VU, ou ambos? → A: Ambos — peak para detecção de clipping, RMS como indicação de nível médio no mesmo meter.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The app MUST detect when an M-AUDIO AIR 192|4 is connected to the Windows machine and identify its two hardware inputs as separate, independently controllable channels.
- **FR-002**: The app MUST clearly display the device's connection status (connected / not connected) at all times.
- **FR-003**: The app MUST display a real-time level meter for Input 1, reflecting that channel's current signal level independently of Input 2.
- **FR-004**: The app MUST display a real-time level meter for Input 2, reflecting that channel's current signal level independently of Input 1.
- **FR-005**: Each channel's meter MUST visually distinguish three states: silence/no signal, normal activity, and clipping (signal at or above 0 dBFS, the digital ceiling).
- **FR-005a**: Each channel's meter MUST display both a peak indicator (for accurate clipping detection at 0 dBFS) and an RMS/average-level indication of the same signal, within the same meter.
- **FR-006**: The app MUST provide a trim control (fader or knob) per input channel that increases or decreases that channel's monitored signal level independently of the other channel, across a range of -12 dB to +12 dB.
- **FR-007**: Trim adjustments MUST apply a digital gain to the signal already captured from the device; the app is not required to control the interface's physical/analog preamp circuitry.
- **FR-008**: Trim adjustments MUST be reflected in the corresponding channel's meter immediately (see SC-002 for the specific responsiveness target).
- **FR-009**: The app MUST provide a Mute control per input channel that silences that channel's audible playback and monitored signal (see FR-019) without affecting the other channel's signal.
- **FR-010**: The app MUST provide a Solo control per input channel; engaging Solo on a channel silences every other channel's audible playback and monitored signal regardless of their individual mute state, until Solo is released.
- **FR-011**: When Solo is released, each channel MUST return to the mute/trim state it held immediately before Solo was engaged.
- **FR-012**: If every channel is soloed simultaneously, the app MUST treat this as equivalent to no channel being soloed (all channels remain active).
- **FR-013**: The app MUST clearly and persistently indicate each channel's current mute and solo state (not only momentarily at the instant the user toggles it).
- **FR-014**: The app MUST persist each channel's trim, mute, and solo settings across app restarts and restore them automatically the next time the app opens with the device connected.
- **FR-015**: The app MUST detect disconnection of the AIR 192|4 while running and clearly indicate the loss of device instead of showing stale or frozen meter data.
- **FR-016**: The app MUST detect reconnection of the AIR 192|4 while running and resume live metering and previously configured controls without requiring an app restart.
- **FR-017**: The app MUST prevent a second concurrent instance from independently driving the same device's controls.
- **FR-018**: This feature's mute, solo, and trim controls affect only this app's own audible playback and metering of the two inputs; they are not required to alter the raw signal delivered by the AIR 192|4 to other applications (DAWs, conferencing software, Windows recording) in this iteration.
- **FR-019**: The app MUST provide audible monitoring by routing each input's processed signal (after trim, mute, and solo are applied) to a Windows audio output device selectable by the user, so the user can hear what they are adjusting in real time.
- **FR-020**: Every control (meters as status indicators, trim, mute, solo) MUST be fully operable via keyboard alone, MUST meet sufficient color contrast for all visual states (including clipping and mute/solo indicators), and MUST expose screen-reader-readable labels and state announcements.

### Key Entities

- **Input Channel**: One of the AIR 192|4's two hardware inputs. Attributes: channel identifier (Input 1 / Input 2), current signal level, clipping state, trim value, mute state, solo state, connection status.
- **Audio Output Device**: The Windows playback device selected by the user to hear the processed (trim/mute/solo-applied) signal from the two Input Channels.
- **Device**: The M-AUDIO AIR 192|4 interface as a whole. Attributes: connection status, the two Input Channels it exposes.
- **Channel Settings Profile**: The saved trim/mute/solo values for each Input Channel, persisted between app sessions and restored on next launch.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can visually determine, within 1 second of a signal starting, whether each of the two inputs has signal present and whether it is clipping.
- **SC-002**: Meter movement and audible effect from a trim, mute, or solo change are perceived by the user as immediate, with visible meter response within 100ms of the control being adjusted.
- **SC-003**: A user unfamiliar with the app can identify how to mute, solo, and adjust trim for a given input within 30 seconds of first opening the app, without external instructions.
- **SC-004**: Saved trim/mute/solo settings are restored correctly 100% of the time across app restarts with the device connected.
- **SC-005**: The app correctly reflects device disconnect/reconnect events (per Edge Cases) in under 3 seconds, with zero instances of stale meter data being mistaken for a live signal during informal testing.

## Assumptions

- The AIR 192|4's physical input gain knobs are analog and are not documented as controllable over USB; therefore "trim" in this feature is implemented as a digital gain applied to the already-captured signal, not a command sent to the hardware preamp (confirmed with the user).
- For this first iteration, mute/solo/trim only affect what this app itself monitors and displays; they do not need to alter the signal any other application receives from the device. The user has indicated that a **future** iteration is expected to grow this app into the primary control point for the device — potentially operating at the driver level so other software on the machine is served through it — but that is explicitly out of scope for this feature.
- The app targets a single M-AUDIO AIR 192|4 connected at a time; behavior with multiple simultaneous AIR-series devices is not addressed by this feature.
- "Understanding everything the interface has to offer" is treated as a discovery/groundwork activity that informs this and future specs, rather than a user-facing requirement in itself; only the capabilities explicitly requested (two input meters, trim, mute, solo) are captured as requirements here.
- Users are expected to already have any necessary Windows driver for the AIR 192|4 installed; driver installation/setup is out of scope for this feature.
- Standard consumer/prosumer usage is assumed (a single user adjusting their own two inputs), not a multi-operator or networked-control scenario.
