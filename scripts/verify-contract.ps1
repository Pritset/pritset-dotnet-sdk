$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$lock = Get-Content -LiteralPath (Join-Path $root 'contract/contract.lock.json') -Raw | ConvertFrom-Json
$openApiPath = Join-Path $root 'contract/openapi.yaml'
$actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $openApiPath).Hash.ToLowerInvariant()
if ($actualHash -ne $lock.openapiSha256) {
    throw 'Vendored OpenAPI hash does not match contract lock.'
}
$openApi = Get-Content -LiteralPath $openApiPath -Raw
$match = [regex]::Match($openApi, '(?m)^  version:\s+([^\s]+)\s*$')
if (-not $match.Success -or $match.Groups[1].Value -ne $lock.contractVersion) {
    throw 'OpenAPI version does not match contract lock.'
}
$fixtures = @(
    'documents/webhook-job.json',
    'errors/field-errors.json',
    'errors/plain-text.txt',
    'errors/validation-problem.json',
    'templates/get.json',
    'templates/list.json'
)
foreach ($fixture in $fixtures) {
    if (-not (Test-Path -LiteralPath (Join-Path $root "contract/fixtures/$fixture"))) {
        throw "Missing contract fixture: $fixture"
    }
}
Write-Output "Contract $($lock.contractVersion) verified ($actualHash)."
