using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pritset.Exceptions;

namespace Pritset.Internal;

internal static class ErrorParser
{
    private const int MaxErrorBodyBytes = 64 * 1024;

    internal static async Task<PritsetApiException> ParseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string body = await ReadLimitedAsync(response.Content, cancellationToken).ConfigureAwait(false);
        string message = $"Pritset API request failed with status {(int)response.StatusCode}.";
        string? traceId = Header(response, "X-Trace");
        var fieldErrors = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        try
        {
            if (JToken.Parse(body) is JObject root)
            {
                if (root["title"]?.Type == JTokenType.String)
                {
                    message = root.Value<string>("title") ?? message;
                }
                if (root["traceId"]?.Type == JTokenType.String)
                {
                    traceId = root.Value<string>("traceId");
                }

                JObject source = root["errors"] as JObject ?? root;
                foreach (JProperty property in source.Properties())
                {
                    if (property.Name is "type" or "title" or "status" or "traceId")
                    {
                        continue;
                    }
                    if (property.Value.Type == JTokenType.String)
                    {
                        fieldErrors[property.Name] = new[] { property.Value.Value<string>() ?? string.Empty };
                    }
                    else if (property.Value is JArray array)
                    {
                        var messages = new List<string>();
                        foreach (JToken item in array)
                        {
                            if (item.Type == JTokenType.String && item.Value<string>() is string itemMessage)
                            {
                                messages.Add(itemMessage);
                            }
                        }
                        if (messages.Count > 0)
                        {
                            fieldErrors[property.Name] = messages;
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                message = body.Trim();
            }
        }

        return new PritsetApiException(
            message,
            (int)response.StatusCode,
            fieldErrors,
            traceId,
            Header(response, "Retry-After"),
            body);
    }

    private static async Task<string> ReadLimitedAsync(HttpContent content, CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
        Stream stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int remaining = MaxErrorBodyBytes;
        while (remaining > 0)
        {
#if NET8_0_OR_GREATER
            int read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, remaining)), cancellationToken).ConfigureAwait(false);
#else
            int read = await stream.ReadAsync(chunk, 0, Math.Min(chunk.Length, remaining), cancellationToken).ConfigureAwait(false);
#endif
            if (read == 0)
            {
                break;
            }
            buffer.Write(chunk, 0, read);
            remaining -= read;
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string? Header(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out IEnumerable<string>? values)
            ? string.Join(",", values)
            : null;
    }
}
