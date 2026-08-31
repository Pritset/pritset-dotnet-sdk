using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pritset.Exceptions;
using Pritset.Internal;
using Pritset.Resources;

namespace Pritset;

/// <summary>Client for the Pritset Document API.</summary>
public sealed class PritsetClient : IDisposable
{
    /// <summary>The semantic version of this SDK.</summary>
    public const string Version = "0.1.5";

    private readonly string _accessToken;
    private readonly string _secret;
    private readonly Uri _baseUrl;
    private readonly TimeSpan _timeout;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    /// <summary>Creates a Pritset client.</summary>
    public PritsetClient(string accessToken, string secret, PritsetClientOptions? options = null)
    {
        ValidateCredential(accessToken, nameof(accessToken));
        ValidateCredential(secret, nameof(secret));
        options ??= new PritsetClientOptions();
        _baseUrl = ValidateBaseUrl(options.BaseUrl);
        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Timeout must be greater than zero.");
        }

        _accessToken = accessToken;
        _secret = secret;
        _timeout = options.Timeout;
        if (options.HttpClient is null)
        {
            _httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }, disposeHandler: true)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
            _ownsHttpClient = true;
        }
        else
        {
            if (!options.HttpClientIsRedirectSafe)
            {
                throw new ArgumentException(
                    "Injected HttpClient instances must have automatic redirects disabled and HttpClientIsRedirectSafe set to true.",
                    nameof(options));
            }
            _httpClient = options.HttpClient;
            _ownsHttpClient = false;
        }

        Templates = new TemplatesResource(this);
        Documents = new DocumentsResource(this);
    }

    /// <summary>Gets template-management operations.</summary>
    public TemplatesResource Templates { get; }

    /// <summary>Gets document-generation operations.</summary>
    public DocumentsResource Documents { get; }

    /// <inheritdoc />
    public override string ToString() => $"PritsetClient(BaseUrl={_baseUrl.GetLeftPart(UriPartial.Path)}, Credentials=[REDACTED])";

    internal async Task<T> SendJsonAsync<T>(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string?>? query,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, query, content, "application/json");
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
#if NET8_0_OR_GREATER
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
        try
        {
            T? result = JsonConvert.DeserializeObject<T>(json, JsonDefaults.Settings);
            return result is null
                ? throw new PritsetTransportException("Pritset returned an unexpected JSON response.")
                : result;
        }
        catch (JsonException)
        {
            throw new PritsetTransportException("Pritset returned an invalid JSON response.");
        }
    }

    internal async Task<bool> SendBooleanAsync(
        HttpMethod method,
        string path,
        HttpContent content,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, null, content, "application/json");
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
#if NET8_0_OR_GREATER
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
        try
        {
            JToken document = JToken.Parse(json);
            if (document.Type != JTokenType.Boolean)
            {
                throw new PritsetTransportException("Pritset returned an unexpected validation response.");
            }
            return document.Value<bool>();
        }
        catch (JsonException)
        {
            throw new PritsetTransportException("Pritset returned an invalid JSON response.");
        }
    }

    internal async Task SendEmptyAsync(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, null, null, "application/json");
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<BinaryResponse> SendBinaryAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, null, content, "*/*");
        HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
#if NET8_0_OR_GREATER
            Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
            Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
            return new BinaryResponse(response, stream);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PritsetClient));
        }
#endif
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            PritsetApiException apiException = await ErrorParser.ParseAsync(response, timeout.Token).ConfigureAwait(false);
            response.Dispose();
            throw apiException;
        }
        catch (PritsetApiException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            bool canceled = cancellationToken.IsCancellationRequested;
            throw new PritsetTransportException(
                canceled ? "The Pritset request was canceled." : "The Pritset request timed out.",
                isTimeout: !canceled,
                isCanceled: canceled);
        }
        catch (Exception)
        {
            throw new PritsetTransportException("The request to Pritset failed before a response was received.");
        }
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string?>? query,
        HttpContent? content,
        string accept)
    {
        var request = new HttpRequestMessage(method, BuildUri(path, query)) { Content = content };
        request.Headers.TryAddWithoutValidation("Authorization", _accessToken);
        request.Headers.TryAddWithoutValidation("X-Secret", _secret);
        request.Headers.UserAgent.ParseAdd("pritset-dotnet/" + Version);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        return request;
    }

    private Uri BuildUri(string path, IReadOnlyDictionary<string, string?>? query)
    {
        string baseText = _baseUrl.AbsoluteUri.TrimEnd('/');
        var builder = new UriBuilder(baseText + "/" + path.TrimStart('/'));
        if (query is not null)
        {
            var pairs = new List<string>();
            foreach (KeyValuePair<string, string?> item in query)
            {
                if (item.Value is not null)
                {
                    pairs.Add(Uri.EscapeDataString(item.Key) + "=" + Uri.EscapeDataString(item.Value));
                }
            }
            builder.Query = string.Join("&", pairs);
        }
        return builder.Uri;
    }

    private static Uri ValidateBaseUrl(Uri? baseUrl)
    {
        if (baseUrl is null || !baseUrl.IsAbsoluteUri || string.IsNullOrEmpty(baseUrl.Host))
        {
            throw new ArgumentException("Base URL must be an absolute HTTP(S) URL.", nameof(baseUrl));
        }
        if (!string.IsNullOrEmpty(baseUrl.UserInfo) || !string.IsNullOrEmpty(baseUrl.Query) || !string.IsNullOrEmpty(baseUrl.Fragment))
        {
            throw new ArgumentException("Base URL must not contain credentials, a query, or a fragment.", nameof(baseUrl));
        }

        bool https = string.Equals(baseUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        string host = baseUrl.Host.Trim('[', ']');
        bool loopback = string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host == "127.0.0.1"
            || host == "::1";
        bool loopbackHttp = string.Equals(baseUrl.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && loopback;
        if (!https && !loopbackHttp)
        {
            throw new ArgumentException("Base URL must use HTTPS; HTTP is permitted only for loopback development.", nameof(baseUrl));
        }
        return new Uri(baseUrl.AbsoluteUri.TrimEnd('/'), UriKind.Absolute);
    }

    private static void ValidateCredential(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Credential is required.", parameterName);
        }
        if (value.Contains("\r") || value.Contains("\n"))
        {
            throw new ArgumentException("Credential must not contain line breaks.", parameterName);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
