# Feature Specification: Device & Monitoring Audio Controls

**Feature Branch**: `003-device-audio-controls`

**Created**: 2026-09-03

**Status**: Draft

**Input**: User description: "o meters sempre estao medindo, independente se esta mute, solo, etc. Hoje quando desativo a monitoração ele para de medir, enquanto o comportamento deve ser de apenas eu parar me ouvir. / o driver da m-audio permite mudar sample rate e buffer size, eu quero que o software possa controlar isso tambem, imagino que seja melhor o software fazer o controle deste app/driver da maudio do que desenvovler uma especie de driver que controle isso (precisamos investigar o que é possivel ou nao, o que é melhor, etc) / o painel de controle de gravação do windows fornece algumas configurações de "Formato padrao" como mostra no print, quero poder controlar isso pelo nosso app, deixando o 48khz 32 bits como padrao / trocar o range do trim de infinito até +10"

## Clarifications

### Session 2026-09-03

- Q: Os controles de formato do Windows e do driver M-Audio (User Stories 2 e 3) devem valer sempre para o dispositivo M-Audio especificamente, ou para o dispositivo que estiver ativo no momento (selecionado via feature 002)? → A: Os controles ficam visíveis/habilitados apenas quando o dispositivo M-Audio é o dispositivo ativo no momento; se o usuário trocar para outro dispositivo, esses controles somem ou ficam desabilitados.
- Q: Quando o trim chega ao mínimo (exibido como -∞), o app deve cortar o sinal para silêncio digital absoluto (ganho = 0, idêntico ao mute), ou aplicar apenas uma atenuação muito forte que ainda carrega um sinal residual inaudível? → A: Silêncio digital absoluto — no mínimo, o trim multiplica o sinal por zero, igual ao mute.
- Q: Se o formato do dispositivo (sample rate/bit depth) salvo de uma sessão anterior não for mais suportado quando o M-Audio reconectar ou o app reiniciar, o que o app deve fazer? → A: Cair para 48kHz/32-bit (o padrão de fábrica) automaticamente e avisar o usuário que a preferência salva não era mais compatível.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Meters keep measuring regardless of monitoring/mute/solo (Priority: P1)

As a user, I want the input meters to keep showing real signal level at all times, so that I can confirm my source is live and hot even when I've turned off monitoring (stopped listening) or muted/soloed a channel.

**Why this priority**: This is a correctness bug in existing, shipped functionality (feature 001). Today, disabling monitoring or muting/soloing a channel silences the meters along with the audio, which can make a user believe their interface or source has stopped working when it hasn't. It's the smallest, most self-contained change and unblocks trust in the rest of the metering UI.

**Independent Test**: With a live input signal, toggle monitoring off, then mute the channel, then solo the other channel — in every case the meter for the affected channel must keep reflecting the actual incoming signal level (post-trim), while no audio is heard through the output when monitoring is off, the channel is muted, or another channel is soloed.

**Acceptance Scenarios**:

1. **Given** a live signal on Input 1 and monitoring enabled, **When** the user disables monitoring, **Then** Input 1's meter continues to move with the incoming signal and no audio is heard at the output.
2. **Given** a live signal on Input 1, **When** the user mutes Input 1, **Then** Input 1's meter continues to move with the incoming signal and Input 1 is inaudible at the output.
3. **Given** live signals on both inputs, **When** the user solos Input 2, **Then** Input 1's meter continues to move with its incoming signal even though Input 1 is inaudible at the output.
4. **Given** monitoring disabled, muted, or soloed-out, **When** the incoming signal clips, **Then** the meter's clipping indicator still activates.

---

### User Story 2 - Set the Windows default recording format from the app (Priority: P2)

As a user, I want to choose the Windows "Default Format" (sample rate and bit depth) for the M-Audio recording device directly from AIR Control, so that I don't have to open the Windows Sound control panel, and so the device stays on a known-good configuration (48 kHz / 32-bit) by default.

This control is only shown/enabled while the M-Audio device is the app's currently active device (per feature 002's device selection); if the user switches the active device to something else, this control is hidden or disabled until M-Audio becomes active again.

**Why this priority**: Directly addresses a recurring manual step (opening Windows' Sound settings) and standardizes the setup on a configuration the user has validated works well, reducing misconfiguration risk.

**Independent Test**: Open AIR Control's device settings, change the format to a supported sample rate/bit depth combination, confirm Windows' own Sound control panel reflects the new selection, and confirm this matches what a fresh install defaults to (48 kHz, 32-bit).

**Acceptance Scenarios**:

1. **Given** AIR Control is running with no prior format preference saved, **When** the app starts for the first time, **Then** the Windows default format for the M-Audio recording device is set to 48 kHz / 32-bit.
2. **Given** the device settings panel is open, **When** the user selects a different supported sample rate/bit depth combination, **Then** the change is applied to the Windows default format and is visible if the user opens the native Windows Sound control panel afterward.
3. **Given** the user selects a sample rate/bit depth combination the device does not support, **When** they attempt to apply it, **Then** the app rejects the change with an explanation and leaves the previous format active.
4. **Given** a format change was just applied, **When** audio is already being monitored, **Then** the app recovers monitoring/metering automatically without requiring the user to restart the app.

---

### User Story 3 - Control the M-Audio driver's sample rate and buffer size from the app (Priority: P3)

As a user, I want to change the M-Audio interface's sample rate and buffer size (the same settings exposed by M-Audio's own control panel/driver) from within AIR Control, so that I have a single place to tune my audio setup instead of switching between two apps.

Like the Windows default format control, this is only shown/enabled while the M-Audio device is the app's currently active device; it is hidden or disabled when a different device is active.

**Why this priority**: Highest value for workflow convenience, but also the highest technical uncertainty — it depends on what the M-Audio driver/control-panel application actually exposes for external/programmatic control, which is not yet known. It is sequenced after the two lower-risk stories so that a "no viable path" outcome does not block the rest of this feature.

**Independent Test**: From AIR Control, change the buffer size and/or sample rate and confirm (a) the M-Audio control panel reflects the same values, and (b) AIR Control's own metering/monitoring continues to work correctly at the new setting.

**Acceptance Scenarios**:

1. **Given** the M-Audio driver currently exposes a sample rate and a buffer size, **When** the user opens AIR Control's device settings, **Then** the current values are displayed, sourced from the driver rather than assumed.
2. **Given** a supported buffer size is available, **When** the user selects a different buffer size in AIR Control, **Then** the M-Audio driver's configuration is updated to match, and this is confirmed by inspecting the M-Audio control panel.
3. **Given** a supported sample rate is available, **When** the user selects a different sample rate in AIR Control, **Then** the M-Audio driver's configuration is updated to match, and AIR Control's monitoring/metering resume correctly at the new rate.
4. **Given** the investigation determines that no supported, non-invasive way exists to control the M-Audio driver from third-party software, **When** this is confirmed, **Then** AIR Control clearly communicates that this setting must be changed in the M-Audio control panel, rather than silently doing nothing.

---

### User Story 4 - Wider trim range with headroom for boosting quiet sources (Priority: P4)

As a user, I want the per-channel trim control to go all the way down to silence and up to +10 dB (instead of the current -12 dB to +12 dB range), so that I can fully attenuate a channel with trim alone and have a bit less maximum boost, matching how I actually use it.

**Why this priority**: Small, self-contained, low-risk change confined to an existing control; no dependency on the other stories.

**Independent Test**: Move the trim slider to its minimum and confirm the channel is effectively silent; move it to its maximum and confirm it reads +10 dB and the signal is boosted by 10 dB relative to unity.

**Acceptance Scenarios**:

1. **Given** the trim control for a channel, **When** the user drags it to its minimum, **Then** the displayed value reads as effectively silent (**-∞**) and the channel produces no audible output.
2. **Given** the trim control for a channel, **When** the user drags it to its maximum, **Then** the displayed value reads +10 dB and the channel's signal is boosted accordingly.
3. **Given** a saved trim value from before this change (e.g. +12 dB, the previous maximum), **When** the app loads that saved profile, **Then** the value is clamped into the new range (+10 dB) rather than rejected or crashing.

---

### Edge Cases

- What happens if the user changes the Windows default format or the M-Audio driver's sample rate/buffer size while a capture is actively running? Monitoring and metering must recover automatically once the change completes, without requiring an app restart.
- What happens if the M-Audio device is unplugged or powered off while the user is viewing/changing device format or driver settings? The app must show the device as unavailable and disable the controls rather than erroring.
- What happens if two audio applications (AIR Control and the M-Audio control panel) are open at the same time and both change the same setting? The last applied change wins, and AIR Control MUST reflect the actual current device state rather than an assumed one (i.e., it re-reads state instead of trusting its own cache).
- What happens when a channel is both muted and soloed-out at the same time? The meter still reflects real input signal in all combinations of mute/solo/monitoring.
- What happens to a channel's audible output (not its meter) when trim is set to its minimum (silence) and the channel is also unmuted and monitoring is on? It should be silent — trim silence and mute both result in inaudible output, and both are independent from metering, which keeps measuring.
- What happens if the persisted recording device format is no longer supported by the M-Audio device when it reconnects or the app restarts? The app falls back to the 48 kHz/32-bit default and informs the user the saved preference was no longer compatible.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST measure and display each input channel's meter (peak/RMS/clipping) based on the real incoming signal level (after trim is applied) regardless of that channel's mute state, solo state, or the global monitoring on/off state.
- **FR-002**: The system MUST continue to prevent audible output for a channel when monitoring is disabled, the channel is muted, or another channel is soloed — only the audible output path is affected, never the metering path.
- **FR-003**: The system MUST allow the user to view and change the Windows "Default Format" (sample rate and bit depth) of the M-Audio recording device from within the app, and MUST hide or disable this control whenever the M-Audio device is not the app's currently active device.
- **FR-004**: The system MUST default the Windows recording device format to 48 kHz / 32-bit the first time it configures the device (fresh install / no prior preference saved).
- **FR-005**: The system MUST persist the user's chosen recording device format and re-apply it if the device is reconnected or the app restarts. If the persisted format is no longer supported by the device at that time, the system MUST fall back to the 48 kHz/32-bit default (per FR-004) and clearly inform the user that the saved preference was no longer compatible.
- **FR-006**: The system MUST validate a requested format against what the device actually supports before applying it, and MUST leave the previous format active with a clear explanation if the request is invalid or fails.
- **FR-007**: The system MUST investigate and document which sample rate and buffer size controls the M-Audio driver/control-panel software exposes for external control, and whether a supported, non-invasive integration path exists, before committing to a specific control mechanism.
- **FR-008**: Where a supported integration path exists, the system MUST allow the user to view the M-Audio driver's current sample rate and buffer size, and to change them from within the app, with the change reflected in the M-Audio control panel. This control MUST be hidden or disabled whenever the M-Audio device is not the app's currently active device.
- **FR-009**: Where no supported integration path exists, the system MUST clearly inform the user that sample rate/buffer size must be changed via the M-Audio control panel, rather than presenting a control that silently fails.
- **FR-010**: The system MUST recover monitoring and metering automatically after a device format, sample rate, or buffer size change, without requiring the user to restart the app.
- **FR-011**: The system MUST change the per-channel trim range from -12 dB…+12 dB to a minimum representing effective silence (displayed as -∞) up to +10 dB. At the minimum, trim MUST apply a gain of exactly zero (bit-exact digital silence), the same effect as muting the channel, not merely a very low attenuation.
- **FR-012**: The system MUST clamp any previously saved trim value that falls outside the new range (e.g. a stored +12 dB) into the new range when loading it, rather than rejecting the saved profile.

### Key Entities

- **Channel Meter Reading**: The measured peak/RMS/clipping state for an input channel; now explicitly independent of that channel's mute/solo state and of the global monitoring toggle.
- **Monitoring State**: The global on/off toggle that controls whether processed audio is sent to the output device; no longer tied to whether metering runs.
- **Recording Device Format**: The Windows-level sample rate and bit depth configured for the M-Audio recording device (the same values shown in Windows' Sound control panel "Advanced"/"Default Format" tab), with a persisted user preference and a 48 kHz/32-bit default.
- **Driver Configuration**: The M-Audio driver-level sample rate and buffer size (the same values shown in the M-Audio control panel), whose controllability from AIR Control depends on the outcome of the FR-007 investigation.
- **Channel Trim**: The per-channel digital gain setting, now ranging from effective silence (-∞) to +10 dB instead of -12 dB to +12 dB.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the time, input meters keep reflecting live signal level while monitoring is off, a channel is muted, or a channel is soloed-out — verified across all combinations of these three states.
- **SC-002**: A user can set the recording device's default format to 48 kHz/32-bit (or confirm it's already set) from within AIR Control in under 15 seconds, without opening the Windows Sound control panel.
- **SC-003**: On a fresh install, the recording device's default format is 48 kHz/32-bit without any user action.
- **SC-004**: The feasibility investigation for M-Audio driver control produces a clear, documented answer (supported and how, or not supported and why) before any driver-control UI is built.
- **SC-005**: If driver control is feasible, a user can change the M-Audio driver's sample rate or buffer size from AIR Control and see monitoring/metering resume correctly, in under 15 seconds, without restarting the app.
- **SC-006**: A user can drive a channel's trim from full silence to +10 dB using a single control, with 0% of previously-saved trim profiles (including old +12 dB values) causing a load failure.

## Assumptions

- "Meters" refers to the existing peak/RMS/clipping metering introduced in feature 001; this feature only changes what signal feeds the meter, not the metering algorithm itself.
- Effective silence (-∞) for trim is implemented as a gain of exactly zero (bit-exact digital silence, same effect as mute), not a literal mathematical infinity and not merely a very low residual attenuation.
- "M-Audio control panel/driver" and "Windows default format" are two distinct, independently-configurable layers (driver-level sample rate/buffer size vs. Windows' own recording-device default format), and this feature addresses both, but treats driver-level control (User Story 3) as investigation-first because its feasibility is unknown, while Windows-level format control (User Story 2) is known to be feasible via the standard Windows audio APIs.
- The M-Audio hardware/driver currently in use is the same interface already integrated in feature 001 (AIR 192|4 or equivalent), and any driver-level investigation is scoped to that device.
- Existing mute/solo/routing behavior (from feature 002) is otherwise unchanged; only the metering signal source and the trim range change.
