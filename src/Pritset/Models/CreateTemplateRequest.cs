namespace Pritset.Models;

/// <summary>Contains values used to create a template.</summary>
public sealed class CreateTemplateRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public Upload? Template { get; set; }
}
