using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace FFXIVGilTrack.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base("Gil Tracker###FFXIVGilTrackMain", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar)
    {
        this.plugin = plugin;
        Size = new Vector2(440, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 260),
            MaximumSize = new Vector2(620, 520)
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        // Compact, native-feeling layout: clear hierarchy, subtle accent, no decorative assets.
        ImGui.TextColored(new Vector4(0.35f, 0.82f, 0.75f, 1f), "GIL TRACKER");
        ImGui.SameLine();
        ImGui.TextDisabled("/giltracker");
        ImGui.Separator();
        ImGui.Spacing();

        var playerState = Plugin.PlayerState;
        if (playerState.IsLoaded)
        {
            ImGui.Text("CHARACTER");
            ImGui.SameLine(110f);
            ImGui.Text("Character detected");

            if (playerState.ClassJob.IsValid)
            {
                ImGui.Text("JOB");
                ImGui.SameLine(110f);
                ImGui.Text($"{playerState.ClassJob.Value.Abbreviation}  ·  Lv. {playerState.Level}");
            }
        }
        else
        {
            ImGui.TextDisabled("Log in to FFXIV to start tracking your gil.");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Text("TRACKING");
        ImGui.SameLine(110f);
        if (playerState.IsLoaded)
            ImGui.TextColored(new Vector4(0.35f, 0.82f, 0.55f, 1f), "● Active");
        else
            ImGui.TextDisabled("○ Waiting for character");

        var configured = !string.IsNullOrWhiteSpace(plugin.Configuration.SubmissionApiUrl)
            && !string.IsNullOrWhiteSpace(plugin.Configuration.SubmissionApiKey);
        ImGui.Text("DASHBOARD UPLOAD");
        ImGui.SameLine(150f);
        if (configured)
            ImGui.TextColored(new Vector4(0.35f, 0.82f, 0.75f, 1f), "Configured");
        else
            ImGui.TextColored(new Vector4(0.95f, 0.68f, 0.30f, 1f), "Setup needed");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextWrapped("Gil changes are recorded locally first. When your personal HTTPS API key is configured, queued events are uploaded automatically and retried if the connection is unavailable.");
        ImGui.Spacing();

        if (ImGui.Button("Settings", new Vector2(120, 0)))
            plugin.ToggleConfigUi();
        ImGui.SameLine();
        if (ImGui.Button("Close", new Vector2(90, 0)))
            IsOpen = false;
    }
}
