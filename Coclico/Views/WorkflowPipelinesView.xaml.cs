#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Coclico.Models;
using Coclico.Services;
using Coclico.ViewModels;

namespace Coclico.Views;

public partial class WorkflowPipelinesView : UserControl
{
    private WorkflowPipelinesViewModel VM => (WorkflowPipelinesViewModel)DataContext;

    public WorkflowPipelinesView()
    {
        InitializeComponent();
        DataContext = new WorkflowPipelinesViewModel();
    }

    private void TbChainName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        TbChainName.Visibility = Visibility.Collapsed;
        TbxChainName.Text = VM.SelectedChain?.Name ?? string.Empty;
        TbxChainName.Visibility = Visibility.Visible;
        TbxChainName.Focus();
        TbxChainName.SelectAll();
    }

    private void TbxChainName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Escape)
            CommitChainRename();
    }

    private void TbxChainName_LostFocus(object sender, RoutedEventArgs e)
        => CommitChainRename();

    private void CommitChainRename()
    {
        if (VM.SelectedChain != null && !string.IsNullOrWhiteSpace(TbxChainName.Text))
            VM.SelectedChain.Name = TbxChainName.Text.Trim();

        TbxChainName.Visibility = Visibility.Collapsed;
        TbChainName.Visibility = Visibility.Visible;
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "S\u00e9lectionner un ex\u00e9cutable",
            Filter = "Ex\u00e9cutables (*.exe)|*.exe|Tous les fichiers (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true && sender is FrameworkElement fe && fe.Tag is PipelineStep item)
            item.ProgramPath = dlg.FileName;
    }

    private bool _isPanning;
    private Point _panStart;

    private void FlowScroll_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.MiddleButton != MouseButtonState.Pressed) return;
        _isPanning = true;
        _panStart = e.GetPosition(FlowScroll);
        FlowScroll.CaptureMouse();
        FlowScroll.Cursor = Cursors.SizeWE;
        e.Handled = true;
    }

    private void FlowScroll_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        var pos = e.GetPosition(FlowScroll);
        var dx = _panStart.X - pos.X;
        FlowScroll.ScrollToHorizontalOffset(FlowScroll.HorizontalOffset + dx);
        _panStart = pos;
    }

    private void FlowScroll_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;
        _isPanning = false;
        FlowScroll.ReleaseMouseCapture();
        FlowScroll.Cursor = Cursors.Arrow;
    }
}
