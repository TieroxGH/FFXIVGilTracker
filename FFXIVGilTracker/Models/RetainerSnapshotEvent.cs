using System;

namespace GilTracker.Models;

/// <summary>A point-in-time retainer gil balance tied to its owning character.</summary>
public sealed record RetainerSnapshotEvent(
    string EventId,
    DateTimeOffset Timestamp,
    long Balance,
    string CharacterId,
    string? CharacterName,
    string? World,
    string? DataCenter,
    string RetainerId,
    string RetainerName);
