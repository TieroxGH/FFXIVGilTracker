using Dalamud.Configuration;
using System;

namespace FFXIVGilTrack;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;
    public bool IsConfigWindowMovable { get; set; } = true;

    // Public HTTPS receiver URL and a per-person API key. Never put InfluxDB tokens here.
    public string SubmissionApiUrl { get; set; } = string.Empty;
    public string SubmissionApiKey { get; set; } = string.Empty;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
