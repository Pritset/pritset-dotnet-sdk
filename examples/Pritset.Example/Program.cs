using Pritset;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run --project examples/Pritset.Example -- <template-id>");
    return 1;
}

string token = Environment.GetEnvironmentVariable("PRITSET_ACCESS_TOKEN")
    ?? throw new InvalidOperationException("Set PRITSET_ACCESS_TOKEN.");
string secret = Environment.GetEnvironmentVariable("PRITSET_SECRET")
    ?? throw new InvalidOperationException("Set PRITSET_SECRET.");

using var client = new PritsetClient(token, secret);
using BinaryResponse pdf = await client.Documents.GenerateAsync(args[0], new
{
    invoice = new { number = "INV-1042", customer = "Ada Lovelace" },
});
await pdf.SaveToFileAsync("invoice.pdf");
Console.WriteLine("Saved invoice.pdf");
return 0;
