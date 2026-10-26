using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using RotmgTrackerV1.Core.Services;

namespace RotmgTrackerV1.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddWpfBlazorWebView();
        
        // Rejestrujemy nasz serwis z Core jako Singleton (jedna wspólna instancja dla całej aplikacji)
        serviceCollection.AddSingleton<TrackerStateService>();

        Resources.Add("services", serviceCollection.BuildServiceProvider());

        InitializeComponent();
    }
}