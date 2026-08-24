using Pritset;
using Pritset.Models;

using var client = new PritsetClient("compatibility-token", "compatibility-secret");
var options = new ListTemplatesOptions { Page = 1, PageSize = 10 };
if (client.Templates is null || client.Documents is null || options.Page != 1 || PritsetClient.Version != "0.1.0")
{
    throw new InvalidOperationException("Pritset compatibility smoke test failed.");
}
Console.WriteLine("Pritset compatibility smoke test passed.");
