using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Jieping.App.Services;

namespace Jieping.App;

public partial class App : Application
{
    private readonly ICrashReportService _crashReportService = new FileCrashReportService();

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        base.OnStartup(e);

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var reportPath = _crashReportService.WriteReport(e.Exception, "DispatcherUnhandledException");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            MessageBox.Show(
                $"Jieping hit an unexpected error. A local crash report was saved to:\n{reportPath}",
                "Jieping",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        e.Handled = true;
        Shutdown(1);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _crashReportService.WriteReport(exception, "UnhandledException");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _crashReportService.WriteReport(e.Exception, "UnobservedTaskException");
        e.SetObserved();
    }
}
