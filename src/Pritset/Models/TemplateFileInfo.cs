using System;
using Newtonsoft.Json;

namespace Pritset.Models;

/// <summary>Metadata for a stored template file.</summary>
public sealed class TemplateFileInfo
{
    [JsonProperty("contentType")]
    public string ContentType { get; set; } = string.Empty;

    [JsonProperty("lastModified")]
    public DateTimeOffset LastModified { get; set; }

    [JsonProperty("objectName")]
    public string ObjectName { get; set; } = string.Empty;

    [JsonProperty("size")]
    public long Size { get; set; }
}
