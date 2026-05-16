using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Jieping.App.Dialogs;
using Jieping.App.Models;
using Jieping.App.Overlays;
using Jieping.App.Services;
using Jieping.App.ViewModels;
using WinForms = System.Windows.Forms;

namespace Jieping.App;

public partial class MainWindow : Window
{
    private const int StartStopHotkeyId = 0x4A50;
    private const int StopHotkeyId = 0x4A51;
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkF9 = 0x78;
    private const uint VkF10 = 0x79;
    private HwndSource? _source;
    private readonly HashSet<int> _registeredHotkeyIds = [];
    private MainWindowViewModel? _viewModel;
    private bool _isMiniMode;
    private double _normalLeft;
    private double _normalTop;
    private double _normalWidth;
    private double _normalHeight;

    public MainWindow()
    {
        InitializeComponent();
        var windowEnumerationService = new Win32WindowEnumerationService();
        _viewModel = new MainWindowViewModel(
            () => RegionSelectionOverlay.SelectRegion(this),
            () => WindowPickerDialog.SelectWindow(windowEnumerationService, this),
            SelectOutputDirectory,
            new FfmpegVideoRecorderService(),
            windowEnumeration: windowEnumerationService);
        _viewModel.RecordingShellSuppressionRequested += OnRecordingShellSuppressionRequested;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        DataContext = _viewModel;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _source?.AddHook(WndProc);
        RegisterGlobalHotkeys();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.RecordingShellSuppressionRequested -= OnRecordingShellSuppressionRequested;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        UnregisterGlobalHotkeys();
        _source?.RemoveHook(WndProc);
        _source = null;
        base.OnClosing(e);
    }

    private string? SelectOutputDirectory(string currentDirectory)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Choose where Jieping saves recordings",
            UseDescriptionForTitle = true,
            SelectedPath = currentDirectory,
            ShowNewFolderButton = true
        };

        return dialog.ShowDialog() == WinForms.DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    private void OnRecordingShellSuppressionRequested(bool suppress)
    {
        if (suppress)
        {
            if (!_registeredHotkeyIds.Contains(StopHotkeyId))
            {
                _viewModel?.ReportRecordingShellSuppressionSkipped();
                return;
            }

            Hide();
            return;
        }

        RestoreFullShell();

        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.State) or nameof(MainWindowViewModel.SelectedMode))
        {
            ApplyRecordingShellMode();
        }
    }

    private void ApplyRecordingShellMode()
    {
        if (_viewModel is null)
        {
            return;
        }

        if (ShouldUseMiniMode(_viewModel))
        {
            EnterMiniMode();
            return;
        }

        RestoreFullShell();
    }

    private static bool ShouldUseMiniMode(MainWindowViewModel viewModel)
    {
        return viewModel.SelectedMode != RecordingMode.FullScreen &&
               viewModel.State is RecordingState.Countdown or RecordingState.Recording or RecordingState.Paused or RecordingState.Stopping;
    }

    private void EnterMiniMode()
    {
        if (_isMiniMode)
        {
            return;
        }

        _normalLeft = Left;
        _normalTop = Top;
        _normalWidth = Width;
        _normalHeight = Height;

        FullLayout.Visibility = Visibility.Collapsed;
        MiniLayout.Visibility = Visibility.Visible;
        MinWidth = 560;
        MinHeight = 120;
        Width = 560;
        Height = 120;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;

        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left, area.Right - Width - 24);
        Top = Math.Max(area.Top, area.Bottom - Height - 24);
        _isMiniMode = true;
        Activate();
    }

    private void RestoreFullShell()
    {
        if (!_isMiniMode)
        {
            return;
        }

        MiniLayout.Visibility = Visibility.Collapsed;
        FullLayout.Visibility = Visibility.Visible;
        Topmost = false;
        ResizeMode = ResizeMode.CanMinimize;
        MinWidth = 1080;
        MinHeight = 720;
        Width = _normalWidth > 0 ? _normalWidth : 1080;
        Height = _normalHeight > 0 ? _normalHeight : 720;
        Left = _normalLeft;
        Top = _normalTop;
        _isMiniMode = false;
    }

    private void RegisterGlobalHotkeys()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var failedHotkeys = new List<string>();
        RegisterHotkey(handle, StartStopHotkeyId, ModControl | ModShift | ModNoRepeat, VkF9, "Ctrl+Shift+F9", failedHotkeys);
        RegisterHotkey(handle, StopHotkeyId, ModControl | ModShift | ModNoRepeat, VkF10, "Ctrl+Shift+F10", failedHotkeys);

        if (failedHotkeys.Count > 0 && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ReportGlobalHotkeyRegistrationFailure(string.Join(", ", failedHotkeys));
        }
    }

    private void RegisterHotkey(
        nint handle,
        int id,
        uint modifiers,
        uint virtualKey,
        string label,
        ICollection<string> failedHotkeys)
    {
        if (RegisterHotKey(handle, id, modifiers, virtualKey))
        {
            _registeredHotkeyIds.Add(id);
            return;
        }

        failedHotkeys.Add(label);
    }

    private void UnregisterGlobalHotkeys()
    {
        var handle = new WindowInteropHelper(this).Handle;
        foreach (var hotkeyId in _registeredHotkeyIds)
        {
            UnregisterHotKey(handle, hotkeyId);
        }

        _registeredHotkeyIds.Clear();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WmHotkey || DataContext is not MainWindowViewModel viewModel)
        {
            return nint.Zero;
        }

        var hotkeyId = wParam.ToInt32();
        if (hotkeyId == StartStopHotkeyId)
        {
            ExecuteFirstAvailable(viewModel.StopRecordingCommand, viewModel.StartRecordingCommand);
            handled = true;
        }
        else if (hotkeyId == StopHotkeyId)
        {
            ExecuteIfAvailable(viewModel.StopRecordingCommand);
            handled = true;
        }

        return nint.Zero;
    }

    private static void ExecuteFirstAvailable(params RelayCommand[] commands)
    {
        foreach (var command in commands)
        {
            if (ExecuteIfAvailable(command))
            {
                return;
            }
        }
    }

    private static bool ExecuteIfAvailable(RelayCommand command)
    {
        if (!command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
