using System.Collections.Generic;
using Newtonsoft.Json;

namespace Pritset.Models;

/// <summary>A page of templates.</summary>
public sealed class TemplatePage
{
    [JsonProperty("data")]
    public IReadOnlyList<Template> Data { get; set; } = new List<Template>();

    [JsonProperty("total")]
    public int Total { get; set; }
}
