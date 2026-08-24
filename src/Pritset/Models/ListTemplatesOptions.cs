namespace Pritset.Models;

/// <summary>Controls template pagination and sorting.</summary>
public sealed class ListTemplatesOptions
{
    public string? Query { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public string? SortBy { get; set; }
    public SortDirection? SortDirection { get; set; }
}

/// <summary>Specifies template sort direction.</summary>
public enum SortDirection
{
    Ascending = 0,
    Descending = 1,
}
