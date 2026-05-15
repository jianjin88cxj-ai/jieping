using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Jieping.App.Models;
using Jieping.App.Recording;
using Jieping.App.Services;
using NAudio.Wave;

namespace Jieping.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private const int RecordingCountdownSeconds = 3;
    private const int MaxHistoryItems = 20;
    private const string DefaultLanguageCode = "en-US";
    private readonly Func<CaptureRegion?> _selectRegion;
    private readonly Func<DesktopWindowInfo?> _selectWindow;
    private readonly Func<string, string?> _selectOutputDirectory;
    private readonly IVideoRecorderService _videoRecorder;
    private readonly IVideoPostProcessorService _videoPostProcessor;
    private readonly IUpdateCheckService _updateCheckService;
    private readonly IUpdatePackageDownloadService _updatePackageDownloadService;
    private readonly ICrashReportService _crashReportService;
    private readonly IRecordingHistoryStore _recordingHistoryStore;
    private readonly IDisplayBoundsService _displayBounds;
    private readonly IWindowEnumerationService _windowEnumeration;
    private readonly AppConfiguration _configuration = new();
    private RecordingMode _selectedMode = RecordingMode.Region;
    private RecordingState _state = RecordingState.Idle;
    private RecordingTarget? _target;
    private string _statusMessage = "Select a recording target to begin.";
    private string? _lastOutputPath;
    private string? _lastGifOutputPath;
    private DateTimeOffset? _recordingStartedAt;
    private TimeSpan _recordingElapsedBeforeCurrentRun = TimeSpan.Zero;
    private string _elapsedTime = "00:00:00";
    private readonly DispatcherTimer _elapsedTimer;
    private VideoRecorderSession? _activeSession;
    private MicrophoneDeviceInfo? _selectedMicrophoneDevice;
    private DisplayInfo? _selectedDisplay;
    private RecordingPresetInfo? _selectedRecordingPreset;
    private VideoBitrateOption? _selectedVideoBitrate;
    private LanguageOption? _selectedLanguage;
    private bool _applyingPreset;
    private bool _isPostProcessing;
    private string _trimStartSeconds = "0";
    private string _trimEndSeconds = "0";
    private int _gifFrameRate = 12;
    private int _gifWidth = 960;
    private bool _isCheckingForUpdate;
    private bool _isDownloadingUpdate;
    private string _updateStatusMessage = "Update check not configured.";
    private string? _downloadedUpdatePackagePath;
    private string _diagnosticsStatusMessage = "Crash reports are disabled.";
    private UpdateCheckResult? _lastUpdateCheckResult;
    private CancellationTokenSource? _countdownTokenSource;

    public MainWindowViewModel(
        Func<CaptureRegion?>? selectRegion = null,
        Func<DesktopWindowInfo?>? selectWindow = null,
        Func<string, string?>? selectOutputDirectory = null,
        IVideoRecorderService? videoRecorder = null,
        IVideoPostProcessorService? videoPostProcessor = null,
        IUpdateCheckService? updateCheckService = null,
        IUpdatePackageDownloadService? updatePackageDownloadService = null,
        ICrashReportService? crashReportService = null,
        IRecordingHistoryStore? recordingHistoryStore = null,
        IDisplayBoundsService? displayBounds = null,
        IWindowEnumerationService? windowEnumeration = null)
    {
        _selectRegion = selectRegion ?? (() => null);
        _selectWindow = selectWindow ?? (() => null);
        _selectOutputDirectory = selectOutputDirectory ?? (_ => null);
        _videoRecorder = videoRecorder ?? new FfmpegVideoRecorderService();
        _videoPostProcessor = videoPostProcessor ?? new FfmpegVideoPostProcessorService();
        _updateCheckService = updateCheckService ?? new ManifestUpdateCheckService();
        _updatePackageDownloadService = updatePackageDownloadService ?? new UpdatePackageDownloadService();
        _crashReportService = crashReportService ?? new FileCrashReportService();
        _recordingHistoryStore = recordingHistoryStore ?? new JsonRecordingHistoryStore();
        _displayBounds = displayBounds ?? new DisplayBoundsService();
        _windowEnumeration = windowEnumeration ?? new Win32WindowEnumerationService();
        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimer.Tick += (_, _) => UpdateElapsedTime();
        SelectModeCommand = new RelayCommand(SelectMode, _ => !IsRecordingLocked);
        SelectTargetCommand = new RelayCommand(_ => SelectTarget(), _ => !IsRecordingLocked);
        BrowseOutputDirectoryCommand = new RelayCommand(_ => BrowseOutputDirectory(), _ => !IsRecordingLocked);
        StartRecordingCommand = new RelayCommand(_ => StartRecordingAsync(), _ => State == RecordingState.TargetSelected);
        PauseResumeCommand = new RelayCommand(_ => PauseOrResumeRecordingAsync(), _ => State is RecordingState.Recording or RecordingState.Paused);
        StopRecordingCommand = new RelayCommand(_ => StopRecordingAsync(), _ => State is RecordingState.Countdown or RecordingState.Recording or RecordingState.Paused);
        OpenLastFileCommand = new RelayCommand(_ => OpenLastFile(), _ => CanOpenLastOutput);
        OpenLastFolderCommand = new RelayCommand(_ => OpenLastFolder(), _ => CanOpenLastOutput);
        TrimLastOutputCommand = new RelayCommand(_ => TrimLastOutputAsync(), _ => CanTrimLastOutput);
        ExportGifCommand = new RelayCommand(_ => ExportGifAsync(), _ => CanExportGif);
        OpenLastGifCommand = new RelayCommand(_ => OpenLastGif(), _ => CanOpenLastGif);
        OpenHistoryFileCommand = new RelayCommand(OpenHistoryFile, CanOpenHistoryItem);
        OpenHistoryFolderCommand = new RelayCommand(OpenHistoryFolder, CanOpenHistoryItem);
        ClearHistoryCommand = new RelayCommand(_ => ClearHistory(), _ => RecordingHistory.Count > 0);
        CheckForUpdatesCommand = new RelayCommand(_ => CheckForUpdatesAsync(), _ => CanCheckForUpdates);
        OpenUpdatePackageCommand = new RelayCommand(_ => OpenUpdatePackage(), _ => CanOpenUpdatePackage);
        DownloadUpdatePackageCommand = new RelayCommand(_ => DownloadUpdatePackageAsync(), _ => CanDownloadUpdatePackage);
        OpenDownloadedUpdatePackageCommand = new RelayCommand(_ => OpenDownloadedUpdatePackage(), _ => CanOpenDownloadedUpdatePackage);
        OpenCrashReportFolderCommand = new RelayCommand(_ => OpenCrashReportFolder());
        _selectedLanguage = LanguageOptions.FirstOrDefault(language => language.Code == _configuration.LanguageCode)
            ?? LanguageOptions.First(language => language.Code == DefaultLanguageCode);
        _configuration.LanguageCode = _selectedLanguage.Code;
        _statusMessage = Text("SelectTargetToBegin");
        _updateStatusMessage = Text("UpdateCheckNotConfigured");
        _diagnosticsStatusMessage = Text("CrashReportsDisabled");
        CrashReportingOptions.IsEnabled = _configuration.AllowCrashReports;
        RecordingPresets = CreateRecordingPresets();
        VideoBitrateOptions = CreateVideoBitrateOptions();
        SelectedRecordingPreset = RecordingPresets.First(preset => preset.Name == _configuration.RecordingPreset);
        SelectedVideoBitrate = VideoBitrateOptions.First(option => option.KilobitsPerSecond == _configuration.VideoBitrateKbps);
        Displays = _displayBounds.GetDisplays();
        SelectedDisplay = Displays.FirstOrDefault(display => display.IsPrimary) ?? Displays.FirstOrDefault();
        MicrophoneDevices = EnumerateMicrophoneDevices();
        SelectedMicrophoneDevice = MicrophoneDevices.FirstOrDefault();
        LoadRecordingHistory();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<bool>? RecordingShellSuppressionRequested;

    public RelayCommand SelectModeCommand { get; }

    public RelayCommand SelectTargetCommand { get; }

    public RelayCommand BrowseOutputDirectoryCommand { get; }

    public RelayCommand StartRecordingCommand { get; }

    public RelayCommand StopRecordingCommand { get; }

    public RelayCommand PauseResumeCommand { get; }

    public RelayCommand OpenLastFileCommand { get; }

    public RelayCommand OpenLastFolderCommand { get; }

    public RelayCommand TrimLastOutputCommand { get; }

    public RelayCommand ExportGifCommand { get; }

    public RelayCommand OpenLastGifCommand { get; }

    public RelayCommand OpenHistoryFileCommand { get; }

    public RelayCommand OpenHistoryFolderCommand { get; }

    public RelayCommand ClearHistoryCommand { get; }

    public RelayCommand CheckForUpdatesCommand { get; }

    public RelayCommand OpenUpdatePackageCommand { get; }

    public RelayCommand DownloadUpdatePackageCommand { get; }

    public RelayCommand OpenDownloadedUpdatePackageCommand { get; }

    public RelayCommand OpenCrashReportFolderCommand { get; }

    public ObservableCollection<RecordingHistoryItem> RecordingHistory { get; } = [];

    public IReadOnlyList<int> FrameRateOptions { get; } = [15, 30, 60];

    public IReadOnlyList<int> GifFrameRateOptions { get; } = [8, 12, 15, 24];

    public IReadOnlyList<int> GifWidthOptions { get; } = [480, 720, 960, 1280];

    public IReadOnlyList<string> QualityOptions { get; } = ["Standard", "High"];

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new LanguageOption("en-US", "English"),
        new LanguageOption("zh-CN", "中文")
    ];

    public IReadOnlyList<RecordingPresetInfo> RecordingPresets { get; }

    public IReadOnlyList<VideoBitrateOption> VideoBitrateOptions { get; }

    public IReadOnlyList<DisplayInfo> Displays { get; }

    public IReadOnlyList<MicrophoneDeviceInfo> MicrophoneDevices { get; }

    public LanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (!SetField(ref _selectedLanguage, value) || value is null)
            {
                return;
            }

            _configuration.LanguageCode = value.Code;
            RefreshLocalizedProperties();
        }
    }

    public string this[string key] => Text(key);

    public RecordingMode SelectedMode
    {
        get => _selectedMode;
        private set
        {
            if (SetField(ref _selectedMode, value))
            {
                Target = null;
                State = RecordingState.Idle;
                StatusMessage = string.Format(CultureInfo.CurrentCulture, Text("ModeChangedSelectTarget"), ModeLabel);
                OnPropertyChanged(nameof(ModeLabel));
                OnPropertyChanged(nameof(TargetSummary));
                OnPropertyChanged(nameof(CanSelectDisplay));
            }
        }
    }

    public RecordingState State
    {
        get => _state;
        private set
        {
            if (SetField(ref _state, value))
            {
                OnPropertyChanged(nameof(StateLabel));
                OnPropertyChanged(nameof(PauseResumeLabel));
                OnPropertyChanged(nameof(StopRecordingLabel));
                OnPropertyChanged(nameof(CanChangeTarget));
                OnPropertyChanged(nameof(CanOpenLastOutput));
                OnPropertyChanged(nameof(CanTrimLastOutput));
                OnPropertyChanged(nameof(CanExportGif));
                OnPropertyChanged(nameof(CanOpenLastGif));
                OnPropertyChanged(nameof(CanUseMicrophoneAudio));
                OnPropertyChanged(nameof(CanSelectMicrophoneDevice));
                OnPropertyChanged(nameof(CanSelectDisplay));
                OnPropertyChanged(nameof(CanEditWatermarkText));
                RefreshCommands();
            }
        }
    }

    public int FrameRate
    {
        get => _configuration.FrameRate;
        set
        {
            if (_configuration.FrameRate != value)
            {
                _configuration.FrameRate = value;
                OnPropertyChanged();
                MarkPresetCustomIfNeeded();
            }
        }
    }

    public string Quality
    {
        get => _configuration.Quality;
        set
        {
            if (_configuration.Quality != value)
            {
                _configuration.Quality = value;
                OnPropertyChanged();
                MarkPresetCustomIfNeeded();
            }
        }
    }

    public RecordingPresetInfo? SelectedRecordingPreset
    {
        get => _selectedRecordingPreset;
        set
        {
            if (!SetField(ref _selectedRecordingPreset, value) || value is null)
            {
                return;
            }

            _configuration.RecordingPreset = value.Name;
            if (value.IsCustom)
            {
                return;
            }

            _applyingPreset = true;
            try
            {
                FrameRate = value.FrameRate;
                Quality = value.Quality;
                SelectedVideoBitrate = VideoBitrateOptions.First(option => option.KilobitsPerSecond == value.VideoBitrateKbps);
            }
            finally
            {
                _applyingPreset = false;
            }
        }
    }

    public VideoBitrateOption? SelectedVideoBitrate
    {
        get => _selectedVideoBitrate;
        set
        {
            if (SetField(ref _selectedVideoBitrate, value) && value is not null)
            {
                _configuration.VideoBitrateKbps = value.KilobitsPerSecond;
                MarkPresetCustomIfNeeded();
            }
        }
    }

    public bool CaptureCursor
    {
        get => _configuration.CaptureCursor;
        set
        {
            if (_configuration.CaptureCursor != value)
            {
                _configuration.CaptureCursor = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HighlightMouseClicks
    {
        get => _configuration.HighlightMouseClicks;
        set
        {
            if (_configuration.HighlightMouseClicks != value)
            {
                _configuration.HighlightMouseClicks = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IncludeWatermark
    {
        get => _configuration.IncludeWatermark;
        set
        {
            if (_configuration.IncludeWatermark != value)
            {
                _configuration.IncludeWatermark = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanEditWatermarkText));
            }
        }
    }

    public string WatermarkText
    {
        get => _configuration.WatermarkText;
        set
        {
            if (_configuration.WatermarkText != value)
            {
                _configuration.WatermarkText = value;
                OnPropertyChanged();
            }
        }
    }

    public string UpdateManifestLocation
    {
        get => _configuration.UpdateManifestLocation;
        set
        {
            if (_configuration.UpdateManifestLocation != value)
            {
                _configuration.UpdateManifestLocation = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanCheckForUpdates));
                RefreshCommands();
            }
        }
    }

    public bool AllowCrashReports
    {
        get => _configuration.AllowCrashReports;
        set
        {
            if (_configuration.AllowCrashReports != value)
            {
                _configuration.AllowCrashReports = value;
                CrashReportingOptions.IsEnabled = value;
                DiagnosticsStatusMessage = value
                    ? Text("CrashReportsEnabled")
                    : Text("CrashReportsDisabled");
                OnPropertyChanged();
            }
        }
    }

    public bool CaptureSystemAudio
    {
        get => _configuration.CaptureSystemAudio;
        set
        {
            if (_configuration.CaptureSystemAudio != value)
            {
                _configuration.CaptureSystemAudio = value;
                OnPropertyChanged();
            }
        }
    }

    public DisplayInfo? SelectedDisplay
    {
        get => _selectedDisplay;
        set
        {
            if (SetField(ref _selectedDisplay, value) && SelectedMode == RecordingMode.FullScreen)
            {
                Target = null;
                State = RecordingState.Idle;
                StatusMessage = Text("DisplayChangedSelectTarget");
            }
        }
    }

    public bool CaptureMicrophoneAudio
    {
        get => _configuration.CaptureMicrophoneAudio;
        set
        {
            if (_configuration.CaptureMicrophoneAudio != value)
            {
                _configuration.CaptureMicrophoneAudio = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSelectMicrophoneDevice));
            }
        }
    }

    public MicrophoneDeviceInfo? SelectedMicrophoneDevice
    {
        get => _selectedMicrophoneDevice;
        set
        {
            if (SetField(ref _selectedMicrophoneDevice, value))
            {
                _configuration.MicrophoneDeviceNumber = value?.DeviceNumber;
                OnPropertyChanged(nameof(CanUseMicrophoneAudio));
                OnPropertyChanged(nameof(CanSelectMicrophoneDevice));
            }
        }
    }

    public string OutputDirectory
    {
        get => _configuration.OutputDirectory;
        set
        {
            if (_configuration.OutputDirectory != value)
            {
                _configuration.OutputDirectory = value;
                OnPropertyChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string? LastOutputPath
    {
        get => _lastOutputPath;
        private set
        {
            if (SetField(ref _lastOutputPath, value))
            {
                OnPropertyChanged(nameof(CanOpenLastOutput));
                OnPropertyChanged(nameof(CanTrimLastOutput));
                OnPropertyChanged(nameof(CanExportGif));
                RefreshCommands();
            }
        }
    }

    public string? LastGifOutputPath
    {
        get => _lastGifOutputPath;
        private set
        {
            if (SetField(ref _lastGifOutputPath, value))
            {
                OnPropertyChanged(nameof(CanOpenLastGif));
                RefreshCommands();
            }
        }
    }

    public string TrimStartSeconds
    {
        get => _trimStartSeconds;
        set
        {
            if (SetField(ref _trimStartSeconds, value))
            {
                OnPropertyChanged(nameof(CanTrimLastOutput));
                RefreshCommands();
            }
        }
    }

    public string TrimEndSeconds
    {
        get => _trimEndSeconds;
        set
        {
            if (SetField(ref _trimEndSeconds, value))
            {
                OnPropertyChanged(nameof(CanTrimLastOutput));
                RefreshCommands();
            }
        }
    }

    public int GifFrameRate
    {
        get => _gifFrameRate;
        set
        {
            if (SetField(ref _gifFrameRate, value))
            {
                OnPropertyChanged(nameof(CanExportGif));
                RefreshCommands();
            }
        }
    }

    public int GifWidth
    {
        get => _gifWidth;
        set
        {
            if (SetField(ref _gifWidth, value))
            {
                OnPropertyChanged(nameof(CanExportGif));
                RefreshCommands();
            }
        }
    }

    public string ElapsedTime
    {
        get => _elapsedTime;
        private set => SetField(ref _elapsedTime, value);
    }

    public string CurrentVersion => GetCurrentVersion().ToString();

    public string UpdateStatusMessage
    {
        get => _updateStatusMessage;
        private set => SetField(ref _updateStatusMessage, value);
    }

    public string? DownloadedUpdatePackagePath
    {
        get => _downloadedUpdatePackagePath;
        private set
        {
            if (SetField(ref _downloadedUpdatePackagePath, value))
            {
                OnPropertyChanged(nameof(CanOpenDownloadedUpdatePackage));
                RefreshCommands();
            }
        }
    }

    public string DiagnosticsStatusMessage
    {
        get => _diagnosticsStatusMessage;
        private set => SetField(ref _diagnosticsStatusMessage, value);
    }

    public string CrashReportDirectory => _crashReportService.ReportDirectory;

    public string ModeLabel => SelectedMode switch
    {
        RecordingMode.Region => Text("Region"),
        RecordingMode.Window => Text("Window"),
        RecordingMode.FullScreen => Text("FullScreen"),
        _ => Text("Unknown")
    };

    public string StateLabel => Text($"State{State}");

    public string PauseResumeLabel => State == RecordingState.Paused ? Text("Resume") : Text("Pause");

    public string StopRecordingLabel => State == RecordingState.Countdown ? Text("Cancel") : Text("Stop");

    public bool CanChangeTarget => !IsRecordingLocked;

    public bool CanOpenLastOutput => State == RecordingState.Completed && !string.IsNullOrWhiteSpace(LastOutputPath) && !_isPostProcessing;

    public bool CanTrimLastOutput => CanOpenLastOutput && !_isPostProcessing;

    public bool CanExportGif => CanOpenLastOutput && !_isPostProcessing;

    public bool CanOpenLastGif => State == RecordingState.Completed && !string.IsNullOrWhiteSpace(LastGifOutputPath) && !_isPostProcessing;

    public bool CanUseMicrophoneAudio => CanChangeTarget && MicrophoneDevices.Count > 0;

    public bool CanSelectMicrophoneDevice => CanUseMicrophoneAudio && CaptureMicrophoneAudio;

    public bool CanSelectDisplay => CanChangeTarget && SelectedMode == RecordingMode.FullScreen && Displays.Count > 1;

    public bool CanEditWatermarkText => CanChangeTarget && IncludeWatermark;

    public bool CanCheckForUpdates => !IsRecordingLocked && !_isCheckingForUpdate && !_isDownloadingUpdate;

    public bool CanOpenUpdatePackage =>
        _lastUpdateCheckResult is { IsUpdateAvailable: true } result &&
        !string.IsNullOrWhiteSpace(result.PackageUrl);

    public bool CanDownloadUpdatePackage =>
        CanOpenUpdatePackage &&
        !_isCheckingForUpdate &&
        !_isDownloadingUpdate &&
        !string.IsNullOrWhiteSpace(_lastUpdateCheckResult?.Sha256);

    public bool CanOpenDownloadedUpdatePackage =>
        !string.IsNullOrWhiteSpace(DownloadedUpdatePackagePath) &&
        File.Exists(DownloadedUpdatePackagePath);

    public string TargetSummary => _target?.Description ?? $"{ModeLabel}: {Text("NotSelected")}";

    private bool IsRecordingLocked => State is RecordingState.Countdown or RecordingState.Recording or RecordingState.Paused or RecordingState.Stopping;

    private RecordingTarget? Target
    {
        get => _target;
        set
        {
            if (_target != value)
            {
                _target = value;
                OnPropertyChanged(nameof(TargetSummary));
            }
        }
    }

    private void SelectMode(object? parameter)
    {
        if (parameter is string mode && Enum.TryParse<RecordingMode>(mode, out var parsedMode))
        {
            SelectedMode = parsedMode;
        }
    }

    private void SelectTarget()
    {
        if (SelectedMode == RecordingMode.Region)
        {
            SelectRegionTarget();
            return;
        }

        if (SelectedMode == RecordingMode.Window)
        {
            SelectWindowTarget();
            return;
        }

        Target = SelectedMode switch
        {
            RecordingMode.FullScreen => CreateFullScreenTarget(),
            _ => null
        };

        State = RecordingState.TargetSelected;
            StatusMessage = Text("TargetSelected");
        LastOutputPath = null;
        LastGifOutputPath = null;
    }

    private void BrowseOutputDirectory()
    {
        var selectedDirectory = _selectOutputDirectory(OutputDirectory);
        if (string.IsNullOrWhiteSpace(selectedDirectory))
        {
            StatusMessage = Text("OutputDirectorySelectionCanceled");
            return;
        }

        OutputDirectory = selectedDirectory;
        StatusMessage = Text("OutputDirectoryUpdated");
    }

    private void SelectRegionTarget()
    {
        var selectedRegion = _selectRegion();
        if (selectedRegion is null)
        {
            StatusMessage = Text("RegionSelectionCanceled");
            return;
        }

        Target = new RecordingTarget(
            RecordingMode.Region,
            $"{Text("Region")}: {selectedRegion.Width} x {selectedRegion.Height} {Text("At")} ({selectedRegion.X}, {selectedRegion.Y})",
            selectedRegion);
        State = RecordingState.TargetSelected;
        StatusMessage = Text("RegionTargetSelected");
        LastOutputPath = null;
        LastGifOutputPath = null;
    }

    private void SelectWindowTarget()
    {
        var selectedWindow = _selectWindow();
        if (selectedWindow is null)
        {
            StatusMessage = Text("WindowSelectionCanceled");
            return;
        }

        Target = CreateWindowTarget(selectedWindow, selectedWindow.Bounds);
        State = RecordingState.TargetSelected;
        StatusMessage = Text("WindowTargetSelected");
        LastOutputPath = null;
        LastGifOutputPath = null;
    }

    private async Task StartRecordingAsync()
    {
        if (Target is null)
        {
            State = RecordingState.Error;
            StatusMessage = Text("SelectTargetBeforeStarting");
            return;
        }

        var countdownTokenSource = new CancellationTokenSource();
        _countdownTokenSource = countdownTokenSource;

        try
        {
            State = RecordingState.Countdown;
            RecordingShellSuppressionRequested?.Invoke(true);
            await Task.Delay(TimeSpan.FromMilliseconds(250), countdownTokenSource.Token);
            for (var remaining = RecordingCountdownSeconds; remaining > 0; remaining--)
            {
                StatusMessage = string.Format(CultureInfo.CurrentCulture, Text("RecordingStartsIn"), remaining);
                await Task.Delay(TimeSpan.FromSeconds(1), countdownTokenSource.Token);
            }

            countdownTokenSource.Token.ThrowIfCancellationRequested();
            var target = RefreshTargetBeforeRecording(Target);
            Directory.CreateDirectory(OutputDirectory);
            _activeSession = await _videoRecorder.StartAsync(new VideoRecorderStartRequest(
                target,
                OutputDirectory,
                FrameRate,
                Quality,
                VideoBitrateKbps: SelectedVideoBitrate?.KilobitsPerSecond,
                CaptureCursor: CaptureCursor,
                HighlightMouseClicks: HighlightMouseClicks,
                CaptureSystemAudio: CaptureSystemAudio,
                CaptureMicrophoneAudio: CaptureMicrophoneAudio,
                MicrophoneDeviceNumber: SelectedMicrophoneDevice?.DeviceNumber,
                IncludeWatermark: IncludeWatermark,
                WatermarkText: WatermarkText));
            _recordingStartedAt = _activeSession.StartedAt;
            _recordingElapsedBeforeCurrentRun = TimeSpan.Zero;
            UpdateElapsedTime();
            _elapsedTimer.Start();
            State = RecordingState.Recording;
            LastOutputPath = _activeSession.OutputPath;
            LastGifOutputPath = null;
            StatusMessage = target.Mode switch
            {
                RecordingMode.Region => string.Format(CultureInfo.CurrentCulture, Text("RecordingSelectedRegion"), _activeSession.Width, _activeSession.Height),
                RecordingMode.Window => string.Format(CultureInfo.CurrentCulture, Text("RecordingSelectedWindow"), _activeSession.Width, _activeSession.Height),
                RecordingMode.FullScreen => string.Format(CultureInfo.CurrentCulture, Text("RecordingFullScreen"), _activeSession.Width, _activeSession.Height),
                _ => string.Format(CultureInfo.CurrentCulture, Text("RecordingGeneratedFrames"), _activeSession.Width, _activeSession.Height)
            };
        }
        catch (OperationCanceledException)
        {
            RecordingShellSuppressionRequested?.Invoke(false);
            State = RecordingState.TargetSelected;
            StatusMessage = Text("RecordingCountdownCanceled");
        }
        catch (Exception ex)
        {
            RecordingShellSuppressionRequested?.Invoke(false);
            _elapsedTimer.Stop();
            _recordingStartedAt = null;
            _recordingElapsedBeforeCurrentRun = TimeSpan.Zero;
            UpdateElapsedTime();
            State = RecordingState.Error;
            StatusMessage = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_countdownTokenSource, countdownTokenSource))
            {
                _countdownTokenSource = null;
            }

            countdownTokenSource.Dispose();
        }
    }

    private void OpenLastFile()
    {
        if (string.IsNullOrWhiteSpace(LastOutputPath) || !File.Exists(LastOutputPath))
        {
            StatusMessage = Text("RecordedFileUnavailable");
            RefreshCommands();
            return;
        }

        try
        {
            OpenWithWindowsShell(LastOutputPath);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(CultureInfo.CurrentCulture, Text("CouldNotOpenRecordedFile"), ex.Message);
        }
    }

    private void OpenLastFolder()
    {
        if (string.IsNullOrWhiteSpace(LastOutputPath))
        {
            StatusMessage = Text("RecordedFileNotAvailableYet");
            RefreshCommands();
            return;
        }

        var directory = Path.GetDirectoryName(LastOutputPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            StatusMessage = Text("RecordingFolderUnavailable");
            RefreshCommands();
            return;
        }

        try
        {
            OpenWithWindowsShell(directory);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(CultureInfo.CurrentCulture, Text("CouldNotOpenRecordingFolder"), ex.Message);
        }
    }

    private void OpenLastGif()
    {
        if (string.IsNullOrWhiteSpace(LastGifOutputPath) || !File.Exists(LastGifOutputPath))
        {
            StatusMessage = Text("GifFileUnavailable");
            RefreshCommands();
            return;
        }

        try
        {
            OpenWithWindowsShell(LastGifOutputPath);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(CultureInfo.CurrentCulture, Text("CouldNotOpenGifFile"), ex.Message);
        }
    }

    private async Task TrimLastOutputAsync()
    {
        if (string.IsNullOrWhiteSpace(LastOutputPath) || !File.Exists(LastOutputPath))
        {
            StatusMessage = Text("RecordedFileUnavailable");
            RefreshCommands();
            return;
        }

        if (!TryParseTrimSeconds(TrimStartSeconds, "trim start", out var trimStartSeconds) ||
            !TryParseTrimSeconds(TrimEndSeconds, "trim end", out var trimEndSeconds))
        {
            return;
        }

        _isPostProcessing = true;
        OnPropertyChanged(nameof(CanOpenLastOutput));
        OnPropertyChanged(nameof(CanTrimLastOutput));
        RefreshCommands();
        var sourcePath = LastOutputPath;
        StatusMessage = Text("TrimmingRecording");

        try
        {
            var outputPath = await _videoPostProcessor.TrimAsync(new VideoTrimRequest(
                sourcePath,
                trimStartSeconds,
                trimEndSeconds));
            LastOutputPath = outputPath;
            LastGifOutputPath = null;
            AddHistoryItem(outputPath, "Trimmed MP4", $"Trimmed {FormatSecondsForDisplay(trimStartSeconds)}s start, {FormatSecondsForDisplay(trimEndSeconds)}s end");
            StatusMessage = Text("TrimmedCopyCreated");
        }
        catch (Exception ex)
        {
            LastOutputPath = sourcePath;
            StatusMessage = ex.Message;
        }
        finally
        {
            _isPostProcessing = false;
            OnPropertyChanged(nameof(CanOpenLastOutput));
            OnPropertyChanged(nameof(CanTrimLastOutput));
            RefreshCommands();
        }
    }

    private async Task ExportGifAsync()
    {
        if (string.IsNullOrWhiteSpace(LastOutputPath) || !File.Exists(LastOutputPath))
        {
            StatusMessage = Text("RecordedFileUnavailable");
            RefreshCommands();
            return;
        }

        _isPostProcessing = true;
        OnPropertyChanged(nameof(CanOpenLastOutput));
        OnPropertyChanged(nameof(CanTrimLastOutput));
        OnPropertyChanged(nameof(CanExportGif));
        OnPropertyChanged(nameof(CanOpenLastGif));
        RefreshCommands();
        StatusMessage = Text("ExportingGif");

        try
        {
            LastGifOutputPath = await _videoPostProcessor.ExportGifAsync(new VideoGifExportRequest(
                LastOutputPath,
                GifFrameRate,
                GifWidth));
            AddHistoryItem(LastGifOutputPath, "GIF", $"{GifFrameRate} FPS, max width {GifWidth}px");
            StatusMessage = Text("GifExportCreated");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            _isPostProcessing = false;
            OnPropertyChanged(nameof(CanOpenLastOutput));
            OnPropertyChanged(nameof(CanTrimLastOutput));
            OnPropertyChanged(nameof(CanExportGif));
            OnPropertyChanged(nameof(CanOpenLastGif));
            RefreshCommands();
        }
    }

    private async Task StopRecordingAsync()
    {
        if (State == RecordingState.Countdown)
        {
            _countdownTokenSource?.Cancel();
            StatusMessage = Text("CancelingRecordingCountdown");
            return;
        }

        AccumulateCurrentRecordingSegment();
        _elapsedTimer.Stop();
        UpdateElapsedTime();
        State = RecordingState.Stopping;

        try
        {
            LastOutputPath = await _videoRecorder.StopAsync();
            AddHistoryItem(LastOutputPath, "MP4", $"{ModeLabel}, {FrameRate} FPS");
            _activeSession = null;
            State = RecordingState.Completed;
            RecordingShellSuppressionRequested?.Invoke(false);
            StatusMessage = Text("RecordingFinalized");
        }
        catch (Exception ex)
        {
            _activeSession = null;
            State = RecordingState.Error;
            RecordingShellSuppressionRequested?.Invoke(false);
            StatusMessage = ex.Message;
        }
    }

    private async Task PauseOrResumeRecordingAsync()
    {
        try
        {
            if (State == RecordingState.Recording)
            {
                await _videoRecorder.PauseAsync();
                AccumulateCurrentRecordingSegment();
                _elapsedTimer.Stop();
                UpdateElapsedTime();
                State = RecordingState.Paused;
                StatusMessage = Text("RecordingPaused");
                return;
            }

            if (State == RecordingState.Paused)
            {
                await _videoRecorder.ResumeAsync();
                _recordingStartedAt = DateTimeOffset.Now;
                _elapsedTimer.Start();
                State = RecordingState.Recording;
                StatusMessage = Text("RecordingResumed");
            }
        }
        catch (Exception ex)
        {
            State = RecordingState.Error;
            StatusMessage = ex.Message;
        }
    }

    private RecordingTarget CreateFullScreenTarget()
    {
        var display = SelectedDisplay
            ?? Displays.FirstOrDefault(displayInfo => displayInfo.IsPrimary)
            ?? throw new InvalidOperationException(Text("DisplayBoundsUnavailable"));
        var bounds = display.Bounds;

        return new RecordingTarget(
            RecordingMode.FullScreen,
            $"{Text("FullScreen")}: {display.DisplayName}",
            bounds);
    }

    private RecordingTarget CreateWindowTarget(DesktopWindowInfo window, CaptureRegion region)
    {
        return new RecordingTarget(
            RecordingMode.Window,
            $"{Text("Window")}: {window.Title} ({window.ProcessName}) - {region.Width} x {region.Height} {Text("At")} ({region.X}, {region.Y})",
            region,
            window.Handle);
    }

    private RecordingTarget RefreshTargetBeforeRecording(RecordingTarget target)
    {
        if (target.Mode != RecordingMode.Window)
        {
            return target;
        }

        if (target.WindowHandle is not { } windowHandle)
        {
            State = RecordingState.Error;
            throw new InvalidOperationException(Text("SelectedWindowUnavailable"));
        }

        var resolvedWindow = _windowEnumeration.ResolveWindow(windowHandle);
        var region = resolvedWindow.Bounds;

        var refreshedTarget = new RecordingTarget(
            target.Mode,
            $"{Text("Window")}: {resolvedWindow.Title} ({resolvedWindow.ProcessName}) - {region.Width} x {region.Height} {Text("At")} ({region.X}, {region.Y})",
            region,
            windowHandle);
        Target = refreshedTarget;
        return refreshedTarget;
    }

    private void UpdateElapsedTime()
    {
        if (_recordingStartedAt is null)
        {
            ElapsedTime = _recordingElapsedBeforeCurrentRun.ToString(@"hh\:mm\:ss");
            return;
        }

        ElapsedTime = (_recordingElapsedBeforeCurrentRun + (DateTimeOffset.Now - _recordingStartedAt.Value))
            .ToString(@"hh\:mm\:ss");
    }

    private void AccumulateCurrentRecordingSegment()
    {
        if (_recordingStartedAt is null)
        {
            return;
        }

        _recordingElapsedBeforeCurrentRun += DateTimeOffset.Now - _recordingStartedAt.Value;
        _recordingStartedAt = null;
    }

    private void RefreshLocalizedProperties()
    {
        OnPropertyChanged("Item[]");
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(PauseResumeLabel));
        OnPropertyChanged(nameof(StopRecordingLabel));
        OnPropertyChanged(nameof(TargetSummary));

        if (StatusMessage is "Select a recording target to begin." or "请选择录制目标后开始。")
        {
            StatusMessage = Text("SelectTargetToBegin");
        }

        if (UpdateStatusMessage is "Update check not configured." or "未配置更新检查。")
        {
            UpdateStatusMessage = Text("UpdateCheckNotConfigured");
        }

        if (DiagnosticsStatusMessage is "Crash reports are disabled." or "崩溃报告已关闭。")
        {
            DiagnosticsStatusMessage = Text("CrashReportsDisabled");
        }
    }

    private void RefreshCommands()
    {
        SelectModeCommand.RaiseCanExecuteChanged();
        SelectTargetCommand.RaiseCanExecuteChanged();
        BrowseOutputDirectoryCommand.RaiseCanExecuteChanged();
        StartRecordingCommand.RaiseCanExecuteChanged();
        PauseResumeCommand.RaiseCanExecuteChanged();
        StopRecordingCommand.RaiseCanExecuteChanged();
        OpenLastFileCommand.RaiseCanExecuteChanged();
        OpenLastFolderCommand.RaiseCanExecuteChanged();
        TrimLastOutputCommand.RaiseCanExecuteChanged();
        ExportGifCommand.RaiseCanExecuteChanged();
        OpenLastGifCommand.RaiseCanExecuteChanged();
        OpenHistoryFileCommand.RaiseCanExecuteChanged();
        OpenHistoryFolderCommand.RaiseCanExecuteChanged();
        ClearHistoryCommand.RaiseCanExecuteChanged();
        CheckForUpdatesCommand.RaiseCanExecuteChanged();
        OpenUpdatePackageCommand.RaiseCanExecuteChanged();
        DownloadUpdatePackageCommand.RaiseCanExecuteChanged();
        OpenDownloadedUpdatePackageCommand.RaiseCanExecuteChanged();
        OpenCrashReportFolderCommand.RaiseCanExecuteChanged();
    }

    private void OpenCrashReportFolder()
    {
        try
        {
            Directory.CreateDirectory(_crashReportService.ReportDirectory);
            OpenWithWindowsShell(_crashReportService.ReportDirectory);
        }
        catch (Exception ex)
        {
            DiagnosticsStatusMessage = string.Format(CultureInfo.CurrentCulture, Text("CouldNotOpenCrashReportFolder"), ex.Message);
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        _isCheckingForUpdate = true;
        OnPropertyChanged(nameof(CanCheckForUpdates));
        RefreshCommands();
        UpdateStatusMessage = Text("CheckingForUpdates");

        try
        {
            _lastUpdateCheckResult = await _updateCheckService.CheckAsync(
                UpdateManifestLocation,
                GetCurrentVersion());
            DownloadedUpdatePackagePath = null;
            UpdateStatusMessage = _lastUpdateCheckResult.IsUpdateAvailable
                ? $"{_lastUpdateCheckResult.Summary} SHA256: {_lastUpdateCheckResult.Sha256 ?? Text("NotProvided")}"
                : _lastUpdateCheckResult.Summary;
            OnPropertyChanged(nameof(CanOpenUpdatePackage));
            OnPropertyChanged(nameof(CanDownloadUpdatePackage));
        }
        catch (Exception ex)
        {
            _lastUpdateCheckResult = null;
            DownloadedUpdatePackagePath = null;
            UpdateStatusMessage = ex.Message;
            OnPropertyChanged(nameof(CanOpenUpdatePackage));
            OnPropertyChanged(nameof(CanDownloadUpdatePackage));
        }
        finally
        {
            _isCheckingForUpdate = false;
            OnPropertyChanged(nameof(CanCheckForUpdates));
            RefreshCommands();
        }
    }

    private void OpenUpdatePackage()
    {
        if (_lastUpdateCheckResult is not { IsUpdateAvailable: true } result ||
            string.IsNullOrWhiteSpace(result.PackageUrl))
        {
            UpdateStatusMessage = Text("NoUpdatePackageToOpen");
            RefreshCommands();
            return;
        }

        try
        {
            OpenWithWindowsShell(result.PackageUrl);
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = string.Format(CultureInfo.CurrentCulture, Text("CouldNotOpenUpdatePackage"), ex.Message);
        }
    }

    private async Task DownloadUpdatePackageAsync()
    {
        if (_lastUpdateCheckResult is not { IsUpdateAvailable: true } result)
        {
            UpdateStatusMessage = Text("NoUpdatePackageToDownload");
            RefreshCommands();
            return;
        }

        _isDownloadingUpdate = true;
        OnPropertyChanged(nameof(CanCheckForUpdates));
        OnPropertyChanged(nameof(CanDownloadUpdatePackage));
        OnPropertyChanged(nameof(CanOpenDownloadedUpdatePackage));
        RefreshCommands();
        UpdateStatusMessage = Text("DownloadingUpdatePackage");

        try
        {
            var downloaded = await _updatePackageDownloadService.DownloadAsync(result);
            DownloadedUpdatePackagePath = downloaded.PackagePath;
            var verification = downloaded.Sha256Verified ? Text("Sha256Verified") : Text("Sha256NotProvided");
            UpdateStatusMessage = string.Format(CultureInfo.CurrentCulture, Text("DownloadedUpdatePackage"), downloaded.SizeBytes, verification);
        }
        catch (Exception ex)
        {
            DownloadedUpdatePackagePath = null;
            UpdateStatusMessage = ex.Message;
        }
        finally
        {
            _isDownloadingUpdate = false;
            OnPropertyChanged(nameof(CanCheckForUpdates));
            OnPropertyChanged(nameof(CanDownloadUpdatePackage));
            OnPropertyChanged(nameof(CanOpenDownloadedUpdatePackage));
            RefreshCommands();
        }
    }

    private void OpenDownloadedUpdatePackage()
    {
        if (string.IsNullOrWhiteSpace(DownloadedUpdatePackagePath) || !File.Exists(DownloadedUpdatePackagePath))
        {
            UpdateStatusMessage = Text("DownloadedUpdatePackageUnavailable");
            RefreshCommands();
            return;
        }

        try
        {
            OpenWithWindowsShell(DownloadedUpdatePackagePath);
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = string.Format(CultureInfo.CurrentCulture, Text("CouldNotOpenDownloadedPackage"), ex.Message);
        }
    }

    private void AddHistoryItem(string? path, string artifactType, string details)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var duplicate = RecordingHistory.FirstOrDefault(item =>
            string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            RecordingHistory.Remove(duplicate);
        }

        RecordingHistory.Insert(0, new RecordingHistoryItem(path, artifactType, DateTimeOffset.Now, details));
        while (RecordingHistory.Count > MaxHistoryItems)
        {
            RecordingHistory.RemoveAt(RecordingHistory.Count - 1);
        }

        SaveRecordingHistory();
        ClearHistoryCommand.RaiseCanExecuteChanged();
    }

    private void OpenHistoryFile(object? parameter)
    {
        if (parameter is not RecordingHistoryItem item)
        {
            return;
        }

        if (!File.Exists(item.Path))
        {
            StatusMessage = Text("HistoryFileUnavailable");
            RefreshCommands();
            return;
        }

        try
        {
            OpenWithWindowsShell(item.Path);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(CultureInfo.CurrentCulture, Text("CouldNotOpenHistoryFile"), ex.Message);
        }
    }

    private void OpenHistoryFolder(object? parameter)
    {
        if (parameter is not RecordingHistoryItem item)
        {
            return;
        }

        var directory = Path.GetDirectoryName(item.Path);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            StatusMessage = Text("HistoryFolderUnavailable");
            RefreshCommands();
            return;
        }

        try
        {
            OpenWithWindowsShell(directory);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(CultureInfo.CurrentCulture, Text("CouldNotOpenHistoryFolder"), ex.Message);
        }
    }

    private void ClearHistory()
    {
        RecordingHistory.Clear();
        _recordingHistoryStore.Clear();
        StatusMessage = Text("RecordingHistoryCleared");
        RefreshCommands();
    }

    private void LoadRecordingHistory()
    {
        foreach (var item in _recordingHistoryStore.Load(MaxHistoryItems))
        {
            RecordingHistory.Add(item);
        }

        ClearHistoryCommand.RaiseCanExecuteChanged();
    }

    private void SaveRecordingHistory()
    {
        _recordingHistoryStore.Save(RecordingHistory, MaxHistoryItems);
    }

    private static bool CanOpenHistoryItem(object? parameter)
    {
        return parameter is RecordingHistoryItem item && File.Exists(item.Path);
    }

    private static string FormatSecondsForDisplay(double seconds) => seconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static Version GetCurrentVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
    }

    private bool TryParseTrimSeconds(string value, string label, out double seconds)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            seconds = 0;
            return true;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out seconds) &&
            !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
        {
            StatusMessage = string.Format(CultureInfo.CurrentCulture, Text("EnterValidSeconds"), label);
            return false;
        }

        if (seconds < 0)
        {
            var titleCaseLabel = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(label);
            StatusMessage = string.Format(CultureInfo.CurrentCulture, Text("SecondsCannotBeNegative"), titleCaseLabel);
            return false;
        }

        return true;
    }

    private static void OpenWithWindowsShell(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    public void ReportGlobalHotkeyRegistrationFailure(string hotkeys)
    {
        if (string.IsNullOrWhiteSpace(hotkeys))
        {
            return;
        }

        StatusMessage = string.Format(CultureInfo.CurrentCulture, Text("GlobalHotkeyUnavailable"), hotkeys);
    }

    public void ReportRecordingShellSuppressionSkipped()
    {
        StatusMessage = Text("RecordingStartedShellVisible");
    }

    private static IReadOnlyList<MicrophoneDeviceInfo> EnumerateMicrophoneDevices()
    {
        var devices = new List<MicrophoneDeviceInfo>();
        for (var deviceNumber = 0; deviceNumber < WaveIn.DeviceCount; deviceNumber++)
        {
            var capabilities = WaveIn.GetCapabilities(deviceNumber);
            devices.Add(new MicrophoneDeviceInfo(deviceNumber, capabilities.ProductName));
        }

        return devices;
    }

    private static IReadOnlyList<RecordingPresetInfo> CreateRecordingPresets()
    {
        return
        [
            new RecordingPresetInfo("Compact", 15, "Standard", 4_000),
            new RecordingPresetInfo("Balanced", 30, "Standard", null),
            new RecordingPresetInfo("Smooth", 60, "High", 12_000),
            new RecordingPresetInfo("Custom", 30, "Standard", null, IsCustom: true)
        ];
    }

    private static IReadOnlyList<VideoBitrateOption> CreateVideoBitrateOptions()
    {
        return
        [
            VideoBitrateOption.Auto,
            new VideoBitrateOption("4 Mbps", 4_000),
            new VideoBitrateOption("8 Mbps", 8_000),
            new VideoBitrateOption("12 Mbps", 12_000),
            new VideoBitrateOption("20 Mbps", 20_000)
        ];
    }

    private void MarkPresetCustomIfNeeded()
    {
        if (_applyingPreset)
        {
            return;
        }

        var matchingPreset = RecordingPresets.FirstOrDefault(preset =>
            !preset.IsCustom &&
            preset.FrameRate == FrameRate &&
            string.Equals(preset.Quality, Quality, StringComparison.OrdinalIgnoreCase) &&
            preset.VideoBitrateKbps == SelectedVideoBitrate?.KilobitsPerSecond);
        SelectedRecordingPreset = matchingPreset ?? RecordingPresets.First(preset => preset.IsCustom);
    }

    private string Text(string key)
    {
        var languageCode = _selectedLanguage?.Code ?? _configuration.LanguageCode;
        if (!LocalizedText.TryGetValue(languageCode, out var resources))
        {
            resources = LocalizedText[DefaultLanguageCode];
        }

        if (resources.TryGetValue(key, out var value))
        {
            return value;
        }

        return LocalizedText[DefaultLanguageCode].TryGetValue(key, out var fallback)
            ? fallback
            : key;
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LocalizedText =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultLanguageCode] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["AppSubtitle"] = "Local Windows screen recorder",
                ["Settings"] = "Settings",
                ["Language"] = "Language",
                ["RecordingMode"] = "Recording Mode",
                ["Region"] = "Region",
                ["Window"] = "Window",
                ["FullScreen"] = "Full Screen",
                ["CurrentTarget"] = "Current Target",
                ["State"] = "State",
                ["Elapsed"] = "Elapsed",
                ["VideoSettings"] = "Video Settings",
                ["Preset"] = "Preset",
                ["FrameRate"] = "Frame Rate",
                ["Quality"] = "Quality",
                ["Bitrate"] = "Bitrate",
                ["Display"] = "Display",
                ["SaveTo"] = "Save To",
                ["Browse"] = "Browse",
                ["Audio"] = "Audio",
                ["RecordSystemAudio"] = "Record system audio",
                ["RecordMicrophone"] = "Record microphone",
                ["Pointer"] = "Pointer",
                ["RecordMouseCursor"] = "Record mouse cursor",
                ["HighlightMouseClicks"] = "Highlight mouse clicks",
                ["Watermark"] = "Watermark",
                ["AddTextWatermark"] = "Add text watermark",
                ["Status"] = "Status",
                ["OpenFile"] = "Open File",
                ["OpenFolder"] = "Open Folder",
                ["PostProcessing"] = "Post-Processing",
                ["TrimCopy"] = "Trim Copy",
                ["StartSeconds"] = "Start seconds",
                ["EndSeconds"] = "End seconds",
                ["SaveTrimmedCopy"] = "Save Trimmed Copy",
                ["GifExport"] = "GIF Export",
                ["Fps"] = "FPS",
                ["Width"] = "Width",
                ["ExportGif"] = "Export GIF",
                ["OpenGif"] = "Open GIF",
                ["RecordingHistory"] = "Recording History",
                ["RecentItems"] = "recent items",
                ["Clear"] = "Clear",
                ["Open"] = "Open",
                ["Folder"] = "Folder",
                ["Updates"] = "Updates",
                ["Current"] = "Current",
                ["Manifest"] = "Manifest",
                ["CheckForUpdates"] = "Check for Updates",
                ["DownloadUpdate"] = "Download Update",
                ["OpenDownloadedPackage"] = "Open Downloaded Package",
                ["OpenDownloadPage"] = "Open Download Page",
                ["Diagnostics"] = "Diagnostics",
                ["SaveCrashReportsLocally"] = "Save crash reports locally",
                ["CrashReportsHelp"] = "Crash reports may include error messages and stack traces. They stay on this computer.",
                ["OpenCrashReportsFolder"] = "Open Crash Reports Folder",
                ["SelectTarget"] = "Select Target",
                ["StartRecording"] = "Start Recording",
                ["Pause"] = "Pause",
                ["Resume"] = "Resume",
                ["Stop"] = "Stop",
                ["Cancel"] = "Cancel",
                ["NotSelected"] = "Not selected",
                ["Unknown"] = "Unknown",
                ["At"] = "at",
                ["StateIdle"] = "Idle",
                ["StateTargetSelected"] = "Target Selected",
                ["StateCountdown"] = "Countdown",
                ["StateRecording"] = "Recording",
                ["StatePaused"] = "Paused",
                ["StateStopping"] = "Stopping",
                ["StateCompleted"] = "Completed",
                ["StateError"] = "Error",
                ["SelectTargetToBegin"] = "Select a recording target to begin.",
                ["UpdateCheckNotConfigured"] = "Update check not configured.",
                ["CrashReportsDisabled"] = "Crash reports are disabled.",
                ["CrashReportsEnabled"] = "Local crash reports are enabled.",
                ["ModeChangedSelectTarget"] = "Mode changed to {0}. Select a target.",
                ["DisplayChangedSelectTarget"] = "Display changed. Select a target.",
                ["TargetSelected"] = "Target selected.",
                ["OutputDirectorySelectionCanceled"] = "Output directory selection canceled.",
                ["OutputDirectoryUpdated"] = "Output directory updated.",
                ["RegionSelectionCanceled"] = "Region selection canceled.",
                ["RegionTargetSelected"] = "Region target selected.",
                ["WindowSelectionCanceled"] = "Window selection canceled.",
                ["WindowTargetSelected"] = "Window target selected.",
                ["SelectTargetBeforeStarting"] = "Select a recording target before starting.",
                ["RecordingStartsIn"] = "Recording starts in {0}...",
                ["RecordingSelectedRegion"] = "Recording selected region to {0} x {1} MP4.",
                ["RecordingSelectedWindow"] = "Recording selected window to {0} x {1} MP4.",
                ["RecordingFullScreen"] = "Recording full screen to {0} x {1} MP4.",
                ["RecordingGeneratedFrames"] = "Recording generated frames to {0} x {1} MP4.",
                ["RecordingCountdownCanceled"] = "Recording countdown canceled.",
                ["RecordedFileUnavailable"] = "Recorded file is no longer available.",
                ["RecordedFileNotAvailableYet"] = "Recorded file is not available yet.",
                ["RecordingFolderUnavailable"] = "Recording folder is no longer available.",
                ["GifFileUnavailable"] = "GIF file is no longer available.",
                ["TrimmingRecording"] = "Trimming recording...",
                ["TrimmedCopyCreated"] = "Trimmed copy created.",
                ["ExportingGif"] = "Exporting GIF...",
                ["GifExportCreated"] = "GIF export created.",
                ["CancelingRecordingCountdown"] = "Canceling recording countdown.",
                ["RecordingFinalized"] = "Recording finalized.",
                ["RecordingPaused"] = "Recording paused.",
                ["RecordingResumed"] = "Recording resumed.",
                ["CheckingForUpdates"] = "Checking for updates...",
                ["NotProvided"] = "not provided",
                ["NoUpdatePackageToOpen"] = "No update package is available to open.",
                ["NoUpdatePackageToDownload"] = "No update package is available to download.",
                ["DownloadingUpdatePackage"] = "Downloading update package...",
                ["Sha256Verified"] = "SHA256 verified",
                ["Sha256NotProvided"] = "SHA256 not provided",
                ["DownloadedUpdatePackage"] = "Downloaded update package ({0} bytes, {1}).",
                ["DownloadedUpdatePackageUnavailable"] = "Downloaded update package is no longer available.",
                ["HistoryFileUnavailable"] = "History file is no longer available.",
                ["HistoryFolderUnavailable"] = "History folder is no longer available.",
                ["RecordingHistoryCleared"] = "Recording history cleared.",
                ["DisplayBoundsUnavailable"] = "Display bounds are not available.",
                ["SelectedWindowUnavailable"] = "The selected window is no longer available or is minimized.",
                ["GlobalHotkeyUnavailable"] = "Global hotkey unavailable: {0}. Recording controls still work in the app.",
                ["RecordingStartedShellVisible"] = "Recording started. The main window stayed visible because the stop hotkey is unavailable.",
                ["CouldNotOpenRecordedFile"] = "Could not open recorded file: {0}",
                ["CouldNotOpenRecordingFolder"] = "Could not open recording folder: {0}",
                ["CouldNotOpenGifFile"] = "Could not open GIF file: {0}",
                ["CouldNotOpenCrashReportFolder"] = "Could not open crash report folder: {0}",
                ["CouldNotOpenUpdatePackage"] = "Could not open update package: {0}",
                ["CouldNotOpenDownloadedPackage"] = "Could not open downloaded package: {0}",
                ["CouldNotOpenHistoryFile"] = "Could not open history file: {0}",
                ["CouldNotOpenHistoryFolder"] = "Could not open history folder: {0}",
                ["EnterValidSeconds"] = "Enter a valid {0} value in seconds.",
                ["SecondsCannotBeNegative"] = "{0} cannot be negative."
            },
            ["zh-CN"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["AppSubtitle"] = "本地 Windows 录屏工具",
                ["Settings"] = "设置",
                ["Language"] = "语言",
                ["RecordingMode"] = "录制模式",
                ["Region"] = "区域",
                ["Window"] = "窗口",
                ["FullScreen"] = "全屏",
                ["CurrentTarget"] = "当前目标",
                ["State"] = "状态",
                ["Elapsed"] = "已录制",
                ["VideoSettings"] = "视频设置",
                ["Preset"] = "预设",
                ["FrameRate"] = "帧率",
                ["Quality"] = "质量",
                ["Bitrate"] = "码率",
                ["Display"] = "显示器",
                ["SaveTo"] = "保存到",
                ["Browse"] = "浏览",
                ["Audio"] = "音频",
                ["RecordSystemAudio"] = "录制系统声音",
                ["RecordMicrophone"] = "录制麦克风",
                ["Pointer"] = "指针",
                ["RecordMouseCursor"] = "录制鼠标指针",
                ["HighlightMouseClicks"] = "高亮鼠标点击",
                ["Watermark"] = "水印",
                ["AddTextWatermark"] = "添加文字水印",
                ["Status"] = "状态",
                ["OpenFile"] = "打开文件",
                ["OpenFolder"] = "打开文件夹",
                ["PostProcessing"] = "后期处理",
                ["TrimCopy"] = "裁剪副本",
                ["StartSeconds"] = "开始秒数",
                ["EndSeconds"] = "结束秒数",
                ["SaveTrimmedCopy"] = "保存裁剪副本",
                ["GifExport"] = "导出 GIF",
                ["Fps"] = "FPS",
                ["Width"] = "宽度",
                ["ExportGif"] = "导出 GIF",
                ["OpenGif"] = "打开 GIF",
                ["RecordingHistory"] = "录制历史",
                ["RecentItems"] = "条最近记录",
                ["Clear"] = "清空",
                ["Open"] = "打开",
                ["Folder"] = "文件夹",
                ["Updates"] = "更新",
                ["Current"] = "当前版本",
                ["Manifest"] = "清单",
                ["CheckForUpdates"] = "检查更新",
                ["DownloadUpdate"] = "下载更新",
                ["OpenDownloadedPackage"] = "打开已下载包",
                ["OpenDownloadPage"] = "打开下载页",
                ["Diagnostics"] = "诊断",
                ["SaveCrashReportsLocally"] = "本地保存崩溃报告",
                ["CrashReportsHelp"] = "崩溃报告可能包含错误消息和堆栈信息，并且只保存在本机。",
                ["OpenCrashReportsFolder"] = "打开崩溃报告文件夹",
                ["SelectTarget"] = "选择目标",
                ["StartRecording"] = "开始录制",
                ["Pause"] = "暂停",
                ["Resume"] = "继续",
                ["Stop"] = "停止",
                ["Cancel"] = "取消",
                ["NotSelected"] = "未选择",
                ["Unknown"] = "未知",
                ["At"] = "位置",
                ["StateIdle"] = "空闲",
                ["StateTargetSelected"] = "已选择目标",
                ["StateCountdown"] = "倒计时",
                ["StateRecording"] = "录制中",
                ["StatePaused"] = "已暂停",
                ["StateStopping"] = "停止中",
                ["StateCompleted"] = "已完成",
                ["StateError"] = "错误",
                ["SelectTargetToBegin"] = "请选择录制目标后开始。",
                ["UpdateCheckNotConfigured"] = "未配置更新检查。",
                ["CrashReportsDisabled"] = "崩溃报告已关闭。",
                ["CrashReportsEnabled"] = "本地崩溃报告已开启。",
                ["ModeChangedSelectTarget"] = "模式已切换为{0}。请选择目标。",
                ["DisplayChangedSelectTarget"] = "显示器已更改。请选择目标。",
                ["TargetSelected"] = "已选择目标。",
                ["OutputDirectorySelectionCanceled"] = "已取消选择输出目录。",
                ["OutputDirectoryUpdated"] = "输出目录已更新。",
                ["RegionSelectionCanceled"] = "已取消选择区域。",
                ["RegionTargetSelected"] = "已选择区域目标。",
                ["WindowSelectionCanceled"] = "已取消选择窗口。",
                ["WindowTargetSelected"] = "已选择窗口目标。",
                ["SelectTargetBeforeStarting"] = "开始前请选择录制目标。",
                ["RecordingStartsIn"] = "{0} 秒后开始录制...",
                ["RecordingSelectedRegion"] = "正在录制选定区域，输出 {0} x {1} MP4。",
                ["RecordingSelectedWindow"] = "正在录制选定窗口，输出 {0} x {1} MP4。",
                ["RecordingFullScreen"] = "正在录制全屏，输出 {0} x {1} MP4。",
                ["RecordingGeneratedFrames"] = "正在录制生成画面，输出 {0} x {1} MP4。",
                ["RecordingCountdownCanceled"] = "录制倒计时已取消。",
                ["RecordedFileUnavailable"] = "录制文件已不可用。",
                ["RecordedFileNotAvailableYet"] = "录制文件尚不可用。",
                ["RecordingFolderUnavailable"] = "录制文件夹已不可用。",
                ["GifFileUnavailable"] = "GIF 文件已不可用。",
                ["TrimmingRecording"] = "正在裁剪录制...",
                ["TrimmedCopyCreated"] = "已创建裁剪副本。",
                ["ExportingGif"] = "正在导出 GIF...",
                ["GifExportCreated"] = "已创建 GIF 导出。",
                ["CancelingRecordingCountdown"] = "正在取消录制倒计时。",
                ["RecordingFinalized"] = "录制已完成。",
                ["RecordingPaused"] = "录制已暂停。",
                ["RecordingResumed"] = "录制已继续。",
                ["CheckingForUpdates"] = "正在检查更新...",
                ["NotProvided"] = "未提供",
                ["NoUpdatePackageToOpen"] = "没有可打开的更新包。",
                ["NoUpdatePackageToDownload"] = "没有可下载的更新包。",
                ["DownloadingUpdatePackage"] = "正在下载更新包...",
                ["Sha256Verified"] = "SHA256 已验证",
                ["Sha256NotProvided"] = "未提供 SHA256",
                ["DownloadedUpdatePackage"] = "已下载更新包（{0} 字节，{1}）。",
                ["DownloadedUpdatePackageUnavailable"] = "已下载的更新包已不可用。",
                ["HistoryFileUnavailable"] = "历史文件已不可用。",
                ["HistoryFolderUnavailable"] = "历史文件夹已不可用。",
                ["RecordingHistoryCleared"] = "录制历史已清空。",
                ["DisplayBoundsUnavailable"] = "显示器边界不可用。",
                ["SelectedWindowUnavailable"] = "选定窗口已不可用或已最小化。",
                ["GlobalHotkeyUnavailable"] = "全局快捷键不可用：{0}。仍可在应用内使用录制控制。",
                ["RecordingStartedShellVisible"] = "录制已开始。由于停止快捷键不可用，主窗口保持可见。",
                ["CouldNotOpenRecordedFile"] = "无法打开录制文件：{0}",
                ["CouldNotOpenRecordingFolder"] = "无法打开录制文件夹：{0}",
                ["CouldNotOpenGifFile"] = "无法打开 GIF 文件：{0}",
                ["CouldNotOpenCrashReportFolder"] = "无法打开崩溃报告文件夹：{0}",
                ["CouldNotOpenUpdatePackage"] = "无法打开更新包：{0}",
                ["CouldNotOpenDownloadedPackage"] = "无法打开已下载包：{0}",
                ["CouldNotOpenHistoryFile"] = "无法打开历史文件：{0}",
                ["CouldNotOpenHistoryFolder"] = "无法打开历史文件夹：{0}",
                ["EnterValidSeconds"] = "请输入有效的{0}秒数。",
                ["SecondsCannotBeNegative"] = "{0}不能为负数。"
            }
        };

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
