using GilTracker.Models;
using GilTracker.Storage;
using System;

namespace GilTracker.Tracking;

public sealed class BalanceMonitor
{
    private readonly JsonlEventQueue queue;
    private long? previous;

    private DateTimeOffset? pendingMarketBoardPurchaseAt;
    private string? pendingMarketBoardItemName;
    private uint pendingMarketBoardQuantity;
    private DateTimeOffset? pendingTeleportAt;

    private static readonly TimeSpan TeleportMatchWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MarketBoardMatchWindow = TimeSpan.FromSeconds(5);

    public BalanceMonitor(JsonlEventQueue queue) => this.queue = queue;

    public void NotifyTeleport() => pendingTeleportAt = DateTimeOffset.UtcNow;

    public void ResetBaseline()
    {
        previous = null;
        pendingMarketBoardPurchaseAt = null;
        pendingMarketBoardItemName = null;
        pendingMarketBoardQuantity = 0;
        pendingTeleportAt = null;
    }

    public void NotifyMarketBoardPurchase(string itemName, uint quantity)
    {
        pendingMarketBoardPurchaseAt = DateTimeOffset.UtcNow;
        pendingMarketBoardItemName = itemName;
        pendingMarketBoardQuantity = quantity;
    }

    public void Tick(string? characterId, string? characterName, string? world, string? dataCenter)
    {
        var current = GilBalanceReader.TryRead();
        if (current is null || current < 0)
        {
            ResetBaseline();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (previous is null)
        {
            previous = current;
            return;
        }

        var delta = current.Value - previous.Value;
        previous = current;

        if (delta == 0)
        {
            ExpirePendingPurchase(now);
            ExpirePendingTeleport(now);
            return;
        }

        string category;
        string description;
        string confidence;

        if (delta > 0)
        {
            // Every observed increase is recorded as income, even when the game
            // does not expose enough information to identify its source.
            category = "gil_income";
            description = "Gil gained; source not identified";
            confidence = "high"; // The balance increase is confirmed; source is unknown.

            // A pending expense signal must not classify an income event.
            ExpirePendingPurchase(now);
            ExpirePendingTeleport(now);
        }
        else if (pendingMarketBoardPurchaseAt is { } purchaseAt &&
                 now >= purchaseAt &&
                 now - purchaseAt <= MarketBoardMatchWindow)
        {
            category = "market_board";
            description =
                $"Market Board purchase: {pendingMarketBoardItemName ?? "Unknown item"} " +
                $"x{pendingMarketBoardQuantity} (matched by timing)";
            confidence = "medium";

            ClearPendingPurchase();
            pendingTeleportAt = null;
        }
        else if (pendingTeleportAt is { } teleportAt &&
                 now >= teleportAt &&
                 now - teleportAt <= TeleportMatchWindow)
        {
            category = "teleport";
            description = "Possible teleport fee (matched by area-transition timing)";
            confidence = "low";
            pendingTeleportAt = null;
            ExpirePendingPurchase(now);
        }
        else
        {
            category = "unknown";
            description = "Unclassified expense";
            confidence = "unknown";
            ExpirePendingPurchase(now);
            ExpirePendingTeleport(now);
        }

        queue.Enqueue(new GilEvent(
            Timestamp: now,
            Balance: current.Value,
            Delta: delta,
            Category: category,
            Description: description,
            Confidence: confidence)
        {
            EventId = Guid.NewGuid().ToString("N"),
            CharacterId = characterId,
            CharacterName = characterName,
            World = world,
            DataCenter = dataCenter
        });
    }

    private void ClearPendingPurchase()
    {
        pendingMarketBoardPurchaseAt = null;
        pendingMarketBoardItemName = null;
        pendingMarketBoardQuantity = 0;
    }

    private void ExpirePendingTeleport(DateTimeOffset now)
    {
        if (pendingTeleportAt is { } teleportAt &&
            now - teleportAt > TeleportMatchWindow)
            pendingTeleportAt = null;
    }

    private void ExpirePendingPurchase(DateTimeOffset now)
    {
        if (pendingMarketBoardPurchaseAt is { } purchaseAt &&
            now - purchaseAt > MarketBoardMatchWindow)
            ClearPendingPurchase();
    }
}
