using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;
using RotmgTrackerV1.Core.Services;
using RotmgTrackerV1.Desktop.Components;
using RotmgTrackerV1.Sniffer.Services;

namespace RotmgTrackerV1.Desktop;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var appBuilder = PhotinoBlazorAppBuilder.CreateDefault(args);

        appBuilder.Services.AddSingleton<TrackerStateService>();
        appBuilder.Services.AddSingleton<GameDataService>();
        appBuilder.Services.AddSingleton<RotmgSnifferService>();
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