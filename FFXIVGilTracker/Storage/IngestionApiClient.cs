using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GilTracker.Models;

namespace GilTracker.Storage;

/// <summary>
/// Sends queued events to the private GilTracker API. InfluxDB credentials never
/// leave the server. Events remain queued until the API acknowledges the batch.
/// </summary>
public sealed class IngestionApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    { PropertyNameCaseInsensitive = true };

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly JsonlEventQueue queue;
    private readonly string endpoint;
    private readonly string apiKey;

    public IngestionApiClient(JsonlEventQueue queue, string baseUrl, string apiKey)
    {
        this.queue = queue;
        endpoint = $"{baseUrl.TrimEnd('/')}/v1/events";
        this.apiKey = apiKey.Trim();
    }

    public async Task<bool> FlushAsync(CancellationToken cancellationToken = default)
    {
        var batch = queue.Peek(250);
        if (batch.Count == 0) return true;

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(batch, JsonOptions), Encoding.UTF8, "application/json");

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // Never log credentials or request headers. The API returns generic error bodies.
                FFXIVGilTrack.Plugin.Log.Warning($"[GilTracker] Submission API returned HTTP {(int)response.StatusCode}; queued events will be retried.");
                return false;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("accepted", out var acceptedElement) ||
                !acceptedElement.TryGetInt32(out var accepted) || accepted != batch.Count)
            {
                FFXIVGilTrack.Plugin.Log.Warning("[GilTracker] Submission API response did not acknowledge the full batch; queued events will be retried.");
                return false;
            }

            queue.Acknowledge(batch.Select(e => e.EventId).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!).ToArray());
            return true;
        }
        catch (HttpRequestException ex)
        {
            FFXIVGilTrack.Plugin.Log.Warning(ex, "[GilTracker] Submission API network error; queued events will be retried.");
            return false;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            FFXIVGilTrack.Plugin.Log.Warning(ex, "[GilTracker] Submission API timed out; queued events will be retried.");
            return false;
        }
        catch (JsonException ex)
        {
            FFXIVGilTrack.Plugin.Log.Warning(ex, "[GilTracker] Submission API returned an invalid acknowledgement; queued events will be retried.");
            return false;
        }
    }

    public void Dispose() => http.Dispose();
}
