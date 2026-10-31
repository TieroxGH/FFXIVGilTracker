using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace FFXIVGilTrack.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private string apiUrl;
    private string apiKey;
    private string status = "Changes save automatically.";

    public ConfigWindow(Plugin plugin) : base("Gil Tracker Settings###FFXIVGilTrackSettings", ImGuiWindowFlags.NoCollapse)
    {
        Size = new Vector2(540, 250);
        SizeCondition = ImGuiCond.FirstUseEver;
        configuration = plugin.Configuration;
        apiUrl = configuration.SubmissionApiUrl;
        apiKey = configuration.SubmissionApiKey;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextColored(new Vector4(0.35f, 0.82f, 0.75f, 1f), "CONNECTION");
        ImGui.TextWrapped("Connect to your Gil Tracker dashboard. Your personal API key is stored in your local Dalamud plugin configuration. Never use an InfluxDB token here.");
        ImGui.Spacing();

        ImGui.Text("HTTPS API URL");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##GilTrackerApiUrl", "https://your-giltracker-host", ref apiUrl, 512))
        {
            configuration.SubmissionApiUrl = apiUrl.Trim();
            configuration.Save();
            status = "URL saved.";
        }

        ImGui.Spacing();
        ImGui.Text("PERSONAL API KEY");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##GilTrackerApiKey", "Paste your personal key", ref apiKey, 512, ImGuiInputTextFlags.Password))
        {
            configuration.SubmissionApiKey = apiKey.Trim();
            configuration.Save();
            status = "Key saved.";
        }

        ImGui.Spacing();
        if (ImGui.Button("Save settings", new Vector2(140, 0)))
        {
            configuration.SubmissionApiUrl = apiUrl.Trim();
            configuration.SubmissionApiKey = apiKey.Trim();
            configuration.Save();
            status = "Settings saved.";
        }
        ImGui.SameLine();
        ImGui.TextDisabled(status);
        ImGui.Spacing();
        ImGui.TextDisabled("Uploads require HTTPS. Events remain queued locally until acknowledged by the server.");
    }
}
