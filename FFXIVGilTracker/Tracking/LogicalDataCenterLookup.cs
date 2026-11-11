using System;
using System.Collections.Generic;

namespace FFXIVGilTrack.Tracking;

/// <summary>
/// Resolves a world's logical data center. Since same-region matchmaking was added,
/// the World sheet's DataCenter link no longer reliably identifies the logical DC.
/// Keep this list aligned with Square Enix's current world groupings.
/// </summary>
public static class LogicalDataCenterLookup
{
    private static readonly IReadOnlyDictionary<string, string> DataCenterByWorld =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // North America
            ["Adamantoise"] = "Aether",
            ["Cactuar"] = "Aether",
            ["Faerie"] = "Aether",
            ["Gilgamesh"] = "Aether",
            ["Jenova"] = "Aether",
            ["Midgardsormr"] = "Aether",
            ["Sargatanas"] = "Aether",
            ["Siren"] = "Aether",
            ["Balmung"] = "Crystal",
            ["Brynhildr"] = "Crystal",
            ["Coeurl"] = "Crystal",
            ["Diabolos"] = "Crystal",
            ["Goblin"] = "Crystal",
            ["Malboro"] = "Crystal",
            ["Mateus"] = "Crystal",
            ["Zalera"] = "Crystal",
            ["Cuchulainn"] = "Dynamis",
            ["Golem"] = "Dynamis",
            ["Halicarnassus"] = "Dynamis",
            ["Kraken"] = "Dynamis",
            ["Maduin"] = "Dynamis",
            ["Marilith"] = "Dynamis",
            ["Rafflesia"] = "Dynamis",
            ["Seraph"] = "Dynamis",
            ["Behemoth"] = "Primal",
            ["Excalibur"] = "Primal",
            ["Exodus"] = "Primal",
            ["Famfrit"] = "Primal",
            ["Hyperion"] = "Primal",
            ["Lamia"] = "Primal",
            ["Leviathan"] = "Primal",
            ["Ultros"] = "Primal",

            // Europe
            ["Cerberus"] = "Chaos",
            ["Louisoix"] = "Chaos",
            ["Moogle"] = "Chaos",
            ["Omega"] = "Chaos",
            ["Phantom"] = "Chaos",
            ["Ragnarok"] = "Chaos",
            ["Sagittarius"] = "Chaos",
            ["Spriggan"] = "Chaos",
            ["Alpha"] = "Light",
            ["Lich"] = "Light",
            ["Odin"] = "Light",
            ["Phoenix"] = "Light",
            ["Raiden"] = "Light",
            ["Shiva"] = "Light",
            ["Twintania"] = "Light",
            ["Zodiark"] = "Light",

            // Oceania
            ["Bismarck"] = "Materia",
            ["Ravana"] = "Materia",
            ["Sephirot"] = "Materia",
            ["Sophia"] = "Materia",
            ["Zurvan"] = "Materia",

            // Japan
            ["Aegis"] = "Elemental",
            ["Atomos"] = "Elemental",
            ["Carbuncle"] = "Elemental",
            ["Garuda"] = "Elemental",
            ["Gungnir"] = "Elemental",
            ["Kujata"] = "Elemental",
            ["Tonberry"] = "Elemental",
            ["Typhon"] = "Elemental",
            ["Alexander"] = "Gaia",
            ["Bahamut"] = "Gaia",
            ["Durandal"] = "Gaia",
            ["Fenrir"] = "Gaia",
            ["Ifrit"] = "Gaia",
            ["Ridill"] = "Gaia",
            ["Tiamat"] = "Gaia",
            ["Ultima"] = "Gaia",
            ["Anima"] = "Mana",
            ["Asura"] = "Mana",
            ["Chocobo"] = "Mana",
            ["Hades"] = "Mana",
            ["Ixion"] = "Mana",
            ["Masamune"] = "Mana",
            ["Pandaemonium"] = "Mana",
            ["Titan"] = "Mana",
            ["Belias"] = "Meteor",
            ["Mandragora"] = "Meteor",
            ["Ramuh"] = "Meteor",
            ["Shinryu"] = "Meteor",
            ["Unicorn"] = "Meteor",
            ["Valefor"] = "Meteor",
            ["Yojimbo"] = "Meteor",
            ["Zeromus"] = "Meteor",
        };

    public static string GetName(string homeWorldName) =>
        DataCenterByWorld.TryGetValue(homeWorldName, out var dataCenter)
            ? dataCenter
            : "Unknown";
}
