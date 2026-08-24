using Newtonsoft.Json;

namespace Pritset.Models;

/// <summary>A template and its stored-file metadata.</summary>
public sealed class TemplateDetails
{
    [JsonProperty("template")]
    public Template Template { get; set; } = new Template();

    [JsonProperty("fileInfo")]
    public TemplateFileInfo FileInfo { get; set; } = new TemplateFileInfo();
}
