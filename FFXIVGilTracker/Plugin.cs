using Dalamud.Game.Command;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVGilTrack.Windows;
using GilTracker.Storage;
using GilTracker.Tracking;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dalamud.Game;
using Dalamud.Game.Network.Structures;
using Dalamud.Utility;

namespace FFXIVGilTrack;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IMarketBoard MarketBoard { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    private const string CommandName = "/giltracker";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("FFXIVGilTrack");
    private ConfigWindow ConfigWindow { get; init; }
    private JsonlEventQueue gilEventQueue = null!;
    private JsonlRetainerEventQueue retainerEventQueue = null!;
    private BalanceMonitor balanceMonitor = null!;

    private IngestionApiClient? ingestionClient;
    private RetainerIngestionApiClient? retainerIngestionClient;
    private string? activeApiUrl;
    private string? activeApiKey;
    private CancellationTokenSource? uploadCts;
    private Task uploadTask = Task.CompletedTask;

    private static readonly TimeSpan InfluxFlushInterval =
        TimeSpan.FromSeconds(5);

    private DateTime nextGilPoll = DateTime.MinValue;
    private ulong activeCharacterContentId;
    private static readonly TimeSpan GilPollInterval = TimeSpan.FromSeconds(2);
    // Retainer snapshots are observed only from retainer UI lifecycle events.
    private readonly Dictionary<(string CharacterId, ulong RetainerId), (string Name, uint Gil)> lastRetainerReadings = new();
    private MainWindow MainWindow { get; init; }

    private bool wasBetweenAreas;
    private DateTime lastTeleportSignal = DateTime.MinValue;
    private static readonly TimeSpan TeleportMatchWindow =
        TimeSpan.FromSeconds(10);

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        // Create a durable local queue in Dalamud's plugin config directory.
        gilEventQueue = new JsonlEventQueue(
            PluginInterface.GetPluginConfigDirectory());
        retainerEventQueue = new JsonlRetainerEventQueue(
            PluginInterface.GetPluginConfigDirectory());

        // Start monitoring gil changes.
        balanceMonitor = new BalanceMonitor(gilEventQueue);

       
        // Keep the upload loop alive even before setup is complete. The user can enter
        // their personal HTTPS URL/key in settings without having to reinstall the plugin.
        uploadCts = new CancellationTokenSource();
        uploadTask = Task.Run(() => FlushSubmissionLoopAsync(uploadCts.Token));
 

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Gil Tracker window."
        });

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        // This adds a button to the plugin installer entry of this plugin which allows
        // toggling the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Adds another button doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Framework.Update += OnFrameworkUpdate;

        // Opening the retainer window is the user intent signal. RequestedUpdate
        // runs after the game applies refreshed addon data, so it catches refreshed
        // balances without polling while the window remains open.
        // Subscribe without a name filter while diagnosing client addon names. The
        // handler immediately filters to retainer UI (and the generic retainer choice
        // menu), so unrelated addon events are ignored.
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, OnRetainerAddonEvent);
        AddonLifecycle.RegisterListener(AddonEvent.PostShow, OnRetainerAddonEvent);
        AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, OnRetainerAddonEvent);

        MarketBoard.ItemPurchased += OnMarketBoardItemPurchased;

        // Add a simple message to the log with level set to information
        // Use /xllog to open the log window in-game
        // Example output format for plugin log messages.
        Log.Information($"[{PluginInterface.Manifest.Name}] Plugin loaded.");
    }

    public void Dispose()
    {
        MarketBoard.ItemPurchased -= OnMarketBoardItemPurchased;
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, OnRetainerAddonEvent);
        AddonLifecycle.UnregisterListener(AddonEvent.PostShow, OnRetainerAddonEvent);
        AddonLifecycle.UnregisterListener(AddonEvent.PostRequestedUpdate, OnRetainerAddonEvent);

        if (uploadCts != null)
        {
            uploadCts.Cancel();
            try { uploadTask.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { /* Expected during shutdown. */ }
            uploadCts.Dispose();
        }

        ingestionClient?.Dispose();
        retainerIngestionClient?.Dispose();
        Framework.Update -= OnFrameworkUpdate;
        
        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnMarketBoardItemPurchased(IMarketBoardPurchase purchase)
    {
        var itemName = ItemUtil.GetItemName(purchase.CatalogId, false).ToString();

        Log.Information(
            $"[GilTracker] Market Board purchase: {itemName}, " +
            $"item={purchase.CatalogId}, quantity={purchase.ItemQuantity}");

        balanceMonitor.NotifyMarketBoardPurchase(
            itemName,
            purchase.ItemQuantity);
    }

    
    private void OnFrameworkUpdate(IFramework framework)
    {

        // Don't count changes across login/logout gaps.
        if (!ClientState.IsLoggedIn)
        {
            activeCharacterContentId = 0;
            lastRetainerReadings.Clear();
            balanceMonitor.ResetBaseline();
            nextGilPoll = DateTime.MinValue;
            return;
        }

        // Reset the baseline if the logged-in character changes. Otherwise a character
        // switch could be misinterpreted as a huge gain/loss for the previous character.
        var contentId = PlayerState.ContentId;
        if (contentId == 0)
        {
            balanceMonitor.ResetBaseline();
            nextGilPoll = DateTime.MinValue;
            return;
        }
        if (contentId != activeCharacterContentId)
        {
            activeCharacterContentId = contentId;
            lastRetainerReadings.Clear();
            balanceMonitor.ResetBaseline();
            nextGilPoll = DateTime.MinValue;
        }

        var now = DateTime.UtcNow;

        var betweenAreas = Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas];

        if (betweenAreas && !wasBetweenAreas)
        {
            lastTeleportSignal = DateTime.UtcNow;
            balanceMonitor.NotifyTeleport();

            Log.Information("[GilTracker] Area transition detected.");
        }

        wasBetweenAreas = betweenAreas;
        // Poll every two seconds, not every game frame.
        if (now < nextGilPoll)
            return;

        nextGilPoll = now + GilPollInterval;

        try
        {
            var characterId = GetPseudonymousCharacterId(contentId);
            var characterName = PlayerState.CharacterName;

            // Use the character's home world, not the world they may be visiting.
            var homeWorld = PlayerState.HomeWorld.Value;
            var worldName = homeWorld.Name.ExtractText();
            var dataCenterName = homeWorld.DataCenter.Value.Name.ExtractText();

            balanceMonitor.Tick(characterId, characterName, worldName, dataCenterName);

        }
        catch (Exception ex)
        {
            Log.Error(ex, "Gil Tracker failed to read or record gil.");
        }
    }

    private void OnRetainerAddonEvent(AddonEvent type, AddonArgs args)
    {
        var addonName = args.AddonName;
        if (!addonName.Contains("Retainer", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(addonName, "SelectString", StringComparison.Ordinal))
            return;

        Log.Information($"[GilTracker] Retainer UI lifecycle event: {type}, addon={addonName}.");

        if (!ClientState.IsLoggedIn || PlayerState.ContentId == 0)
            return;

        try
        {
            var contentId = PlayerState.ContentId;
            var characterId = GetPseudonymousCharacterId(contentId);
            var characterName = PlayerState.CharacterName;
            var homeWorld = PlayerState.HomeWorld.Value;
            var worldName = homeWorld.Name.ExtractText();
            var dataCenterName = homeWorld.DataCenter.Value.Name.ExtractText();

            var retainerReadings = RetainerBalanceReader.TryReadAll();
            if (retainerReadings is null)
                return;

            foreach (var retainer in retainerReadings)
            {
                var retainerKey = (characterId, retainer.RetainerId);
                if (lastRetainerReadings.TryGetValue(retainerKey, out var previousRetainer) &&
                    previousRetainer.Gil == retainer.Gil &&
                    string.Equals(previousRetainer.Name, retainer.Name, StringComparison.Ordinal))
                    continue;

                var snapshot = new GilTracker.Models.RetainerSnapshotEvent(
                    EventId: Guid.NewGuid().ToString("N"),
                    Timestamp: DateTimeOffset.UtcNow,
                    Balance: retainer.Gil,
                    CharacterId: characterId,
                    CharacterName: characterName,
                    World: worldName,
                    DataCenter: dataCenterName,
                    RetainerId: retainer.RetainerId.ToString("X16", CultureInfo.InvariantCulture),
                    RetainerName: retainer.Name);
                retainerEventQueue.Enqueue(snapshot);

                Log.Information(
                    $"[GilTracker] Retainer gil observed: {retainer.Name} " +
                    $"(retainer_id={retainer.RetainerId:X16}) balance={retainer.Gil}; queued for submission.");

                lastRetainerReadings[retainerKey] = (retainer.Name, retainer.Gil);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[GilTracker] Failed to read retainer gil after retainer UI update.");
        }
    }

    private static string GetPseudonymousCharacterId(ulong contentId)
    {
        // Hash the private game Content ID so the same character is stable across sessions
        // without sending the raw Content ID to the ingestion server.
        var bytes = Encoding.UTF8.GetBytes(contentId.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private void RefreshIngestionClient()
    {
        var url = Configuration.SubmissionApiUrl?.Trim() ?? string.Empty;
        var key = Configuration.SubmissionApiKey?.Trim() ?? string.Empty;
        if (string.Equals(url, activeApiUrl, StringComparison.Ordinal) &&
            string.Equals(key, activeApiKey, StringComparison.Ordinal))
            return;

        ingestionClient?.Dispose();
        retainerIngestionClient?.Dispose();
        ingestionClient = null;
        retainerIngestionClient = null;
        activeApiUrl = url;
        activeApiKey = key;

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            Log.Information("[GilTracker] Remote submission is not configured. Enter the HTTPS API URL and personal API key in settings.");
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var apiUri) || apiUri.Scheme != Uri.UriSchemeHttps)
        {
            Log.Warning("[GilTracker] Submission API URL must use HTTPS. Uploading is disabled until corrected.");
            return;
        }

        ingestionClient = new IngestionApiClient(gilEventQueue, url, key);
        retainerIngestionClient = new RetainerIngestionApiClient(retainerEventQueue, url, key);
        Log.Information("[GilTracker] Private HTTPS character and retainer submission configured.");
    }

    private async Task FlushSubmissionLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                RefreshIngestionClient();
                if (ingestionClient != null)
                {
                    var success = await ingestionClient
                        .FlushAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (!success)
                        Log.Warning("[GilTracker] Submission failed; queued events will be retried.");
                }

                if (retainerIngestionClient != null)
                {
                    var success = await retainerIngestionClient
                        .FlushAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (!success)
                        Log.Warning("[GilTracker] Retainer submission failed; queued snapshots will be retried.");
                }

                await Task.Delay(InfluxFlushInterval, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[GilTracker] Unexpected submission error.");
            }
        }
    }


    private void OnCommand(string command, string args)
    {
        // In response to the slash command, toggle the display status of our main ui
        MainWindow.Toggle();
    }
    
    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
