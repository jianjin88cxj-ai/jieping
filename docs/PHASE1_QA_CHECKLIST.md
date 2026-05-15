# Phase 1 QA Checklist

Date: 2026-05-15

## Build And Launch

- [x] `dotnet build .\Jieping.slnx` succeeds with 0 warnings and 0 errors.
- [x] App launches on Windows.
- [x] Main window renders and responds.

## Region Selection

- [x] Region mode can open a transparent selection overlay.
- [x] User can drag to select a rectangle.
- [x] Overlay displays selected width and height while dragging.
- [x] Selection can be confirmed.
- [x] Selection can be canceled without changing the previous target.
- [x] Main window displays selected region bounds.

## Region Recording

- [x] User can record a selected region.
- [x] Recording can run for at least 10 seconds.
- [x] Stop finalizes the MP4 file.
- [x] Output video contains the selected region content.
- [x] Output MP4 decodes successfully with FFmpeg.

## Full Screen Recording

- [x] Full Screen mode maps to the primary display.
- [x] User can record the primary display.
- [x] Stop finalizes the MP4 file.
- [x] Output resolution matches the primary display capture size.
- [x] Output MP4 decodes successfully with FFmpeg.

## Window Recording

- [x] Window mode opens a visible-window picker.
- [x] Picker displays window title and process name.
- [x] User can select a visible desktop application window.
- [x] User can record the selected window area.
- [x] Stop finalizes the MP4 file.
- [x] Output video contains the selected window content.
- [x] Minimized selected windows are blocked before recording starts.

## Output And Completion

- [x] Default output directory is `%USERPROFILE%\Videos\Jieping`.
- [x] User can choose a non-default output directory.
- [x] Output file is saved to the chosen directory.
- [x] App displays the final output path.
- [x] Open File action is available after completion.
- [x] Open Folder action is available after completion.

## Video Settings

- [x] Frame rate setting is wired to the recorder.
- [x] 15 FPS output was verified with `ffprobe`.
- [x] 30 FPS output was verified in region, full screen, and window recordings.
- [ ] 60 FPS output should be rechecked during performance tuning.

## Known MVP Limitations

- System audio and microphone recording have been implemented after the Phase 1 MVP.
- Pause/resume has been implemented after the Phase 1 MVP.
- Full screen display selection has been implemented after the Phase 1 MVP.
- Window recording now follows the selected window position during recording; output size stays fixed to the start bounds.
- Output actions use the Windows shell; behavior depends on default file associations.
