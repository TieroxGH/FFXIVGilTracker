using FFXIVClientStructs.FFXIV.Client.Game;

namespace GilTracker.Tracking;

/// <summary>
/// Reads the player's gil from the currency inventory container.
/// Native FFXIV structures are version-dependent: verify signatures against the
/// FFXIVClientStructs version installed with your current Dalamud SDK.
/// </summary>
public static unsafe class GilBalanceReader
{
    public static long? TryRead()
    {
        var manager = InventoryManager.Instance();
        if (manager == null)
            return null;

        var container = manager->GetInventoryContainer(InventoryType.Currency);
        if (container == null)
            return null;

        // Gil is normally the first slot in the Currency container.
        // Validate this assumption after game patches against current client structs.
        var slot = container->GetInventorySlot(0);
        if (slot == null)
            return null;

        return (long)slot->Quantity;
    }
}
