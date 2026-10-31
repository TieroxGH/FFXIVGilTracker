using System.Text.Json;
using GilTracker.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GilTracker.Storage;

/// <summary>
/// Small durable queue. Each event is one JSON line. A successful delivery
/// removes only the batch that was acknowledged by the server.
/// </summary>
public sealed class JsonlEventQueue
{
    private readonly string path;
    private readonly object gate = new();

    public JsonlEventQueue(string pluginConfigDirectory)
    {
        Directory.CreateDirectory(pluginConfigDirectory);
        path = Path.Combine(pluginConfigDirectory, "gil-events.jsonl");
    }

    public void Enqueue(GilEvent item)
    {
        var json = JsonSerializer.Serialize(item);
        lock (gate)
            File.AppendAllText(path, json + Environment.NewLine);
    }

    public IReadOnlyList<GilEvent> Peek(int maxItems = 250)
    {
        lock (gate)
        {
            if (!File.Exists(path))
                return Array.Empty<GilEvent>();

            var lines = File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            var allItems = new List<GilEvent>(lines.Length);
            var rewrittenLines = new List<string>(lines.Length);
            var migrated = false;
            foreach (var line in lines)
            {
                try
                {
                    var item = JsonSerializer.Deserialize<GilEvent>(line);
                    if (item is null)
                    {
                        rewrittenLines.Add(line);
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(item.EventId))
                    {
                        item = item with { EventId = Guid.NewGuid().ToString("N") };
                        migrated = true;
                    }
                    allItems.Add(item);
                    rewrittenLines.Add(JsonSerializer.Serialize(item));
                }
                catch (JsonException)
                {
                    // Keep malformed lines on disk for manual recovery.
                    rewrittenLines.Add(line);
                }
            }

            // Persist generated IDs once, so retries keep the same event identity.
            if (migrated)
            {
                var temp = path + ".tmp";
                File.WriteAllLines(temp, rewrittenLines);
                File.Move(temp, path, overwrite: true);
            }

            return allItems.Take(maxItems).ToArray();
        }
    }

    public void Acknowledge(IReadOnlyCollection<string> eventIds)
    {
        if (eventIds is null || eventIds.Count == 0) return;

        // Acknowledge by stable event ID rather than physical line count. This
        // preserves malformed/blank lines and avoids removing the wrong event
        // when the JSONL file contains a line that Peek() could not deserialize.
        var acknowledgedIds = new HashSet<string>(eventIds, StringComparer.Ordinal);

        lock (gate)
        {
            if (!File.Exists(path)) return;

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
                    var item = JsonSerializer.Deserialize<GilEvent>(line);
                    if (item?.EventId is not null && acknowledgedIds.Contains(item.EventId))
                        continue;
                }
                catch (JsonException)
                {
                    // Keep malformed lines for manual recovery; never let them
                    // shift the acknowledgement position for valid events.
                }

                remaining.Add(line);
            }

            var temp = path + ".tmp";
            File.WriteAllLines(temp, remaining);
            File.Move(temp, path, overwrite: true);
        }
    }
}
