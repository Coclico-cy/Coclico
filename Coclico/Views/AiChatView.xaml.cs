#nullable enable
using System;
using System.IO;
using System.Management;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Coclico.Services;

namespace Coclico.Views;

public partial class AiChatView : UserControl
{
    public event EventHandler? CloseRequested;
#pragma warning disable CS0067
    public event EventHandler<string>? ActionRequested;
#pragma warning restore CS0067

    private readonly IAiService _service = ServiceContainer.GetRequired<IAiService>();
    private CancellationTokenSource? _inferenceCts;
    private CancellationTokenSource? _downloadCts;
    private bool _isThinking;
    private bool _aiEnabled;
    private long _ramGb;
    private int _cpuCores;
    private long _diskFreeGb;

    private const string ModelDownloadUrl =
        "https://huggingface.co/kiwifrite/Assistant_ia_chat/resolve/main/IA-support-chat.gguf";

    public AiChatView()
    {
        InitializeComponent();
        DevPanel.Visibility       = Visibility.Visible;
        MessagesScroll.Visibility = Visibility.Collapsed;
        InputPanel.Visibility     = Visibility.Collapsed;

        RefreshModelState();

        Loaded += async (_, _) => await LoadPcSpecsAsync();
    }

    private async Task LoadPcSpecsAsync()
    {
        long ramGb = 0;
        int cpuCores = 0;
        long diskFreeGb = 0;

        await Task.Run(() =>
        {
            ramGb      = GetTotalRamGb();
            cpuCores   = Environment.ProcessorCount;
            diskFreeGb = GetDiskFreeGb();
        });

        _ramGb      = ramGb;
        _cpuCores   = cpuCores;
        _diskFreeGb = diskFreeGb;

        DetectAndShowPcSpecs();
    }

    private void DetectAndShowPcSpecs()
    {
        TxtRamValue.Text = $"{_ramGb} Go";
        if (_ramGb >= 32)
            SetBadge(TxtRamValue, RamBadgeBg, "#0F2D1A", "#4ADE80");
        else if (_ramGb >= 16)
            SetBadge(TxtRamValue, RamBadgeBg, "#2D1A0A", "#FCD34D");
        else
            SetBadge(TxtRamValue, RamBadgeBg, "#2D0A0A", "#F87171");

        TxtCpuValue.Text = $"{_cpuCores} c\u0153urs";
        if (_cpuCores >= 8)
            SetBadge(TxtCpuValue, CpuBadgeBg, "#0F2D1A", "#4ADE80");
        else if (_cpuCores >= 4)
            SetBadge(TxtCpuValue, CpuBadgeBg, "#2D1A0A", "#FCD34D");
        else
            SetBadge(TxtCpuValue, CpuBadgeBg, "#2D0A0A", "#F87171");

        TxtDiskValue.Text = $"{_diskFreeGb} Go libres";
        if (_diskFreeGb >= 10)
            SetBadge(TxtDiskValue, DiskBadgeBg, "#0F2D1A", "#4ADE80");
        else if (_diskFreeGb >= 3)
            SetBadge(TxtDiskValue, DiskBadgeBg, "#2D1A0A", "#FCD34D");
        else
            SetBadge(TxtDiskValue, DiskBadgeBg, "#2D0A0A", "#F87171");

        BuildPerfWarning();
    }

    private static void SetBadge(TextBlock tb, SolidColorBrush bg, string bgHex, string fgHex)
    {
        bg.Color = (Color)ColorConverter.ConvertFromString(bgHex);
        tb.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgHex));
    }

    private void BuildPerfWarning()
    {
        bool lowRam  = _ramGb < 16;
        bool lowCpu  = _cpuCores < 4;
        bool lowDisk = _diskFreeGb < 3;

        if (!lowRam && !lowCpu && !lowDisk) return;

        var sb = new StringBuilder();
        if (lowRam)
            sb.AppendLine(
                $"\u26a0\ufe0f RAM insuffisante ({_ramGb} Go d\u00e9tect\u00e9s). L'IA requiert au moins 16 Go \u2014 risque de plantage.");
        if (lowCpu)
            sb.AppendLine($"\u26a0\ufe0f Processeur faible ({_cpuCores} c\u0153urs). L'IA risque d'\u00eatre tr\u00e8s lente.");
        if (lowDisk)
            sb.AppendLine($"\u26a0\ufe0f Espace disque limit\u00e9 ({_diskFreeGb} Go libres). Il faut ~2.5 Go pour t\u00e9l\u00e9charger le mod\u00e8le.");

        var msg = sb.ToString().Trim();
        TxtPerfWarning.Text       = msg;
        WarningPerfCard.Visibility = Visibility.Visible;

        TxtLowSpecBanner.Text    = "Configuration PC insuffisante \u2014 l'IA peut \u00eatre instable.";
        BannerLowSpec.Visibility = Visibility.Visible;

        TxtBtnActivate.Text = "Activer l'IA  (non recommand\u00e9)";
    }

    private static long GetTotalRamGb()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (ManagementObject obj in searcher.Get())
                return (long)((ulong)obj["TotalPhysicalMemory"] / (1024UL * 1024 * 1024));
        }
        catch { }
        return (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024L * 1024 * 1024));
    }

    private static long GetDiskFreeGb()
    {
        try
        {
            var modelDir = Path.GetDirectoryName(AiChatService.ModelPath)
                           ?? AppDomain.CurrentDomain.BaseDirectory;
            var drive = new DriveInfo(Path.GetPathRoot(modelDir) ?? "C:\\");
            return drive.AvailableFreeSpace / (1024L * 1024 * 1024);
        }
        catch { return 999; }
    }

    private void RefreshModelState()
    {
        if (AiChatService.IsModelAvailable)
        {
            DownloadSection.Visibility = Visibility.Collapsed;
            BtnActivateAi.Visibility   = Visibility.Visible;
        }
        else
        {
            DownloadSection.Visibility = Visibility.Visible;
            BtnActivateAi.Visibility   = Visibility.Collapsed;
        }
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (!_aiEnabled)
        {
            TxtAiStatus.Text = "D\u00e9sactiv\u00e9e par d\u00e9faut";
            return;
        }
        TxtAiStatus.Text = _service.IsInitialized
            ? "Pr\u00eate \u2713"
            : (AiChatService.IsModelAvailable ? "En attente\u2026" : "Mod\u00e8le manquant");
    }

    private async void BtnDownloadModel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await BtnDownloadModel_ClickAsync(e);
        }
        catch (Exception ex) { LoggingService.LogException(ex, "AiChatView.BtnDownloadModel_Click"); }
    }

    private async Task BtnDownloadModel_ClickAsync(RoutedEventArgs e)
    {
        if (_diskFreeGb < 3)
        {
            MessageBox.Show(
                $"Espace disque insuffisant ({_diskFreeGb} Go libres).\nIl faut au moins 3 Go pour t\u00e9l\u00e9charger le mod\u00e8le.",
                "Espace insuffisant", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnDownloadModel.IsEnabled   = false;
        BtnCancelDownload.Visibility = Visibility.Visible;
        ProgressContainer.Visibility = Visibility.Visible;
        TxtBtnDownload.Text          = "T\u00e9l\u00e9chargement en cours\u2026";
        TxtDownloadStatus.Text       = "Connexion \u00e0 Hugging Face\u2026";

        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;

        var destPath = AiChatService.ModelPath;
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "Coclico/1.0");
            http.Timeout = Timeout.InfiniteTimeSpan;

            using var response = await http.GetAsync(ModelDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var tmpPath    = destPath + ".tmp";

            using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream    = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer    = new byte[81920];
            long received = 0;
            int  read;
            var  lastUpdate = DateTime.UtcNow;
            long lastBytes  = 0;

            while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                received += read;

                var now = DateTime.UtcNow;
                if ((now - lastUpdate).TotalMilliseconds >= 300)
                {
                    var elapsed  = (now - lastUpdate).TotalSeconds;
                    var speed    = (received - lastBytes) / elapsed;
                    lastUpdate   = now;
                    lastBytes    = received;

                    var pct      = totalBytes > 0 ? (double)received / totalBytes * 100 : 0;
                    var recvMb   = received / (1024.0 * 1024);
                    var totalMb  = totalBytes > 0 ? totalBytes / (1024.0 * 1024) : 0;
                    var speedMb  = speed / (1024.0 * 1024);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        DownloadProgress.Value = pct;
                        TxtDownloadStatus.Text = $"T\u00e9l\u00e9chargement\u2026 {pct:F1}%";
                        TxtDownloadSpeed.Text  = $"{speedMb:F1} Mo/s";
                        TxtDownloadBytes.Text  = totalBytes > 0
                            ? $"{recvMb:F0} Mo / {totalMb:F0} Mo"
                            : $"{recvMb:F0} Mo";
                    });
                }
            }

            await fileStream.FlushAsync(ct);
            fileStream.Close();

            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(tmpPath, destPath);

            await Dispatcher.InvokeAsync(() =>
            {
                DownloadProgress.Value       = 100;
                TxtDownloadStatus.Text       = "T\u00e9l\u00e9chargement termin\u00e9 !";
                TxtDownloadSpeed.Text        = "";
                TxtDownloadBytes.Text        = "";
                BtnCancelDownload.Visibility = Visibility.Collapsed;

                RefreshModelState();
            });
        }
        catch (OperationCanceledException)
        {
            var tmpPath = destPath + ".tmp";
            if (File.Exists(tmpPath)) try { File.Delete(tmpPath); } catch { }

            await Dispatcher.InvokeAsync(() =>
            {
                ProgressContainer.Visibility = Visibility.Collapsed;
                BtnCancelDownload.Visibility = Visibility.Collapsed;
                BtnDownloadModel.IsEnabled   = true;
                TxtBtnDownload.Text          = "T\u00e9l\u00e9charger le mod\u00e8le IA";
            });
        }
        catch (Exception ex)
        {
            var tmpPath = destPath + ".tmp";
            if (File.Exists(tmpPath)) try { File.Delete(tmpPath); } catch { }

            await Dispatcher.InvokeAsync(() =>
            {
                TxtDownloadStatus.Text       = $"Erreur : {ex.Message}";
                TxtDownloadSpeed.Text        = "";
                BtnCancelDownload.Visibility = Visibility.Collapsed;
                BtnDownloadModel.IsEnabled   = true;
                TxtBtnDownload.Text          = "R\u00e9essayer";
            });
        }
        finally
        {
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    private void BtnCancelDownload_Click(object sender, RoutedEventArgs e)
    {
        _downloadCts?.Cancel();
    }

    private void BtnActivateAi_Click(object sender, RoutedEventArgs e)
    {
        bool lowSpec = _ramGb < 16 || _cpuCores < 4;
        string warning = lowSpec
            ? $"\n\u26a0\ufe0f ATTENTION : Votre PC a {_ramGb} Go de RAM et {_cpuCores} c\u0153urs CPU.\n" +
              "Des ralentissements importants ou un crash m\u00e9moire sont possibles.\n"
            : "\n\u2705 Votre configuration semble compatible.\n";

        var result = MessageBox.Show(
            "Coclico AI tourne enti\u00e8rement en local sur votre PC.\n" +
            warning +
            "\nCONFIGURATION REQUISE :\n" +
            "  \u2022 16 Go de RAM minimum (32 Go recommand\u00e9)\n" +
            "  \u2022 Processeur r\u00e9cent (2018 ou +), 4 c\u0153urs min.\n" +
            "  \u2022 ~2.5 Go d'espace disque libre\n\n" +
            "Voulez-vous activer l'IA ?",
            "Coclico AI \u2014 Confirmation",
            MessageBoxButton.YesNo,
            lowSpec ? MessageBoxImage.Warning : MessageBoxImage.Information);

        if (result != MessageBoxResult.Yes) return;

        _aiEnabled = true;
        DevBadge.Visibility       = Visibility.Collapsed;
        DevPanel.Visibility       = Visibility.Collapsed;
        MessagesScroll.Visibility = Visibility.Visible;
        InputPanel.Visibility     = Visibility.Visible;
        UpdateStatus();
    }

    private void SetThinking(bool thinking)
    {
        _isThinking = thinking;
        BtnSend.IsEnabled  = !thinking;
        TxtInput.IsEnabled = !thinking;

        if (thinking)
        {
            TxtAiStatus.Text = "R\u00e9flexion en cours\u2026";
            TypingBubble.Visibility = Visibility.Visible;

            var pulse = new DoubleAnimation(0.35, 1.0, TimeSpan.FromMilliseconds(800))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            TxtReflecting.BeginAnimation(UIElement.OpacityProperty, pulse);
            ScrollToBottom();
        }
        else
        {
            TypingBubble.Visibility = Visibility.Collapsed;
            TxtReflecting.BeginAnimation(UIElement.OpacityProperty, null);
            UpdateStatus();
        }
    }

    private void AddUserBubble(string text)
    {
        var bubble = new Border
        {
            Style = (Style)Resources["UserBubble"],
            Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = Brushes.White, LineHeight = 18 }
        };
        MessagesPanel.Children.Insert(MessagesPanel.Children.Count - 1, bubble);
        ScrollToBottom();
    }

    private TextBlock AddAiBubble()
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(0xC4, 0xB5, 0xFD)), LineHeight = 18 };
        var bubble = new Border { Style = (Style)Resources["AiBubble"], Child = tb };
        MessagesPanel.Children.Insert(MessagesPanel.Children.Count - 1, bubble);
        return tb;
    }

    private void ScrollToBottom() => MessagesScroll.ScrollToEnd();

    private async void BtnSend_Click(object sender, RoutedEventArgs e)
    {
        try { await SendAsync(); }
        catch (Exception ex) { LoggingService.LogException(ex, "AiChatView.BtnSend_Click"); }
    }

    private async void TxtInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_isThinking)
        {
            e.Handled = true;
            try { await SendAsync(); }
            catch (Exception ex) { LoggingService.LogException(ex, "AiChatView.TxtInput_KeyDown"); }
        }
    }

    private async Task SendAsync()
    {
        var text = TxtInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(text) || !AiChatService.IsModelAvailable) return;

        TxtInput.Clear();
        AddUserBubble(text);
        SetThinking(true);

        _inferenceCts?.Cancel();
        _inferenceCts?.Dispose();
        _inferenceCts = new CancellationTokenSource();
        var ct = _inferenceCts.Token;

        TextBlock? aiBlock = null;
        var sb = new StringBuilder();

        try
        {
            if (!_service.IsInitialized) TxtAiStatus.Text = "Chargement du mod\u00e8le\u2026";

            await foreach (var token in _service.SendMessageAsync(text, ct))
            {
                sb.Append(token);
                var snap = AiActionParser.Clean(sb.ToString());

                await Dispatcher.InvokeAsync(() =>
                {
                    if (TypingBubble.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(snap))
                    {
                        TypingBubble.Visibility = Visibility.Collapsed;
                        TxtReflecting.BeginAnimation(UIElement.OpacityProperty, null);
                        aiBlock = AddAiBubble();
                    }

                    if (aiBlock != null) aiBlock.Text = snap;

                    ScrollToBottom();
                });
            }

            var finalText = AiActionParser.Clean(sb.ToString());

            await Dispatcher.InvokeAsync(() =>
            {
                if (aiBlock != null) aiBlock.Text = finalText;
                SetThinking(false);
                ScrollToBottom();
            });

            _service.RecordExchange(text, finalText);

            _ = Task.Run(() =>
            {
                try
                {
                    MemoryCleanerService.ForceGcCollect();
                    MemoryCleanerService.EmptyWorkingSets();
                    MemoryCleanerService.FlushModifiedFileCache();
                }
                catch { }
            });
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.InvokeAsync(() => { aiBlock?.Text += " [annul\u00e9]"; SetThinking(false); });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                var errBlock = AddAiBubble();
                errBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                errBlock.Text = $"Erreur : {ex.Message}";
                SetThinking(false);
            });
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        if (_isThinking)
        {
            _inferenceCts?.Cancel();
            SetThinking(false);
        }

        for (int i = MessagesPanel.Children.Count - 2; i >= 1; i--)
            MessagesPanel.Children.RemoveAt(i);

        _service.ResetConversation();
        TxtInput.Clear();
        UpdateStatus();
        ScrollToBottom();
    }
}
