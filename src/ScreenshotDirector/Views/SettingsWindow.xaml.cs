using System.Windows;
using ScreenshotDirector.ViewModels;

namespace ScreenshotDirector.Views;

public partial class SettingsWindow : Window
{
    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.RequestClose += (s, args) => Close();
        }
    }

    private void OnAddDestinationClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.AddDestination("New Folder", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
    }

    private void OnRemoveDestinationClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.RemoveSelected();
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.MoveSelectedUp();
    }

    private void OnMoveDownClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.MoveSelectedDown();
    }

    private void OnBrowsePathClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.BrowseSelectedPath();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.SaveAsync();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.Cancel();
    }
}
