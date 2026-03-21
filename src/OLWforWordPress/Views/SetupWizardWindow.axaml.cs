using Avalonia.Controls;
using Avalonia.Interactivity;
using OLWforWordPress.ViewModels;

namespace OLWforWordPress.Views;

public partial class SetupWizardWindow : Window
{
    public SetupWizardWindow()
    {
        InitializeComponent();
    }

    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SetupWizardViewModel vm)
        {
            await vm.ConnectAsync();
        }
    }
}
