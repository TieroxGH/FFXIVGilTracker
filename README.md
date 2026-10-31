# FFXIV Gil Tracker

A Dalamud plugin that tracks Final Fantasy XIV gil balance changes and can submit events to a private HTTPS ingestion API for dashboards.

## Features

- Tracks gil balance and detected changes.
- Identifies characters using a pseudonymous hash rather than sending the raw game Content ID.
- Can queue events locally and submit them to a configured HTTPS API.
- Supports character identity and home-world/data-center tags.
