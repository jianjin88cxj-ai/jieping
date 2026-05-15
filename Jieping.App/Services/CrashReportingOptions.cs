namespace Jieping.App.Services;

public static class CrashReportingOptions
{
    private static readonly object Gate = new();
    private static bool _isEnabled;

    public static bool IsEnabled
    {
        get
        {
            lock (Gate)
            {
                return _isEnabled;
            }
        }
        set
        {
            lock (Gate)
            {
                _isEnabled = value;
            }
        }
    }
}
