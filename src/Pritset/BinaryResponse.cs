using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Pritset;

/// <summary>Wraps a streaming binary response.</summary>
public sealed class BinaryResponse : IDisposable
{
    private readonly HttpResponseMessage _response;

    internal BinaryResponse(HttpResponseMessage response, Stream stream)
    {
        _response = response;
        Stream = stream;
        ContentType = response.Content.Headers.ContentType?.MediaType;
        ContentLength = response.Content.Headers.ContentLength;
        Trace = response.Headers.TryGetValues("X-Trace", out var values) ? string.Join(",", values) : null;
    }

    /// <summary>Gets the response stream.</summary>
    public Stream Stream { get; }

    /// <summary>Gets the response media type.</summary>
    public string? ContentType { get; }

    /// <summary>Gets the response content length.</summary>
    public long? ContentLength { get; }

    /// <summary>Gets serialized processing timing diagnostics.</summary>
    public string? Trace { get; }

    /// <summary>Saves the remaining stream contents to a file.</summary>
    public async Task SaveToFileAsync(string path, CancellationToken cancellationToken = default)
    {
        using var destination = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await Stream.CopyToAsync(destination, 81920, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stream.Dispose();
        _response.Dispose();
    }
}
