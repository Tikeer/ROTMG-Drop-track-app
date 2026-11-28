using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;
using RotmgTrackerV1.Core.Services;
using RotmgTrackerV1.Core.Services.Sprites;
using RotmgTrackerV1.Desktop.Components;
using RotmgTrackerV1.Sniffer.Services;

namespace RotmgTrackerV1.Desktop;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // SharpPcap trzyma po jednym wątku z puli na każdy interfejs sieciowy (na macOS ~25).
        // Bez wyższego minimum pula dokłada wątki co ~500 ms i przez ~15 s kolejka zdarzeń UI stoi.
        ThreadPool.SetMinThreads(Math.Max(Environment.ProcessorCount * 4, 64), 64);

        var appBuilder = PhotinoBlazorAppBuilder.CreateDefault(args);

        appBuilder.Services.AddSingleton<TrackerStateService>();
        appBuilder.Services.AddSingleton<GameDataService>();
        appBuilder.Services.AddSingleton<SpriteAtlasService>();
        appBuilder.Services.AddSingleton<RotmgSnifferService>();
        appBuilder.Services.AddSingleton<UiSettings>();
        appBuilder.Services.AddSingleton<AppStartup>();
        appBuilder.RootComponents.Add<Dashboard>("#app");

        var app = appBuilder.Build();

        app.MainWindow
            .SetTitle("RotMG Drop & DPS Tracker")
            .SetUseOsDefaultSize(false)
            .SetSize(1280, 860)
            .SetMinSize(900, 620)
            .Center();

        app.Run();
    }
}