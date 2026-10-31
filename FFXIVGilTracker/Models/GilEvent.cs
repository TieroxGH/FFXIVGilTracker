using System;

namespace GilTracker.Models;

public sealed record GilEvent(
    DateTimeOffset Timestamp,
    long Balance,
    long Delta,
    string Category,
    string Description,
    string Confidence)
{
    // Created once when enqueued and persisted in the JSONL queue for safe retries.
    public string? EventId { get; init; }

    // Stable pseudonymous character identity and display label captured when the event occurs.
    // Nullable for backward compatibility with already queued JSONL events.
    public string? CharacterId { get; init; }
    public string? CharacterName { get; init; }

    // Home world and data center are captured with the event for dashboard filtering.
    // Nullable for backward compatibility with already queued JSONL events.
    public string? World { get; init; }
    public string? DataCenter { get; init; }
}
