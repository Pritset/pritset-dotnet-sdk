using Newtonsoft.Json;

namespace Pritset.Models;

/// <summary>A reusable Pritset document template.</summary>
public sealed class Template
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("tags")]
    public string? Tags { get; set; }

    [JsonProperty("templateObject")]
    public string? TemplateObject { get; set; }
}
