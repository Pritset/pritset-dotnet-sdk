using System;
using System.Net.Http;

namespace Pritset;

/// <summary>Configures a <see cref="PritsetClient"/>.</summary>
public sealed class PritsetClientOptions
{
    /// <summary>Gets or sets the Pritset API base URL.</summary>
    public Uri BaseUrl { get; set; } = new Uri("https://api.pritset.com", UriKind.Absolute);

    /// <summary>Gets or sets the total request timeout.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets an optional caller-owned HTTP client.</summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>
    /// Confirms that an injected HTTP client has automatic redirects disabled.
    /// This acknowledgment is required because <see cref="HttpClient"/> does not
    /// expose its underlying redirect policy for inspection.
    /// </summary>
    public bool HttpClientIsRedirectSafe { get; set; }
}
