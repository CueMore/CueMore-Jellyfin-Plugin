using System;
using System.Collections.Generic;
using Jellyfin.Plugin.CueMore.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.CueMore;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        // One-time migration: installs that saved the old localhost or
        // pre-rebrand marqueehq default (or have no URL) move to CueMore.
        var cfg = Configuration;
        if (string.IsNullOrWhiteSpace(cfg.ApiBaseUrl) ||
            cfg.ApiBaseUrl.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) ||
            cfg.ApiBaseUrl.Contains("marqueehq.vercel.app", StringComparison.OrdinalIgnoreCase))
        {
            cfg.ApiBaseUrl = "https://cuemore.vercel.app";
            SaveConfiguration();
        }
    }

    public static Plugin? Instance { get; private set; }

    public override Guid Id => new("7c2e9f4a-1b3d-4e5f-9a8b-2c4d6e8f0a1b");

    public override string Name => "CueMore";

    public override string Description => "Automatic scrobbling to your CueMore profile: what you finish watching in Jellyfin is marked as watched in CueMore.";

    public IEnumerable<PluginPageInfo> GetPages() => new[]
    {
        new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        }
    };
}
