#nullable enable
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Coclico.Services;

namespace Coclico.Views;

public partial class AvatarCropWindow : Window
{
    private readonly BitmapImage _sourceBitmap;
    private readonly TranslateTransform _translate = new TranslateTransform();
    private readonly ScaleTransform _scale = new ScaleTransform(1, 1);
    private Point _lastDrag;
    private bool _isDragging;

    public string? ResultFilePath { get; private set; }

    public AvatarCropWindow(string imagePath)
    {
        InitializeComponent();

        _sourceBitmap = new BitmapImage();
        _sourceBitmap.BeginInit();
        _sourceBitmap.UriSource = new Uri(imagePath);
        _sourceBitmap.CacheOption = BitmapCacheOption.OnLoad;
        _sourceBitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        _sourceBitmap.EndInit();

        SrcImage.Source = _sourceBitmap;
        SrcImage.RenderTransform = new TransformGroup { Children = new TransformCollection { _scale, _translate } };

        ImageCanvas.Width = 400;
        ImageCanvas.Height = 400;

        SrcImage.MouseLeftButtonDown += SrcImage_MouseLeftButtonDown;
        SrcImage.MouseLeftButtonUp += SrcImage_MouseLeftButtonUp;
        SrcImage.MouseMove += SrcImage_MouseMove;
        SrcImage.MouseWheel += SrcImage_MouseWheel;
    }

    private void SrcImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _lastDrag = e.GetPosition(this);
        SrcImage.CaptureMouse();
    }

    private void SrcImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        SrcImage.ReleaseMouseCapture();
    }

    private void SrcImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var pos = e.GetPosition(this);
        var dx = pos.X - _lastDrag.X;
        var dy = pos.Y - _lastDrag.Y;
        _translate.X += dx;
        _translate.Y += dy;
        _lastDrag = pos;
    }

    private void SrcImage_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var change = e.Delta > 0 ? 0.05 : -0.05;
        ZoomSlider.Value = Math.Clamp(ZoomSlider.Value + change, ZoomSlider.Minimum, ZoomSlider.Maximum);
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var v = e.NewValue;
        _scale.ScaleX = v;
        _scale.ScaleY = v;
        ZoomLabel.Text = ((int)(v * 100)).ToString() + "%";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ResultFilePath = null;
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            const int outSize = 128;

            double canvasSize = 400;
            double overlaySize = 256;
            double overlayLeft = (canvasSize - overlaySize) / 2.0;
            double overlayTop = (canvasSize - overlaySize) / 2.0;

            double displayedWidth = _sourceBitmap.PixelWidth * _scale.ScaleX;
            double displayedHeight = _sourceBitmap.PixelHeight * _scale.ScaleY;

            var overlayCenter = new Point(overlayLeft + overlaySize / 2.0, overlayTop + overlaySize / 2.0);

            double imgRenderedW = SrcImage.ActualWidth > 0 ? SrcImage.ActualWidth : displayedWidth;
            double imgRenderedH = SrcImage.ActualHeight > 0 ? SrcImage.ActualHeight : displayedHeight;

            double imgLeft = (canvasSize - imgRenderedW) / 2.0 + _translate.X;
            double imgTop = (canvasSize - imgRenderedH) / 2.0 + _translate.Y;

            double relX = overlayLeft - imgLeft;
            double relY = overlayTop - imgTop;

            double srcX = relX / (_scale.ScaleX);
            double srcY = relY / (_scale.ScaleY);
            double srcW = overlaySize / _scale.ScaleX;
            double srcH = overlaySize / _scale.ScaleY;

            int px = (int)Math.Max(0, Math.Floor(srcX));
            int py = (int)Math.Max(0, Math.Floor(srcY));
            int pw = (int)Math.Max(1, Math.Min(_sourceBitmap.PixelWidth - px, Math.Round(srcW)));
            int ph = (int)Math.Max(1, Math.Min(_sourceBitmap.PixelHeight - py, Math.Round(srcH)));

            var cropped = new CroppedBitmap(_sourceBitmap, new Int32Rect(px, py, pw, ph));

            var resized = new TransformedBitmap(cropped, new ScaleTransform((double)outSize / pw, (double)outSize / ph));

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(resized));

            string tmp = Path.Combine(Path.GetTempPath(), "coclico_avatar_" + Guid.NewGuid().ToString("N") + ".png");
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            {
                encoder.Save(fs);
            }

            ResultFilePath = tmp;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AvatarCropWindow.BtnCrop_Click");
            MessageBox.Show($"Erreur lors du recadrage : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
