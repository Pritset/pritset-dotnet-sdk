# Contributing

Use the .NET 8 or .NET 10 SDK. Compatibility consumers cover .NET 5, 6, and 7.

```powershell
pwsh ./scripts/verify-contract.ps1
dotnet restore Pritset.sln --locked-mode
dotnet format Pritset.sln --verify-no-changes --no-restore
dotnet build Pritset.sln -c Release --no-restore
dotnet test tests/Pritset.Tests/Pritset.Tests.csproj -c Release --no-build
dotnet pack src/Pritset/Pritset.csproj -c Release -o artifacts
```

Changes to API behavior must remain compatible with `contract/openapi.yaml`. If the shared contract changes, update the vendored contract, fixtures, lock hash, tests, and README together.

Treat warnings and analyzer findings as errors. Keep public APIs nullable-aware, asynchronous, cancellation-aware, and stream-first for large bodies. Do not introduce automatic retries without a contract and idempotency review.

Never commit credentials, `.env`, generated documents containing customer data, NuGet packages, or build output.
