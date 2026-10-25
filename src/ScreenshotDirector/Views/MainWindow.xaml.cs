using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using ScreenshotDirector.Core.Models;
using ScreenshotDirector.ViewModels;

namespace ScreenshotDirector.Views;

public partial class MainWindow : Window
{
    private MainControlViewModel? ViewModel => DataContext as MainControlViewModel;
    private bool _isExplicitClose;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.RequestClose += (s, args) =>
            {
                _isExplicitClose = true;
                Close();
            };

            ViewModel.RequestHideToTray += (s, args) =>
            {
                Hide();
            };
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isExplicitClose)
        {
            base.OnClosing(e);
            return;
        }

        // If configured to run in system tray, minimize to tray on 'X' click
        if (ViewModel != null && ViewModel.RunInSystemTray)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            // If not running in system tray, stopping window exits the program
            ViewModel?.StopProgram();
            base.OnClosing(e);
        }
    }

    // Navigation Handlers
    private void OnNavHomeClick(object sender, RoutedEventArgs e) => ViewModel?.NavigateTo("Home");
    private void OnNavPresetsClick(object sender, RoutedEventArgs e) => ViewModel?.NavigateTo("Presets");
    private void OnNavPopupClick(object sender, RoutedEventArgs e) => ViewModel?.NavigateTo("PopupSettings");
    private void OnNavGeneralClick(object sender, RoutedEventArgs e) => ViewModel?.NavigateTo("GeneralSettings");
    private void OnNavAboutClick(object sender, RoutedEventArgs e) => ViewModel?.NavigateTo("About");

    private void OnGitHubClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/diaba1445-glitch/Screenshot-Director",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Could not open repository URL: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnTogglePauseClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.TogglePause();
    }

    private void OnStopProgramClick(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "Are you sure you want to stop Screenshot Director and exit?",
            "Stop Screenshot Director",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _isExplicitClose = true;
            ViewModel?.StopProgram();
        }
    }

    private void OnTestPopupClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.TestPopup();
    }

    private void OnAddTemplateScreenshotsClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.AddTemplateFolder("screenshots");
    }

    private void OnAddTemplateDesktopClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.AddTemplateFolder("desktop");
    }

    private void OnAddTemplateDownloadsClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.AddTemplateFolder("downloads");
    }

    private void OnAddCustomDestinationClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.BrowseNewDestination();
    }

    private void OnBrowseNewDestinationClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.BrowseNewDestination();
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

    private void OnMinimizeToTrayClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void OnSetSessionDefaultClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Folder to Set as Session Default",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            ViewModel?.SetSessionDefault(dialog.SelectedPath);
        }
    }

    private void OnClearSessionDefaultClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.ClearSessionDefault();
    }

    private void OnSetLightGlassThemeClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetPopupTheme(PopupThemeMode.LightGlass);
    }

    private void OnSetDarkGlassThemeClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetPopupTheme(PopupThemeMode.DarkGlass);
    }

    private void OnClearRecentCapturesClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.ClearRecentCaptures();
    }

    private void OnOpenFileClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecentCaptureItem item })
        {
            ViewModel?.OpenFile(item.FilePath);
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecentCaptureItem item })
        {
            ViewModel?.OpenFolder(item.DirectoryPath);
        }
    }
}
