using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GilTracker.Models;

namespace GilTracker.Storage;

/// <summary>Sends queued retainer snapshots to the private GilTracker API.</summary>
public sealed class RetainerIngestionApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    { PropertyNameCaseInsensitive = true };

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly JsonlRetainerEventQueue queue;
    private readonly string endpoint;
    private readonly string apiKey;

    public RetainerIngestionApiClient(JsonlRetainerEventQueue queue, string baseUrl, string apiKey)
    {
        this.queue = queue;
        endpoint = $"{baseUrl.TrimEnd('/')}/v1/retainers";
        this.apiKey = apiKey.Trim();
    }

    public async Task<bool> FlushAsync(CancellationToken cancellationToken = default)
    {
        var batch = queue.Peek(250);
        if (batch.Count == 0)
            return true;

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(batch, JsonOptions), Encoding.UTF8, "application/json");

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                FFXIVGilTrack.Plugin.Log.Warning($"[GilTracker] Retainer API returned HTTP {(int)response.StatusCode}; queued snapshots will be retried.");
                return false;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("accepted", out var acceptedElement) ||
                !acceptedElement.TryGetInt32(out var accepted) || accepted != batch.Count)
            {
                FFXIVGilTrack.Plugin.Log.Warning("[GilTracker] Retainer API did not acknowledge the full batch; queued snapshots will be retried.");
                return false;
            }

            queue.Acknowledge(batch.Select(item => item.EventId).ToArray());
            return true;
        }
        catch (HttpRequestException ex)
        {
            FFXIVGilTrack.Plugin.Log.Warning(ex, "[GilTracker] Retainer API network error; queued snapshots will be retried.");
            return false;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            FFXIVGilTrack.Plugin.Log.Warning(ex, "[GilTracker] Retainer API timed out; queued snapshots will be retried.");
            return false;
        }
        catch (JsonException ex)
        {
            FFXIVGilTrack.Plugin.Log.Warning(ex, "[GilTracker] Retainer API returned an invalid acknowledgement; queued snapshots will be retried.");
            return false;
        }
    }

    public void Dispose() => http.Dispose();
}
