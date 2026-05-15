using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Jieping.App.Services;

public sealed class FileCrashReportService : ICrashReportService
{
    public string ReportDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Jieping",
        "CrashReports");

    public string? WriteReport(Exception exception, string source)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (!CrashReportingOptions.IsEnabled)
        {
            return null;
        }

        Directory.CreateDirectory(ReportDirectory);
        var reportId = Guid.NewGuid().ToString("N")[..8];
        var reportPath = Path.Combine(
            ReportDirectory,
            $"crash-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}-{reportId}.txt");

        File.WriteAllText(reportPath, BuildReport(exception, source, reportId), Encoding.UTF8);
        return reportPath;
    }

    private static string BuildReport(Exception exception, string source, string reportId)
    {
        var assembly = Assembly.GetExecutingAssembly().GetName();
        var builder = new StringBuilder();
        builder.AppendLine("Jieping Crash Report");
        builder.AppendLine($"ReportId: {reportId}");
        builder.AppendLine($"GeneratedAt: {DateTimeOffset.Now:O}");
        builder.AppendLine($"Source: {source}");
        builder.AppendLine($"Version: {assembly.Version}");
        builder.AppendLine($"DotNetRuntime: {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine($"OSVersion: {Environment.OSVersion}");
        builder.AppendLine($"Process64Bit: {Environment.Is64BitProcess}");
        builder.AppendLine($"Machine64Bit: {Environment.Is64BitOperatingSystem}");
        builder.AppendLine();
        AppendException(builder, exception, 0);
        return builder.ToString();
    }

    private static void AppendException(StringBuilder builder, Exception exception, int depth)
    {
        var prefix = depth == 0 ? string.Empty : $"Inner[{depth}].";
        builder.AppendLine($"{prefix}Type: {exception.GetType().FullName}");
        builder.AppendLine($"{prefix}Message: {exception.Message}");
        builder.AppendLine($"{prefix}StackTrace:");
        builder.AppendLine(exception.StackTrace ?? "<no stack trace>");

        if (exception.InnerException is not null)
        {
            builder.AppendLine();
            AppendException(builder, exception.InnerException, depth + 1);
        }
    }
}
