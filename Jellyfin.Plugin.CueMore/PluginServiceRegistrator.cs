using Jellyfin.Plugin.CueMore.Api;
using Jellyfin.Plugin.CueMore.Configuration;
using Jellyfin.Plugin.CueMore.Scrobbling;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.CueMore;

/// <summary>
/// Registers plugin services with Jellyfin's DI container.
/// The server picks this up automatically if the type implements
/// IPluginServiceRegistrator and lives in the plugin assembly.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient(HttpClientNames.CueMore);

        serviceCollection.AddSingleton<CueMoreClient>();
        serviceCollection.AddSingleton<DeviceCodeClient>();
        serviceCollection.AddSingleton<PlayedStateTracker>();
        serviceCollection.AddSingleton<ProgressPlayedTracker>();
        serviceCollection.AddSingleton<ProgressThrottle>();
        serviceCollection.AddSingleton<PayloadBuilder>();

        // Hosted services: Jellyfin starts/stops them with the server.
        serviceCollection.AddHostedService<ScrobbleManager>();
    }
}

public static class HttpClientNames
{
    public const string CueMore = "CueMore";
}
