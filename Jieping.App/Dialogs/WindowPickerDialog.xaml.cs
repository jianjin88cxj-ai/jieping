using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Jieping.App.Models;
using Jieping.App.Services;

namespace Jieping.App.Dialogs;

public partial class WindowPickerDialog : Window
{
    private readonly IWindowEnumerationService _windowEnumeration;

    public WindowPickerDialog(IWindowEnumerationService windowEnumeration)
    {
        _windowEnumeration = windowEnumeration;
        InitializeComponent();
        ApplyLocalization();
        RefreshWindows();
    }

    public DesktopWindowInfo? SelectedWindow { get; private set; }

    public static DesktopWindowInfo? SelectWindow(
        IWindowEnumerationService windowEnumeration,
        Window? owner = null)
    {
        var dialog = new WindowPickerDialog(windowEnumeration)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true ? dialog.SelectedWindow : null;
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        RefreshWindows();
    }

    private void OnSelectClick(object sender, RoutedEventArgs e)
    {
        ConfirmSelection();
    }

    private void OnWindowListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ConfirmSelection();
    }

    private void ApplyLocalization()
    {
        if (CultureInfo.CurrentUICulture.Name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
        {
            Title = "选择窗口";
            TitleText.Text = "选择可见窗口";
            TitleColumn.Header = "标题";
            ProcessColumn.Header = "进程";
            SizeColumn.Header = "尺寸";
            RefreshButton.Content = "刷新";
            CancelButton.Content = "取消";
            SelectButton.Content = "选择";
            return;
        }

        Title = "Select Window";
        TitleText.Text = "Select a visible window";
        TitleColumn.Header = "Title";
        ProcessColumn.Header = "Process";
        SizeColumn.Header = "Size";
        RefreshButton.Content = "Refresh";
        CancelButton.Content = "Cancel";
        SelectButton.Content = "Select";
    }

    private void RefreshWindows()
    {
        WindowList.ItemsSource = _windowEnumeration
            .GetVisibleWindows()
            .Select(window => new WindowListItem(
                window,
                window.Title,
                window.ProcessName,
                FormatSize(window.Bounds)))
            .ToList();

        if (WindowList.Items.Count > 0)
        {
            WindowList.SelectedIndex = 0;
        }
    }

    private void ConfirmSelection()
    {
        if (WindowList.SelectedItem is not WindowListItem selectedWindow)
        {
            return;
        }

        SelectedWindow = selectedWindow.Window;
        DialogResult = true;
        Close();
    }

    private static string FormatSize(CaptureRegion bounds)
    {
        return CultureInfo.CurrentUICulture.Name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
            ? string.Format(CultureInfo.CurrentCulture, "{0} x {1}，位置 ({2}, {3})", bounds.Width, bounds.Height, bounds.X, bounds.Y)
            : string.Format(CultureInfo.CurrentCulture, "{0} x {1} at ({2}, {3})", bounds.Width, bounds.Height, bounds.X, bounds.Y);
    }

    private sealed record WindowListItem(
        DesktopWindowInfo Window,
        string Title,
        string ProcessName,
        string SizeDisplay);
}
