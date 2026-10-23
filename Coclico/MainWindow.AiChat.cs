using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Coclico.Services;
namespace Coclico;

public partial class MainWindow
{
    private void BtnAiChat_Click(object sender, RoutedEventArgs e)
    {
        ToggleChatPanel(!_isChatOpen);
    }

    private void AiChatPanel_CloseRequested(object? sender, EventArgs e)
    {
        ToggleChatPanel(false);
    }

    private void AiChatPanel_ActionRequested(object? sender, string action)
        => AsyncEventSafe.Run(() => AiChatPanel_ActionRequestedAsync(sender, action), "MainWindow.AiChatPanel_ActionRequested");

    private async Task AiChatPanel_ActionRequestedAsync(object? sender, string action)
    {
        try
        {
            ToggleChatPanel(false);

            string? targetTag = TagForAiAction(action);
            if (targetTag != null)
            {
                RecordStigmergyTransition(targetTag);
            }

            UIElement? target = targetTag == null ? null : GetOrCreateView(targetTag);

            ServiceContainer.GetRequired<IAiService>().CurrentStatusContext = action switch
            {
                "open_dashboard" => "L'utilisateur est sur le Tableau de Bord (stats CPU/RAM/Disque).",
                "open_programs" => "L'utilisateur consulte la liste des Applications installées.",
                "open_disk" => "L'utilisateur est sur l'analyse de l'espace disque (carte radiale des zones du disque dur).",
                "open_health" or "open_defense" => "L'utilisateur est sur le module Santé, Défense & Réparation Windows.",
                "open_network" => "L'utilisateur consulte le module Réseau & Latence.",
                "open_power" => "L'utilisateur est dans le module CPU & Alimentation (plans Coclico économie/optimal/performance, mode adaptatif).",
                "open_installer" => "L'utilisateur est dans l'Installeur Winget.",
                "open_cleaning" => "L'utilisateur est dans le Nettoyage système.",
                "open_ramcleaner" => "L'utilisateur est dans le RAM Cleaner.",
                "open_settings" => "L'utilisateur est dans les Paramètres.",
                "open_help" => "L'utilisateur consulte l'Aide et le support.",
                _ => ServiceContainer.GetRequired<IAiService>().CurrentStatusContext
            };

            FindNavButton(targetTag)?.IsChecked = true;

            if (target != null)
            {
                await NavigateWithTransition(target);
                PreloadPredictedViews(targetTag!);
            }
        }
        catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.AiChatPanel_ActionRequested"); }
    }

    private void BtnFloatingAi_Click(object sender, RoutedEventArgs e)
    {
        ToggleChatPanel(!_isChatOpen);
    }

    private void ToggleChatPanel(bool open)
    {
        _isChatOpen = open;
        var slideTransform = AiChatOverlay.RenderTransform as TranslateTransform;

        if (open)
        {
            BtnFloatingAi.Visibility = Visibility.Collapsed;
            AiChatOverlay.Visibility = Visibility.Visible;
            var anim = new DoubleAnimation(0, Motion.ChatOpen)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            slideTransform?.BeginAnimation(TranslateTransform.XProperty, anim);
        }
        else
        {
            _ = ServiceContainer.GetOptional<IAiService>()?.UnloadAsync();
            try { CentralUnitService.Current.NotifyAiActivity(); }
            catch (Exception ex) { LoggingService.LogException(ex, "MainWindow.ToggleChatPanel.NotifyAiActivity"); }
            var anim = new DoubleAnimation(Motion.ChatPanelWidth, Motion.ChatClose)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += OnAiChatAnimCompleted;
            slideTransform?.BeginAnimation(TranslateTransform.XProperty, anim);
        }
    }

    private void OnAiChatAnimCompleted(object? sender, EventArgs e)
    {
        AiChatOverlay.Visibility = Visibility.Collapsed;
        AppSettings? s = ServiceContainer.GetOptional<SettingsService>()?.Settings;
        BtnFloatingAi.Visibility = (s?.AiEnableFloatingButton ?? true) ? Visibility.Visible : Visibility.Collapsed;
        if (sender is AnimationClock clock)
        {
            clock.Completed -= OnAiChatAnimCompleted;
        }
    }
}
