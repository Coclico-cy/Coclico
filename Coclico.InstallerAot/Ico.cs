using Avalonia.Media;

namespace Coclico.Installer;

public static class AppBrushes
{
    public static readonly IBrush BgBase = new SolidColorBrush(Color.FromRgb(0x07, 0x08, 0x0B));
    public static readonly IBrush BgCard = new SolidColorBrush(Color.FromRgb(0x13, 0x14, 0x1A));
    public static readonly IBrush BgSurface = new SolidColorBrush(Color.FromRgb(0x0C, 0x0D, 0x12));
    public static readonly IBrush BorderCard = new SolidColorBrush(Color.FromRgb(0x2A, 0x22, 0x26));
    public static readonly IBrush TextPrimary = new SolidColorBrush(Color.FromRgb(0xFA, 0xF5, 0xF2));
    public static readonly IBrush TextSecondary = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));
    public static readonly IBrush AccentPrimary = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
    public static readonly IBrush AccentSecondary = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x35));
    public static readonly IBrush AccentCyan = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE));
    public static readonly IBrush AccentSuccess = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
    public static readonly IBrush StepperActiveBackground = new SolidColorBrush(Color.FromRgb(0x1C, 0x18, 0x22));
    public static readonly IBrush StepperInactiveNumber = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
    public static readonly IBrush StepperInactiveLabel = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
    public static readonly IBrush White = Brushes.White;
    public static readonly IBrush Transparent = Brushes.Transparent;
}

public static class Ico
{
    public static readonly Geometry CheckCircle = Geometry.Parse(
        "F0M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2Zm-1.41 14.24-4.24-4.23 1.41-1.42 2.82 2.82 6.24-6.24 1.42 1.41-7.65 7.66Z");

    public static readonly Geometry Info = Geometry.Parse(
        "F0M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2Zm-1 5h2v2h-2V7Zm0 4h2v6h-2v-6Z");

    public static readonly Geometry Desktop = Geometry.Parse(
        "F0M2 4h20v11H2V4Zm2 2v7h16V6H4Zm6 11h4v2h-4v-2Zm-5 3h14v2H5v-2Z");

    public static readonly Geometry Sparkle = Geometry.Parse(
        "M12 2l2.2 5.3L20 9.5l-4.4 3.4L16.3 19 12 16.3 7.7 19l.7-6.1L4 9.5l5.8-2.2L12 2Z");

    public static readonly Geometry Heart = Geometry.Parse(
        "M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35Z");

    public static readonly Geometry Delete = Geometry.Parse(
        "M6 7h12l-1.14 14.15H7.14L6 7Zm3-4h6l1 2h4v2H4V5h4l1-2Z");

    public static readonly Geometry Warning = Geometry.Parse(
        "F0M12 2 1 21h22L12 2Zm1 14h-2v2h2v-2Zm0-7h-2v5h2V9Z");

    public static readonly Geometry Folder = Geometry.Parse(
        "M3 5h6l2 2h10v12H3V5Z");

    public static readonly Geometry Gear = Geometry.Parse(
        "F0M19.14 12.94a7.07 7.07 0 0 0 0-1.88l2.03-1.58a.5.5 0 0 0 .12-.64l-1.92-3.32a.5.5 0 0 0-.61-.22l-2.39.96a7.03 7.03 0 0 0-1.62-.94l-.36-2.54a.5.5 0 0 0-.5-.42h-3.84a.5.5 0 0 0-.5.42l-.36 2.54c-.59.24-1.13.56-1.62.94l-2.39-.96a.5.5 0 0 0-.61.22L2.65 8.84a.5.5 0 0 0 .12.64l2.03 1.58a7.07 7.07 0 0 0 0 1.88L2.77 14.5a.5.5 0 0 0-.12.64l1.92 3.32c.14.24.42.34.61.22l2.39-.96c.49.38 1.03.7 1.62.94l.36 2.54c.04.24.25.42.5.42h3.84c.25 0 .46-.18.5-.42l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.19.12.47.02.61-.22l1.92-3.32a.5.5 0 0 0-.12-.64l-2.03-1.58ZM12 15.5a3.5 3.5 0 1 1 0-7 3.5 3.5 0 0 1 0 7Z");

    public static readonly Geometry Database = Geometry.Parse(
        "M12 3c4.97 0 9 1.34 9 3s-4.03 3-9 3-9-1.34-9-3 4.03-3 9-3Zm9 5.16v3.84c0 1.66-4.03 3-9 3s-9-1.34-9-3V8.16c1.98.98 5.11 1.59 9 1.59s7.02-.61 9-1.59Zm0 6.31V18c0 1.66-4.03 3-9 3s-9-1.34-9-3v-2.53c1.98.98 5.11 1.59 9 1.59s7.02-.61 9-1.59Z");
}
