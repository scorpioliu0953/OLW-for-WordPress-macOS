using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OLWforWordPress.Services;
using OLWforWordPress.ViewModels;
using OLWforWordPress.Views;

namespace OLWforWordPress;

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
            var settingsService = new BlogSettingsService();
            var settings = settingsService.Load();

            if (settings == null)
            {
                // Show setup wizard first
                var setupVm = new SetupWizardViewModel(settingsService);
                var setupWindow = new SetupWizardWindow { DataContext = setupVm };
                setupVm.SetupCompleted += (_, blogSettings) =>
                {
                    setupWindow.Close();
                    var mainVm = new MainWindowViewModel(blogSettings, settingsService);
                    desktop.MainWindow = new MainWindow { DataContext = mainVm };
                    desktop.MainWindow.Show();
                };
                desktop.MainWindow = setupWindow;
            }
            else
            {
                var mainVm = new MainWindowViewModel(settings, settingsService);
                desktop.MainWindow = new MainWindow { DataContext = mainVm };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
