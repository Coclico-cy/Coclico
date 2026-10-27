using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Coclico.Installer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Window main;
            try
            {
                main = Program.IsUninstallMode ? new Views.UninstallWindow() : new MainWindow();
            }
            catch (Exception ex) when (ex is not System.OutOfMemoryException)
            {
                Program.LogCrash(ex);
                Environment.Exit(1);
                return;
            }

            desktop.MainWindow = main;
            main.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
