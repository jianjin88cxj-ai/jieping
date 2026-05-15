namespace Jieping.App.Models;

public sealed record CaptureRegion(int X, int Y, int Width, int Height)
{
    public string Description => $"Region: {Width} x {Height} at ({X}, {Y})";
}
