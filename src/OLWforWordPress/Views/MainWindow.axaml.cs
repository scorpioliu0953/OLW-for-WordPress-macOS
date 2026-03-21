using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OLWforWordPress.Models;
using OLWforWordPress.ViewModels;

namespace OLWforWordPress.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.LoadDataAsync();
        }
    }

    private void OnNewPostClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.NewPost();
        }
    }

    private async void OnPublishClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.PublishAsync();
        }
    }

    private async void OnInsertImageClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "選擇圖片",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("圖片檔案")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp" }
                }
            }
        });

        if (files.Count > 0)
        {
            var path = files[0].Path.LocalPath;
            var url = await vm.UploadImageAsync(path);
            if (url != null)
            {
                var imgTag = $"<img src=\"{url}\" alt=\"\" />";
                vm.HtmlContent += $"\n{imgTag}\n";
            }
        }
    }

    private async void OnRecentPostSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox) return;
        if (listBox.SelectedItem is not BlogPostListItem item) return;
        if (DataContext is not MainWindowViewModel vm) return;

        listBox.SelectedItem = null; // Reset selection
        await vm.OpenPostAsync(item.Post);
    }

    private async void OnDeletePostClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (!vm.IsEditing) return;

        // Simple confirmation - in production you'd use a dialog
        await vm.DeleteCurrentPostAsync();
    }
}
