# Pritset .NET SDK

Official .NET client for managing Pritset DOCX templates and generating PDFs.

Version `0.1.0` targets Pritset SDK contract `1.0.0`.

## Requirements and compatibility

- .NET 8 or .NET 10 for fully supported builds
- Best-effort compatibility with .NET 5, 6, and 7 through `netstandard2.0`
- A Pritset access token and secret

The package targets `netstandard2.0` and `net8.0`. It uses `Task`, `CancellationToken`, `Stream`, and an optional caller-owned `HttpClient`.

## Installation

After the first release is published:

```bash
dotnet add package Pritset --version 0.1.0
```

## Create a client

```csharp
using Pritset;

string token = Environment.GetEnvironmentVariable("PRITSET_ACCESS_TOKEN")
    ?? throw new InvalidOperationException("Set PRITSET_ACCESS_TOKEN.");
string secret = Environment.GetEnvironmentVariable("PRITSET_SECRET")
    ?? throw new InvalidOperationException("Set PRITSET_SECRET.");

using var pritset = new PritsetClient(token, secret);
```

Pritset expects the access token directly in the `Authorization` header—do not add a `Bearer` prefix. The secret is sent in `X-Secret`. Credentials are private, are redacted from `PritsetClient.ToString()`, and should never be committed or logged.

## Generate and save a PDF

Binary responses are streamed. Dispose `BinaryResponse` after reading or saving it.

```csharp
using BinaryResponse pdf = await pritset.Documents.GenerateAsync(
    "template-id",
    new
    {
        invoice = new { number = "INV-1042", customer = "Ada Lovelace" },
    });

await pdf.SaveToFileAsync("invoice.pdf");
```

To process the stream directly:

```csharp
using BinaryResponse pdf = await pritset.Documents.GenerateAsync("template-id", data);
await pdf.Stream.CopyToAsync(destinationStream, cancellationToken);
```

Response metadata is available through `ContentType`, `ContentLength`, and `Trace`.

## Manage templates

```csharp
using Pritset.Models;

TemplatePage page = await pritset.Templates.ListAsync(new ListTemplatesOptions
{
    Query = "invoice",
    Page = 1,
    PageSize = 25,
    SortBy = "name",
    SortDirection = SortDirection.Ascending,
});

TemplateDetails details = await pritset.Templates.GetAsync("template-id");
```

Uploads can come from a path or any readable stream. Path uploads own their file stream and must be disposed. Stream uploads leave the caller's stream open by default.

```csharp
using Upload docx = Upload.FromPath(
    "invoice.docx",
    "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

Template created = await pritset.Templates.CreateAsync(new CreateTemplateRequest
{
    Name = "Monthly invoice",
    Tags = "invoice,monthly",
    Template = docx,
});

Template updated = await pritset.Templates.UpdateAsync(created.Id, new UpdateTemplateRequest
{
    Name = "Monthly invoice v2",
    Tags = "invoice,monthly",
});

using BinaryResponse source = await pritset.Templates.DownloadAsync(created.Id);
await source.SaveToFileAsync("downloaded-template.docx");

await pritset.Templates.DeleteAsync(created.Id);
```

## Validate a template

```csharp
using Upload docx = Upload.FromPath("invoice.docx");
bool valid = await pritset.Templates.ValidateAsync(docx, new
{
    invoice = new { number = "INV-1042" },
});
```

Template validation uses the same token-and-secret authentication as the other public template operations and is supported by contract `1.0.0`.

## Webhook generation

```csharp
WebhookJob job = await pritset.Documents.GenerateWebhookAsync(
    "template-id",
    new { invoice = new { number = "INV-1042" } },
    new Uri("https://example.com/webhooks/pritset"));
```

Webhook URLs must be absolute HTTP(S) URLs and cannot contain embedded credentials. The SDK sends the URL to Pritset; it does not call the webhook itself.

## Raw JSON

Pass a JSON string when data is already encoded. Invalid JSON is rejected before a request is sent.

```csharp
using BinaryResponse pdf = await pritset.Documents.GenerateAsync(
    "template-id",
    "{\"name\":\"Ada\"}");
```

## Errors

```csharp
using Pritset.Exceptions;

try
{
    using BinaryResponse pdf = await pritset.Documents.GenerateAsync("template-id", data);
}
catch (PritsetApiException exception)
{
    Console.WriteLine(exception.StatusCode);
    Console.WriteLine(exception.TraceId);
    Console.WriteLine(exception.RetryAfter);

    foreach ((string field, IReadOnlyList<string> messages) in exception.FieldErrors)
    {
        Console.WriteLine($"{field}: {string.Join(", ", messages)}");
    }
}
catch (PritsetTransportException exception)
{
    Console.WriteLine($"Timeout: {exception.IsTimeout}; canceled: {exception.IsCanceled}");
}
```

API error bodies are capped before being retained. Transport exceptions intentionally do not retain their original exception because it may contain a request with authentication headers. The SDK does not retry automatically in `0.1.x`.

## Timeouts and cancellation

```csharp
using var pritset = new PritsetClient(token, secret, new PritsetClientOptions
{
    Timeout = TimeSpan.FromSeconds(90),
});

using var cancellation = new CancellationTokenSource();
using BinaryResponse pdf = await pritset.Documents.GenerateAsync(
    "template-id",
    data,
    cancellation.Token);
```

## Custom HTTP clients and local development

An injected `HttpClient` remains caller-owned. Because .NET does not expose its handler's redirect policy, the SDK requires an explicit acknowledgment that redirects are disabled.

```csharp
var handler = new HttpClientHandler { AllowAutoRedirect = false };
var httpClient = new HttpClient(handler);

using var pritset = new PritsetClient(token, secret, new PritsetClientOptions
{
    HttpClient = httpClient,
    HttpClientIsRedirectSafe = true,
    Timeout = TimeSpan.FromSeconds(90),
});
```

The SDK-created transport disables redirects so credentials cannot be forwarded to another origin. API base URLs require HTTPS except for exact `localhost`, `127.0.0.1`, or `::1` development endpoints:

```csharp
using var local = new PritsetClient("test-token", "test-secret", new PritsetClientOptions
{
    BaseUrl = new Uri("http://127.0.0.1:8080"),
});
```

## Runnable example

```bash
dotnet run --project examples/Pritset.Example -- template-id
```

The example reads `PRITSET_ACCESS_TOKEN` and `PRITSET_SECRET`, generates a PDF, and saves it as `invoice.pdf`.

## Contract and API documentation

- SDK contract: [`pritset/pritset-sdk-contract`](https://github.com/pritset/pritset-sdk-contract), version `1.0.0`
- API documentation: [pritset.com/docs/api](https://pritset.com/docs/api)

## Development

```powershell
pwsh ./scripts/verify-contract.ps1
dotnet restore Pritset.sln --locked-mode
dotnet format Pritset.sln --verify-no-changes --no-restore
dotnet build Pritset.sln -c Release --no-restore
dotnet test tests/Pritset.Tests/Pritset.Tests.csproj -c Release --no-build
dotnet pack src/Pritset/Pritset.csproj -c Release -o artifacts
```

See [CONTRIBUTING.md](CONTRIBUTING.md) and [SECURITY.md](SECURITY.md).

## License

MIT © Pritset. See [LICENSE](LICENSE).
