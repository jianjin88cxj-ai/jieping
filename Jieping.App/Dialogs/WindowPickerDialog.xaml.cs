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

    private void RefreshWindows()
    {
        WindowList.ItemsSource = _windowEnumeration.GetVisibleWindows();
        if (WindowList.Items.Count > 0)
        {
            WindowList.SelectedIndex = 0;
        }
    }

    private void ConfirmSelection()
    {
        if (WindowList.SelectedItem is not DesktopWindowInfo selectedWindow)
        {
            return;
        }

        SelectedWindow = selectedWindow;
        DialogResult = true;
        Close();
    }
}
