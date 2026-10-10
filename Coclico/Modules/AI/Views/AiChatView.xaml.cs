using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Coclico.Services;
using Coclico.Services.AI;

namespace Coclico.Views;

public partial class AiChatView : UserControl
{
    private sealed record ProviderOption(AiProviderType Provider, string Label);

    public event EventHandler? CloseRequested;

    public event EventHandler<string>? ActionRequested;

    private readonly IAiService _aiService;

    private readonly AiToolExecutionService _toolService;

    private AttachedImage? _attachment;

    private CancellationTokenSource? _inferenceCts;

    private bool _isThinking;

    private bool _hardwareChecked;

    private readonly LocalizationService? _loc;

    public AiChatView()
    {
        InitializeComponent();
        _aiService = ServiceContainer.GetRequired<IAiService>();
        _toolService = ServiceContainer.GetRequired<AiToolExecutionService>();
        _toolService.NavigationRequested += (_, route) => ActionRequested?.Invoke(this, route);
        _loc = ServiceContainer.GetOptional<LocalizationService>();
        if (_loc != null)
        {
            _loc.LanguageChanged += OnLanguageChanged;
        }

        Unloaded += (_, _) =>
        {
            if (_loc != null)
            {
                _loc.LanguageChanged -= OnLanguageChanged;
            }
        };
        PopulateProviders();
        UpdateMasterToggleUi();
        UpdateHardwareToggleUi();
        UpdateAutoExecUI();
        UpdateStatus();
        Loaded += OnViewLoaded;
    }

    private void OnLanguageChanged(string? lang)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            UpdateMasterToggleUi();
            UpdateHardwareToggleUi();
            UpdateAutoExecUI();
            UpdateStatus();
        });
    }

    private async void OnViewLoaded(object sender, RoutedEventArgs e)
    {
        if (_hardwareChecked)
        {
            return;
        }
        _hardwareChecked = true;
        AiGpuVendor vendor = await Task.Run(AiHardwareService.DetectVendor).ConfigureAwait(true);
        bool cudaReady = await Task.Run(AiHardwareService.IsCudaRuntimeAvailable).ConfigureAwait(true);
        await ApplyHardwareEnvironmentAsync(vendor, cudaReady);
    }

    private async Task ApplyHardwareEnvironmentAsync(AiGpuVendor vendor, bool cudaReady)
    {
        if (_aiService.CurrentProvider != AiProviderType.LocalGGUF || !_aiService.IsEnabled)
        {
            return;
        }

        if (vendor == AiGpuVendor.Amd)
        {
            if (_aiService.UseGpu)
            {
                MessageBox.Show(
                    "Coclico a détecté une carte graphique AMD (" + AiHardwareService.GetGpuDisplayName() + ")." + Environment.NewLine + Environment.NewLine +
                    "Il n'existe pas de runtime d'accélération IA officiel pour AMD sur Windows (ROCm est réservé à Linux)." + Environment.NewLine +
                    "Le Copilot s'exécute donc sur le processeur (AVX2), sans consommer de VRAM.",
                    "Coclico Copilot — Carte AMD détectée",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                await _aiService.SetHardwareModeAsync(false);
                UpdateHardwareToggleUi();
                UpdateStatus();
            }
            return;
        }

        if (vendor == AiGpuVendor.Nvidia && _aiService.UseGpu && !cudaReady)
        {
            MessageBoxResult choice = MessageBox.Show(
                "Coclico a détecté une carte NVIDIA (" + AiHardwareService.GetGpuDisplayName() + ") mais le runtime CUDA 12 est absent : le Copilot s'exécute actuellement sur le processeur (lent)." + Environment.NewLine + Environment.NewLine +
                "Télécharger et installer le runtime CUDA maintenant (environ 1,4 Go) ?",
                "Coclico Copilot — Runtime CUDA manquant",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (choice == MessageBoxResult.Yes)
            {
                await DownloadCudaRuntimeFromChatAsync();
            }
        }
    }

    private async Task DownloadCudaRuntimeFromChatAsync()
    {
        SetThinking(true);
        LocalDownloadProgress.Visibility = Visibility.Visible;
        LocalDownloadProgress.IsIndeterminate = false;
        LocalDownloadProgress.Minimum = 0;
        LocalDownloadProgress.Maximum = 100;
        var progress = new Progress<AiCudaProgress>(p =>
        {
            LocalDownloadProgress.Value = Math.Max(0, Math.Min(100, p.Percent));
            TxtBannerModelNotice.Text = p.Message;
        });
        try
        {
            await AiHardwareService.DownloadCudaRuntimeAsync(progress, CancellationToken.None);
            ToastService.Show("✅ Runtime CUDA installé avec succès.");
            MessageBox.Show(
                "Runtime CUDA installé avec succès. Redémarrez Coclico pour activer l'accélération GPU (VRAM).",
                "Coclico Copilot — CUDA installé",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ToastService.Show($"Échec de l'installation du runtime CUDA : {ex.Message}");
            MessageBox.Show(
                "Échec de l'installation du runtime CUDA :" + Environment.NewLine + ex.Message,
                "Coclico Copilot — Installation CUDA",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            LocalDownloadProgress.Visibility = Visibility.Collapsed;
            SetThinking(false);
            UpdateStatus();
        }
    }

    private void PopulateProviders()
    {
        CmbProvider.ItemsSource = Enum.GetValues<AiProviderType>()
            .Select(provider => new ProviderOption(
                provider,
                provider == AiProviderType.LocalGGUF ? "Local model (GGUF)" : "Ollama (local)"))
            .ToList();
        CmbProvider.SelectedValue = _aiService.CurrentProvider;
        RefreshModelsForCurrentProvider();
    }

    private void RefreshModelsForCurrentProvider()
    {
        IReadOnlyList<AiModelInfo> models = AiModelCatalog.GetModelsForProvider(_aiService.CurrentProvider);
        CmbModel.ItemsSource = models.Select(m => m.DisplayName).ToList();
        AiModelInfo? current = models.FirstOrDefault(m => m.Id == _aiService.CurrentModel);
        if (current == null || (_aiService.CurrentProvider == AiProviderType.LocalGGUF && !((_aiService as AiChatService)?.IsModelDownloaded(current.Id) ?? false)))
        {
            AiModelInfo? downloaded = models.FirstOrDefault(m => (_aiService as AiChatService)?.IsModelDownloaded(m.Id) == true);
            if (downloaded != null)
            {
                current = downloaded;
                _aiService.CurrentModel = current.Id;
            }
        }
        current ??= models.FirstOrDefault();
        if (current != null)
        {
            CmbModel.SelectedItem = current.DisplayName;
        }
        UpdateBannerState();
    }

    private void UpdateBannerState()
    {
        if (_aiService.CurrentProvider == AiProviderType.LocalGGUF)
        {
            AiModelInfo modelInfo = AiModelCatalog.AllModels.FirstOrDefault(m => m.Id == _aiService.CurrentModel && m.Provider == AiProviderType.LocalGGUF)
                            ?? AiModelCatalog.GetDefaultModel(AiProviderType.LocalGGUF);
            bool isAvail = (_aiService as AiChatService)?.IsModelDownloaded(modelInfo.Id) ?? false;
            BannerDownloadLocal.Visibility = isAvail ? Visibility.Collapsed : Visibility.Visible;
            if (!isAvail)
            {
                TxtBannerModelNotice.Text = string.Format(
                    TryFindResource("Ai_BannerModelMissing") as string ?? "Modèle local '{0}' non présent sur le disque (~{1} Mo).",
                    modelInfo.DisplayName, modelInfo.SizeEstimateMb);
            }
        }
        else
        {
            BannerDownloadLocal.Visibility = Visibility.Collapsed;
        }
    }

    private async void CmbProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbProvider.SelectedValue is AiProviderType provider && provider != _aiService.CurrentProvider)
        {
            await _aiService.SwitchProviderAsync(provider);
            RefreshModelsForCurrentProvider();
            UpdateHardwareToggleUi();
            UpdateStatus();
        }
    }

    private async void CmbModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbModel.SelectedItem is string displayName)
        {
            IReadOnlyList<AiModelInfo> models = AiModelCatalog.GetModelsForProvider(_aiService.CurrentProvider);
            AiModelInfo? match = models.FirstOrDefault(m => m.DisplayName == displayName);
            if (match != null && match.Id != _aiService.CurrentModel)
            {
                await _aiService.SwitchProviderAsync(_aiService.CurrentProvider, match.Id);
                UpdateStatus();
                UpdateBannerState();
            }
        }
    }

    private void UpdateStatus()
    {
        if (!_aiService.IsEnabled)
        {
            TxtStatusSubtitle.Text = "En veille (0 Mo RAM/VRAM)";
            UpdateHardwareToggleUi();
            return;
        }
        AiProviderType prov = _aiService.CurrentProvider;
        string model = _aiService.CurrentModel;
        TxtStatusSubtitle.Text = prov == AiProviderType.LocalGGUF
            ? (_aiService.IsInitialized
                ? $"Local GGUF ({(_aiService.UseGpu ? "GPU VRAM" : "CPU")}) — Actif"
                : $"Local GGUF ({(_aiService.UseGpu ? "GPU VRAM" : "CPU")}) — Prêt")
            : $"{prov} — {model}";
        UpdateHardwareToggleUi();
    }

    private void UpdateMasterToggleUi()
    {
        bool isEnabled = _aiService.IsEnabled;
        DotMasterStatus.Background = isEnabled
            ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E))
            : new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
        TxtMasterStatus.Text = isEnabled
            ? (_loc?.Get("Ai_Master_Active") ?? "Activé")
            : (_loc?.Get("Ai_Master_Standby") ?? "En veille");
        BannerDisabledState.Visibility = isEnabled ? Visibility.Collapsed : Visibility.Visible;
        TxtInput.IsEnabled = isEnabled && !_isThinking;
        BtnSend.IsEnabled = isEnabled && !_isThinking;
    }

    private void UpdateHardwareToggleUi()
    {
        bool useGpu = _aiService.UseGpu;
        TxtHardwareIcon.Text = useGpu ? "⚡" : "💻";
        TxtHardwareMode.Text = useGpu ? "GPU (VRAM)" : "CPU pur";
        HardwarePill.Visibility = _aiService.CurrentProvider == AiProviderType.LocalGGUF
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void BtnMasterToggle_Click(object sender, RoutedEventArgs e)
    {
        bool newEnabled = !_aiService.IsEnabled;
        await _aiService.SetEnabledAsync(newEnabled);
        UpdateMasterToggleUi();
        UpdateStatus();
        if (newEnabled)
        {
            ToastService.Show("🟢 Copilot activé et opérationnel.");
        }
        else
        {
            ToastService.Show("💤 Copilot en veille. Mémoire vive déchargée (0 Mo).");
        }
    }

    private void BtnWakeUp_Click(object sender, RoutedEventArgs e)
    {
        BtnMasterToggle_Click(sender, e);
    }

    private async void BtnHardwareToggle_Click(object sender, RoutedEventArgs e)
    {
        AiGpuVendor vendor = await Task.Run(AiHardwareService.DetectVendor).ConfigureAwait(true);
        if (vendor != AiGpuVendor.Nvidia && !_aiService.UseGpu)
        {
            MessageBox.Show(
                "Aucune carte NVIDIA détectée : l'accélération GPU n'est pas disponible pour le Copilot (les cartes AMD ne disposent d'aucun runtime IA officiel sur Windows). L'exécution reste sur le processeur (AVX2).",
                "Coclico Copilot — Accélération GPU",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        bool newGpu = !_aiService.UseGpu;
        await _aiService.SetHardwareModeAsync(newGpu);
        UpdateHardwareToggleUi();
        UpdateStatus();
        ToastService.Show(newGpu
            ? "⚡ Accélération GPU activée (VRAM RTX/GTX dédiée)."
            : "💻 Mode CPU pur activé (0 Mo VRAM, charge GPU nulle).");
    }

    private async void BtnUnloadModel_Click(object sender, RoutedEventArgs e)
    {
        await _aiService.UnloadAsync();
        UpdateStatus();
        ToastService.Show("Modèle local déchargé. Mémoire vive libérée (0 Mo).");
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        if (_isThinking)
        {
            _inferenceCts?.Cancel();
            SetThinking(false);
        }
        for (int i = MessagesPanel.Children.Count - 2; i >= 1; i--)
        {
            MessagesPanel.Children.RemoveAt(i);
        }
        _aiService.ResetConversation();
        TxtInput.Clear();
        RemoveAttachment();
        UpdateStatus();
        ScrollToBottom();
    }

    private async void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
        await _aiService.UnloadAsync();
        UpdateStatus();
    }

    private void SetAttachment(AttachedImage img)
    {
        _attachment = img;
        ImgAttachedThumb.Source = img.PreviewSource;
        TxtAttachedName.Text = img.FileName;
        AttachedImageBadge.Visibility = Visibility.Visible;
    }

    private void RemoveAttachment()
    {
        _attachment = null;
        AttachedImageBadge.Visibility = Visibility.Collapsed;
        ImgAttachedThumb.Source = null;
    }

    private void BtnRemoveAttachment_Click(object sender, RoutedEventArgs e)
    {
        RemoveAttachment();
    }

    private void BtnCaptureScreen_Click(object sender, RoutedEventArgs e)
    {
        AttachedImage? img = AiVisionService.CaptureScreen();
        if (img != null)
        {
            SetAttachment(img);
        }
        else
        {
            ToastService.Show("Impossible de capturer l'écran.");
        }
    }

    private void BtnPasteImage_Click(object sender, RoutedEventArgs e)
    {
        AttachedImage? img = AiVisionService.FromClipboard();
        if (img != null)
        {
            SetAttachment(img);
        }
        else
        {
            ToastService.Show("Aucune image trouvée dans le presse-papier.");
        }
    }

    private void UserControl_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                AttachedImage? img = AiVisionService.FromFilePath(files[0]);
                if (img != null)
                {
                    SetAttachment(img);
                }
            }
        }
    }

    private async Task ExecuteDirectChipActionAsync(AiActionRequest action, string promptText)
    {
        if (_isThinking || !_aiService.IsEnabled)
        {
            return;
        }
        TxtInput.Clear();
        RemoveAttachment();
        AddUserBubble(promptText);
        SetThinking(true);
        string confirmMsg = _toolService.GetDefaultActionConfirmation(action);
        TextBlock aiBlock = AddAiBubble();
        aiBlock.Text = confirmMsg;
        Border? card = null;
        _ = await Dispatcher.InvokeAsync(() => card = AddActionCard(action));
        try
        {
            AiActionResult result = await _toolService.ExecuteActionAsync(action with { Confirmed = true });
            await Dispatcher.InvokeAsync(() =>
            {
                if (card != null)
                {
                    UpdateActionCard(card, result);
                }
                ToastService.Show(result.Message);
            });
            _aiService.RecordExchange(promptText, confirmMsg);
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (card != null)
                {
                    UpdateActionCard(card, new AiActionResult(false, $"Erreur : {ex.Message}"));
                }
                ToastService.Show($"Erreur : {ex.Message}");
            });
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                SetThinking(false);
                ScrollToBottom();
            });
        }
    }

    private async void ChipCleanRam_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteDirectChipActionAsync(new AiActionRequest("CLEAN_RAM", null, "[ACTION:CLEAN_RAM]"), "⚡ Nettoyer la mémoire vive (RAM)");
    }

    private async void ChipCleanTemp_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteDirectChipActionAsync(new AiActionRequest("CLEAN_TEMP", null, "[ACTION:CLEAN_TEMP]"), "🧹 Nettoyer les fichiers temporaires");
    }

    private async void ChipScanSfc_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteDirectChipActionAsync(new AiActionRequest("SCAN_SFC", null, "[ACTION:SCAN_SFC]"), "🔍 Lancer un scan d'intégrité système (SFC)");
    }

    private async void ChipScanDism_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteDirectChipActionAsync(new AiActionRequest("SCAN_DISM", null, "[ACTION:SCAN_DISM]"), "🛠️ Réparer l'image Windows (DISM)");
    }

    private async void ChipScanDefender_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteDirectChipActionAsync(new AiActionRequest("DEFENDER_SCAN", null, "[ACTION:DEFENDER_SCAN]"), "🛡️ Lancer l'antivirus Windows Defender");
    }

    private async void ChipDiagnose_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteDirectChipActionAsync(new AiActionRequest("DIAGNOSE_SYSTEM", null, "[ACTION:DIAGNOSE_SYSTEM]"), "📊 Diagnostic complet des performances");
    }

    private void ChipsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scv)
        {
            if (e.Delta < 0)
            {
                scv.LineRight();
            }
            else
            {
                scv.LineLeft();
            }
            e.Handled = true;
        }
    }

    private void TxtInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtInputPlaceholder.Visibility = string.IsNullOrEmpty(TxtInput.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void TxtInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_isThinking)
        {
            e.Handled = true;
            await SendAsync();
        }
        else if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            AttachedImage? img = AiVisionService.FromClipboard();
            if (img != null)
            {
                e.Handled = true;
                SetAttachment(img);
            }
        }
    }

    private async void BtnSend_Click(object sender, RoutedEventArgs e)
    {
        await SendAsync();
    }

    private async Task SendAsync()
    {
        string text = TxtInput.Text.Trim();
        AttachedImage? attach = _attachment;
        if (string.IsNullOrWhiteSpace(text) && attach == null)
        {
            return;
        }
        if (attach != null)
        {
            IReadOnlyList<AiModelInfo> models = AiModelCatalog.GetModelsForProvider(_aiService.CurrentProvider);
            AiModelInfo currentModelInfo = models.FirstOrDefault(m => m.Id == _aiService.CurrentModel)
                                   ?? AiModelCatalog.GetDefaultModel(_aiService.CurrentProvider);
            if (!currentModelInfo.SupportsVision)
            {
                ToastService.Show("Le modèle actuel ne prend pas en charge les images. Sélectionnez Ollama avec un modèle de vision, par exemple Llama 3.2 Vision.");
                return;
            }
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            text = "Analyse cette image pour moi.";
        }
        TxtInput.Clear();
        RemoveAttachment();
        AddUserBubble(text, attach?.PreviewSource);
        SetThinking(true);
        _inferenceCts?.Cancel();
        _inferenceCts?.Dispose();
        _inferenceCts = new CancellationTokenSource();
        CancellationToken ct = _inferenceCts.Token;
        if (attach == null)
        {
            List<AiActionRequest> preDetected = _toolService.DetectUserIntentActions(text);
            if (AiToolExecutionService.IsDirectActionCommand(text, preDetected))
            {
                var distinctPreActions = preDetected
                    .GroupBy(a => AiToolExecutionService.NormalizeActionType(a.ActionType))
                    .Select(g => g.First())
                    .ToList();
                string confirmMsg = _toolService.GetDefaultActionConfirmation(distinctPreActions[0]);
                TextBlock directAiBlock = AddAiBubble();
                directAiBlock.Text = confirmMsg;
                try
                {
                    foreach (AiActionRequest action in distinctPreActions)
                    {
                        Border? card = null;
                        _ = await Dispatcher.InvokeAsync(() => card = AddActionCard(action));
                        AiActionResult result = await _toolService.ExecuteActionAsync(action with { Confirmed = true }, ct);
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (card != null)
                            {
                                UpdateActionCard(card, result);
                            }
                        });
                        ToastService.Show(result.Message);
                    }
                    _aiService.RecordExchange(text, confirmMsg);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        TextBlock errBlock = AddAiBubble();
                        errBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                        errBlock.Text = $"Erreur : {ex.Message}";
                    });
                }
                finally
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        SetThinking(false);
                        ScrollToBottom();
                    });
                }
                return;
            }
        }
        TextBlock? aiBlock = null;
        var sb = new StringBuilder();
        try
        {
            await foreach (string token in _aiService.SendMessageWithVisionAsync(
                text,
                attach?.Base64Data,
                attach?.MimeType,
                ct))
            {
                _ = sb.Append(token);
                string snap = _toolService.SanitizeStreamingText(_toolService.StripActionTags(AiActionParser.Clean(sb.ToString())));
                await Dispatcher.InvokeAsync(() =>
                {
                    if (TypingBubble.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(snap))
                    {
                        TypingBubble.Visibility = Visibility.Collapsed;
                        aiBlock = AddAiBubble();
                    }
                    aiBlock?.Text = snap;
                    ScrollToBottom();
                });
            }
            string fullRawResponse = sb.ToString();
            List<AiActionRequest> actions = _toolService.ExtractActions(fullRawResponse);
            if (actions.Count == 0 && !AiToolExecutionService.IsCasualOrGreeting(text) && !text.Contains('?'))
            {
                List<AiActionRequest> detected = _toolService.DetectUserIntentActions(text);
                actions.AddRange(detected);
            }
            var distinctActions = actions
                .GroupBy(a => AiToolExecutionService.NormalizeActionType(a.ActionType))
                .Select(g => g.First())
                .ToList();
            string cleanAiText = distinctActions.Count > 0
                ? _toolService.SanitizeActionResponse(fullRawResponse, distinctActions)
                : _toolService.StripActionTags(AiActionParser.Clean(fullRawResponse));
            await Dispatcher.InvokeAsync(() =>
            {
                if (!string.IsNullOrWhiteSpace(cleanAiText))
                {
                    aiBlock ??= AddAiBubble();
                    aiBlock.Text = cleanAiText;
                }
                ScrollToBottom();
            });
            _aiService.RecordExchange(text, cleanAiText);
            if (_toolService.ShouldExecuteActions(text, distinctActions))
            {
                foreach (AiActionRequest proposedAction in distinctActions)
                {
                    Border? card = null;
                    bool confirmed = true;
                    if (!_autoExecuteActions && AiToolExecutionService.RequiresConfirmation(proposedAction.ActionType))
                    {
                        var tcs = new TaskCompletionSource<bool>();
                        _ = await Dispatcher.InvokeAsync(() => card = AddActionConfirmationCard(proposedAction, tcs));
                        using CancellationTokenRegistration reg = ct.Register(() => tcs.TrySetCanceled());
                        try
                        {
                            confirmed = await tcs.Task;
                        }
                        catch (OperationCanceledException)
                        {
                            confirmed = false;
                        }
                        if (!confirmed)
                        {
                            ToastService.ShowInfo("Action refusée par l'utilisateur.");
                            continue;
                        }
                    }
                    else
                    {
                        _ = await Dispatcher.InvokeAsync(() => card = AddActionCard(proposedAction));
                    }
                    AiActionRequest action = proposedAction with { Confirmed = true };
                    AiActionResult result = await _toolService.ExecuteActionAsync(action, ct);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (card != null)
                        {
                            UpdateActionCard(card, result);
                        }
                    });
                    ToastService.Show(result.Message);
                }
            }
            await Dispatcher.InvokeAsync(() =>
            {
                SetThinking(false);
                ScrollToBottom();
            });
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                aiBlock?.Text += " [annulé]";
                SetThinking(false);
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                TextBlock errBlock = AddAiBubble();
                errBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                errBlock.Text = $"Erreur : {ex.Message}";
                SetThinking(false);
            });
        }
    }

    private void SetThinking(bool thinking)
    {
        _isThinking = thinking;
        BtnSend.IsEnabled = !thinking && _aiService.IsEnabled;
        TxtInput.IsEnabled = !thinking && _aiService.IsEnabled;
        TypingBubble.Visibility = thinking ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatus();
        if (thinking)
        {
            ScrollToBottom();
        }
    }

    private void AddUserBubble(string text, BitmapSource? image = null)
    {
        var sp = new StackPanel();
        if (image != null)
        {
            var imgControl = new Image
            {
                Source = image,
                MaxWidth = 260,
                MaxHeight = 180,
                Margin = new Thickness(0, 0, 0, 6),
                Stretch = Stretch.Uniform
            };
            _ = sp.Children.Add(imgControl);
        }
        _ = sp.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = Brushes.White,
            LineHeight = 18
        });
        var bubble = new Border
        {
            Style = (Style)Resources["UserBubble"],
            Child = sp
        };
        MessagesPanel.Children.Insert(MessagesPanel.Children.Count - 1, bubble);
        ScrollToBottom();
    }

    private TextBlock AddAiBubble()
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
            LineHeight = 18
        };
        var bubble = new Border { Style = (Style)Resources["AiBubble"], Child = tb };
        MessagesPanel.Children.Insert(MessagesPanel.Children.Count - 1, bubble);
        return tb;
    }

    private void ScrollToBottom()
    {
        MessagesScroll.ScrollToEnd();
    }

    private async void BtnDownloadGguf_Click(object sender, RoutedEventArgs e)
    {
        AiModelInfo modelInfo = AiModelCatalog.AllModels.FirstOrDefault(m => m.Id == _aiService.CurrentModel && m.Provider == AiProviderType.LocalGGUF)
                        ?? AiModelCatalog.GetDefaultModel(AiProviderType.LocalGGUF);
        string? url = modelInfo.DownloadUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            ToastService.Show("Aucune URL de téléchargement disponible pour ce modèle.");
            return;
        }
        string dest = (_aiService as AiChatService)?.GetLocalModelFullPath(modelInfo.Id)
                   ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resource", "model", modelInfo.Id);
        string? dir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(dir))
        {
            _ = Directory.CreateDirectory(dir);
        }
        BtnDownloadGguf.IsEnabled = false;
        LocalDownloadProgress.Visibility = Visibility.Visible;
        LocalDownloadProgress.IsIndeterminate = true;
        string tmp = dest + ".tmp";
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromHours(2);
            using HttpResponseMessage resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            _ = resp.EnsureSuccessStatusCode();
            long? totalBytes = resp.Content.Headers.ContentLength;
            LocalDownloadProgress.IsIndeterminate = !totalBytes.HasValue;
            if (totalBytes.HasValue)
            {
                LocalDownloadProgress.Minimum = 0;
                LocalDownloadProgress.Maximum = 100;
            }
            using Stream s = await resp.Content.ReadAsStreamAsync();
            using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            byte[] buffer = new byte[81920];
            long totalRead = 0;
            int read;
            DateTime lastUiUpdate = DateTime.MinValue;
            while ((read = await s.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, read));
                totalRead += read;
                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    DateTime now = DateTime.UtcNow;
                    if ((now - lastUiUpdate).TotalMilliseconds > 200 || totalRead == totalBytes.Value)
                    {
                        lastUiUpdate = now;
                        double pct = (double)totalRead / totalBytes.Value * 100.0;
                        LocalDownloadProgress.Value = pct;
                        TxtBannerModelNotice.Text = $"Téléchargement de {modelInfo.DisplayName} : {pct:F0}% ({totalRead / (1024 * 1024)} Mo / {totalBytes.Value / (1024 * 1024)} Mo)";
                    }
                }
            }
            fs.Close();
            if (File.Exists(dest))
            {
                File.Delete(dest);
            }
            File.Move(tmp, dest);
            BannerDownloadLocal.Visibility = Visibility.Collapsed;
            ToastService.Show($"Modèle {modelInfo.DisplayName} téléchargé avec succès !");
            UpdateStatus();
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch (Exception cleanupEx)
            {
                LoggingService.LogException(cleanupEx, "AiChatView.DownloadCleanup");
            }
            ToastService.Show($"Erreur téléchargement : {ex.Message}");
            UpdateBannerState();
        }
        finally
        {
            LocalDownloadProgress.Visibility = Visibility.Collapsed;
            BtnDownloadGguf.IsEnabled = true;
        }
    }
}
