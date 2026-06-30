using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace ETWCrop.App;

/// <summary>
/// Applies a light or dark theme to the application by swapping the merged color dictionary, and
/// keeps it in sync with the Windows system theme (including live changes while the app is open).
/// </summary>
internal static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    // DWM attribute that toggles the dark (immersive) title bar for a window.
    private const int DwmwaUseImmersiveDarkMode = 20;

    private static readonly Uri LightColors = new("Themes/LightColors.xaml", UriKind.Relative);
    private static readonly Uri DarkColors = new("Themes/DarkColors.xaml", UriKind.Relative);
    private static readonly Uri BaseStyles = new("Themes/BaseStyles.xaml", UriKind.Relative);

    private static bool _subscribed;
    private static bool _currentIsDark;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Gets a value indicating whether the dark theme is currently applied.</summary>
    public static bool IsDarkTheme => _currentIsDark;

    /// <summary>
    /// Applies the theme that matches the current Windows setting and, on first call, begins
    /// listening for system theme changes so the app updates live.
    /// </summary>
    public static void Initialize()
    {
        Apply(IsSystemInDarkMode());

        if (!_subscribed)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _subscribed = true;
        }
    }

    /// <summary>Reads the Windows "apps" theme preference; defaults to light if unavailable.</summary>
    public static bool IsSystemInDarkMode()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            // The value is 1 for light, 0 for dark. A missing value means light.
            return key?.GetValue(AppsUseLightThemeValue) is int useLight && useLight == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
        {
            return;
        }

        bool isDark = IsSystemInDarkMode();
        if (isDark == _currentIsDark)
        {
            return;
        }

        // The notification can arrive off the UI thread; marshal the dictionary swap back to it.
        Application.Current?.Dispatcher.Invoke(() => Apply(isDark));
    }

    private static void Apply(bool isDark)
    {
        Application app = Application.Current;
        if (app is null)
        {
            return;
        }

        var colors = new ResourceDictionary { Source = isDark ? DarkColors : LightColors };
        var styles = new ResourceDictionary { Source = BaseStyles };

        app.Resources.MergedDictionaries.Clear();
        app.Resources.MergedDictionaries.Add(colors);
        app.Resources.MergedDictionaries.Add(styles);

        _currentIsDark = isDark;

        // Recolor the OS title bar of every open window to match the theme.
        foreach (Window window in app.Windows)
        {
            ApplyTitleBar(window);
        }
    }

    /// <summary>
    /// Applies the current theme's dark/light setting to a window's OS title bar. Safe to call
    /// before or after the window is shown; if the native handle does not exist yet, the title bar
    /// is updated once the window is sourced.
    /// </summary>
    public static void ApplyTitleBar(Window window)
    {
        if (window is null)
        {
            return;
        }

        var helper = new WindowInteropHelper(window);
        if (helper.Handle != IntPtr.Zero)
        {
            SetTitleBarDark(helper.Handle, _currentIsDark);
            return;
        }

        // The handle is not created until the window is sourced; apply it then.
        void OnSourceInitialized(object? sender, EventArgs e)
        {
            window.SourceInitialized -= OnSourceInitialized;
            SetTitleBarDark(new WindowInteropHelper(window).Handle, _currentIsDark);
        }

        window.SourceInitialized += OnSourceInitialized;
    }

    private static void SetTitleBarDark(IntPtr hwnd, bool isDark)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            int useDark = isDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));
        }
        catch
        {
            // DWM attribute is unavailable on older Windows; ignore and keep the default chrome.
        }
    }
}
