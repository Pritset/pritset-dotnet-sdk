using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Pritset.Internal;
using Pritset.Models;

namespace Pritset.Resources;

/// <summary>Document-generation operations.</summary>
public sealed class DocumentsResource
{
    private readonly PritsetClient _client;

    internal DocumentsResource(PritsetClient client) => _client = client;

    public Task<BinaryResponse> GenerateAsync(
        string templateId,
        object? data,
        CancellationToken cancellationToken = default)
    {
        string path = TemplatePath("/api/template/process/direct/", templateId);
        MultipartFormDataContent content = MultipartFactory.Create(("data", DocumentData.Serialize(data)));
        return _client.SendBinaryAsync(HttpMethod.Post, path, content, cancellationToken);
    }

    public Task<WebhookJob> GenerateWebhookAsync(
        string templateId,
        object? data,
        Uri webhookUrl,
        CancellationToken cancellationToken = default)
    {
        ValidateWebhookUrl(webhookUrl);
        MultipartFormDataContent content = MultipartFactory.Create(
            ("data", DocumentData.Serialize(data)),
            ("url", webhookUrl.AbsoluteUri));
        return _client.SendJsonAsync<WebhookJob>(
            HttpMethod.Post,
            TemplatePath("/api/template/process/webhook/", templateId),
            null,
            content,
            cancellationToken);
    }

    private static string TemplatePath(string prefix, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Template ID is required.", nameof(id));
        }
        return prefix + Uri.EscapeDataString(id);
    }

    private static void ValidateWebhookUrl(Uri? webhookUrl)
    {
        if (webhookUrl is null || !webhookUrl.IsAbsoluteUri || string.IsNullOrEmpty(webhookUrl.Host))
        {
            throw new ArgumentException("Webhook URL must be an absolute HTTP(S) URL.", nameof(webhookUrl));
        }
        if (webhookUrl.Scheme != Uri.UriSchemeHttp && webhookUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Webhook URL must use HTTP or HTTPS.", nameof(webhookUrl));
        }
        if (!string.IsNullOrEmpty(webhookUrl.UserInfo))
        {
            throw new ArgumentException("Webhook URL must not contain credentials.", nameof(webhookUrl));
        }
    }
}
