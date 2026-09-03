# Feature Specification: Channel Routing & Device Selection

**Feature Branch**: `002-channel-routing-device-select`

**Created**: 2026-09-02

**Status**: Draft

**Input**: User description: "como proxima feature deste app, quero poder mudar a forma de roteamento do canais. hoje alguns programas entendem os dois inputs como stereo, mas como eu so uso o 1 pro meu mic, o som acaba saindo na esquerda apenas. quero poder fazer stereo, usar o canal 1 ou 2 como mono, e as combinações mais utilizadas para este tipo de interface. alem disso, quero poder escolher o dispositivo que quero usar na aplicação, claro: se encontrar o m-audio, use ele como padrão, mas que tenha um lugar onde posso escolher."

## Clarifications

### Session 2026-09-02

- Q: Quando o modo Combined Mono soma o Input 1 e o Input 2, o app deve reduzir o ganho do resultado para evitar que a soma ultrapasse o teto digital, ou deve somar os sinais em nível cheio sem compensação? → A: Somar com compensação de ganho (ex.: (In1+In2)/2, ou -6dB) para evitar clipping introduzido pela soma.
- Q: Qual deve ser o modo de roteamento padrão na primeiríssima vez que o app abre, antes de o usuário ter escolhido ou salvo qualquer preferência de roteamento? → A: Stereo (Input 1 → Left, Input 2 → Right) como padrão de primeiro uso.
- Q: Se o dispositivo ativo tiver apenas 1 canal de entrada, qual deve ser o fallback, já que Stereo (o exemplo de fallback seguro no edge case) também exigiria 2 canais? → A: Fallback para o primeiro/modo mais simples compatível com o dispositivo (ex.: Input 1 as Mono, se só houver 1 canal).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Hear a single microphone centered, not panned to one side (Priority: P1)

As a user with a microphone plugged into only Input 1, I want the app to play that mic in both ears (or both output channels) instead of only the left side, so that I don't have to manually work around software that assumes both inputs are always a stereo pair.

**Why this priority**: This is the exact problem the user is experiencing today — it makes the app unpleasant to use for the most common single-mic setup, so it must be fixed first.

**Independent Test**: Can be fully tested by connecting a microphone to Input 1 only, selecting the "Input 1 as mono" routing mode, and confirming the monitored/played-back audio is audible equally on both the left and right output channels.

**Acceptance Scenarios**:

1. **Given** a signal on Input 1 only, **When** the user selects the routing mode that sends Input 1 to both output channels, **Then** the played-back and metered signal appears equally on both left and right, with no audio on the left-only or right-only.
2. **Given** a signal on Input 2 only, **When** the user selects the routing mode that sends Input 2 to both output channels, **Then** the played-back and metered signal appears equally on both left and right.
3. **Given** the user has selected a mono routing mode, **When** the app is closed and reopened, **Then** the same routing mode is restored automatically.

---

### User Story 2 - Choose a routing mode from the standard set for a 2-input interface (Priority: P1)

As a user, I want to pick from a clear list of routing modes — true Stereo (Input 1 = Left, Input 2 = Right), Input 1 as Mono, Input 2 as Mono, and a combined Mono (both inputs summed together on both output channels) — so that I can match the routing to whatever I've plugged in without needing to reconfigure cables or other software.

**Why this priority**: The routing selector is the mechanism that delivers User Story 1 and every other routing scenario; without a way to choose a mode, no routing behavior is reachable.

**Independent Test**: Can be fully tested by cycling through each available routing mode while feeding known signals into Input 1 and Input 2, and confirming the output channels behave as that mode's description promises.

**Acceptance Scenarios**:

1. **Given** signals on both Input 1 and Input 2, **When** the user selects Stereo mode, **Then** Input 1 is heard/metered only on the left output channel and Input 2 only on the right.
2. **Given** signals on both Input 1 and Input 2, **When** the user selects the combined Mono mode, **Then** both inputs are summed together and the same combined signal appears equally on both output channels.
3. **Given** any routing mode is active, **When** the user switches to a different routing mode, **Then** the change takes effect without restarting the app and without an audible glitch, pop, or long dropout.
4. **Given** the user changes the routing mode, **When** the app is closed and reopened, **Then** the previously selected routing mode is restored automatically.

---

### User Story 3 - Choose which audio device the app uses, with the M-Audio interface as the default (Priority: P2)

As a user, I want the app to automatically use my M-Audio AIR interface when it's plugged in, but also have a clearly visible place where I can pick a different input device if I need to, so that the app isn't hard-locked to one specific piece of hardware.

**Why this priority**: Device auto-detection already gets the primary hardware working with zero setup; explicit selection is the important escape hatch for when the user has more than one interface or wants to use something else, but it is secondary to fixing the routing problem itself.

**Independent Test**: Can be fully tested by connecting an M-Audio AIR interface alongside at least one other Windows audio input device, confirming the app selects the M-Audio device automatically on launch, then using the device picker to switch to the other device and confirming the app starts using it instead.

**Acceptance Scenarios**:

1. **Given** an M-Audio AIR interface and at least one other input device are both connected, **When** the app starts, **Then** the app automatically selects the M-Audio AIR interface as the active input device.
2. **Given** no M-Audio AIR interface is connected but another input device is available, **When** the app starts, **Then** the app does not silently guess; it shows a clear device-selection prompt or state instead of assuming a device.
3. **Given** the app is running, **When** the user opens the device selector and picks a different available input device, **Then** the app switches to that device and its two input channels become the ones shown, metered, and routed.
4. **Given** the user has manually selected a non-default device, **When** the app is closed and reopened while that device is still connected, **Then** the app restores that same manual selection instead of reverting to auto-detecting the M-Audio interface.
5. **Given** the user's manually selected device is disconnected, **When** the app starts (or the device is unplugged while running), **Then** the app clearly indicates no active device is available and offers the device selector, falling back to auto-detecting the M-Audio interface if it becomes available.

---

### Edge Cases

- What happens if the user selects Stereo mode but only one input actually has a signal? The unused side simply stays silent/at rest; this is expected behavior, not an error.
- What happens if the selected routing mode references an input channel that the currently active device doesn't have (e.g., a device with only one input)? The app MUST disable or hide routing modes that don't apply to the active device's channel count.
- What happens when the user switches the active device while a routing mode is selected? The app MUST re-validate the routing mode against the newly selected device's channels and fall back to the simplest routing mode the new device supports if the previous mode no longer applies (e.g., Stereo if the device has 2 channels; Input 1 as Mono if the device exposes only 1 channel).
- What happens when the combined Mono mode would sum two clipping inputs? The app MUST still apply the same clipping indication already defined for metering (per the existing input-monitoring feature) to the combined result. Combined Mono applies gain compensation to the sum (see Clarifications), so clipping in the combined result reflects genuine clipping of the (compensated) combined signal, not an artifact of summing two full-level inputs.
- What happens if two M-Audio AIR interfaces are connected at the same time? The app picks one (the first one enumerated by Windows) as the default and relies on the manual device selector for the user to choose the other; multi-device simultaneous use is out of scope.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The app MUST provide a routing mode selector with at least the following modes: Stereo (Input 1 → Left, Input 2 → Right), Input 1 as Mono (Input 1 → both Left and Right), Input 2 as Mono (Input 2 → both Left and Right), and Combined Mono (Input 1 and Input 2 summed together with gain compensation, e.g. averaged/-6dB, so the combined signal does not clip solely from summing two full-level inputs → both Left and Right).
- **FR-002**: The app MUST apply the selected routing mode to both the audible monitoring output and the on-screen meters, consistently with each other.
- **FR-003**: Changing the routing mode MUST take effect immediately, without requiring an app restart, and without producing an audible glitch, pop, or dropout longer than what is already defined for control changes in the existing input-monitoring feature.
- **FR-004**: The app MUST persist the selected routing mode across app restarts and restore it automatically the next time the app opens with a compatible device connected. On the very first launch, before any routing mode has ever been selected or persisted, the app MUST default to Stereo mode (subject to FR-005 if the active device doesn't support it).
- **FR-005**: The app MUST hide or disable routing modes that do not apply to the number of input channels exposed by the currently active device. When the currently selected routing mode is no longer valid for the active device (e.g., after a device switch), the app MUST fall back to the simplest routing mode the device supports (per channel count required: Input 1 as Mono needs 1 channel; Stereo, Input 2 as Mono, and Combined Mono need 2).
- **FR-006**: Per-channel trim, mute, and solo controls (as defined by the existing input-monitoring feature) MUST continue to apply to the underlying Input 1 / Input 2 signals before routing is applied, regardless of the selected routing mode.
- **FR-007**: The app MUST detect all available Windows audio input devices and present them in a device selector that the user can open at any time.
- **FR-008**: On launch, if an M-Audio AIR interface is among the detected input devices, the app MUST automatically select it as the active device unless the user has previously made a manual device selection that is still available.
- **FR-009**: If no M-Audio AIR interface is detected and no valid prior manual selection is available, the app MUST clearly prompt the user to choose an input device rather than guessing one.
- **FR-010**: The app MUST allow the user to manually select any detected input device as the active device, and MUST apply that selection immediately, replacing the previously active device's channels, meters, and routing.
- **FR-011**: The app MUST persist a manual device selection across app restarts, and restore it automatically as long as that device is still available; if it is not available, the app MUST fall back to auto-detecting the M-Audio AIR interface (per FR-008) or prompting the user (per FR-009).
- **FR-012**: The app MUST detect when the active device is disconnected while running and clearly indicate loss of device (consistent with the existing input-monitoring feature's disconnect handling), while keeping the device selector available so the user can pick another connected device.

### Key Entities

- **Routing Mode**: The active mapping from the device's two hardware input channels to the app's two output/monitoring channels (Left/Right). Attributes: mode identifier (Stereo, Input 1 Mono, Input 2 Mono, Combined Mono), the channel(s) it requires from the active device.
- **Audio Input Device**: A Windows-recognized audio interface with one or more input channels that the app can capture from. Attributes: device name/identifier, number of input channels, connection status, whether it is the auto-detected default (M-Audio AIR) or a manual user selection.
- **Device & Routing Preferences**: The persisted record of the user's selected input device and routing mode, restored automatically between app sessions.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user with a microphone on a single input can get that microphone centered on both output channels within 10 seconds of first opening the routing selector, without external instructions.
- **SC-002**: Switching routing modes produces an audible/metered effect within 100ms, matching the responsiveness already established for other real-time controls.
- **SC-003**: When an M-Audio AIR interface is connected and no manual selection has been made, the app selects it as the active device automatically 100% of the time on launch.
- **SC-004**: Saved routing mode and device selection are restored correctly 100% of the time across app restarts when the relevant device is connected.
- **SC-005**: A user can switch to a different connected input device in under 15 seconds using the device selector, without needing to restart the app.

## Assumptions

- "The most used combinations for this type of interface" is interpreted as: true Stereo, Input 1 Mono, Input 2 Mono, and Combined Mono (both inputs summed). Less common variants (e.g., swapped Stereo with Input 2 on the left) are not included in this iteration and can be added later if needed.
- Routing is applied on top of the existing per-channel trim/mute/solo processing from the input-monitoring feature, not as a replacement for it.
- "The device" the user wants to select refers to the audio input device (the capture interface, e.g., M-Audio AIR vs. another interface or a built-in microphone), not the audio output/playback device, which is already covered as a separate selectable setting by the existing input-monitoring feature.
- Device auto-detection matches by the device's Windows-reported name containing "M-Audio" and "AIR"; exact matching rules are an implementation detail to be resolved during planning.
- As with the existing input-monitoring feature, this feature targets a single active input device at a time; using multiple input devices simultaneously is out of scope.
- Routing mode and device selection preferences are stored using the same persistence mechanism already established for trim/mute/solo settings.
