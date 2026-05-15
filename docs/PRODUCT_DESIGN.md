# Jieping Product Design

## 1. Product Summary

Jieping is a local Windows screen recorder built with WPF and FFmpeg. It records selected regions, selected windows, or selected displays to MP4 files.

Primary goals:

- Let users select what to record quickly.
- Produce playable local MP4 files.
- Keep recording controls simple and predictable.
- Avoid capturing Jieping's own main window during active recording.
- Keep implementation local-first with no cloud dependency.

## 2. Current Status

Phase 1 MVP is implemented and validated.

Phase 2 audio and control improvements are implemented:

- System audio recording.
- Microphone recording.
- Global hotkeys.
- Pause/resume.
- Recording countdown.
- Mouse cursor capture setting.

Phase 3 recording quality improvements are implemented:

- Window-follow recording is implemented and UI-smoke-tested.
- Fullscreen display selection is implemented and UI-smoke-tested.
- Main window auto-hide during recording is implemented and hotkey-smoke-tested.
- Recording presets are implemented and smoke-tested.
- Quality and bitrate controls are implemented and smoke-tested.

Phase 4 post-processing has started:

- Simple start/end trimming is implemented and service-smoke-tested.
- GIF export is implemented and service-smoke-tested.
- Mouse click highlight is implemented and recording-smoke-tested.
- Watermark option is implemented and recording-smoke-tested.
- Recording history is implemented and view-model-smoke-tested.
- English and Chinese language selection is implemented and view-model-smoke-tested.

## 3. Recording Modes

### Region Recording

Users can draw a rectangle on the virtual desktop.

Behavior:

- Overlay spans the virtual desktop.
- Drag creates a selected rectangle.
- Size badge shows selected width and height while dragging.
- Enter or double-click confirms.
- Escape or right-click cancels.
- Output video contains the selected region.

Known notes:

- Region coordinates are converted to physical pixels before recording.
- Manual mixed-DPI verification remains useful.

### Window Recording

Users can choose a visible desktop application window.

Behavior:

- Window picker lists visible top-level windows with title and process name.
- The Jieping process is excluded from the picker.
- Recording follows the selected window position during recording.
- Output dimensions are fixed to the selected window size at recording start.
- If the window shrinks, unused area is padded with black.
- If the window grows, capture is cropped to the original output canvas.
- Closed, minimized, hidden, cloaked, or too-small windows produce clear errors.

### Full Screen Recording

Users can record a selected display.

Behavior:

- Full Screen mode defaults to the primary display.
- A Display selector lists detected displays.
- Secondary displays can be selected when available.
- Display bounds come from `System.Windows.Forms.Screen.AllScreens`.
- Output dimensions are normalized to even values for H.264 compatibility.

Future option:

- Record all displays into one large canvas.

## 4. Controls

### Main Window

The main window provides:

- Recording mode buttons: Region, Window, Full Screen.
- Header settings language selector: English, Chinese.
- Current target summary.
- State and elapsed time.
- Recording preset selector.
- Frame rate setting.
- Quality setting.
- Video bitrate selector.
- Display selector for Full Screen mode.
- Output directory selector.
- System audio toggle.
- Microphone toggle and device picker.
- Mouse cursor capture toggle.
- Mouse click highlight toggle.
- Text watermark toggle and watermark text input.
- Start, Pause/Resume, Stop/Cancel controls.
- Open File and Open Folder actions after completion.
- Start/end trim fields and Save Trimmed Copy action after completion.
- GIF FPS, max-width, export, and Open GIF actions after completion.
- In-session recording history with open file, open folder, and clear actions.

### Countdown

Start Recording runs a 3 second countdown before FFmpeg capture starts.

Behavior:

- State is `Countdown`.
- Status shows `Recording starts in N...`.
- Stop button changes to Cancel.
- Target and recording settings are locked.
- Canceling countdown restores `TargetSelected` and does not create an MP4.
- Elapsed time starts only after actual recording begins.

### Global Hotkeys

Global hotkeys:

- `Ctrl+Shift+F9`: start/stop toggle.
- `Ctrl+Shift+F10`: stop-only.

If a hotkey is unavailable, the app reports a non-fatal status message and keeps normal UI controls usable.

### Main Window Auto-Hide

The main window hides during countdown and active recording so it is not captured by Region or Full Screen modes.

Behavior:

- Main window hides only when the stop hotkey is registered.
- Main window remains hidden during countdown, recording, paused, and stopping states.
- Main window restores after countdown cancellation, successful finalization, or error.
- If the stop hotkey is unavailable, the window stays visible so the user can stop from the UI.

## 5. Audio

System audio:

- Optional and disabled by default.
- Uses NAudio WASAPI loopback capture.
- Writes temporary WAV and muxes with video through FFmpeg.

Microphone:

- Optional and disabled by default.
- Uses NAudio input device capture.
- Device picker is enabled when microphone capture is enabled.
- Writes temporary WAV and muxes with video through FFmpeg.

Mixed audio:

- If system audio and microphone are both enabled, FFmpeg mixes both WAV inputs into one AAC audio stream.
- Requested audio sources that produce no usable samples fail clearly instead of silently producing partial audio.

## 6. Video Output

Default output:

- Container: MP4.
- Video codec: H.264.
- Pixel format: yuv420p.
- File names use timestamped `Recording_yyyy-MM-dd_HHmmss.mp4`.
- Default folder: user's Videos/Jieping folder.

Quality modes:

- Standard: faster encode, higher CRF.
- High: slower encode, lower CRF.

Bitrate modes:

- Auto: use CRF-based quality mode.
- 4 Mbps, 8 Mbps, 12 Mbps, 20 Mbps: use target video bitrate with constrained maxrate/buffer settings.
- Actual file bitrate can be lower than the target on simple or low-motion content.

Frame rates:

- 15 FPS.
- 30 FPS.
- 60 FPS.

Recording presets:

- Compact: 15 FPS, Standard quality, 4 Mbps.
- Balanced: 30 FPS, Standard quality, Auto bitrate.
- Smooth: 60 FPS, High quality, 12 Mbps.
- Custom: manual frame-rate, quality, or bitrate override.

## 7. Post-Processing

### Trim Copy

Users can create a trimmed copy of the last completed recording.

Behavior:

- Trim start and trim end values are entered in seconds.
- Empty values are treated as 0 seconds.
- Negative trim values are rejected.
- FFprobe reads the actual source duration before trimming.
- FFmpeg re-encodes the trimmed segment to H.264/AAC MP4 for accurate cuts.
- The original recording is preserved.
- Trimmed copies are written beside the source using `_trimmed` naming.
- After success, Open File and Open Folder target the trimmed copy.

### GIF Export

Users can export the last completed recording or trimmed copy as a looping GIF.

Behavior:

- GIF frame rate can be selected from preset FPS values.
- GIF max width can be selected from preset widths.
- Small videos are not upscaled beyond their source width.
- FFmpeg uses a palette generation and palette use filter chain for better GIF color quality.
- GIFs are written beside the source using `_gif.gif` naming.
- GIF export does not replace the active MP4 output path, so trimming remains tied to the current MP4 source.
- Open GIF opens the latest generated GIF artifact.

### Mouse Click Highlight

Users can record visual click pulses in the output video.

Behavior:

- Highlight mouse clicks is enabled by default.
- Left, right, and middle button press edges generate short expanding rings.
- Click coordinates are read from the live cursor position during frame capture.
- Rings are drawn after desktop capture and before cursor drawing, so the pointer remains visible above the highlight.
- Window-follow recordings convert click coordinates against the current per-frame window bounds.
- Pause waits the frame pump, so click pulses are not accumulated while paused.

### Text Watermark

Users can burn a text watermark into the recording.

Behavior:

- Watermark is disabled by default.
- Default watermark text is `Jieping`.
- Empty watermark text is skipped.
- The watermark is drawn in the bottom-right corner after desktop capture and before cursor drawing.
- Text uses a semi-transparent dark backing and light foreground for readability.
- Font size scales with capture size and is bounded for small and large recordings.
- Long watermark text is trimmed with an ellipsis instead of overflowing the video bounds.

### Recording History

Users can review recent artifacts created by the app.

Behavior:

- History is persisted locally in `%LOCALAPPDATA%\Jieping\recording-history.json`.
- Successful MP4 recordings, trimmed MP4 copies, and GIF exports are added to history.
- Failed or canceled operations do not add history entries.
- History keeps the most recent 20 artifacts.
- Each item shows file name, artifact type, created time, and details.
- Each item can open the file or its folder.
- Clearing the current target does not clear history.
- Clearing history removes the history JSON only and does not delete recording files.
- History stores local file paths, artifact type, creation time, and short details; it does not store thumbnails, window titles, audio device names, crash reports, or update manifest data.

## 8. Error Handling

Errors should be short and actionable.

Examples:

- FFmpeg is missing.
- Output directory is unavailable.
- Selected window was closed.
- Selected window was minimized.
- Selected window is no longer visible.
- Audio device is unavailable.
- Requested audio did not capture usable samples.
- Trim values remove the entire recording.

## 9. Roadmap

### Phase 1: Video Recording MVP

Implemented:

- Desktop app shell.
- Recording mode selection.
- Region selection overlay.
- Full screen capture.
- Region capture.
- MP4 generation.
- Stop and finalize flow.
- Output path display.
- Basic window enumeration and window recording.

### Phase 2: Audio and Control Improvements

Implemented:

- System audio recording.
- Microphone recording.
- Global hotkeys.
- Pause and resume.
- Recording countdown.
- Mouse cursor capture settings.

### Phase 3: Recording Quality Improvements

Implemented:

- Window-follow recording.
- Fullscreen display selection.
- Main window auto-hide to avoid self-capture.
- Recording presets.
- Quality and bitrate controls.

### Phase 4: Post-Processing

Implemented:

- Simple start/end trimming.
- GIF export.
- Mouse click highlight.
- Watermark option.
- Recording history.

Planned:

- Persistent history across app restarts.

### Phase 5: Distribution

Implemented:

- App installer.
- Signing pipeline.
- Auto-update check.
- Crash reporting with user consent.

Auto-update check behavior:

- The app can check an HTTP/HTTPS or local JSON manifest.
- The manifest carries app id, runtime, version, package URL, SHA256, and publication metadata.
- The app compares the manifest version against its current assembly version.
- When an update is available, the app can open the package URL for manual download.
- The app can download the update package into `%LOCALAPPDATA%\Jieping\Updates`.
- Downloaded packages are written through a temporary file and only promoted after SHA256 verification succeeds.
- The app does not replace itself while running in this phase.
- The package script can generate `update-manifest.json` after package signing and SHA256 generation.

Crash reporting behavior:

- Crash reporting is disabled by default.
- Users can opt in to saving local crash reports.
- Reports are written to `%LOCALAPPDATA%\Jieping\CrashReports`.
- Reports include a report id, timestamp, app version, .NET runtime, OS version, bitness, exception type, message, stack trace, and inner exceptions.
- Reports are not uploaded automatically.
- The app can open the crash report folder for the user.

## 10. Validation Policy

Before marking a milestone complete:

- Run `dotnet build .\Jieping.slnx`.
- Exercise the real WPF app path when UI behavior changed.
- Probe generated MP4 files with `ffprobe`.
- Decode generated MP4 files with `ffmpeg -v error -i <file> -f null -`.
- Update `docs/VALIDATION_NOTES.md` with real evidence.

## 11. Immediate Next Step

Phase 5 distribution is implemented at the current planned scope. Remaining hardening options:

1. Replace manual verified download with a dedicated updater such as Velopack.
2. Add a real release signing certificate and trusted timestamping.
