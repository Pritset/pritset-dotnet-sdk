using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Pritset;
using Pritset.Exceptions;
using Pritset.Models;

namespace Pritset.ProductionLifecycle;

internal static class Program
{
    private const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const int MaximumResponseBytes = 5 * 1024 * 1024;

    private static async Task<int> Main()
    {
        LifecycleConfiguration? configuration = null;
        string? templateId = null;
        string? originalName = null;
        string? updatedName = null;
        bool creationAttempted = false;
        bool deleted = false;
        Exception? failure = null;

        try
        {
            configuration = LifecycleConfiguration.Load();
            string runId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            originalName = configuration.RunPrefix + "-" + runId;
            updatedName = originalName + "-updated";
            object data = new
            {
                title = "Pritset SDK production test-user validation",
                description = "Lifecycle run " + runId,
                advantages = new[]
                {
                    new { title = "Contract", description = "All public template operations completed." },
                    new { title = "Cleanup", description = "The temporary template is deleted after validation." },
                },
            };

            using var client = new PritsetClient(configuration.AccessToken, configuration.Secret, new PritsetClientOptions
            {
                BaseUrl = configuration.BaseUrl,
                Timeout = TimeSpan.FromSeconds(120),
            });

            Console.WriteLine("Validating template");
            using (Upload validationUpload = Upload.FromPath(configuration.TemplatePath, DocxContentType))
            {
                Ensure(await client.Templates.ValidateAsync(validationUpload, data).ConfigureAwait(false), "Template validation returned false.");
            }
            Passed("validate template");

            creationAttempted = true;
            Console.WriteLine("Creating template");
            Template created;
            using (Upload createUpload = Upload.FromPath(configuration.TemplatePath, DocxContentType))
            {
                created = await client.Templates.CreateAsync(new CreateTemplateRequest
                {
                    Name = originalName,
                    Tags = configuration.RunPrefix + ",dotnet",
                    Template = createUpload,
                }).ConfigureAwait(false);
            }
            templateId = RequiredResponseValue(created.Id, "Create response did not include a template ID.");
            Ensure(created.Name == originalName, "Create response returned an unexpected template name.");
            Passed("create template");

            Console.WriteLine("Filter templates");
            TemplatePage page = await client.Templates.ListAsync(new ListTemplatesOptions
            {
                Query = originalName,
                Page = 1,
                PageSize = 100,
            }).ConfigureAwait(false);
            Ensure(page.Data.Any(template => template.Id == templateId), "Created template was not returned by list.");
            Passed("list templates");

            Console.WriteLine("Template details");
            TemplateDetails details = await client.Templates.GetAsync(templateId).ConfigureAwait(false);
            Ensure(details.Template.Id == templateId, "Template details returned an unexpected ID.");
            Ensure(details.FileInfo.Size > 0, "Template details reported an empty file.");
            Passed("get template details");

            Console.WriteLine("Template update");
            Template updated = await client.Templates.UpdateAsync(templateId, new UpdateTemplateRequest
            {
                Name = updatedName,
                Tags = configuration.RunPrefix + ",dotnet,updated",
            }).ConfigureAwait(false);
            Ensure(updated.Id == templateId, "Update response returned an unexpected template ID.");
            Ensure(updated.Name == updatedName, "Update response returned an unexpected template name.");
            Passed("update template");

            Console.WriteLine("Template download");
            using (BinaryResponse download = await client.Templates.DownloadAsync(templateId).ConfigureAwait(false))
            {
                byte[] docx = await ReadBoundedAsync(download.Stream, MaximumResponseBytes).ConfigureAwait(false);
                Ensure(docx.Length > 4 && docx[0] == (byte)'P' && docx[1] == (byte)'K', "Downloaded template was not a DOCX ZIP archive.");
            }
            Passed("download template");

            Console.WriteLine("Generate direct PDF");
            using (BinaryResponse document = await client.Documents.GenerateAsync(templateId, data).ConfigureAwait(false))
            {
                byte[] pdf = await ReadBoundedAsync(document.Stream, MaximumResponseBytes).ConfigureAwait(false);
                Ensure(pdf.Length > 5 && Encoding.ASCII.GetString(pdf, 0, 5) == "%PDF-", "Generated document was not a PDF.");
            }
            Passed("generate direct PDF");

            Console.WriteLine("Generate webhook PDF");
            WebhookJob job = await client.Documents.GenerateWebhookAsync(templateId, data, configuration.WebhookUrl).ConfigureAwait(false);
            RequiredResponseValue(job.Id, "Webhook response did not include a job ID.");
            Passed("submit webhook PDF generation (delivery is not asserted)");

            if (configuration.WebhookSettleSeconds > 0)
            {
                Console.WriteLine($"Waiting {configuration.WebhookSettleSeconds} seconds before template cleanup");
                await Task.Delay(TimeSpan.FromSeconds(configuration.WebhookSettleSeconds)).ConfigureAwait(false);
            }

            Console.WriteLine("Template deletion");
            await client.Templates.DeleteAsync(templateId).ConfigureAwait(false);
            deleted = true;
            Passed("delete template");

            await ExpectNotFoundAsync(() => client.Templates.GetAsync(templateId)).ConfigureAwait(false);
            Passed("confirm deleted template returns 404");
            Console.WriteLine(".NET SDK production test-user lifecycle passed.");
        }
        catch (Exception exception)
        {
            failure = exception;
            Console.Error.WriteLine(exception.Message);
            if (exception is PritsetApiException { StatusCode: 401 })
            {
                Console.Error.WriteLine(
                    "Production authentication failed (401). Confirm PRITSET_ACCESS_TOKEN is the raw Pritset token "
                    + "without a Bearer prefix and PRITSET_SECRET is the matching secret for the same production test user.");
            }
        }

        if (configuration is not null)
        {
            using var cleanupClient = new PritsetClient(configuration.AccessToken, configuration.Secret, new PritsetClientOptions
            {
                BaseUrl = configuration.BaseUrl,
                Timeout = TimeSpan.FromSeconds(120),
            });
            try
            {
                if (templateId is not null && !deleted)
                {
                    await DeleteWithRetryAsync(cleanupClient, templateId).ConfigureAwait(false);
                    Console.WriteLine("Cleanup removed the temporary template.");
                }
                else if (creationAttempted && templateId is null && originalName is not null && updatedName is not null)
                {
                    await CleanupByNameAsync(cleanupClient, originalName, updatedName).ConfigureAwait(false);
                }
            }
            catch (Exception cleanupException)
            {
                Console.Error.WriteLine("Cleanup failed: " + cleanupException.Message);
                failure ??= cleanupException;
            }
        }

        return failure is null ? 0 : 1;
    }

    private static async Task CleanupByNameAsync(PritsetClient client, string originalName, string updatedName)
    {
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                TemplatePage page = await client.Templates.ListAsync(new ListTemplatesOptions
                {
                    Query = originalName,
                    Page = 1,
                    PageSize = 100,
                }).ConfigureAwait(false);
                List<Template> leaked = page.Data
                    .Where(template => template.Name == originalName || template.Name == updatedName)
                    .ToList();
                foreach (Template template in leaked)
                {
                    await DeleteWithRetryAsync(client, template.Id).ConfigureAwait(false);
                    Console.WriteLine("Fallback cleanup removed temporary template " + template.Id + ".");
                }
                if (leaked.Count > 0 || attempt == 5)
                {
                    return;
                }
            }
            catch (Exception exception) when (IsRetryable(exception) && attempt < 5)
            {
            }
            await Task.Delay(TimeSpan.FromSeconds(1 << (attempt - 1))).ConfigureAwait(false);
        }
    }

    private static async Task DeleteWithRetryAsync(PritsetClient client, string templateId)
    {
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                await client.Templates.DeleteAsync(templateId).ConfigureAwait(false);
                return;
            }
            catch (PritsetApiException exception) when (exception.StatusCode == 404)
            {
                return;
            }
            catch (Exception exception) when (IsRetryable(exception) && attempt < 5)
            {
                await Task.Delay(TimeSpan.FromSeconds(1 << (attempt - 1))).ConfigureAwait(false);
            }
        }
    }

    private static bool IsRetryable(Exception exception) =>
        exception is PritsetTransportException
        || exception is PritsetApiException { StatusCode: 429 or >= 500 };

    private static async Task ExpectNotFoundAsync(Func<Task<TemplateDetails>> operation)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (PritsetApiException exception) when (exception.StatusCode == 404)
        {
            return;
        }
        throw new InvalidOperationException("Deleted template remained accessible.");
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream source, int maximumBytes)
    {
        using var destination = new MemoryStream();
        byte[] buffer = new byte[81920];
        int total = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximumBytes + 1 - total))).ConfigureAwait(false);
            if (read == 0)
            {
                return destination.ToArray();
            }
            total += read;
            if (total > maximumBytes)
            {
                throw new InvalidOperationException($"Binary response exceeded the {maximumBytes}-byte safety limit.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
        }
    }

    private static string RequiredResponseValue(string value, string message)
    {
        Ensure(!string.IsNullOrWhiteSpace(value), message);
        return value;
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Passed(string step) => Console.WriteLine("PASS: " + step);
}

internal sealed class LifecycleConfiguration
{
    private static readonly Regex PrefixPattern = new(
        "^[a-z0-9](?:[a-z0-9-]{0,46}[a-z0-9])?$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);
    private static readonly Regex LoopbackHttpPattern = new(
        "^http://(?:localhost|127[.]0[.]0[.]1|\\[::1\\])(?::[0-9]+)?(?:/.*)?$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase);

    private LifecycleConfiguration(
        Uri baseUrl,
        string accessToken,
        string secret,
        Uri webhookUrl,
        string templatePath,
        string runPrefix,
        int webhookSettleSeconds)
    {
        BaseUrl = baseUrl;
        AccessToken = accessToken;
        Secret = secret;
        WebhookUrl = webhookUrl;
        TemplatePath = templatePath;
        RunPrefix = runPrefix;
        WebhookSettleSeconds = webhookSettleSeconds;
    }

    internal Uri BaseUrl { get; }
    internal string AccessToken { get; }
    internal string Secret { get; }
    internal Uri WebhookUrl { get; }
    internal string TemplatePath { get; }
    internal string RunPrefix { get; }
    internal int WebhookSettleSeconds { get; }

    internal static LifecycleConfiguration Load()
    {
        (Uri baseUrl, bool production) = ValidateBaseUrl(RequiredEnvironment("PRITSET_BASE_URL"));
        if (production && Environment.GetEnvironmentVariable("PRITSET_ALLOW_PRODUCTION") != "true")
        {
            throw new InvalidOperationException("Refusing api.pritset.com without PRITSET_ALLOW_PRODUCTION=true.");
        }
        if (production && Environment.GetEnvironmentVariable("PRITSET_PRODUCTION_TEST_USER_CONFIRMED") != "true")
        {
            throw new InvalidOperationException(
                "Refusing api.pritset.com until PRITSET_PRODUCTION_TEST_USER_CONFIRMED=true confirms dedicated test-user credentials.");
        }

        string accessToken = ValidateCredential(RequiredEnvironment("PRITSET_ACCESS_TOKEN"), "PRITSET_ACCESS_TOKEN");
        string secret = ValidateCredential(RequiredEnvironment("PRITSET_SECRET"), "PRITSET_SECRET");
        Uri webhookUrl = ValidateWebhookUrl(RequiredEnvironment("PRITSET_WEBHOOK_URL"), production);
        string templatePath = Environment.GetEnvironmentVariable("PRITSET_TEMPLATE_PATH")?.Trim()
            ?? Path.Combine("tests", "fixtures", "staging-template.docx");
        ValidateDocx(templatePath);

        string runPrefix = Environment.GetEnvironmentVariable("PRITSET_TEST_RUN_PREFIX")?.Trim()
            ?? "pritset-sdk-production-test";
        if (!PrefixPattern.IsMatch(runPrefix))
        {
            throw new InvalidOperationException("PRITSET_TEST_RUN_PREFIX must contain 1-48 lowercase letters, digits, or dashes and cannot end with a dash.");
        }

        string settleText = Environment.GetEnvironmentVariable("PRITSET_WEBHOOK_SETTLE_SECONDS")?.Trim() ?? "10";
        if (!int.TryParse(settleText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int settle)
            || settle is < 0 or > 60)
        {
            throw new InvalidOperationException("PRITSET_WEBHOOK_SETTLE_SECONDS must be an integer from 0 to 60.");
        }
        return new LifecycleConfiguration(baseUrl, accessToken, secret, webhookUrl, Path.GetFullPath(templatePath), runPrefix, settle);
    }

    internal static (Uri Uri, bool IsProduction) ValidateBaseUrl(string rawValue)
    {
        if (!Uri.TryCreate(rawValue, UriKind.Absolute, out Uri? uri)
            || string.IsNullOrEmpty(uri.Host)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("PRITSET_BASE_URL must be an absolute HTTP(S) URL.");
        }
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("PRITSET_BASE_URL cannot contain credentials, a query, or a fragment.");
        }
        string canonicalHost = uri.Host.Trim('[', ']').TrimEnd('.');
        bool loopbackHttp = uri.Scheme == Uri.UriSchemeHttp && LoopbackHttpPattern.IsMatch(rawValue);
        if (uri.Scheme != Uri.UriSchemeHttps && !loopbackHttp)
        {
            throw new InvalidOperationException("PRITSET_BASE_URL must use HTTPS unless it targets an exact loopback host.");
        }
        bool production = canonicalHost.Equals("api.pritset.com", StringComparison.OrdinalIgnoreCase);
        if (production && rawValue != "https://api.pritset.com")
        {
            throw new InvalidOperationException("Production tests must target exactly https://api.pritset.com.");
        }
        return (uri, production);
    }

    internal static void ValidateDocx(string path)
    {
        if (!path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("PRITSET_TEMPLATE_PATH must identify a .docx file.");
        }
        var info = new FileInfo(path);
        if (!info.Exists || (info.Attributes & FileAttributes.Directory) != 0 || info.Length is < 1 or > 5_120_000)
        {
            throw new InvalidOperationException("PRITSET_TEMPLATE_PATH must be a readable DOCX file between 1 byte and 5,120,000 bytes.");
        }
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(info.FullName);
            if (archive.GetEntry("[Content_Types].xml") is null || archive.GetEntry("word/document.xml") is null)
            {
                throw new InvalidOperationException("PRITSET_TEMPLATE_PATH is not a valid DOCX archive.");
            }
        }
        catch (InvalidDataException)
        {
            throw new InvalidOperationException("PRITSET_TEMPLATE_PATH is not a valid DOCX archive.");
        }
        catch (IOException)
        {
            throw new InvalidOperationException("PRITSET_TEMPLATE_PATH is not a readable DOCX file.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException("PRITSET_TEMPLATE_PATH is not a readable DOCX file.");
        }
    }

    private static string RequiredEnvironment(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value) || value == "replace-me")
        {
            throw new InvalidOperationException("Set " + name + " before running the production lifecycle.");
        }
        return value;
    }

    private static string ValidateCredential(string value, string name)
    {
        if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || value.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException(name + " must be the raw value without a Bearer prefix or whitespace.");
        }
        return value;
    }

    private static Uri ValidateWebhookUrl(string value, bool production)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || string.IsNullOrEmpty(uri.Host)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("PRITSET_WEBHOOK_URL must be an absolute HTTP(S) URL without embedded credentials.");
        }
        if (production && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("PRITSET_WEBHOOK_URL must use HTTPS for a production test.");
        }
        return uri;
    }
}
