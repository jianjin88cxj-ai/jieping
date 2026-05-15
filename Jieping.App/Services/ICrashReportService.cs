namespace Jieping.App.Services;

public interface ICrashReportService
{
    string ReportDirectory { get; }

    string? WriteReport(Exception exception, string source);
}
