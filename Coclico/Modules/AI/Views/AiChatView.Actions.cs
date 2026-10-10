using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Coclico.Services;
using Coclico.Services.AI;

namespace Coclico.Views;

public partial class AiChatView : UserControl
{
    private sealed class ActionCardControls
    {
        public required TextBlock StatusBadge { get; init; }
        public required Border BadgeBorder { get; init; }
        public required TextBlock DetailsText { get; init; }
    }

    private bool _autoExecuteActions;

    private void BtnDismissBetaNotice_Click(object sender, RoutedEventArgs e)
    {
        BetaNoticeBanner.Visibility = Visibility.Collapsed;
    }

    private void BtnAutoExecToggle_Click(object sender, RoutedEventArgs e)
    {
        _autoExecuteActions = !_autoExecuteActions;
        UpdateAutoExecUI();
    }

    private void UpdateAutoExecUI()
    {
        if (TxtAutoExecMode == null || TxtAutoExecIcon == null)
        {
            return;
        }

        if (_autoExecuteActions)
        {
            TxtAutoExecIcon.Text = "⚡";
            TxtAutoExecMode.Text = _loc?.Get("Ai_AutoExec_Direct") ?? "Sans confirmation (direct)";
            TxtAutoExecMode.Foreground = (Brush)FindResource("PrimaryBrush");
            AutoExecPill.BorderBrush = (Brush)FindResource("PrimaryBrush");
            AutoExecPill.ToolTip = _loc?.Get("Ai_AutoExec_Direct_Tip") ?? "Mode direct actif : les actions système sont exécutées immédiatement sans confirmation.";
            ToastService.ShowInfo("Mode sans demande activé : l'IA exécutera les actions directement.");
        }
        else
        {
            TxtAutoExecIcon.Text = "🛡️";
            TxtAutoExecMode.Text = _loc?.Get("Ai_AutoExec_Confirm") ?? "Avec confirmation";
            TxtAutoExecMode.Foreground = (Brush)FindResource("TextSecondaryBrush");
            AutoExecPill.BorderBrush = (Brush)FindResource("BorderSubtleBrush");
            AutoExecPill.ToolTip = _loc?.Get("Ai_AutoExec_Confirm_Tip") ?? "Mode avec confirmation actif : l'IA demande confirmation dans le chat avant chaque action.";
            ToastService.ShowInfo("Mode avec confirmation réactivé.");
        }
    }

    private static string GetActionTitle(AiActionRequest action)
    {
        return action.ActionType switch
        {
            "CLEAN_RAM" or "RAM_CLEAN" or "NETTOYAGE_RAM" or "NETTOYER_RAM" or "VIDER_RAM" => "⚡ Optimisation Mémoire RAM",
            "CONFIGURE_RAM_DAEMON" => "⚙️ Configuration Démon RAM Automatique",
            "SET_RAM_PROFILE" => $"🧠 Profil Mémoire RAM : {action.Parameter ?? "Smart"}",
            "OPTIMIZE_NETWORK" => $"🌐 Optimisation Réseau & Cartes ({action.Parameter ?? "Ultra Gaming"})",
            "BENCHMARK_NETWORK" => "📊 Benchmark Réseau & Test DNS",
            "RESET_NETWORK_DEFAULTS" => "🔄 Restauration Réseau Microsoft par Défaut",
            "ANALYZE_CRASHES" => "💥 Analyseur de Crashs & Écrans Bleus (BSOD)",
            "CLEAN_TEMP" or "TEMP_CLEAN" or "NETTOYAGE_TEMP" or "NETTOYER_TEMP" or "VIDER_TEMP" => "🧹 Nettoyage Fichiers Temporaires",
            "SCAN_HEALTH" or "HEALTH_SCAN" or "SANTE" => "🛡️ Sécurité Windows Defender",
            "SCAN_SFC" or "SFC_SCAN" or "SFC" or "ANALYSE_SFC" => "🔍 Réparation Fichiers Système (SFC)",
            "SCAN_DISM" or "DISM_SCAN" or "DISM" or "ANALYSE_DISM" => "🛠️ Restauration Image Windows (DISM)",
            "DEFENDER_SCAN" or "SCAN_DEFENDER" or "ANTIVIRUS" => "🛡️ Analyse Antivirus Defender",
            "FULL_REPAIR" or "SYSTEM_REPAIR" or "REPAIR_SYSTEM" => "🚀 Réparation Intégrale 1-Clic",
            "RESET_NETWORK" or "NETWORK_RESET" => "🌐 Réinitialisation Réseau & DNS",
            "RESET_UPDATE" or "UPDATE_RESET" => "🔄 Réinitialisation Windows Update",
            "DIAGNOSE_SYSTEM" or "SYSTEM_DIAGNOSE" or "DIAGNOSTIC" => "📊 Diagnostic Système Live",
            "SNAPSHOT_NETWORK" => "🛡️ Instantané Réseau (Sauvegarde)",
            "RESTORE_NETWORK_SNAPSHOT" => "⏪ Restauration Instantané Réseau",
            "UPDATE_APPS" or "MAJ_APP" => "⬆️ Mise à Jour des Applications",
            "NAVIGATE" or "NAVIGUER" => $"🧭 Navigation vers {action.Parameter}",
            "OPEN_APP" or "OUVRIR_APP" => $"🚀 Lancement de {action.Parameter}",
            "INSTALL_WINGET" or "INSTALLER" => $"📦 Installation Winget : {action.Parameter}",
            _ => $"⚙️ Action : {action.ActionType}"
        };
    }

    private (Grid Header, TextBlock Badge, Border BadgeBorder) BuildActionHeader(string title, string badgeText)
    {
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tbTitle = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA5, 0xB4, 0xFC)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tbTitle, 0);
        _ = headerGrid.Children.Add(tbTitle);

        var badgeBorder = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(40, 245, 158, 11)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 2, 7, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        var tbBadge = new TextBlock
        {
            Text = badgeText,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11))
        };
        badgeBorder.Child = tbBadge;
        Grid.SetColumn(badgeBorder, 1);
        _ = headerGrid.Children.Add(badgeBorder);

        return (headerGrid, tbBadge, badgeBorder);
    }

    private static TextBlock BuildDetailsText(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 16
        };
    }

    private Border BuildActionCard(Grid cardGrid, ActionCardControls controls)
    {
        var card = new Border
        {
            Background = (Brush)FindResource("BgElevatedBrush"),
            BorderBrush = (Brush)FindResource("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(6, 4, 50, 4),
            Child = cardGrid,
            Tag = controls
        };

        MessagesPanel.Children.Insert(MessagesPanel.Children.Count - 1, card);
        ScrollToBottom();
        return card;
    }

    private static void ApplyRoundedCorners(Button button)
    {
        button.Resources.Add(typeof(Border), new Style(typeof(Border))
        {
            Setters = { new Setter(Border.CornerRadiusProperty, new CornerRadius(6)) }
        });
    }

    private Border AddActionConfirmationCard(AiActionRequest action, TaskCompletionSource<bool> tcs)
    {
        var cardGrid = new Grid();
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        (Grid headerGrid, TextBlock tbBadge, Border badgeBorder) = BuildActionHeader(GetActionTitle(action), "Confirmation requise");
        Grid.SetRow(headerGrid, 0);
        _ = cardGrid.Children.Add(headerGrid);

        TextBlock tbDetails = BuildDetailsText("L'assistant propose d'exécuter cette action système sur votre PC. Voulez-vous continuer ?");
        Grid.SetRow(tbDetails, 1);
        _ = cardGrid.Children.Add(tbDetails);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var btnConfirm = new Button
        {
            Content = "⚡ Exécuter l'action",
            Background = (Brush)FindResource("PrimaryBrush"),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Padding = new Thickness(14, 5, 14, 5),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        ApplyRoundedCorners(btnConfirm);

        var btnCancel = new Button
        {
            Content = "✕ Refuser",
            Background = (Brush)FindResource("BgCardBrush"),
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            BorderBrush = (Brush)FindResource("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            FontWeight = FontWeights.Normal,
            FontSize = 11,
            Padding = new Thickness(12, 5, 12, 5),
            Cursor = Cursors.Hand
        };
        ApplyRoundedCorners(btnCancel);

        btnConfirm.Click += (s, e) =>
        {
            btnPanel.Visibility = Visibility.Collapsed;
            tbBadge.Text = "En cours…";
            tbBadge.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            badgeBorder.Background = new SolidColorBrush(Color.FromArgb(40, 245, 158, 11));
            badgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            tbDetails.Text = "Exécution de la commande système en arrière-plan…";
            tbDetails.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            _ = tcs.TrySetResult(true);
        };

        btnCancel.Click += (s, e) =>
        {
            btnPanel.Visibility = Visibility.Collapsed;
            tbBadge.Text = "Refusée";
            tbBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            badgeBorder.Background = new SolidColorBrush(Color.FromArgb(40, 148, 163, 184));
            badgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            tbDetails.Text = "Action refusée par l'utilisateur.";
            tbDetails.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            _ = tcs.TrySetResult(false);
        };

        _ = btnPanel.Children.Add(btnConfirm);
        _ = btnPanel.Children.Add(btnCancel);
        Grid.SetRow(btnPanel, 2);
        _ = cardGrid.Children.Add(btnPanel);

        return BuildActionCard(cardGrid, new ActionCardControls
        {
            StatusBadge = tbBadge,
            BadgeBorder = badgeBorder,
            DetailsText = tbDetails
        });
    }

    private Border AddActionCard(AiActionRequest action)
    {
        var cardGrid = new Grid();
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        (Grid headerGrid, TextBlock tbBadge, Border badgeBorder) = BuildActionHeader(GetActionTitle(action), "En cours…");
        Grid.SetRow(headerGrid, 0);
        _ = cardGrid.Children.Add(headerGrid);

        TextBlock tbDetails = BuildDetailsText("Exécution de la commande système en arrière-plan…");
        Grid.SetRow(tbDetails, 1);
        _ = cardGrid.Children.Add(tbDetails);

        return BuildActionCard(cardGrid, new ActionCardControls
        {
            StatusBadge = tbBadge,
            BadgeBorder = badgeBorder,
            DetailsText = tbDetails
        });
    }

    private void UpdateActionCard(Border card, AiActionResult result)
    {
        if (card.Tag is ActionCardControls ctrl)
        {
            if (result.Success)
            {
                ctrl.StatusBadge.Text = "✅ Succès";
                ctrl.StatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
                ctrl.BadgeBorder.Background = new SolidColorBrush(Color.FromArgb(40, 74, 222, 128));
                ctrl.BadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
            }
            else
            {
                ctrl.StatusBadge.Text = "❌ Échec";
                ctrl.StatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                ctrl.BadgeBorder.Background = new SolidColorBrush(Color.FromArgb(40, 248, 113, 113));
                ctrl.BadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
            }

            ctrl.DetailsText.Text = result.Message + (string.IsNullOrEmpty(result.Details) ? "" : "\n" + result.Details);
            ctrl.DetailsText.Foreground = result.Success
                ? new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC))
                : new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5));
        }
        ScrollToBottom();
    }
}
