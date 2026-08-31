using Pritset.ProductionLifecycle;
using Xunit;

namespace Pritset.Tests;

public sealed class ProductionLifecycleTests
{
    [Fact]
    public void AcceptsOnlyTheExactProductionBaseUrl()
    {
        (Uri uri, bool production) = LifecycleConfiguration.ValidateBaseUrl("https://api.pritset.com");
        Assert.Equal("api.pritset.com", uri.Host);
        Assert.True(production);

        foreach (string unsafeUrl in new[]
        {
            "https://api.pritset.com/",
            "https://api.pritset.com:443",
            "https://api.pritset.com/api",
            "https://api.pritset.com?query=value",
            "https://api.pritset.com./",
        })
        {
            Assert.Throws<InvalidOperationException>(() => LifecycleConfiguration.ValidateBaseUrl(unsafeUrl));
        }
    }

    [Theory]
    [InlineData("https://staging.example.com", false)]
    [InlineData("http://localhost:8080", false)]
    [InlineData("http://127.0.0.1:8080", false)]
    public void AcceptsHttpsAndExactLoopbackDevelopmentUrls(string value, bool expectedProduction)
    {
        (_, bool production) = LifecycleConfiguration.ValidateBaseUrl(value);
        Assert.Equal(expectedProduction, production);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("http://localhost.:8080")]
    [InlineData("http://127.1:8080")]
    [InlineData("https://user:pass@example.com")]
    [InlineData("file:///tmp/template.docx")]
    public void RejectsUnsafeNonProductionBaseUrls(string value)
    {
        Assert.Throws<InvalidOperationException>(() => LifecycleConfiguration.ValidateBaseUrl(value));
    }

    [Fact]
    public void AcceptsTheSharedDocxFixture()
    {
        LifecycleConfiguration.ValidateDocx(Path.Combine("tests", "fixtures", "staging-template.docx"));
    }
}
