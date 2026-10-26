using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;
using RotmgTrackerV1.Core.Services;

namespace RotmgTrackerV1.Desktop;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, uint cbAttribute);

    public MainWindow()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddWpfBlazorWebView();
        serviceCollection.AddSingleton<TrackerStateService>();

        Resources.Add("services", serviceCollection.BuildServiceProvider());

        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        UpdateWindowVisualState();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        UpdateWindowVisualState();
    }

    private void UpdateWindowVisualState()
    {
        bool isMaximized = WindowState == WindowState.Maximized;

        // Ukrywa obramówkę na pełnym ekranie i kompensuje systemowy margines maksymalizacji
        BorderThickness = isMaximized ? new Thickness(0) : new Thickness(1);
        RootGrid.Margin = isMaximized ? new Thickness(6) : new Thickness(0);

        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int attribute = 33; // DWMWA_WINDOW_CORNER_PREFERENCE
            int preference = isMaximized ? 1 : 2; // 1 = Ostre rogi w fullscreenie, 2 = Zaokrąglone w oknie
            DwmSetWindowAttribute(hwnd, attribute, ref preference, sizeof(int));
        }
        catch
        {
            // Ignoruj na starszych wersjach Windows
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}