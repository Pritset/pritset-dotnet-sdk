using System;

namespace Pritset.Exceptions;

/// <summary>Represents a transport failure without a usable API response.</summary>
public sealed class PritsetTransportException : Exception
{
    internal PritsetTransportException(string message, bool isTimeout = false, bool isCanceled = false)
        : base(message)
    {
        IsTimeout = isTimeout;
        IsCanceled = isCanceled;
    }

    /// <summary>Gets whether the SDK request timeout elapsed.</summary>
    public bool IsTimeout { get; }

    /// <summary>Gets whether the caller canceled the request.</summary>
    public bool IsCanceled { get; }
}
