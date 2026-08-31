using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Pritset.Exceptions;
using Pritset.Models;
using Xunit;

namespace Pritset.Tests;

public sealed class PritsetClientTests
{
    [Fact]
    public async Task ListAsyncUsesContractFixtureAndAuthenticationHeaders()
    {
        using PritsetClient client = Client((request, _) =>
        {
            Assert.Equal("/v1/api/template", request.RequestUri!.AbsolutePath);
            Assert.Equal("access-token", Header(request, "Authorization"));
            Assert.Equal("client-secret", Header(request, "X-Secret"));
            Assert.Equal("pritset-dotnet/0.1.5", request.Headers.UserAgent.ToString());
            Assert.Equal("application/json", request.Headers.Accept.Single().MediaType);
            string query = request.RequestUri.Query;
            Assert.Contains("q=invoice", query);
            Assert.Contains("p=2", query);
            Assert.Contains("sorts%5B0%5D.sortBy=name", query);
            return Task.FromResult(JsonResponse(Fixture("templates/list.json")));
        });

        TemplatePage page = await client.Templates.ListAsync(new ListTemplatesOptions
        {
            Query = "invoice",
            Page = 2,
            PageSize = 25,
            SortBy = "name",
            SortDirection = SortDirection.Ascending,
        });

        Assert.Equal(1, page.Total);
        Assert.Equal("Monthly invoice", Assert.Single(page.Data).Name);
    }

    [Fact]
    public async Task GetAsyncPreservesEscapedIdAndDeserializesDetails()
    {
        using PritsetClient client = Client((request, _) =>
        {
            Assert.Contains("/api/template/a%2Fb", request.RequestUri!.OriginalString);
            return Task.FromResult(JsonResponse(Fixture("templates/get.json")));
        });

        TemplateDetails details = await client.Templates.GetAsync("a/b");
        Assert.Equal("a1b2c3d4e5f6", details.Template.Id);
        Assert.Equal(24576, details.FileInfo.Size);
        Assert.Equal(DateTimeOffset.Parse("2026-07-15T09:30:00Z", CultureInfo.InvariantCulture), details.FileInfo.LastModified);
    }

    [Fact]
    public async Task CreateAndUpdateStreamMultipartTemplates()
    {
        int calls = 0;
        using PritsetClient client = Client(async (request, cancellationToken) =>
        {
            calls++;
            MultipartFormDataContent multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (calls == 1)
            {
                Assert.Equal(
                    "invoice.docx",
                    Part(multipart, "template").Headers.ContentDisposition!.FileName!.Trim('"'));
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Contains("Invoice", body);
                Assert.Contains("docx-bytes", body);
                return JsonResponse("""{"id":"new","name":"Invoice","tags":"billing"}""");
            }
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Contains("Invoice v2", body);
            return JsonResponse("""{"id":"new","name":"Invoice v2","tags":null}""");
        });

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("docx-bytes"));
        using Upload upload = Upload.FromStream(stream, "invoice.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        Template created = await client.Templates.CreateAsync(new CreateTemplateRequest
        {
            Name = "Invoice",
            Tags = "billing",
            Template = upload,
        });
        Assert.Equal("new", created.Id);
        Assert.True(stream.CanRead);

        Template updated = await client.Templates.UpdateAsync("new", new UpdateTemplateRequest { Name = "Invoice v2" });
        Assert.Equal("Invoice v2", updated.Name);
    }

    [Fact]
    public async Task ValidateAsyncSendsJsonAndReturnsBoolean()
    {
        using PritsetClient client = Client(async (request, cancellationToken) =>
        {
            MultipartFormDataContent multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            Assert.Equal(
                "template.docx",
                Part(multipart, "file").Headers.ContentDisposition!.FileName!.Trim('"'));
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Contains("{\"invoice\":{\"number\":42}}", body);
            return JsonResponse("true");
        });
        using Upload upload = Upload.FromStream(new MemoryStream(Encoding.UTF8.GetBytes("docx")), "template.docx");

        bool valid = await client.Templates.ValidateAsync(upload, new { invoice = new { number = 42 } });
        Assert.True(valid);
    }

    [Fact]
    public async Task GenerateAndDownloadReturnStreamingResponses()
    {
        int calls = 0;
        using PritsetClient client = Client(async (request, cancellationToken) =>
        {
            calls++;
            Assert.Equal("*/*", request.Headers.Accept.Single().MediaType);
            if (calls == 1)
            {
                string body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Assert.Contains("{\"name\":\"Ada\"}", body);
                var content = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-data"));
                content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                content.Headers.ContentLength = 9;
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
                response.Headers.TryAddWithoutValidation("X-Trace", "timing");
                return response;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("doc-data")),
            };
        });

        using BinaryResponse pdf = await client.Documents.GenerateAsync("template", new { name = "Ada" });
        Assert.Equal("application/pdf", pdf.ContentType);
        Assert.Equal(9, pdf.ContentLength);
        Assert.Equal("timing", pdf.Trace);
        Assert.Equal("%PDF-data", await new StreamReader(pdf.Stream).ReadToEndAsync());

        using BinaryResponse download = await client.Templates.DownloadAsync("template");
        Assert.Equal("doc-data", await new StreamReader(download.Stream).ReadToEndAsync());
    }

    [Fact]
    public async Task GenerateWebhookUsesContractFixture()
    {
        using PritsetClient client = Client(async (request, cancellationToken) =>
        {
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Contains("https://example.com/hooks/pritset", body);
            Assert.Contains("{\"name\":\"Ada\"}", body);
            return JsonResponse(Fixture("documents/webhook-job.json"));
        });

        WebhookJob job = await client.Documents.GenerateWebhookAsync(
            "template",
            """{"name":"Ada"}""",
            new Uri("https://example.com/hooks/pritset"));
        Assert.Equal("57056f7462084dde8902421e9287ea2d", job.Id);
    }

    [Fact]
    public async Task RejectsCredentialBearingWebhookBeforeRequest()
    {
        int calls = 0;
        using PritsetClient client = Client((_, _) =>
        {
            calls++;
            return Task.FromResult(JsonResponse("{}"));
        });

        await Assert.ThrowsAsync<ArgumentException>(() => client.Documents.GenerateWebhookAsync(
            "template",
            new { },
            new Uri("https://user:pass@example.com/hook")));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task NormalizesContractErrorFixtures()
    {
        int calls = 0;
        using PritsetClient client = Client((_, _) =>
        {
            calls++;
            HttpResponseMessage response = calls switch
            {
                1 => JsonResponse(Fixture("errors/validation-problem.json"), HttpStatusCode.BadRequest),
                2 => JsonResponse(Fixture("errors/field-errors.json"), HttpStatusCode.BadRequest),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(Fixture("errors/plain-text.txt")),
                },
            };
            if (calls == 1)
            {
                response.Headers.TryAddWithoutValidation("Retry-After", "10");
            }
            return Task.FromResult(response);
        });

        PritsetApiException validation = await Assert.ThrowsAsync<PritsetApiException>(() => client.Templates.ListAsync());
        Assert.Equal(400, validation.StatusCode);
        Assert.Equal("One or more validation errors occurred.", validation.Message);
        Assert.Equal("The Name field is required.", Assert.Single(validation.FieldErrors["Name"]));
        Assert.Equal("00-example-trace-id-00", validation.TraceId);
        Assert.Equal("10", validation.RetryAfter);

        PritsetApiException fields = await Assert.ThrowsAsync<PritsetApiException>(() => client.Templates.ListAsync());
        Assert.Equal("Data is required", Assert.Single(fields.FieldErrors["Data"]));

        PritsetApiException plain = await Assert.ThrowsAsync<PritsetApiException>(() => client.Templates.GetAsync("missing"));
        Assert.Equal(Fixture("errors/plain-text.txt").Trim(), plain.Message);
    }

    [Fact]
    public async Task DoesNotRetryApiErrors()
    {
        int calls = 0;
        using PritsetClient client = Client((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });

        await Assert.ThrowsAsync<PritsetApiException>(() => client.Templates.ListAsync());
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SanitizesTransportErrorsAndDropsInnerException()
    {
        using PritsetClient client = Client((_, _) => throw new HttpRequestException("access-token client-secret"));
        PritsetTransportException exception = await Assert.ThrowsAsync<PritsetTransportException>(() => client.Templates.ListAsync());
        Assert.DoesNotContain("access-token", exception.Message);
        Assert.DoesNotContain("client-secret", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("access-token", client.ToString());
        Assert.DoesNotContain("client-secret", client.ToString());
    }

    [Fact]
    public async Task DistinguishesCancellationFromTimeout()
    {
        static async Task<HttpResponseMessage> Wait(HttpRequestMessage _, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }

        using PritsetClient timeoutClient = Client(Wait, timeout: TimeSpan.FromMilliseconds(10));
        PritsetTransportException timeout = await Assert.ThrowsAsync<PritsetTransportException>(() => timeoutClient.Templates.ListAsync());
        Assert.True(timeout.IsTimeout);
        Assert.False(timeout.IsCanceled);

        using PritsetClient canceledClient = Client(Wait);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        PritsetTransportException canceled = await Assert.ThrowsAsync<PritsetTransportException>(() =>
            canceledClient.Templates.ListAsync(cancellationToken: cancellation.Token));
        Assert.True(canceled.IsCanceled);
        Assert.False(canceled.IsTimeout);
    }

    [Fact]
    public void RejectsUnsafeConfigurationAndCredentialInjection()
    {
        foreach (string baseUrl in new[]
        {
            "http://api.pritset.com",
            "https://user:pass@api.pritset.com",
            "https://api.pritset.com?token=unsafe",
        })
        {
            Assert.Throws<ArgumentException>(() => new PritsetClient("token", "secret", new PritsetClientOptions
            {
                BaseUrl = new Uri(baseUrl),
            }));
        }
        Assert.Throws<ArgumentException>(() => new PritsetClient("token\r\ninjected", "secret"));
        Assert.Throws<ArgumentException>(() => new PritsetClient("token", "secret", new PritsetClientOptions
        {
            HttpClient = new HttpClient(new FakeHandler((_, _) => Task.FromResult(JsonResponse("{}")))),
        }));
    }

    [Fact]
    public async Task AllowsExplicitLoopbackHttpAndKeepsInjectedClientAlive()
    {
        using var httpClient = new HttpClient(new FakeHandler((_, _) => Task.FromResult(JsonResponse(Fixture("templates/list.json")))));
        using (var client = new PritsetClient("token", "secret", new PritsetClientOptions
        {
            BaseUrl = new Uri("http://127.0.0.1:8080"),
            HttpClient = httpClient,
            HttpClientIsRedirectSafe = true,
        }))
        {
            Assert.Equal(1, (await client.Templates.ListAsync()).Total);
        }

        using HttpResponseMessage response = await httpClient.GetAsync("https://example.com");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UploadFromPathAndSaveToFileWork()
    {
        string directory = Path.Combine(Path.GetTempPath(), "pritset-dotnet-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, "template.docx");
        string output = Path.Combine(directory, "document.pdf");
        await File.WriteAllTextAsync(input, "docx");
        try
        {
            using Upload upload = Upload.FromPath(input);
            Assert.Equal("template.docx", upload.FileName);

            using PritsetClient client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("pdf")),
            }));
            using BinaryResponse response = await client.Documents.GenerateAsync("template", new { });
            await response.SaveToFileAsync(output);
            Assert.Equal("pdf", await File.ReadAllTextAsync(output));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidJsonResponsesAndInputAreRejected()
    {
        using PritsetClient client = Client((_, _) => Task.FromResult(JsonResponse("not-json")));
        await Assert.ThrowsAsync<PritsetTransportException>(() => client.Templates.ListAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => client.Documents.GenerateAsync("template", "{invalid"));
    }

    private static PritsetClient Client(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(new FakeHandler(send));
        return new PritsetClient("access-token", "client-secret", new PritsetClientOptions
        {
            BaseUrl = new Uri("https://example.com/v1"),
            HttpClient = httpClient,
            HttpClientIsRedirectSafe = true,
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
        });
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out IEnumerable<string>? values) ? Assert.Single(values) : string.Empty;

    private static HttpContent Part(MultipartFormDataContent content, string name) =>
        Assert.Single(content, part => string.Equals(
            part.Headers.ContentDisposition?.Name?.Trim('"'),
            name,
            StringComparison.Ordinal));

    private static string Fixture(string path) =>
        File.ReadAllText(Path.Combine("contract", "fixtures", path.Replace('/', Path.DirectorySeparatorChar)));

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        internal FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _send(request, cancellationToken);
    }
}
