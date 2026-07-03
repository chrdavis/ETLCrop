using System.Windows;

namespace ETLCrop.App;

/// <summary>
/// Interaction logic for App.xaml.
/// </summary>
public partial class App : Application
{
    private void App_Startup(object sender, StartupEventArgs e)
    {
        // Apply the light/dark theme that matches Windows before the main window is shown, and
        // begin tracking system theme changes so the app updates live.
        ThemeManager.Initialize();

        var mainWindow = new MainWindow();
        ThemeManager.ApplyTitleBar(mainWindow);
        mainWindow.Show();
    }
}
