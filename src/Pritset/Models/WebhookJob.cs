using Newtonsoft.Json;

namespace Pritset.Models;

/// <summary>An asynchronous webhook generation job.</summary>
public sealed class WebhookJob
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;
}
