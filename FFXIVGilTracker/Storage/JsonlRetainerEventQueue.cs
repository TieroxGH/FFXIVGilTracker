using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GilTracker.Models;

namespace GilTracker.Storage;

/// <summary>Durable queue for retainer balance snapshots awaiting API acknowledgement.</summary>
public sealed class JsonlRetainerEventQueue
{
    private readonly string path;
    private readonly object gate = new();

    public JsonlRetainerEventQueue(string pluginConfigDirectory)
    {
        Directory.CreateDirectory(pluginConfigDirectory);
        path = Path.Combine(pluginConfigDirectory, "retainer-events.jsonl");
    }

    public void Enqueue(RetainerSnapshotEvent item)
    {
        var json = JsonSerializer.Serialize(item);
        lock (gate)
            File.AppendAllText(path, json + Environment.NewLine);
    }

    public IReadOnlyList<RetainerSnapshotEvent> Peek(int maxItems = 250)
    {
        lock (gate)
        {
            if (!File.Exists(path))
                return Array.Empty<RetainerSnapshotEvent>();

            var items = new List<RetainerSnapshotEvent>();
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var item = JsonSerializer.Deserialize<RetainerSnapshotEvent>(line);
                    if (item is not null)
                        items.Add(item);
                }
                catch (JsonException)
                {
                    // Keep malformed lines on disk so data can be recovered manually.
                }
            }

            return items.Take(maxItems).ToArray();
        }
    }

    public void Acknowledge(IReadOnlyCollection<string> eventIds)
    {
        if (eventIds is null || eventIds.Count == 0)
            return;

        var acknowledgedIds = new HashSet<string>(eventIds, StringComparer.Ordinal);
        lock (gate)
        {
            if (!File.Exists(path))
                return;

            var remaining = new List<string>();
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    remaining.Add(line);
                    continue;
                }

                try
                {
                    var item = JsonSerializer.Deserialize<RetainerSnapshotEvent>(line);
                    if (item is not null && !string.IsNullOrWhiteSpace(item.EventId) && acknowledgedIds.Contains(item.EventId))
                        continue;
                }
                catch (JsonException)
                {
                    // Preserve malformed lines rather than discarding them.
                }

                remaining.Add(line);
            }

            var temp = path + ".tmp";
            File.WriteAllLines(temp, remaining);
            File.Move(temp, path, overwrite: true);
        }
    }
}
