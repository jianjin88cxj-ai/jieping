using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Jieping.App.Recording;

namespace Jieping.App.Services;

public sealed class FfmpegVideoPostProcessorService : IVideoPostProcessorService
{
    private const string FfmpegExecutable = "ffmpeg";
    private const string FfprobeExecutable = "ffprobe";

    public async Task<string> TrimAsync(VideoTrimRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var duration = await ProbeDurationAsync(request.InputPath, cancellationToken).ConfigureAwait(false);
        if (request.TrimStartSeconds + request.TrimEndSeconds >= duration)
        {
            throw new VideoRecorderException("Trim values remove the entire recording.");
        }

        var outputDuration = duration - request.TrimStartSeconds - request.TrimEndSeconds;
        if (outputDuration < 0.1)
        {
            throw new VideoRecorderException("Trimmed recording would be too short.");
        }

        var outputPath = CreateTrimmedOutputPath(request.InputPath);
        var tempOutputPath = CreateSiblingTempPath(outputPath, ".trim.mp4");
        var arguments = BuildTrimArguments(request.InputPath, tempOutputPath, request.TrimStartSeconds, outputDuration);

        try
        {
            var stderr = await RunFfmpegAsync(arguments, "trimming", cancellationToken).ConfigureAwait(false);
            if (!File.Exists(tempOutputPath) || new FileInfo(tempOutputPath).Length < 1024)
            {
                throw new VideoRecorderException($"ffmpeg trim completed without creating a usable output file.{FormatError(stderr)}");
            }

            File.Move(tempOutputPath, outputPath, overwrite: false);
            return outputPath;
        }
        catch
        {
            TryDelete(tempOutputPath);
            throw;
        }
    }

    public async Task<string> ExportGifAsync(VideoGifExportRequest request, CancellationToken cancellationToken = default)
    {
        ValidateGifRequest(request);

        var outputPath = CreateDerivedOutputPath(request.InputPath, "_gif", ".gif");
        var tempOutputPath = CreateSiblingTempPath(outputPath, ".gif");
        var arguments = BuildGifArguments(request.InputPath, tempOutputPath, request.FrameRate, request.Width);

        try
        {
            var stderr = await RunFfmpegAsync(arguments, "exporting GIF", cancellationToken).ConfigureAwait(false);
            if (!File.Exists(tempOutputPath) || new FileInfo(tempOutputPath).Length < 1024)
            {
                throw new VideoRecorderException($"ffmpeg GIF export completed without creating a usable output file.{FormatError(stderr)}");
            }

            File.Move(tempOutputPath, outputPath, overwrite: false);
            return outputPath;
        }
        catch
        {
            TryDelete(tempOutputPath);
            throw;
        }
    }

    private static void ValidateRequest(VideoTrimRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath) || !File.Exists(request.InputPath))
        {
            throw new VideoRecorderException("Recorded file is no longer available.");
        }

        if (request.TrimStartSeconds < 0 || request.TrimEndSeconds < 0)
        {
            throw new VideoRecorderException("Trim values cannot be negative.");
        }
    }

    private static void ValidateGifRequest(VideoGifExportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath) || !File.Exists(request.InputPath))
        {
            throw new VideoRecorderException("Recorded file is no longer available.");
        }

        if (request.FrameRate is < 1 or > 30)
        {
            throw new VideoRecorderException("GIF frame rate must be between 1 and 30 FPS.");
        }

        if (request.Width is < 160 or > 1920)
        {
            throw new VideoRecorderException("GIF width must be between 160 and 1920 pixels.");
        }
    }

    private static async Task<double> ProbeDurationAsync(string inputPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = FfprobeExecutable,
            Arguments = string.Join(
                " ",
                "-v error",
                "-show_entries format=duration",
                "-of default=noprint_wrappers=1:nokey=1",
                Quote(inputPath)),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new VideoRecorderException("ffprobe failed to start.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new VideoRecorderException($"ffprobe failed with exit code {process.ExitCode}.{FormatError(stderr)}");
            }

            if (!double.TryParse(stdout.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) ||
                duration <= 0)
            {
                throw new VideoRecorderException("Could not read the recording duration.");
            }

            return duration;
        }
        catch (Exception ex) when (ex is not VideoRecorderException)
        {
            throw new VideoRecorderException(
                "ffprobe could not be started. Install ffmpeg and make sure ffprobe is available on PATH.",
                ex);
        }
    }

    private static async Task<string> RunFfmpegAsync(string arguments, string operation, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = FfmpegExecutable,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new VideoRecorderException($"ffmpeg failed to start while {operation}.");
            var stderrTask = ReadStandardErrorAsync(process.StandardError, cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new VideoRecorderException($"ffmpeg failed while {operation} with exit code {process.ExitCode}.{FormatError(stderr)}");
            }

            return stderr;
        }
        catch (Exception ex) when (ex is not VideoRecorderException)
        {
            throw new VideoRecorderException(
                "ffmpeg could not be started. Install ffmpeg and make sure it is available on PATH.",
                ex);
        }
    }

    private static string BuildTrimArguments(
        string inputPath,
        string outputPath,
        double trimStartSeconds,
        double outputDurationSeconds)
    {
        return string.Join(
            " ",
            "-hide_banner",
            "-loglevel error",
            "-y",
            "-ss",
            FormatSeconds(trimStartSeconds),
            "-i",
            Quote(inputPath),
            "-t",
            FormatSeconds(outputDurationSeconds),
            "-map 0:v:0",
            "-map 0:a?",
            "-c:v libx264",
            "-preset veryfast",
            "-crf 20",
            "-pix_fmt yuv420p",
            "-c:a aac",
            "-movflags +faststart",
            Quote(outputPath));
    }

    private static string BuildGifArguments(string inputPath, string outputPath, int frameRate, int width)
    {
        var filter = string.Join(
            string.Empty,
            $"[0:v]fps={frameRate.ToString(CultureInfo.InvariantCulture)},",
            $"scale='min({width.ToString(CultureInfo.InvariantCulture)},iw)':-2:flags=lanczos,",
            "split[s0][s1];",
            "[s0]palettegen=stats_mode=diff[p];",
            "[s1][p]paletteuse=dither=sierra2_4a:diff_mode=rectangle");

        return string.Join(
            " ",
            "-hide_banner",
            "-loglevel error",
            "-y",
            "-i",
            Quote(inputPath),
            "-filter_complex",
            Quote(filter),
            "-loop 0",
            Quote(outputPath));
    }

    private static string CreateTrimmedOutputPath(string inputPath)
    {
        return CreateDerivedOutputPath(inputPath, "_trimmed", Path.GetExtension(inputPath));
    }

    private static string CreateDerivedOutputPath(string inputPath, string suffix, string extension)
    {
        var directory = Path.GetDirectoryName(inputPath);
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var basePath = Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? Path.GetTempPath() : directory,
            $"{name}{suffix}");

        var candidate = $"{basePath}{extension}";
        for (var index = 2; File.Exists(candidate); index++)
        {
            candidate = $"{basePath}_{index}{extension}";
        }

        return candidate;
    }

    private static string CreateSiblingTempPath(string outputPath, string suffix)
    {
        var directory = Path.GetDirectoryName(outputPath);
        var name = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? Path.GetTempPath() : directory,
            $"{name}.{Guid.NewGuid():N}{suffix}");
    }

    private static async Task<string> ReadStandardErrorAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }

    private static string FormatSeconds(double seconds) => seconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Quote(string value) => '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';

    private static string FormatError(string stderr)
    {
        return string.IsNullOrWhiteSpace(stderr)
            ? string.Empty
            : $" ffmpeg stderr: {stderr.Trim()}";
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
}
