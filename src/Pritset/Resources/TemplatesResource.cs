using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Pritset.Internal;
using Pritset.Models;

namespace Pritset.Resources;

/// <summary>Template-management operations.</summary>
public sealed class TemplatesResource
{
    private readonly PritsetClient _client;

    internal TemplatesResource(PritsetClient client) => _client = client;

    public Task<TemplatePage> ListAsync(
        ListTemplatesOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ListTemplatesOptions();
        if (options.Page < 1 || options.PageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Page and page size must be positive.");
        }
        var query = new Dictionary<string, string?>
        {
            ["q"] = options.Query,
            ["p"] = options.Page.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["s"] = options.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["sorts[0].sortBy"] = options.SortBy,
            ["sorts[0].sortDirection"] = options.SortDirection is null
                ? null
                : ((int)options.SortDirection.Value).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return _client.SendJsonAsync<TemplatePage>(HttpMethod.Get, "/api/template", query, null, cancellationToken);
    }

    public Task<TemplateDetails> GetAsync(string id, CancellationToken cancellationToken = default) =>
        _client.SendJsonAsync<TemplateDetails>(HttpMethod.Get, TemplatePath("/api/template/", id), null, null, cancellationToken);

    public Task<Template> CreateAsync(CreateTemplateRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }
        Required(request.Name, "Template name");
        if (request.Template is null)
        {
            throw new ArgumentException("Template upload is required.", nameof(request));
        }
        var fields = new List<(string Name, string Value)> { ("name", request.Name) };
        if (request.Tags is not null)
        {
            fields.Add(("tags", request.Tags));
        }
        MultipartFormDataContent content = MultipartFactory.Create(fields.ToArray());
        MultipartFactory.AddUpload(content, "template", request.Template);
        return _client.SendJsonAsync<Template>(HttpMethod.Post, "/api/template", null, content, cancellationToken);
    }

    public Task<Template> UpdateAsync(
        string id,
        UpdateTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }
        Required(request.Name, "Template name");
        var fields = new List<(string Name, string Value)> { ("name", request.Name) };
        if (request.Tags is not null)
        {
            fields.Add(("tags", request.Tags));
        }
        MultipartFormDataContent content = MultipartFactory.Create(fields.ToArray());
        if (request.Template is not null)
        {
            MultipartFactory.AddUpload(content, "template", request.Template);
        }
        return _client.SendJsonAsync<Template>(HttpMethod.Put, TemplatePath("/api/template/", id), null, content, cancellationToken);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        _client.SendEmptyAsync(HttpMethod.Delete, TemplatePath("/api/template/", id), cancellationToken);

    public Task<BinaryResponse> DownloadAsync(string id, CancellationToken cancellationToken = default) =>
        _client.SendBinaryAsync(HttpMethod.Get, TemplatePath("/api/template/download/", id), null, cancellationToken);

    public Task<bool> ValidateAsync(Upload upload, object? data, CancellationToken cancellationToken = default)
    {
        if (upload is null)
        {
            throw new ArgumentNullException(nameof(upload));
        }
        MultipartFormDataContent content = MultipartFactory.Create(("data", DocumentData.Serialize(data)));
        MultipartFactory.AddUpload(content, "file", upload);
        return _client.SendBooleanAsync(HttpMethod.Post, "/api/template/process/validate", content, cancellationToken);
    }

    private static string TemplatePath(string prefix, string id)
    {
        Required(id, "Template ID");
        return prefix + Uri.EscapeDataString(id);
    }

    private static void Required(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(label + " is required.");
        }
    }
}
