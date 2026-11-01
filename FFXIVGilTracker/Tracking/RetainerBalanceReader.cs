using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace GilTracker.Tracking;

/// <summary>
/// Reads the retainer gil values currently cached by the game client.
/// These are snapshots from RetainerManager, not a transaction history.
/// </summary>
public static unsafe class RetainerBalanceReader
{
    /// <summary>
    /// Returns null when the retainer manager is not ready; otherwise returns
    /// all retainer records with an assigned ID. An empty list is a valid result.
    /// </summary>
    public static IReadOnlyList<RetainerBalanceSnapshot>? TryReadAll()
    {
        var manager = RetainerManager.Instance();
        if (manager == null || !manager->IsReady)
            return null;

        var snapshots = new List<RetainerBalanceSnapshot>();
        foreach (var retainer in manager->Retainers)
        {
            if (retainer.RetainerId == 0)
                continue;

            var name = retainer.NameString?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = $"Retainer {retainer.RetainerId:X16}";

            snapshots.Add(new RetainerBalanceSnapshot(
                RetainerId: retainer.RetainerId,
                Name: name,
                Gil: retainer.Gil));
        }

        return snapshots;
    }
}

/// <summary>A point-in-time retainer identity and gil balance.</summary>
public sealed record RetainerBalanceSnapshot(ulong RetainerId, string Name, uint Gil);
