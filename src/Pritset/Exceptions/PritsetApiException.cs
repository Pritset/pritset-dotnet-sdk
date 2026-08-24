using System;
using System.Collections.Generic;

namespace Pritset.Exceptions;

/// <summary>Represents a non-success response from the Pritset API.</summary>
public sealed class PritsetApiException : Exception
{
    internal PritsetApiException(
        string message,
        int statusCode,
        IReadOnlyDictionary<string, IReadOnlyList<string>> fieldErrors,
        string? traceId,
        string? retryAfter,
        string responseBody)
        : base(message)
    {
        StatusCode = statusCode;
        FieldErrors = fieldErrors;
        TraceId = traceId;
        RetryAfter = retryAfter;
        ResponseBody = responseBody;
    }

    /// <summary>Gets the HTTP status code.</summary>
    public int StatusCode { get; }

    /// <summary>Gets normalized field validation messages.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> FieldErrors { get; }

    /// <summary>Gets the API trace identifier or timing header, when present.</summary>
    public string? TraceId { get; }

    /// <summary>Gets the Retry-After header, when present.</summary>
    public string? RetryAfter { get; }

    /// <summary>Gets the capped raw response body.</summary>
    public string ResponseBody { get; }
}
