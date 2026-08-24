namespace Pritset.Models;

/// <summary>Contains values used to update a template.</summary>
public sealed class UpdateTemplateRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public Upload? Template { get; set; }
}
