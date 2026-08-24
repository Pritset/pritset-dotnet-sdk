using System;
using System.IO;
using System.Net.Http.Headers;

namespace Pritset;

/// <summary>Describes a DOCX upload backed by a stream.</summary>
public sealed class Upload : IDisposable
{
    private readonly bool _ownsStream;

    private Upload(Stream stream, string fileName, string? contentType, bool ownsStream)
    {
        if (stream is null || !stream.CanRead)
        {
            throw new ArgumentException("Upload stream must be readable.", nameof(stream));
        }
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("\r") || fileName.Contains("\n"))
        {
            throw new ArgumentException("Upload filename must not be empty or contain line breaks.", nameof(fileName));
        }
        if (contentType is not null && !MediaTypeHeaderValue.TryParse(contentType, out _))
        {
            throw new ArgumentException("Upload content type is invalid.", nameof(contentType));
        }

        Stream = stream;
        FileName = fileName;
        ContentType = contentType;
        _ownsStream = ownsStream;
    }

    /// <summary>Gets the upload stream.</summary>
    public Stream Stream { get; }

    /// <summary>Gets the multipart filename.</summary>
    public string FileName { get; }

    /// <summary>Gets the optional media type.</summary>
    public string? ContentType { get; }

    /// <summary>Creates a caller-owned stream upload.</summary>
    public static Upload FromStream(Stream stream, string fileName, string? contentType = null, bool leaveOpen = true) =>
        new Upload(stream, fileName, contentType, ownsStream: !leaveOpen);

    /// <summary>Opens a file-backed upload. Dispose the upload after the request.</summary>
    public static Upload FromPath(string path, string? contentType = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Upload path is required.", nameof(path));
        }
        try
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return new Upload(stream, Path.GetFileName(path), contentType, ownsStream: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ArgumentException("Upload path is not a readable file.", nameof(path));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsStream)
        {
            Stream.Dispose();
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"Upload(FileName={FileName}, ContentType={ContentType ?? "application/octet-stream"})";
}
