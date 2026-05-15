using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Jieping.App.Models;

namespace Jieping.App.Overlays;

public partial class RegionSelectionOverlay : Window
{
    private const double MinimumRegionSize = 16;

    private Point? _dragStart;
    private Rect _currentSelection = Rect.Empty;

    public RegionSelectionOverlay()
    {
        InitializeComponent();
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    public CaptureRegion? SelectedRegion { get; private set; }

    public static CaptureRegion? SelectRegion(Window? owner = null)
    {
        var overlay = new RegionSelectionOverlay
        {
            Owner = owner
        };

        return overlay.ShowDialog() == true ? overlay.SelectedRegion : null;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Activate();
        Focus();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            ConfirmSelection();
            return;
        }

        _dragStart = e.GetPosition(OverlayCanvas);
        CaptureMouse();
        UpdateSelection(_dragStart.Value, _dragStart.Value);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        UpdateSelection(start, e.GetPosition(OverlayCanvas));
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is not { } start)
        {
            return;
        }

        ReleaseMouseCapture();
        UpdateSelection(start, e.GetPosition(OverlayCanvas));
        _dragStart = null;
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        CancelSelection();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelSelection();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            ConfirmSelection();
            e.Handled = true;
        }
    }

    private void UpdateSelection(Point start, Point end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var width = Math.Abs(end.X - start.X);
        var height = Math.Abs(end.Y - start.Y);

        _currentSelection = new Rect(left, top, width, height);

        Canvas.SetLeft(SelectionRectangle, left);
        Canvas.SetTop(SelectionRectangle, top);
        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;
        SelectionRectangle.Visibility = Visibility.Visible;

        SizeText.Text = $"{Math.Round(width)} x {Math.Round(height)}";
        SizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var badgeLeft = Math.Min(left + width + 8, Width - SizeBadge.DesiredSize.Width - 8);
        var badgeTop = Math.Max(8, top - SizeBadge.DesiredSize.Height - 8);
        Canvas.SetLeft(SizeBadge, Math.Max(8, badgeLeft));
        Canvas.SetTop(SizeBadge, badgeTop);
        SizeBadge.Visibility = Visibility.Visible;
    }

    private void ConfirmSelection()
    {
        if (_currentSelection.Width < MinimumRegionSize || _currentSelection.Height < MinimumRegionSize)
        {
            return;
        }

        SelectedRegion = ToPixelRegion(_currentSelection);
        DialogResult = true;
        Close();
    }

    private void CancelSelection()
    {
        SelectedRegion = null;
        DialogResult = false;
        Close();
    }

    private CaptureRegion ToPixelRegion(Rect selection)
    {
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var screenTopLeft = new Point(Left + selection.Left, Top + selection.Top);
        var screenBottomRight = new Point(Left + selection.Right, Top + selection.Bottom);
        var topLeft = transform.Transform(screenTopLeft);
        var bottomRight = transform.Transform(screenBottomRight);

        var x = (int)Math.Round(topLeft.X);
        var y = (int)Math.Round(topLeft.Y);
        var width = (int)Math.Round(bottomRight.X - topLeft.X);
        var height = (int)Math.Round(bottomRight.Y - topLeft.Y);

        return new CaptureRegion(x, y, width, height);
    }
}
