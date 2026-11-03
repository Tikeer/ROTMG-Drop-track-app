using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;
using RotmgTrackerV1.Core.Services;
using RotmgTrackerV1.Desktop.Components;

namespace RotmgTrackerV1.Desktop;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var appBuilder = PhotinoBlazorAppBuilder.CreateDefault(args);

        appBuilder.Services.AddSingleton<TrackerStateService>();
        appBuilder.RootComponents.Add<Dashboard>("#app");

        var app = appBuilder.Build();

        app.MainWindow
            .SetTitle("RotMG Drop Tracker")
            .SetUseOsDefaultSize(false)
            .SetSize(1220, 840)
            .SetMinSize(850, 600)
            .Center();

        app.Run();
    }
}