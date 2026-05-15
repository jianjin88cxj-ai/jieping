# Validation Notes

## 2026-05-16 - App Icon And Window Recording Visibility

Scope verified:

- Generated a custom Jieping app icon for the screen recorder.
- Removed the chroma-key background and produced `Assets\AppIcon.png` with transparent corners.
- Generated `Assets\AppIcon.ico` with Windows icon sizes.
- Configured the WPF project `ApplicationIcon` and main window `Icon`.
- Fixed Window recording mode so it does not hide the Jieping main window during recording.
- Kept Region and Full Screen modes eligible for main-window hiding to avoid self-capture.

Commands run:

```powershell
dotnet build .\Jieping.slnx
dotnet run --project $env:TEMP\jieping-window-mode-shell-smoke\WindowShellSmoke.csproj
```

Result:

- Build succeeded with 0 warnings and 0 errors.
- Window mode shell smoke passed: Window mode start/stop emitted no main-window suppression events.
- Icon PNG validation confirmed RGBA output with transparent corner alpha.

## 2026-05-16 - Main Window UI Redesign

Scope verified:

- Changed the main window to a fixed 1080 x 720 layout with `ResizeMode=CanMinimize` so it cannot be stretched horizontally.
- Replaced the long single-column form with a two-column desktop tool layout.
- Added card-style grouping for recording mode, target state, status, video settings, capture options, post-processing, history, updates, and diagnostics.
- Removed the default WPF tab layout after screenshot verification showed wrapped tabs and visual instability.
- Kept the English/Chinese language selector visible in the header.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

Launch and visual check:

- Started `Jieping.App\bin\Debug\net8.0-windows\Jieping.App.exe`.
- Captured the redesigned fixed-size main window at `artifacts\ui-redesign-main-window-v2.png`.
- Confirmed the window captured at `1080 x 720` and the primary controls rendered without horizontal stretching.

## 2026-05-15 - Settings Language Selector

Scope verified:

- Added a first-viewport header Settings language selector with English and Chinese options.
- Added ViewModel-backed localized UI text lookup.
- Converted the main window's primary visible labels and action buttons from hardcoded English to language-aware bindings.
- Localized common recording, update, diagnostics, and history status messages.
- Kept language switching immediate for the open window by raising indexer and dependent label property changes.

Commands run:

```powershell
dotnet build .\Jieping.slnx
dotnet run --project $env:TEMP\jieping-language-vm-smoke\LanguageSmoke.csproj
```

Result:

- Build succeeded with 0 warnings and 0 errors.
- Language ViewModel smoke passed.

Launch check:

- Started `Jieping.App\bin\Debug\net8.0-windows\Jieping.App.exe`.
- Confirmed the WPF app stayed running for 3 seconds after the XAML localization changes.

## 2026-05-15 - Milestone 0 Foundation

Scope verified:

- Created `Jieping.slnx`.
- Created WPF project `Jieping.App`.
- Added main window controls for Region, Window, and Full Screen modes.
- Added recording states: Idle, TargetSelected, Recording, Stopping, Completed, Error.
- Added placeholder command handlers for selecting a target, starting, and stopping.
- Added initial app configuration for output directory, frame rate, and quality.
- Added root README with build and run instructions.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

Launch check:

- Started `Jieping.App\bin\Debug\net8.0-windows\Jieping.App.exe`.
- Main window title was `Jieping`.
- Process was responding.
- App was closed after verification.

Remaining work:

- Region selection overlay is not implemented yet.
- Screen capture and MP4 encoding are not implemented yet.
- Placeholder start and stop handlers do not create video files.

## 2026-05-15 - Milestone 1 Region Selection

Scope verified:

- Added transparent topmost region selection overlay.
- Added mouse drag selection with normalized bounds.
- Added live size badge while dragging.
- Added Enter/double-click confirm and Esc/right-click cancel.
- Added minimum region rejection below 16 x 16.
- Wired Region mode in the main window to the real overlay.
- Main window now displays selected region size and screen coordinates.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

Automated interaction checks:

- Launched `Jieping.App\bin\Debug\net8.0-windows\Jieping.App.exe`.
- Clicked `Select Target` through UI Automation.
- Dragged from `(220, 220)` to `(520, 390)` and confirmed with Enter.
- Main window showed `Region: 300 x 170 at (220, 220)`.
- Main window state changed to `TargetSelected`.
- Reopened the app, clicked `Select Target`, canceled with Esc.
- Main window stayed at `Region: Not selected` and `State: Idle`.

Remaining work:

- Screen capture for the selected region is not implemented yet.
- MP4 encoding is not implemented yet.
- DPI behavior has code-level conversion support, but still needs manual verification on a scaled display.

## 2026-05-15 - Milestone 2 Basic Video Encoding Pipeline

Scope verified:

- Added internal video recorder service boundary.
- Added FFmpeg-backed MP4 recorder service.
- Added default output filename generation: `Recording_yyyy-MM-dd_HHmmss.mp4`.
- Streamed generated BGRA frames directly to FFmpeg stdin.
- Wired Start and Stop buttons to the real recorder lifecycle.
- Added elapsed recording time display.
- Surfaced FFmpeg startup/finalization errors through the UI status message.

Encoder decision:

- Milestone 2 uses external `ffmpeg` from PATH.
- `ffmpeg.exe` and `ffprobe.exe` were available in the local Windows environment.
- Real screen capture is still deferred to Milestone 3; Milestone 2 uses synthetic frames to prove the encoding and finalization path.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

Automated interaction check:

- Launched `Jieping.App\bin\Debug\net8.0-windows\Jieping.App.exe`.
- Selected a region through the overlay.
- Clicked `Start Recording`.
- Waited about 3 seconds.
- Clicked `Stop`.
- Main window state changed to `Completed`.
- Main window status showed `Recording finalized.`

Generated file:

```text
C:\Users\Administrator\Videos\Jieping\Recording_2026-05-15_004746.mp4
```

Probe result:

```text
codec_name=h264
width=320
height=180
avg_frame_rate=30/1
duration=2.166667
```

Decode check:

```powershell
ffmpeg -v error -i C:\Users\Administrator\Videos\Jieping\Recording_2026-05-15_004746.mp4 -f null -
```

Result:

- Exit code 0.
- File size: 34923 bytes.

Remaining work:

- Replace synthetic frames with actual selected-region screen capture.
- Keep the FFmpeg service boundary and feed real frames into the same pipeline.
- Add better user-facing output actions in Milestone 6.

## 2026-05-15 - Milestone 3 Region Recording MVP

Scope verified:

- Added real selected-region screen capture using Windows GDI `CopyFromScreen`.
- Fed captured BGRA frames into the existing FFmpeg rawvideo pipe.
- Kept the FFmpeg encoder/finalization path from Milestone 2.
- Preserved start/stop lifecycle, elapsed timer, output path display, and completed state.
- Kept generated-frame fallback for non-region targets until full screen/window modes are implemented.

Technical note:

- Region selection already returns device-pixel coordinates.
- The recorder uses the selected region without applying DPI scaling again.
- Captured `Format32bppArgb` bitmap rows are copied into a tightly packed BGRA buffer for FFmpeg.
- Width and height remain normalized to even dimensions for H.264/yuv420p output.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

Automated real recording check:

- Started a topmost Windows test window with fixed green, blue, and magenta regions.
- Launched `Jieping.App\bin\Debug\net8.0-windows\Jieping.App.exe`.
- Selected the test window area through the region overlay.
- Clicked `Start Recording`.
- Recorded long enough to satisfy the 10 second validation requirement.
- Clicked `Stop`.
- Main window state changed to `Completed`.
- Main window status showed `Recording finalized.`

Generated file:

```text
C:\Users\Administrator\Videos\Jieping\Recording_2026-05-15_005518.mp4
```

Probe result:

```text
codec_name=h264
width=320
height=220
avg_frame_rate=30/1
duration=11.500000
```

Real-content frame check:

- Extracted frame: `C:\Users\Administrator\AppData\Local\Temp\jieping-m3-frame-long.png`
- Sampled green test block: `3,252,4`
- Sampled blue test block: `0,0,254`
- Sampled magenta test background: `254,0,253`
- Real color samples detected: `True`

Remaining work:

- Implement Full Screen mode with the same recorder pipeline.
- Replace generated-frame fallback for Full Screen after target mapping is complete.
- Implement Window mode later in Milestone 5.

## 2026-05-15 - Milestone 4 Full Screen Recording

Scope verified:

- Added primary-display bounds service.
- Full Screen mode now creates a real `CaptureRegion` from the primary display pixel bounds.
- Full Screen recording reuses the existing GDI capture and FFmpeg encoding pipeline.
- Added service-level guard so Full Screen cannot silently fall back to generated frames when display bounds are missing.
- Updated recording status text to distinguish Region and Full Screen modes.

Technical note:

- Primary display bounds come from `System.Windows.Forms.Screen.PrimaryScreen.Bounds`.
- These bounds are device-pixel coordinates and match the `CaptureRegion` contract used by `CopyFromScreen`.
- Milestone 4 intentionally records only the primary display, not all virtual displays.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

Automated full screen recording check:

- Launched `Jieping.App\bin\Debug\net8.0-windows\Jieping.App.exe`.
- Selected `Full Screen`.
- Clicked `Select Target`.
- Main window showed `Full Screen: 2560 x 1440 at (0, 0)`.
- Clicked `Start Recording`.
- Clicked `Stop`.
- Main window state changed to `Completed`.
- Main window status showed `Recording finalized.`

Generated file:

```text
C:\Users\Administrator\Videos\Jieping\Recording_2026-05-15_005854.mp4
```

Probe result:

```text
codec_name=h264
width=2560
height=1440
avg_frame_rate=30/1
duration=3.233333
```

Decode check:

```powershell
ffmpeg -v error -i C:\Users\Administrator\Videos\Jieping\Recording_2026-05-15_005854.mp4 -f null -
```

Result:

- Exit code 0.
- Output resolution matched primary display bounds: `2560 x 1440`.

Remaining work:

- Implement visible window enumeration and selection.
- Map selected window bounds into the existing capture/encoder pipeline.
- Add closed/minimized window validation before recording.

## 2026-05-15 - Milestone 5 Window Enumeration And Window Recording

Scope verified:

- Added Win32 visible top-level window enumeration.
- Filters empty titles, minimized windows, shell window, tool windows, cloaked windows, tiny bounds, and this process.
- Added window picker dialog with title, process name, and capture bounds.
- Window mode now stores the selected window handle and initial bounds.
- Before recording, the app revalidates the selected window handle and refreshes bounds.
- Closed, hidden, minimized, or invalid window handles are blocked before FFmpeg starts.
- Window recording reuses the existing GDI capture and FFmpeg encoding pipeline.

Technical note:

- Window bounds use `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` first.
- `GetWindowRect` is used as a fallback if DWM bounds are unavailable.
- Bounds remain device-pixel coordinates for `CopyFromScreen`.
- If a selected window moves or resizes before Start, bounds are resolved again at Start.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

Automated window recording check:

- Started a topmost test window named `Jieping Window Capture Probe`.
- Selected `Window` mode.
- Opened the window picker and selected the test window.
- Main window showed the selected window target and bounds.
- Clicked `Start Recording`, waited, then clicked `Stop`.
- Main window state changed to `Completed`.
- Main window status showed `Recording finalized.`

Generated file:

```text
C:\Users\Administrator\Videos\Jieping\Recording_2026-05-15_011120.mp4
```

Probe result:

```text
codec_name=h264
width=346
height=232
avg_frame_rate=30/1
duration=4.300000
```

Decode check:

```powershell
ffmpeg -v error -i C:\Users\Administrator\Videos\Jieping\Recording_2026-05-15_011120.mp4 -f null -
```

Result:

- Exit code 0.

Real-content frame check:

- Extracted frame: `C:\Users\Administrator\AppData\Local\Temp\jieping-m5-window-frame.png`
- Sampled green test block: `0,255,0`
- Sampled blue test block: `0,0,254`
- Sampled magenta test background: `254,0,253`
- Real window samples detected: `True`

Invalid-window check:

- Selected a visible test window.
- Minimized it before clicking `Start Recording`.
- Main window state changed to `Error`.
- Error message: `Selected window is minimized.`
- No MP4 output path was produced.

Remaining work:

- Add output directory chooser.
- Add completion actions for opening the output file or folder.
- Add a durable manual QA checklist for the completed Phase 1 MVP.

## 2026-05-15 - Milestone 6 MVP Polish And Packaging Readiness

Scope verified:

- Added output directory chooser through a native Windows folder browser.
- Added `Open File` action after recording completion.
- Added `Open Folder` action after recording completion.
- Confirmed frame-rate setting remains wired into the recorder.
- Added durable Phase 1 QA checklist under `docs/PHASE1_QA_CHECKLIST.md`.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

Chosen-directory and frame-rate check:

- Used the same ViewModel command path with an injected output-directory selector.
- Selected non-default output directory:

```text
C:\Users\Administrator\AppData\Local\Temp\jieping-m6-output
```

- Set frame rate to 15 FPS.
- Recorded a region target through the real FFmpeg pipeline.
- Generated file:

```text
C:\Users\Administrator\AppData\Local\Temp\jieping-m6-output\Recording_2026-05-15_011622.mp4
```

Probe result:

```text
codec_name=h264
width=320
height=180
avg_frame_rate=15/1
duration=2.600000
```

Decode result:

- FFmpeg decode exit code 0.

## 2026-05-15 - Phase 2 Microphone Recording

Scope verified:

- Added optional microphone recording next to the existing system audio option.
- Added a microphone device picker populated from active NAudio input devices.
- Extended recording requests and app configuration with microphone enable/device settings.
- When microphone recording is enabled, the recorder writes microphone input to a temporary WAV.
- When either system audio or microphone is enabled, the recorder writes video to a temporary video-only MP4 and muxes final audio with FFmpeg.
- When both system audio and microphone are enabled, FFmpeg mixes both WAV inputs into one AAC audio stream.
- If a requested audio source starts but produces no usable WAV samples, finalization fails clearly instead of silently producing partial audio.

Technical note:

- Microphone recording is disabled by default.
- Microphone capture currently uses NAudio `WaveInEvent` with the selected input device.
- Single-audio and mixed-audio mux paths use explicit FFmpeg stream mapping.
- The microphone device picker is disabled unless microphone recording is enabled and recording settings are editable.

Commands run:

```powershell
dotnet build .\Jieping.slnx
ffmpeg -version
ffprobe -version
```

Result:

- Build succeeded with 0 warnings and 0 errors.
- FFmpeg and FFprobe are available on PATH.

Smoke checks:

- Video-only recording still produces an MP4 with only an H.264 video stream.
- Microphone-only recording produces an MP4 with H.264 video plus AAC audio.
- System-audio-only recording still produces an MP4 with H.264 video plus AAC audio.
- System audio plus microphone produces an MP4 with one H.264 video stream and one mixed AAC audio stream.

- Output file was saved in the chosen directory.

Completion action check:

- Ran a real Full Screen recording through the app UI.
- Generated file:

```text
C:\Users\Administrator\Videos\Jieping\Recording_2026-05-15_011644.mp4
```

- Clicked `Open Folder`.
- Clicked `Open File`.
- Windows shell launched the associated folder/file handlers.
- `Open File` started Windows video playback through `Video.UI`.
- Test-launched media processes were closed after verification.

Phase 1 QA checklist:

- Added `docs/PHASE1_QA_CHECKLIST.md`.
- Region, Full Screen, Window, MP4 output, stop/finalize, output directory, final path display, and open actions are checked.
- 60 FPS is left as a performance-tuning recheck item; 15 FPS and 30 FPS outputs have been verified.

Remaining work:

- Phase 2 starts with audio and control improvements.
- Packaging/signing/installer work remains future scope.

## 2026-05-15 - Phase 2 System Audio Recording

Scope verified:

- Added optional system audio setting in the main window.
- Added `NAudio 2.3.0`.
- Added WASAPI loopback capture for system audio.
- When system audio is enabled, the recorder writes video to a temporary video-only MP4 and system audio to a temporary WAV.
- On stop, FFmpeg muxes video plus WAV into the final MP4 with AAC audio.
- When system audio is disabled, the previous video-only path remains unchanged.
- If system audio startup fails, recording start fails with a clear message instead of silently pretending audio was captured.

Technical note:

- System audio is disabled by default.
- Video frames still use the existing BGRA rawvideo pipe.
- Final audio mux command uses video stream copy and AAC audio encoding.
- The no-active-output-device path was not physically simulated in this environment.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

Video-only regression check:

- Recorded a 320 x 180 region with system audio disabled.
- Generated file:

```text
C:\Users\Administrator\AppData\Local\Temp\jieping-audio-output\Recording_2026-05-15_012358.mp4
```

Probe result:

```text
0,h264,video
duration=3.466667
```

Decode result:

- FFmpeg decode exit code 0.
- No AAC audio stream was present.

System-audio check:

- Played a generated 440 Hz system tone during recording.
- Recorded a 320 x 180 region with system audio enabled.
- Generated file:

```text
C:\Users\Administrator\AppData\Local\Temp\jieping-audio-output\Recording_2026-05-15_012403.mp4
```

Probe result:

```text
0,h264,video
1,aac,audio
duration=3.533333
```

Decode result:

- FFmpeg decode exit code 0.
- Output contains one H.264 video stream and one AAC audio stream.

## 2026-05-15 - Phase 2 Global Hotkeys

Scope verified:

- Added global start/stop toggle hotkey: `Ctrl+Shift+F9`.
- Added global stop-only hotkey: `Ctrl+Shift+F10`.
- Hotkeys are registered after the WPF window handle is created.
- Hotkey messages dispatch to the existing `StartRecordingCommand` and `StopRecordingCommand`.
- The existing command state gates still decide whether a hotkey can start or stop recording.
- Registered hotkeys are unregistered when the main window closes.
- If a hotkey is already owned by another application, the app now shows a non-fatal status message instead of crashing.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

Startup smoke:

- Launched `Jieping.App.exe` from the Debug output.
- The process stayed running for the smoke window and closed normally through the main window.
- This verifies that hotkey registration does not crash the app during startup in the current environment.

## 2026-05-15 - Phase 2 Mouse Cursor Capture Setting

Scope verified:

- Added a `Record mouse cursor` setting, enabled by default.
- Added cursor capture configuration to app configuration and recording start requests.
- The setting is locked while countdown, recording, paused, or stopping states are active.
- When enabled, the recorder draws the visible Win32 cursor into the captured bitmap after `CopyFromScreen` and before raw BGRA frame extraction.
- Cursor drawing accounts for the Win32 cursor hotspot and capture-region offset.
- When disabled, the recorder skips all cursor drawing work.
- Cursor drawing failure is non-fatal; the frame is still emitted.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

UI smoke:

- Launched the WPF app from the Debug output.
- Recorded once with `Record mouse cursor` enabled.
- Recorded once with `Record mouse cursor` disabled.
- Both recordings generated H.264 MP4 files and passed `ffmpeg -v error -i <file> -f null -`.

Phase 2 status:

- Phase 2 audio and control improvements are implemented.

Remaining work:

- Begin Phase 3 recording quality improvements.

## 2026-05-15 - Phase 3 Window Follow Recording

Scope verified:

- Window mode now follows the selected window position while recording.
- The recorder refreshes selected-window bounds before each captured frame.
- Output dimensions stay fixed to the selected window size at recording start because the FFmpeg rawvideo stream uses fixed dimensions.
- If the selected window shrinks, the output keeps the original canvas and pads unused area with black.
- If the selected window grows, capture is cropped to the original output canvas.
- If the selected window is closed, minimized, hidden, cloaked, or becomes too small, recording fails with a clear window-state error on finalization.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

UI smoke:

- Launched Notepad and selected it through Window mode.
- Started recording, moved the Notepad window during capture, then stopped recording.
- Generated output:

```text
C:\Users\Administrator\Videos\Jieping\Recording_2026-05-15_015324.mp4
```

Probe result:

```text
0,h264,video
626,412
duration=2.833333
```

Decode result:

- FFmpeg decode exit code 0.

## 2026-05-15 - Phase 3 Quality And Bitrate Controls

Scope verified:

- Added video bitrate options: Auto, 4 Mbps, 8 Mbps, 12 Mbps, and 20 Mbps.
- Auto bitrate preserves the existing CRF-based quality behavior.
- Fixed bitrate options add FFmpeg `-b:v`, `-maxrate`, and `-bufsize` arguments.
- Recording presets now include bitrate as part of the preset tuple.
- Compact maps to 15 FPS, Standard quality, 4 Mbps.
- Balanced maps to 30 FPS, Standard quality, Auto bitrate.
- Smooth maps to 60 FPS, High quality, 12 Mbps.
- Manual bitrate, frame rate, or quality changes switch to Custom unless the whole tuple matches a named preset.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

View model behavior check:

```text
initial=Balanced,30,Standard,Auto
smooth=Smooth,60,High,12 Mbps
manual8=Custom,60,High,8 Mbps
smoothMatch=Smooth,60,High,12 Mbps
compact=Compact,15,Standard,4 Mbps
```

FFmpeg argument check:

```text
-preset slow -crf 18
-preset veryfast -b:v 8000k -maxrate 16000k -bufsize 32000k
```

8 Mbps encode smoke:

- Generated output:

```text
C:\Users\Administrator\AppData\Local\Temp\jieping-bitrate-output\Recording_2026-05-15_021335.mp4
```

Probe result:

```text
codec_name=h264
width=320
height=180
r_frame_rate=30/1
avg_frame_rate=30/1
bit_rate=27092
```

Decode result:

- FFmpeg decode exit code 0.

Technical note:

- The low measured bitrate is expected for the synthetic low-motion smoke sample; the important verification here is that constrained bitrate arguments are generated and FFmpeg produces a valid MP4.

Phase 3 status:

- Phase 3 recording quality improvements are implemented.

Remaining work:

- Begin Phase 4 post-processing.

## 2026-05-15 - Phase 3 Recording Presets

Scope verified:

- Added recording presets: Compact, Balanced, Smooth, and Custom.
- Balanced is the default preset and maps to 30 FPS with Standard quality.
- Compact maps to 15 FPS with Standard quality.
- Smooth maps to 60 FPS with High quality.
- Selecting a named preset updates frame rate and quality together.
- Manual frame rate or quality changes switch to Custom unless the selected pair matches a named preset.
- Preset selection is locked during countdown and recording through the existing settings lock.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

View model behavior check:

```text
initial=Balanced,30,Standard
smooth=Smooth,60,High
manual30=Custom,30,High
balancedMatch=Balanced,30,Standard
compact=Compact,15,Standard
```

Smooth preset UI smoke:

- Selected Smooth preset through the WPF UI.
- Recorded Full Screen.
- Generated H.264 MP4 output.
- FFprobe result:

```text
codec_name=h264
width=2560
height=1440
r_frame_rate=60/1
avg_frame_rate=60/1
```

- FFmpeg decode exit code 0.

## 2026-05-15 - Phase 3 Exclude Controller From Capture

Scope verified:

- The app now hides the main WPF window when a recording countdown starts.
- The main window stays hidden during countdown, recording, paused, and stopping states.
- The main window is restored and activated after countdown cancellation, successful finalization, or recording error.
- The app only auto-hides when the global stop hotkey is registered; otherwise it keeps the main window visible so the user can still stop from the UI.
- A short delay after hiding gives the desktop time to remove the window before FFmpeg capture starts.
- This first implementation avoids adding a separate floating controller and relies on the existing global hotkeys while hidden.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

Countdown cancel smoke:

- Selected Full Screen target.
- Started countdown.
- Confirmed the main window was hidden during countdown.
- Sent `Ctrl+Shift+F10` through Win32 keyboard events.
- Confirmed the main window was restored.
- Confirmed no MP4 was created.

Recording stop smoke:

- Selected Full Screen target.
- Started countdown and let recording begin.
- Confirmed the main window was hidden during countdown and recording.
- Sent `Ctrl+Shift+F10` through Win32 keyboard events.
- Confirmed the main window was restored after finalization.
- Generated H.264 MP4 output passed `ffmpeg -v error -i <file> -f null -`.

## 2026-05-15 - Phase 3 Fullscreen Display Selection

Scope verified:

- Full Screen mode now exposes a display selector populated from `System.Windows.Forms.Screen.AllScreens`.
- The primary display is selected by default.
- Full Screen target selection uses the selected display bounds instead of always using the primary display.
- The selector is editable only while Full Screen mode is selected and recording settings are not locked.
- Region and window recording continue to use their existing virtual-screen coordinate paths.
- Odd display dimensions are normalized to even output dimensions by the existing encoder request logic.

Detected displays in this environment:

```text
\\.\DISPLAY5 primary=True bounds=0,0,2560,1440
\\.\DISPLAY1 primary=False bounds=-13,1440,1707,1067
```

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

Primary display UI smoke:

- Selected Full Screen mode.
- Selected target with the default primary display.
- Generated H.264 MP4 output at `2560 x 1440`.
- FFmpeg decode exit code 0.

Secondary display UI smoke:

- Selected Full Screen mode.
- Selected `Display 2`.
- Generated H.264 MP4 output at `1706 x 1066`; source display bounds were `1707 x 1067`, normalized to even dimensions for H.264.
- FFmpeg decode exit code 0.

## 2026-05-15 - Phase 2 Pause And Resume

Scope verified:

- Added `Paused` recording state.
- Added recorder service `PauseAsync` and `ResumeAsync` operations.
- Added a Pause/Resume button that is enabled while recording or paused.
- Stop is available from both Recording and Paused states.
- Video frame pumping waits while paused without closing FFmpeg stdin.
- System audio and microphone capture sessions stay alive while paused, but audio buffers are discarded during the pause so the output timeline is compressed with the video.
- Elapsed time now accumulates active recording time and excludes paused duration.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

UI smoke:

- Launched the WPF app from the Debug output.
- Selected Full Screen target.
- Started recording, paused, resumed, and stopped through the UI.
- State transitions observed: `TargetSelected -> Recording -> Paused -> Recording -> Completed`.
- Generated MP4 output passed `ffmpeg -v error -i <file> -f null -`.

## 2026-05-15 - Phase 2 Recording Countdown

Scope verified:

- Added `Countdown` recording state.
- Start Recording now runs a 3 second countdown before starting FFmpeg capture.
- Countdown status shows `Recording starts in N...`.
- During countdown, target selection and recording settings are locked.
- Stop button changes to Cancel during countdown.
- Canceling during countdown returns to `TargetSelected` and does not create an MP4.
- Elapsed time remains `00:00:00` during countdown and starts only after recording begins.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

UI smoke:

- Launched the WPF app from the Debug output.
- Selected Full Screen target.
- Started countdown, canceled it, and confirmed no new MP4 was created.
- Started countdown again, let it complete, recorded, and stopped through the UI.
- State transitions observed for the completed path: `TargetSelected -> Countdown -> Recording -> Completed`.
- Generated MP4 output passed `ffmpeg -v error -i <file> -f null -`.

## 2026-05-15 - Phase 4 Simple Start/End Trimming

Scope verified:

- Added a post-processing service for FFmpeg-based trim copies.
- Added trim start and trim end inputs in seconds.
- Added a `Save Trimmed Copy` action enabled after recording completion.
- Preserved the source recording and wrote trimmed MP4 files beside the source using `_trimmed` naming.
- Used `ffprobe` to read the actual source duration before trimming.
- Updated `LastOutputPath` to the trimmed copy after successful trimming so existing Open File/Open Folder actions point at the new artifact.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

Service smoke:

- Generated a 5 second synthetic MP4 with test video and AAC audio in `%TEMP%\jieping-trim-smoke`.
- Called `FfmpegVideoPostProcessorService.TrimAsync` with `TrimStartSeconds = 1` and `TrimEndSeconds = 1`.
- Generated trimmed output at `%TEMP%\jieping-trim-smoke\source clip_trimmed.mp4`.
- `ffprobe` reported trimmed duration `3.000000`.
- `ffmpeg -v error -i <trimmed.mp4> -f null -` completed successfully.
- Repeated the same service path with a 4 second video-only MP4.
- Generated `%TEMP%\jieping-trim-smoke\video only source_trimmed.mp4`; `ffprobe` reported duration `2.000000`; decode completed successfully.

## 2026-05-15 - Phase 4 GIF Export

Scope verified:

- Added a post-processing GIF export service path.
- Added `Export GIF` and `Open GIF` actions after recording completion.
- Added GIF FPS and max-width selectors.
- Kept `LastOutputPath` pointing at the MP4 source, while storing the latest GIF in a separate `LastGifOutputPath`.
- Used FFmpeg palette generation and palette use filters for GIF quality.
- Wrote GIFs beside the source using `_gif.gif` naming.
- Avoided upscaling source videos smaller than the selected max width.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

Service smoke:

- Generated a 3 second synthetic MP4 with test video and AAC audio in `%TEMP%\jieping-gif-smoke`.
- Called `FfmpegVideoPostProcessorService.ExportGifAsync` with `FrameRate = 12` and `Width = 480`.
- Generated `%TEMP%\jieping-gif-smoke\source clip_gif.gif`.
- `ffprobe` reported `gif,480,270`; `ffmpeg -v error -i <gif> -f null -` completed successfully.
- Repeated the same service path with a 2 second video-only MP4 at `320 x 180`.
- Generated `%TEMP%\jieping-gif-smoke\video only source_gif.gif`.
- `ffprobe` reported `gif,320,180`, confirming the max-width setting did not upscale the smaller source; decode completed successfully.

## 2026-05-15 - Phase 4 Mouse Click Highlight

Scope verified:

- Added a `Highlight mouse clicks` pointer setting enabled by default.
- Passed the setting through `VideoRecorderStartRequest` into the recorder frame pump.
- Added left, right, and middle click pulse detection with `GetAsyncKeyState`.
- Read click coordinates with `GetCursorPos` so hidden cursor visibility does not block click positioning.
- Drew expanding click rings during frame capture after `CopyFromScreen` and before cursor drawing.
- Kept highlighter state inside the live recording run, so paused recordings do not accumulate new click pulses while the frame pump waits.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

Recording smoke:

- Ran a temporary service harness in `%TEMP%\jieping-click-highlight-smoke`.
- Started a 30 FPS region recording at `640 x 360` with cursor capture and click highlight enabled.
- Simulated left and right mouse clicks through Win32 mouse input while recording.
- Generated `%TEMP%\jieping-click-highlight-smoke\Recording_2026-05-15_023100.mp4`.
- `ffprobe` reported `h264,640,360`; `ffmpeg -v error -i <mp4> -f null -` completed successfully.

## 2026-05-15 - Phase 4 Text Watermark

Scope verified:

- Added a text watermark recording option disabled by default.
- Added default watermark text `Jieping`.
- Added UI controls for enabling the watermark and editing watermark text.
- Passed watermark settings through `VideoRecorderStartRequest` into the recorder frame pump.
- Drew the watermark during frame capture after desktop capture and before cursor drawing.
- Used a semi-transparent dark backing and light text for readability.
- Scaled font size with capture dimensions and trimmed long text with ellipsis to keep the watermark inside the frame.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

Recording smoke:

- Ran a temporary service harness in `%TEMP%\jieping-watermark-smoke`.
- Recorded a 30 FPS region at `640 x 360` with watermark enabled and text `Jieping`.
- Generated `%TEMP%\jieping-watermark-smoke\Recording_2026-05-15_023340.mp4`.
- `ffprobe` reported `h264,640,360`; `ffmpeg -v error -i <mp4> -f null -` completed successfully.
- Repeated the same path with a long watermark label to exercise text trimming and in-frame layout.
- Generated `%TEMP%\jieping-watermark-smoke\Recording_2026-05-15_023343.mp4`.
- `ffprobe` reported `h264,640,360`; decode completed successfully.

## 2026-05-15 - Phase 4 Recording History

Scope verified:

- Added an in-memory recent artifact history for the current app session.
- Added history entries for successful MP4 recordings, trimmed MP4 copies, and GIF exports.
- Kept GIF exports separate from the active MP4 path.
- Kept history intact when selecting a new target or clearing current output paths.
- Added per-item Open and Folder actions plus a Clear history action.
- Capped history at the 20 most recent artifacts.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

ViewModel smoke:

- Ran a temporary harness in `%TEMP%\jieping-history-smoke` with fake recorder and post-processor services.
- Selected a region target, completed a recording, and verified one `MP4` history item.
- Ran trim and verified a new `Trimmed MP4` history item and updated current MP4 path.
- Ran GIF export and verified a new `GIF` history item without replacing the current MP4 path.
- Cleared history and verified the collection returned to 0 items.

## 2026-05-15 - Phase 5 Windows Package

Scope verified:

- Added `scripts/package-windows.ps1`.
- The package script publishes `Jieping.App` as a self-contained `win-x64` Release build.
- The package script creates a portable package folder and zip archive.
- The package includes `install.ps1`, `uninstall.ps1`, and `README.txt`.
- The install script copies files to a target install directory and can create a Start Menu shortcut.
- The uninstall script removes the shortcut and install directory.

Commands run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-windows.ps1
```

Result:

- Publish output: `artifacts\publish\win-x64\`.
- Package folder: `artifacts\package\Jieping-win-x64\`.
- Zip archive: `artifacts\Jieping-win-x64-portable.zip`.
- Zip size in this environment: `66425935` bytes.

Package smoke:

- Launched `artifacts\package\Jieping-win-x64\Jieping.App.exe` and confirmed it stayed running for 3 seconds.
- Ran `install.ps1` with `-InstallDir %TEMP%\jieping-package-install-smoke -NoShortcut`.
- Launched the installed `Jieping.App.exe` and confirmed it stayed running for 3 seconds.
- Ran `uninstall.ps1` against the temporary install directory.
- Verified the temporary install directory was removed.

## 2026-05-15 - Phase 5 Signing Pipeline

Scope verified:

- Added `scripts/sign-windows-package.ps1`.
- Signing supports certificate thumbprint, PFX path/password, or a generated local development code-signing certificate.
- Signing applies to packaged `.exe` and `.ps1` files plus the publish output `.exe`.
- Signing regenerates `artifacts\Jieping-win-x64-portable.zip`.
- Signing writes `artifacts\Jieping-win-x64-portable.zip.sha256`.
- `scripts/package-windows.ps1` now supports optional `-Sign` parameters and delegates to the signing script after packaging.

Commands run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-windows.ps1 -Sign -CreateDevCertificate -NoTimestamp
Get-AuthenticodeSignature .\artifacts\package\Jieping-win-x64\Jieping.App.exe
Get-AuthenticodeSignature .\artifacts\package\Jieping-win-x64\install.ps1
Get-AuthenticodeSignature .\artifacts\package\Jieping-win-x64\uninstall.ps1
Get-Content .\artifacts\Jieping-win-x64-portable.zip.sha256
dotnet build .\Jieping.slnx
```

Result:

- Signed with development certificate subject `CN=Jieping Development Code Signing`.
- Development certificate thumbprint in this environment: `91B10123AA56904AAD4441A074EAE080D326A2F4`.
- Authenticode signatures were present on the packaged app executable, install script, and uninstall script.
- `Get-AuthenticodeSignature` returned `UnknownError` with an untrusted root message, which is expected for a self-signed development certificate that has not been trusted locally.
- Signed package hash: `48DE380E2DCC851158851372F614914CD9B9DCD9312BD3905FBA842423E375C4`.
- Signed package launch smoke passed.
- Build succeeded with 0 warnings and 0 errors.

## 2026-05-15 - Phase 5 Auto-Update Check

Scope verified:

- Added stable application version metadata to `Jieping.App.csproj`.
- Added manifest-based update checking through `ManifestUpdateCheckService`.
- Update manifests can be loaded from HTTP/HTTPS URLs or local file paths.
- Manifest validation checks version, package URL, app id, runtime, and SHA256 format.
- Added update UI for current version, manifest location, check action, status text, and opening the update package URL.
- Extended `scripts/package-windows.ps1` with `-Version`, `-Channel`, `-GenerateManifest`, and `-UpdateBaseUrl`.
- Package zips are now versioned as `Jieping-win-x64-<version>-portable.zip`.
- Package manifest generation runs after signing and SHA256 creation.

Commands run:

```powershell
dotnet build .\Jieping.slnx
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-windows.ps1 -Version 0.1.1 -Sign -CreateDevCertificate -NoTimestamp -GenerateManifest -UpdateBaseUrl https://example.com/jieping/stable
```

Result:

- Package output: `artifacts\Jieping-win-x64-0.1.1-portable.zip`.
- Manifest output: `artifacts\update-manifest.json`.
- Manifest package URL: `https://example.com/jieping/stable/Jieping-win-x64-0.1.1-portable.zip`.
- Manifest SHA256: `9B184A6297B694CF3E4D43854FC017441FD8A8F0A3CA70653510637EE983B37E`.

Service smoke:

- Ran a temporary harness in `%TEMP%\jieping-update-smoke`.
- Verified manifest version `0.1.1` reports an available update over current version `0.1.0`.
- Verified manifest version `0.1.1` reports no update over current version `0.1.1`.
- Verified an invalid SHA256 manifest fails with a clear validation error instead of crashing.
- Launched the packaged app and confirmed it stayed running for 3 seconds.

## 2026-05-15 - Phase 5 Local Crash Reports

Scope verified:

- Added local crash report opt-in through `AllowCrashReports`.
- Added `CrashReportingOptions` as the global runtime switch.
- Added `FileCrashReportService` for local report writing under `%LOCALAPPDATA%\Jieping\CrashReports`.
- Added WPF global exception hooks in `App.xaml.cs` for dispatcher, AppDomain, and unobserved task exceptions.
- Removed `StartupUri` and manually creates `MainWindow` after exception hooks are registered.
- Added Diagnostics UI with local crash report consent, report directory display, status text, and Open Crash Reports Folder action.
- Reports include report id, timestamp, source, app version, .NET runtime, OS version, process/OS bitness, exception type, message, stack trace, and inner exceptions.
- Reports are not uploaded automatically.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

Service smoke:

- Ran a temporary harness in `%TEMP%\jieping-crash-smoke`.
- Verified disabled crash reporting returns no report path and creates no new report.
- Enabled crash reporting and wrote a sample report.
- Generated `%LOCALAPPDATA%\Jieping\CrashReports\crash-20260515-025246-736-88d6c56a.txt`.
- Verified the report contains `ReportId`, `DotNetRuntime`, source, exception type, message, and inner exception details.

ViewModel smoke:

- Ran a temporary harness in `%TEMP%\jieping-crash-vm-smoke`.
- Verified `AllowCrashReports` defaults to false.
- Verified enabling it updates `CrashReportingOptions.IsEnabled`.
- Verified Open Crash Reports Folder creates the report directory.
- Verified disabling it updates the global switch back to false.

## 2026-05-15 - Persistent Recording History

Scope verified:

- Added `IRecordingHistoryStore` and `JsonRecordingHistoryStore`.
- Recording history is saved to `%LOCALAPPDATA%\Jieping\recording-history.json`.
- JSON uses a wrapper with `schemaVersion` and `items`.
- Stored item fields are limited to local path, artifact type, creation time, and details.
- History save uses a temporary file and move/replace to avoid partial JSON.
- MainWindowViewModel loads persisted history on startup.
- Adding or clearing history updates the JSON store immediately.
- Clearing history removes the JSON file and does not delete recording files.
- Corrupt JSON falls back to an empty history without crashing.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

Store smoke:

- Ran a temporary harness in `%TEMP%\jieping-history-store-smoke`.
- Verified saved JSON contains `schemaVersion` and `items`.
- Verified load sorts entries by `createdAt` descending.
- Verified missing media files remain in history with `Exists = false` so UI can disable open actions.
- Verified saves are limited to 20 items.
- Verified clear removes the history file.
- Verified corrupt JSON returns an empty list.

ViewModel smoke:

- Ran a temporary harness in `%TEMP%\jieping-history-persist-vm-smoke`.
- Completed a fake recording and verified the history JSON was created.
- Created a new ViewModel instance and verified the MP4 history item was restored from disk.
- Cleared history and verified both the collection and JSON file were removed.

## 2026-05-15 - Verified Update Package Download

Scope verified:

- Added `IUpdatePackageDownloadService` and `UpdatePackageDownloadService`.
- Added `UpdatePackageDownloadResult`.
- Extended `UpdateCheckResult` with manifest package metadata: file name, size, and package type.
- Update packages download to `%LOCALAPPDATA%\Jieping\Updates`.
- Downloads use a temporary file and are promoted only after SHA256 verification succeeds.
- Added ViewModel commands for downloading an available update and opening the verified downloaded package.
- Kept `Open Download Page` as a fallback manual path.

Commands run:

```powershell
dotnet build .\Jieping.slnx
```

Result:

- Build succeeded with 0 warnings and 0 errors.

Service smoke:

- Ran a temporary harness in `%TEMP%\jieping-update-download-smoke`.
- Created a local update package and manifest with a matching SHA256.
- Verified `ManifestUpdateCheckService` loads file name and size metadata.
- Verified `UpdatePackageDownloadService` copies the package, verifies SHA256, and writes the final package file.
- Verified a mismatched SHA256 fails with a clear error and does not promote the temp file.

ViewModel smoke:

- Ran a temporary harness in `%TEMP%\jieping-update-download-vm-smoke`.
- Verified update check enables package download.
- Verified successful download sets `DownloadedUpdatePackagePath` and enables opening the downloaded package.
- Verified a simulated SHA256 mismatch reports an error and leaves no downloaded package path.
