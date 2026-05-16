using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Jieping.App.Models;
using Jieping.App.Recording;
using NAudio.Wave;

namespace Jieping.App.Services;

public sealed class FfmpegVideoRecorderService : IVideoRecorderService
{
    private const string FfmpegExecutable = "ffmpeg";
    private readonly IRecordingOutputPathProvider _pathProvider;
    private readonly object _gate = new();
    private RecorderRun? _current;

    public FfmpegVideoRecorderService()
        : this(new RecordingOutputPathProvider())
    {
    }

    public FfmpegVideoRecorderService(IRecordingOutputPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public bool IsRecording
    {
        get
        {
            lock (_gate)
            {
                return _current is not null;
            }
        }
    }

    public event EventHandler<RecordingPerformanceSnapshot>? PerformanceUpdated;

    public Task<VideoRecorderSession> StartAsync(
        VideoRecorderStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateStartRequest(request);

        lock (_gate)
        {
            if (_current is not null)
            {
                throw new VideoRecorderException("A recording is already in progress.");
            }
        }

        Directory.CreateDirectory(request.OutputDirectory);

        var startedAt = DateTimeOffset.Now;
        var outputPath = _pathProvider.CreateDefaultPath(request.OutputDirectory, startedAt);
        var captureAudio = request.CaptureSystemAudio || request.CaptureMicrophoneAudio;
        var videoOutputPath = captureAudio
            ? CreateSiblingTempPath(outputPath, ".video.mp4")
            : outputPath;
        var pauseController = new PauseController();
        var audioRuns = StartAudioCaptures(request, outputPath, pauseController);
        Process process;
        try
        {
            process = StartFfmpeg(request, videoOutputPath);
        }
        catch
        {
            StopAudioCaptures(audioRuns);
            TryDelete(videoOutputPath);
            throw;
        }

        var run = new RecorderRun(
            process,
            outputPath,
            videoOutputPath,
            audioRuns,
            request.CaptureSystemAudio,
            request.CaptureMicrophoneAudio,
            request.Target.Mode,
            request.Target.Region,
            request.Target.WindowHandle,
            request.Width,
            request.Height,
            request.FrameRate,
            request.CaptureCursor,
            request.HighlightMouseClicks,
            request.IncludeWatermark,
            request.WatermarkText,
            pauseController,
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));

        run.StderrTask = ReadStandardErrorAsync(process.StandardError);
        run.FramePumpTask = PumpFramesAsync(run);

        lock (_gate)
        {
            if (_current is not null)
            {
                run.StopTokenSource.Cancel();
                process.Kill(entireProcessTree: true);
                StopAudioCaptures(audioRuns);
                TryDelete(videoOutputPath);
                throw new VideoRecorderException("A recording is already in progress.");
            }

            _current = run;
        }

        return Task.FromResult(new VideoRecorderSession(
            outputPath,
            request.Width,
            request.Height,
            request.FrameRate,
            startedAt));
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var run = _current ?? throw new VideoRecorderException("No recording is in progress.");
            run.PauseController.Pause();
        }

        return Task.CompletedTask;
    }

    public Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var run = _current ?? throw new VideoRecorderException("No recording is in progress.");
            run.PauseController.Resume();
        }

        return Task.CompletedTask;
    }

    public async Task<string> StopAsync(CancellationToken cancellationToken = default)
    {
        RecorderRun run;
        lock (_gate)
        {
            run = _current ?? throw new VideoRecorderException("No recording is in progress.");
            _current = null;
        }

        run.StopTokenSource.Cancel();

        Exception? framePumpException = null;
        try
        {
            await run.FramePumpTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            framePumpException = ex;
        }

        await run.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await run.StderrTask.ConfigureAwait(false);
        var exitCode = run.Process.ExitCode;
        run.Process.Dispose();
        StopAudioCaptures(run.AudioRuns);
        run.StopTokenSource.Dispose();

        if (exitCode != 0)
        {
            CleanupTempRecordingFiles(run);
            throw new VideoRecorderException(
                $"ffmpeg finalization failed with exit code {exitCode}.{FormatError(stderr)}");
        }

        if (framePumpException is not null)
        {
            CleanupTempRecordingFiles(run);
            throw new VideoRecorderException("Video frame streaming failed.", framePumpException);
        }

        if (!File.Exists(run.VideoOutputPath) || new FileInfo(run.VideoOutputPath).Length == 0)
        {
            CleanupTempRecordingFiles(run);
            throw new VideoRecorderException(
                $"ffmpeg completed but did not create a usable output file at '{run.OutputPath}'.{FormatError(stderr)}");
        }

        await FinalizeOutputAsync(run, cancellationToken).ConfigureAwait(false);

        return run.OutputPath;
    }

    private static void ValidateStartRequest(VideoRecorderStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Target);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputDirectory);

        if (request.FrameRate <= 0)
        {
            throw new VideoRecorderException("Frame rate must be greater than zero.");
        }

        if (request.VideoBitrateKbps is <= 0)
        {
            throw new VideoRecorderException("Video bitrate must be greater than zero when a target bitrate is selected.");
        }

        if (request.Target.Mode is RecordingMode.Region or RecordingMode.Window && request.Target.Region is null)
        {
            throw new VideoRecorderException("Region and window recording require selected capture bounds.");
        }

        if (request.Target.Mode == RecordingMode.FullScreen && request.Target.Region is null)
        {
            throw new VideoRecorderException("Full screen recording requires primary display bounds.");
        }

        if (request.CaptureMicrophoneAudio)
        {
            var deviceCount = WaveIn.DeviceCount;
            if (deviceCount == 0)
            {
                throw new VideoRecorderException("Microphone recording was requested, but no input devices were found.");
            }

            if (request.MicrophoneDeviceNumber is < 0 || request.MicrophoneDeviceNumber >= deviceCount)
            {
                throw new VideoRecorderException("The selected microphone device is no longer available.");
            }
        }
    }

    private static Process StartFfmpeg(VideoRecorderStartRequest request, string outputPath)
    {
        var arguments = BuildArguments(request, outputPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = FfmpegExecutable,
            Arguments = arguments,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            return Process.Start(startInfo)
                ?? throw new VideoRecorderException("ffmpeg failed to start.");
        }
        catch (Exception ex) when (ex is not VideoRecorderException)
        {
            throw new VideoRecorderException(
                "ffmpeg could not be started. Install ffmpeg and make sure it is available on PATH.",
                ex);
        }
    }

    private static string BuildArguments(VideoRecorderStartRequest request, string outputPath)
    {
        var preset = string.Equals(request.Quality, "High", StringComparison.OrdinalIgnoreCase)
            ? "slow"
            : "veryfast";
        var videoRateControlArguments = BuildVideoRateControlArguments(request);

        return string.Join(
            " ",
            "-hide_banner",
            "-loglevel error",
            "-y",
            "-f rawvideo",
            "-pix_fmt bgra",
            $"-s {request.Width}x{request.Height}",
            $"-r {request.FrameRate.ToString(CultureInfo.InvariantCulture)}",
            "-i pipe:0",
            "-an",
            "-c:v libx264",
            $"-preset {preset}",
            videoRateControlArguments,
            "-pix_fmt yuv420p",
            "-movflags +faststart",
            Quote(outputPath));
    }

    private static string BuildVideoRateControlArguments(VideoRecorderStartRequest request)
    {
        if (request.UseTargetVideoBitrate)
        {
            var bitrate = request.VideoBitrateKbps!.Value;
            return string.Join(
                " ",
                $"-b:v {bitrate.ToString(CultureInfo.InvariantCulture)}k",
                $"-maxrate {(bitrate * 2).ToString(CultureInfo.InvariantCulture)}k",
                $"-bufsize {(bitrate * 4).ToString(CultureInfo.InvariantCulture)}k");
        }

        var crf = string.Equals(request.Quality, "High", StringComparison.OrdinalIgnoreCase)
            ? "18"
            : "23";
        return $"-crf {crf}";
    }

    private static IReadOnlyList<AudioCaptureRun> StartAudioCaptures(
        VideoRecorderStartRequest request,
        string outputPath,
        PauseController pauseController)
    {
        var audioRuns = new List<AudioCaptureRun>();
        try
        {
            if (request.CaptureSystemAudio)
            {
                audioRuns.Add(StartSystemAudioCapture(outputPath, pauseController));
            }

            if (request.CaptureMicrophoneAudio)
            {
                audioRuns.Add(StartMicrophoneAudioCapture(outputPath, request.MicrophoneDeviceNumber ?? 0, pauseController));
            }

            return audioRuns;
        }
        catch
        {
            StopAudioCaptures(audioRuns);
            throw;
        }
    }

    private static AudioCaptureRun StartSystemAudioCapture(string outputPath, PauseController pauseController)
    {
        var wavPath = CreateSiblingTempPath(outputPath, ".system-audio.wav");
        try
        {
            var capture = new WasapiLoopbackCapture();
            var writer = new WaveFileWriter(wavPath, capture.WaveFormat);
            var gate = new object();

            capture.DataAvailable += (_, args) =>
            {
                if (pauseController.IsPaused)
                {
                    return;
                }

                lock (gate)
                {
                    try
                    {
                        writer.Write(args.Buffer, 0, args.BytesRecorded);
                        writer.Flush();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            };

            capture.StartRecording();
            return new AudioCaptureRun(
                "system audio",
                wavPath,
                writer,
                gate,
                () => capture.StopRecording(),
                capture.Dispose);
        }
        catch (Exception ex)
        {
            TryDelete(wavPath);
            throw new VideoRecorderException(
                "System audio capture could not be started. Turn off System Audio or connect an active output device.",
                ex);
        }
    }

    private static AudioCaptureRun StartMicrophoneAudioCapture(
        string outputPath,
        int deviceNumber,
        PauseController pauseController)
    {
        var wavPath = CreateSiblingTempPath(outputPath, ".microphone.wav");
        try
        {
            var capture = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = new WaveFormat(44100, 16, 1),
                BufferMilliseconds = 50
            };
            var writer = new WaveFileWriter(wavPath, capture.WaveFormat);
            var gate = new object();

            capture.DataAvailable += (_, args) =>
            {
                if (pauseController.IsPaused)
                {
                    return;
                }

                lock (gate)
                {
                    try
                    {
                        writer.Write(args.Buffer, 0, args.BytesRecorded);
                        writer.Flush();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            };

            capture.StartRecording();
            return new AudioCaptureRun(
                "microphone",
                wavPath,
                writer,
                gate,
                () => capture.StopRecording(),
                capture.Dispose);
        }
        catch (Exception ex)
        {
            TryDelete(wavPath);
            throw new VideoRecorderException(
                "Microphone capture could not be started. Turn off Microphone recording or choose another input device.",
                ex);
        }
    }

    private static void StopAudioCaptures(IEnumerable<AudioCaptureRun> audioRuns)
    {
        foreach (var audioRun in audioRuns)
        {
            try
            {
                audioRun.StopRecording();
            }
            catch
            {
            }

            lock (audioRun.Gate)
            {
                audioRun.Writer.Dispose();
            }

            audioRun.DisposeCapture();
        }
    }

    private static async Task FinalizeOutputAsync(RecorderRun run, CancellationToken cancellationToken)
    {
        var captureAudio = run.CaptureSystemAudio || run.CaptureMicrophoneAudio;
        if (captureAudio && run.AudioRuns.Count == 0)
        {
            PromoteVideoOnlyOutput(run);
            throw new VideoRecorderException(
                "Audio was requested, but no audio capture session was available.");
        }

        var usableAudioRuns = run.AudioRuns
            .Where(audioRun => HasUsableWaveData(audioRun.WavPath))
            .ToArray();
        var unusableAudioRuns = run.AudioRuns
            .Where(audioRun => !usableAudioRuns.Contains(audioRun))
            .ToArray();

        if (captureAudio && unusableAudioRuns.Length > 0)
        {
            PromoteVideoOnlyOutput(run);
            CleanupAudioCaptures(run.AudioRuns);
            var sourceNames = string.Join(", ", unusableAudioRuns.Select(audioRun => audioRun.Name));
            throw new VideoRecorderException(
                $"Audio was requested, but {sourceNames} did not capture usable samples.");
        }

        if (usableAudioRuns.Length > 0)
        {
            await MuxAudioAsync(
                    run.VideoOutputPath,
                    usableAudioRuns.Select(audioRun => audioRun.WavPath).ToArray(),
                    run.OutputPath,
                    cancellationToken)
                .ConfigureAwait(false);
            TryDelete(run.VideoOutputPath);
            CleanupAudioCaptures(run.AudioRuns);
            return;
        }

        if (captureAudio)
        {
            PromoteVideoOnlyOutput(run);
            CleanupAudioCaptures(run.AudioRuns);
            throw new VideoRecorderException(
                "Audio was requested, but no audio samples were captured.");
        }

        PromoteVideoOnlyOutput(run);
        CleanupAudioCaptures(run.AudioRuns);
    }

    private static async Task MuxAudioAsync(
        string videoPath,
        IReadOnlyList<string> audioPaths,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (audioPaths.Count == 0)
        {
            throw new VideoRecorderException("Audio mux was requested without any audio inputs.");
        }

        var muxOutputPath = CreateSiblingTempPath(outputPath, ".mux.mp4");
        var arguments = audioPaths.Count == 1
            ? BuildSingleAudioMuxArguments(videoPath, audioPaths[0], muxOutputPath)
            : BuildMixedAudioMuxArguments(videoPath, audioPaths, muxOutputPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = FfmpegExecutable,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new VideoRecorderException("ffmpeg failed to start while muxing audio.");
        var stderrTask = ReadStandardErrorAsync(process.StandardError);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            TryDelete(muxOutputPath);
            throw new VideoRecorderException(
                $"ffmpeg audio mux failed with exit code {process.ExitCode}.{FormatError(stderr)}");
        }

        if (!File.Exists(muxOutputPath) || new FileInfo(muxOutputPath).Length < 1024)
        {
            TryDelete(muxOutputPath);
            throw new VideoRecorderException("ffmpeg audio mux completed without creating a usable output file.");
        }

        File.Move(muxOutputPath, outputPath, overwrite: true);
    }

    private static string BuildSingleAudioMuxArguments(string videoPath, string audioPath, string outputPath)
    {
        return string.Join(
            " ",
            "-hide_banner",
            "-loglevel error",
            "-y",
            "-i",
            Quote(videoPath),
            "-i",
            Quote(audioPath),
            "-map 0:v:0",
            "-map 1:a:0",
            "-c:v",
            "copy",
            "-c:a",
            "aac",
            "-shortest",
            "-movflags +faststart",
            Quote(outputPath));
    }

    private static string BuildMixedAudioMuxArguments(
        string videoPath,
        IReadOnlyList<string> audioPaths,
        string outputPath)
    {
        var inputs = new List<string>
        {
            "-hide_banner",
            "-loglevel error",
            "-y",
            "-i",
            Quote(videoPath)
        };

        foreach (var audioPath in audioPaths)
        {
            inputs.Add("-i");
            inputs.Add(Quote(audioPath));
        }

        var filterInputs = string.Concat(Enumerable.Range(1, audioPaths.Count).Select(index => $"[{index}:a]"));
        var filter = $"{filterInputs}amix=inputs={audioPaths.Count}:duration=longest:dropout_transition=0[a]";
        inputs.Add("-filter_complex");
        inputs.Add(Quote(filter));
        inputs.Add("-map");
        inputs.Add("0:v:0");
        inputs.Add("-map");
        inputs.Add(Quote("[a]"));
        inputs.Add("-c:v");
        inputs.Add("copy");
        inputs.Add("-c:a");
        inputs.Add("aac");
        inputs.Add("-shortest");
        inputs.Add("-movflags");
        inputs.Add("+faststart");
        inputs.Add(Quote(outputPath));

        return string.Join(" ", inputs);
    }

    private static void PromoteVideoOnlyOutput(RecorderRun run)
    {
        if (string.Equals(run.VideoOutputPath, run.OutputPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        File.Move(run.VideoOutputPath, run.OutputPath, overwrite: true);
    }

    private static bool HasUsableWaveData(string wavPath)
    {
        if (!File.Exists(wavPath) || new FileInfo(wavPath).Length <= 44)
        {
            return false;
        }

        try
        {
            using var reader = new WaveFileReader(wavPath);
            return reader.TotalTime > TimeSpan.FromMilliseconds(100);
        }
        catch
        {
            return false;
        }
    }

    private static string CreateSiblingTempPath(string outputPath, string suffix)
    {
        var directory = Path.GetDirectoryName(outputPath);
        var name = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? Path.GetTempPath() : directory,
            $"{name}.{Guid.NewGuid():N}{suffix}");
    }

    private static void CleanupTempRecordingFiles(RecorderRun run)
    {
        if (!string.Equals(run.VideoOutputPath, run.OutputPath, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(run.VideoOutputPath);
        }

        CleanupAudioCaptures(run.AudioRuns);
    }

    private static void CleanupAudioCaptures(IEnumerable<AudioCaptureRun> audioRuns)
    {
        foreach (var audioRun in audioRuns)
        {
            TryDelete(audioRun.WavPath);
        }
    }

    private Task PumpFramesAsync(RecorderRun run)
    {
        return run.Region is null
            ? PumpSyntheticFramesAsync(run)
            : PumpRegionFramesAsync(run);
    }

    private async Task PumpRegionFramesAsync(RecorderRun run)
    {
        var initialRegion = run.Region
            ?? throw new VideoRecorderException("Region capture requires a selected recording region.");
        var frame = new byte[run.Width * run.Height * 4];
        var timing = new FrameTimingTracker(run.FrameRate);
        var stats = new RecordingPerformanceTracker(run.FrameRate, OnPerformanceUpdated);

        using var bitmap = new Bitmap(run.Width, run.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var clickHighlighter = new MouseClickHighlighter();

        try
        {
            while (!run.StopTokenSource.IsCancellationRequested)
            {
                var pausedDuration = await run.PauseController.WaitWhilePausedAsync(run.StopTokenSource.Token).ConfigureAwait(false);
                if (pausedDuration > TimeSpan.Zero)
                {
                    timing.ExcludePausedDuration(pausedDuration);
                    stats.ResetWindow();
                }

                var frameRegion = ResolveFrameRegion(run, initialRegion);
                CaptureRegionFrame(
                    frameRegion,
                    bitmap,
                    graphics,
                    frame,
                    run.CaptureCursor,
                    run.HighlightMouseClicks,
                    clickHighlighter,
                    run.IncludeWatermark,
                    run.WatermarkText);
                stats.RecordCapture();
                var framesToWrite = timing.GetFramesToWrite();
                for (var index = 0; index < framesToWrite; index++)
                {
                    await WriteFrameAsync(run, frame).ConfigureAwait(false);
                }

                stats.RecordOutputFrames(framesToWrite);
                await DelayUntilNextFrameAsync(run, timing).ConfigureAwait(false);
            }
        }
        finally
        {
            run.Process.StandardInput.Close();
        }
    }

    private static void CaptureRegionFrame(
        CaptureRegion region,
        Bitmap bitmap,
        Graphics graphics,
        byte[] frame,
        bool captureCursor,
        bool highlightMouseClicks,
        MouseClickHighlighter clickHighlighter,
        bool includeWatermark,
        string? watermarkText)
    {
        graphics.Clear(Color.Black);
        var copyWidth = Math.Min(region.Width, bitmap.Width);
        var copyHeight = Math.Min(region.Height, bitmap.Height);
        if (copyWidth <= 0 || copyHeight <= 0)
        {
            throw new VideoRecorderException("Capture area is no longer usable.");
        }

        graphics.CopyFromScreen(
            region.X,
            region.Y,
            0,
            0,
            new Size(copyWidth, copyHeight),
            CopyPixelOperation.SourceCopy);

        if (highlightMouseClicks)
        {
            clickHighlighter.DrawClickHighlights(region, graphics);
        }

        if (includeWatermark)
        {
            DrawWatermark(bitmap, graphics, watermarkText);
        }

        if (captureCursor)
        {
            TryDrawCursor(region, graphics);
        }

        var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var targetOffset = 0;
            var rowBytes = bitmap.Width * 4;
            var stride = Math.Abs(data.Stride);
            var source = data.Stride < 0
                ? data.Scan0 + (nint)((bitmap.Height - 1) * stride)
                : data.Scan0;

            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(source, frame, targetOffset, rowBytes);
                source += data.Stride < 0 ? -(nint)stride : (nint)stride;
                targetOffset += rowBytes;
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static CaptureRegion ResolveFrameRegion(RecorderRun run, CaptureRegion fallbackRegion)
    {
        if (run.Mode != RecordingMode.Window || run.WindowHandle is not { } windowHandle)
        {
            return fallbackRegion;
        }

        return ResolveFollowedWindowRegion(windowHandle);
    }

    private static CaptureRegion ResolveFollowedWindowRegion(nint windowHandle)
    {
        if (windowHandle == nint.Zero || !IsWindow(windowHandle))
        {
            throw new VideoRecorderException("Selected window was closed during recording.");
        }

        if (IsIconic(windowHandle))
        {
            throw new VideoRecorderException("Selected window was minimized during recording.");
        }

        if (!IsWindowVisible(windowHandle) || IsCloakedWindow(windowHandle))
        {
            throw new VideoRecorderException("Selected window is no longer visible during recording.");
        }

        var bounds = GetWindowCaptureBounds(windowHandle);
        if (bounds.Width < 16 || bounds.Height < 16)
        {
            throw new VideoRecorderException("Selected window became too small to record.");
        }

        return bounds;
    }

    private static void TryDrawCursor(CaptureRegion region, Graphics graphics)
    {
        var cursorInfo = new CursorInfo
        {
            CbSize = Marshal.SizeOf<CursorInfo>()
        };

        if (!GetCursorInfo(ref cursorInfo) || cursorInfo.Flags != CursorShowing || cursorInfo.HCursor == nint.Zero)
        {
            return;
        }

        var cursorHandle = CopyIcon(cursorInfo.HCursor);
        if (cursorHandle == nint.Zero)
        {
            return;
        }

        try
        {
            var drawX = cursorInfo.PtScreenPos.X - region.X;
            var drawY = cursorInfo.PtScreenPos.Y - region.Y;

            if (GetIconInfo(cursorHandle, out var iconInfo))
            {
                drawX -= iconInfo.XHotspot;
                drawY -= iconInfo.YHotspot;
                DeleteObject(iconInfo.HbmMask);
                DeleteObject(iconInfo.HbmColor);
            }

            if (drawX >= region.Width || drawY >= region.Height || drawX < -64 || drawY < -64)
            {
                return;
            }

            using var graphicsContext = new GraphicsHdcScope(graphics);
            DrawIconEx(
                graphicsContext.Hdc,
                drawX,
                drawY,
                cursorHandle,
                0,
                0,
                0,
                nint.Zero,
                DiNormal);
        }
        finally
        {
            DestroyIcon(cursorHandle);
        }
    }

    private static void DrawWatermark(Bitmap bitmap, Graphics graphics, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var fontSize = Math.Clamp(Math.Min(bitmap.Width, bitmap.Height) / 28f, 14f, 30f);
        var margin = Math.Max(10, (int)Math.Ceiling(fontSize * 0.8f));
        var maxWidth = Math.Max(40, bitmap.Width - margin * 2);
        var previousSmoothing = graphics.SmoothingMode;
        var previousTextRendering = graphics.TextRenderingHint;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        var measured = graphics.MeasureString(text, font, maxWidth, format);
        var boxWidth = Math.Min(maxWidth, (int)Math.Ceiling(measured.Width) + margin);
        var boxHeight = (int)Math.Ceiling(measured.Height) + margin;
        var x = Math.Max(margin, bitmap.Width - boxWidth - margin);
        var y = Math.Max(margin, bitmap.Height - boxHeight - margin);
        var background = new Rectangle(x, y, boxWidth, boxHeight);
        var textRect = new RectangleF(
            x + margin / 2f,
            y,
            Math.Max(1, boxWidth - margin),
            boxHeight);

        using var backgroundBrush = new SolidBrush(Color.FromArgb(128, 0, 0, 0));
        using var textBrush = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
        graphics.FillRectangle(backgroundBrush, background);
        graphics.DrawString(text, font, textBrush, textRect, format);
        graphics.SmoothingMode = previousSmoothing;
        graphics.TextRenderingHint = previousTextRendering;
    }

    private async Task PumpSyntheticFramesAsync(RecorderRun run)
    {
        var frame = new byte[run.Width * run.Height * 4];
        var timing = new FrameTimingTracker(run.FrameRate);
        var stats = new RecordingPerformanceTracker(run.FrameRate, OnPerformanceUpdated);
        var frameIndex = 0;

        try
        {
            while (!run.StopTokenSource.IsCancellationRequested)
            {
                var pausedDuration = await run.PauseController.WaitWhilePausedAsync(run.StopTokenSource.Token).ConfigureAwait(false);
                if (pausedDuration > TimeSpan.Zero)
                {
                    timing.ExcludePausedDuration(pausedDuration);
                    stats.ResetWindow();
                }

                FillSyntheticFrame(frame, run.Width, run.Height, frameIndex++);
                stats.RecordCapture();
                var framesToWrite = timing.GetFramesToWrite();
                for (var index = 0; index < framesToWrite; index++)
                {
                    await WriteFrameAsync(run, frame).ConfigureAwait(false);
                }

                stats.RecordOutputFrames(framesToWrite);
                await DelayUntilNextFrameAsync(run, timing).ConfigureAwait(false);
            }
        }
        finally
        {
            run.Process.StandardInput.Close();
        }
    }

    private static Task DelayUntilNextFrameAsync(RecorderRun run, FrameTimingTracker timing)
    {
        var delay = timing.GetDelayUntilNextFrame();
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, run.StopTokenSource.Token);
    }

    private static async Task WriteFrameAsync(RecorderRun run, byte[] frame)
    {
        await run.Process.StandardInput.BaseStream
            .WriteAsync(frame, run.StopTokenSource.Token)
            .ConfigureAwait(false);
        await run.Process.StandardInput.BaseStream
            .FlushAsync(run.StopTokenSource.Token)
            .ConfigureAwait(false);
    }

    private static void FillSyntheticFrame(byte[] frame, int width, int height, int frameIndex)
    {
        var offset = 0;
        var movingBarX = frameIndex * 8 % width;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var inBar = Math.Abs(x - movingBarX) < 24;
                frame[offset++] = (byte)((x + frameIndex * 3) % 256);
                frame[offset++] = (byte)((y + frameIndex * 2) % 256);
                frame[offset++] = inBar ? (byte)240 : (byte)((x + y + frameIndex) % 256);
                frame[offset++] = 255;
            }
        }
    }

    private void OnPerformanceUpdated(RecordingPerformanceSnapshot snapshot)
    {
        PerformanceUpdated?.Invoke(this, snapshot);
    }

    private static async Task<string> ReadStandardErrorAsync(StreamReader reader)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }

    private static string Quote(string value)
    {
        return '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
    }

    private static string FormatError(string stderr)
    {
        return string.IsNullOrWhiteSpace(stderr)
            ? string.Empty
            : $" ffmpeg stderr: {stderr.Trim()}";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed class RecorderRun
    {
        public RecorderRun(
            Process process,
            string outputPath,
            string videoOutputPath,
            IReadOnlyList<AudioCaptureRun> audioRuns,
            bool captureSystemAudio,
            bool captureMicrophoneAudio,
            RecordingMode mode,
            CaptureRegion? region,
            nint? windowHandle,
            int width,
            int height,
            int frameRate,
            bool captureCursor,
            bool highlightMouseClicks,
            bool includeWatermark,
            string? watermarkText,
            PauseController pauseController,
            CancellationTokenSource stopTokenSource)
        {
            Process = process;
            OutputPath = outputPath;
            VideoOutputPath = videoOutputPath;
            AudioRuns = audioRuns;
            CaptureSystemAudio = captureSystemAudio;
            CaptureMicrophoneAudio = captureMicrophoneAudio;
            Mode = mode;
            Region = region;
            WindowHandle = windowHandle;
            Width = width;
            Height = height;
            FrameRate = frameRate;
            CaptureCursor = captureCursor;
            HighlightMouseClicks = highlightMouseClicks;
            IncludeWatermark = includeWatermark;
            WatermarkText = watermarkText;
            PauseController = pauseController;
            StopTokenSource = stopTokenSource;
        }

        public Process Process { get; }

        public string OutputPath { get; }

        public string VideoOutputPath { get; }

        public IReadOnlyList<AudioCaptureRun> AudioRuns { get; }

        public bool CaptureSystemAudio { get; }

        public bool CaptureMicrophoneAudio { get; }

        public RecordingMode Mode { get; }

        public CaptureRegion? Region { get; }

        public nint? WindowHandle { get; }

        public int Width { get; }

        public int Height { get; }

        public int FrameRate { get; }

        public bool CaptureCursor { get; }

        public bool HighlightMouseClicks { get; }

        public bool IncludeWatermark { get; }

        public string? WatermarkText { get; }

        public PauseController PauseController { get; }

        public CancellationTokenSource StopTokenSource { get; }

        public Task FramePumpTask { get; set; } = Task.CompletedTask;

        public Task<string> StderrTask { get; set; } = Task.FromResult(string.Empty);
    }

    private sealed record AudioCaptureRun(
        string Name,
        string WavPath,
        WaveFileWriter Writer,
        object Gate,
        Action StopRecording,
        Action DisposeCapture);

    private sealed class FrameTimingTracker
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly int _targetFrameRate;
        private TimeSpan _pausedDuration = TimeSpan.Zero;
        private int _framesWritten;

        public FrameTimingTracker(int targetFrameRate)
        {
            _targetFrameRate = targetFrameRate;
        }

        public void ExcludePausedDuration(TimeSpan pausedDuration)
        {
            _pausedDuration += pausedDuration;
        }

        public int GetFramesToWrite()
        {
            var activeElapsed = _clock.Elapsed - _pausedDuration;
            var expectedFrames = Math.Max(
                _framesWritten + 1,
                (int)Math.Floor(activeElapsed.TotalSeconds * _targetFrameRate) + 1);
            var framesToWrite = expectedFrames - _framesWritten;
            _framesWritten = expectedFrames;
            return framesToWrite;
        }

        public TimeSpan GetDelayUntilNextFrame()
        {
            var activeElapsed = _clock.Elapsed - _pausedDuration;
            var nextFrameTime = TimeSpan.FromSeconds(_framesWritten / (double)_targetFrameRate);
            return nextFrameTime - activeElapsed;
        }
    }

    private sealed class RecordingPerformanceTracker
    {
        private readonly int _targetFrameRate;
        private readonly Action<RecordingPerformanceSnapshot> _publish;
        private readonly Stopwatch _window = Stopwatch.StartNew();
        private int _capturedFrames;
        private int _outputFrames;
        private int _duplicateFrames;

        public RecordingPerformanceTracker(
            int targetFrameRate,
            Action<RecordingPerformanceSnapshot> publish)
        {
            _targetFrameRate = targetFrameRate;
            _publish = publish;
        }

        public void RecordCapture()
        {
            _capturedFrames++;
        }

        public void RecordOutputFrames(int framesWritten)
        {
            _outputFrames += framesWritten;
            _duplicateFrames += Math.Max(0, framesWritten - 1);
            PublishIfDue();
        }

        public void ResetWindow()
        {
            _window.Restart();
            _capturedFrames = 0;
            _outputFrames = 0;
            _duplicateFrames = 0;
        }

        private void PublishIfDue()
        {
            if (_window.Elapsed < TimeSpan.FromSeconds(1))
            {
                return;
            }

            var seconds = Math.Max(0.001, _window.Elapsed.TotalSeconds);
            _publish(new RecordingPerformanceSnapshot(
                _targetFrameRate,
                _capturedFrames / seconds,
                _outputFrames / seconds,
                _duplicateFrames));
            ResetWindow();
        }
    }

    private sealed class GraphicsHdcScope : IDisposable
    {
        private readonly Graphics _graphics;

        public GraphicsHdcScope(Graphics graphics)
        {
            _graphics = graphics;
            Hdc = graphics.GetHdc();
        }

        public nint Hdc { get; }

        public void Dispose()
        {
            _graphics.ReleaseHdc(Hdc);
        }
    }

    private sealed class MouseClickHighlighter
    {
        private static readonly TimeSpan PulseDuration = TimeSpan.FromMilliseconds(450);
        private readonly List<ClickPulse> _pulses = [];
        private bool _wasLeftButtonDown;
        private bool _wasRightButtonDown;
        private bool _wasMiddleButtonDown;

        public void DrawClickHighlights(CaptureRegion region, Graphics graphics)
        {
            var hasCursorPosition = GetCursorPos(out var cursorPosition);
            var leftButtonDown = IsVirtualKeyDown(VkLButton);
            var rightButtonDown = IsVirtualKeyDown(VkRButton);
            var middleButtonDown = IsVirtualKeyDown(VkMButton);
            if (hasCursorPosition && leftButtonDown && !_wasLeftButtonDown)
            {
                _pulses.Add(new ClickPulse(cursorPosition, DateTimeOffset.UtcNow, ClickKind.Left));
            }

            if (hasCursorPosition && rightButtonDown && !_wasRightButtonDown)
            {
                _pulses.Add(new ClickPulse(cursorPosition, DateTimeOffset.UtcNow, ClickKind.Right));
            }

            if (hasCursorPosition && middleButtonDown && !_wasMiddleButtonDown)
            {
                _pulses.Add(new ClickPulse(cursorPosition, DateTimeOffset.UtcNow, ClickKind.Middle));
            }

            _wasLeftButtonDown = leftButtonDown;
            _wasRightButtonDown = rightButtonDown;
            _wasMiddleButtonDown = middleButtonDown;
            DrawActivePulses(region, graphics, DateTimeOffset.UtcNow);
        }

        private void DrawActivePulses(CaptureRegion region, Graphics graphics, DateTimeOffset now)
        {
            for (var index = _pulses.Count - 1; index >= 0; index--)
            {
                var pulse = _pulses[index];
                var age = now - pulse.StartedAt;
                if (age >= PulseDuration)
                {
                    _pulses.RemoveAt(index);
                    continue;
                }

                var x = pulse.ScreenPosition.X - region.X;
                var y = pulse.ScreenPosition.Y - region.Y;
                if (x < -80 || y < -80 || x > region.Width + 80 || y > region.Height + 80)
                {
                    continue;
                }

                var progress = Math.Clamp(age.TotalMilliseconds / PulseDuration.TotalMilliseconds, 0, 1);
                var radius = 10 + (float)(progress * 24);
                var alpha = Math.Clamp((int)(210 * (1 - progress)), 0, 210);
                var baseColor = pulse.Kind switch
                {
                    ClickKind.Right => Color.FromArgb(255, 144, 70),
                    ClickKind.Middle => Color.FromArgb(100, 220, 120),
                    _ => Color.FromArgb(45, 160, 255)
                };
                var ringColor = Color.FromArgb(alpha, baseColor);
                var fillColor = Color.FromArgb(Math.Min(alpha, 80), baseColor);
                var previousSmoothing = graphics.SmoothingMode;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var fill = new SolidBrush(fillColor);
                using var pen = new Pen(ringColor, 4);
                graphics.FillEllipse(fill, x - radius, y - radius, radius * 2, radius * 2);
                graphics.DrawEllipse(pen, x - radius, y - radius, radius * 2, radius * 2);
                graphics.SmoothingMode = previousSmoothing;
            }
        }

        private static bool IsVirtualKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private sealed class PauseController
    {
        private readonly object _gate = new();
        private TaskCompletionSource _resumeSignal = CreateCompletedSignal();

        public bool IsPaused
        {
            get
            {
                lock (_gate)
                {
                    return !_resumeSignal.Task.IsCompleted;
                }
            }
        }

        public void Pause()
        {
            lock (_gate)
            {
                if (!_resumeSignal.Task.IsCompleted)
                {
                    return;
                }

                _resumeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public void Resume()
        {
            lock (_gate)
            {
                _resumeSignal.TrySetResult();
            }
        }

        public async Task<TimeSpan> WaitWhilePausedAsync(CancellationToken cancellationToken)
        {
            Task resumeTask;
            lock (_gate)
            {
                resumeTask = _resumeSignal.Task;
            }

            if (resumeTask.IsCompleted)
            {
                return TimeSpan.Zero;
            }

            var pausedAt = Stopwatch.GetTimestamp();
            await resumeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            return Stopwatch.GetElapsedTime(pausedAt);
        }

        private static TaskCompletionSource CreateCompletedSignal()
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            signal.SetResult();
            return signal;
        }
    }

    private const int CursorShowing = 0x00000001;
    private const int DiNormal = 0x0003;
    private const int DwmwaExtendedFrameBounds = 9;
    private const int DwmwaCloaked = 14;
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const int VkMButton = 0x04;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorInfo(ref CursorInfo pci);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetIconInfo(nint hIcon, out IconInfo piconinfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CopyIcon(nint hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DrawIconEx(
        nint hdc,
        int xLeft,
        int yTop,
        nint hIcon,
        int cxWidth,
        int cyWidth,
        int istepIfAniCur,
        nint hbrFlickerFreeDraw,
        int diFlags);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(nint hObject);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint handle);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint handle, out WindowNativeRect rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint handle,
        int attribute,
        out WindowNativeRect rect,
        int attributeSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint handle,
        int attribute,
        out int value,
        int attributeSize);

    private static bool IsCloakedWindow(nint handle)
    {
        return DwmGetWindowAttribute(handle, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    private static CaptureRegion GetWindowCaptureBounds(nint handle)
    {
        WindowNativeRect rect;

        if (DwmGetWindowAttribute(
                handle,
                DwmwaExtendedFrameBounds,
                out rect,
                Marshal.SizeOf<WindowNativeRect>()) != 0)
        {
            if (!GetWindowRect(handle, out rect))
            {
                throw new VideoRecorderException("Selected window bounds are unavailable during recording.");
            }
        }

        return new CaptureRegion(
            rect.Left,
            rect.Top,
            Math.Max(0, rect.Right - rect.Left),
            Math.Max(0, rect.Bottom - rect.Top));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int CbSize;
        public int Flags;
        public nint HCursor;
        public Point PtScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public bool FIcon;
        public int XHotspot;
        public int YHotspot;
        public nint HbmMask;
        public nint HbmColor;
    }

    private readonly record struct ClickPulse(Point ScreenPosition, DateTimeOffset StartedAt, ClickKind Kind);

    private enum ClickKind
    {
        Left,
        Right,
        Middle
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct WindowNativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }
}
