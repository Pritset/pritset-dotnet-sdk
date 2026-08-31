[CmdletBinding()]
param(
    [string]$EnvFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Import-PritsetEnvironmentFile {
    param([Parameter(Mandatory)][string]$Path)

    $allowedNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    @(
        "PRITSET_BASE_URL",
        "PRITSET_ACCESS_TOKEN",
        "PRITSET_SECRET",
        "PRITSET_WEBHOOK_URL",
        "PRITSET_WEBHOOK_SETTLE_SECONDS",
        "PRITSET_ALLOW_PRODUCTION",
        "PRITSET_PRODUCTION_TEST_USER_CONFIRMED",
        "PRITSET_TEST_RUN_PREFIX",
        "PRITSET_TEMPLATE_PATH"
    ) | ForEach-Object { [void]$allowedNames.Add($_) }

    $values = @{}
    $lineNumber = 0
    foreach ($rawLine in [IO.File]::ReadAllLines($Path)) {
        $lineNumber++
        $line = $rawLine.Trim()
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#")) {
            continue
        }
        if ($line.StartsWith("export ", [StringComparison]::Ordinal)) {
            $line = $line.Substring(7).TrimStart()
        }

        $separator = $line.IndexOf("=")
        if ($separator -le 0) {
            throw "Invalid .env entry at line $lineNumber. Expected NAME=value."
        }
        $name = $line.Substring(0, $separator).Trim()
        if (-not $allowedNames.Contains($name)) {
            throw "Unsupported .env setting '$name' at line $lineNumber."
        }
        if ($values.ContainsKey($name)) {
            throw "Duplicate .env setting '$name' at line $lineNumber."
        }

        $value = $line.Substring($separator + 1).Trim()
        if ($value.Length -ge 2 -and
            (($value.StartsWith('"') -and $value.EndsWith('"')) -or
             ($value.StartsWith("'") -and $value.EndsWith("'")))) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        elseif ($value.StartsWith('"') -or $value.EndsWith('"') -or
                $value.StartsWith("'") -or $value.EndsWith("'")) {
            throw "Unmatched quote in .env setting '$name' at line $lineNumber."
        }
        $values[$name] = $value
    }
    return $values
}

function Get-RequiredEnvironmentValue {
    param(
        [Parameter(Mandatory)][hashtable]$Values,
        [Parameter(Mandatory)][string]$Name
    )

    if (-not $Values.ContainsKey($Name) -or
        [string]::IsNullOrWhiteSpace([string]$Values[$Name]) -or
        $Values[$Name] -eq "replace-me") {
        throw "Set $Name in the .env file before running the production test."
    }
    return [string]$Values[$Name]
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$defaultFixturePath = Join-Path $repositoryRoot "tests/fixtures/staging-template.docx"
$environmentNames = @(
    "PRITSET_BASE_URL",
    "PRITSET_ACCESS_TOKEN",
    "PRITSET_SECRET",
    "PRITSET_WEBHOOK_URL",
    "PRITSET_WEBHOOK_SETTLE_SECONDS",
    "PRITSET_ALLOW_PRODUCTION",
    "PRITSET_PRODUCTION_TEST_USER_CONFIRMED",
    "PRITSET_TEST_RUN_PREFIX",
    "PRITSET_TEMPLATE_PATH"
)

if ([string]::IsNullOrWhiteSpace($EnvFile)) {
    $EnvFile = Join-Path $repositoryRoot ".env"
}
elseif (-not [IO.Path]::IsPathRooted($EnvFile)) {
    $EnvFile = Join-Path (Get-Location) $EnvFile
}
if (-not (Test-Path -LiteralPath $EnvFile -PathType Leaf)) {
    throw ".env file was not found at $EnvFile. Copy .env.example to .env and fill in the production test-user settings."
}
$resolvedEnvFile = (Resolve-Path -LiteralPath $EnvFile).Path

$environmentValues = Import-PritsetEnvironmentFile -Path $resolvedEnvFile
$baseUrl = Get-RequiredEnvironmentValue -Values $environmentValues -Name "PRITSET_BASE_URL"
$accessToken = Get-RequiredEnvironmentValue -Values $environmentValues -Name "PRITSET_ACCESS_TOKEN"
$secret = Get-RequiredEnvironmentValue -Values $environmentValues -Name "PRITSET_SECRET"
$webhookUrl = Get-RequiredEnvironmentValue -Values $environmentValues -Name "PRITSET_WEBHOOK_URL"
$allowProduction = Get-RequiredEnvironmentValue -Values $environmentValues -Name "PRITSET_ALLOW_PRODUCTION"
$testUserConfirmed = Get-RequiredEnvironmentValue -Values $environmentValues -Name "PRITSET_PRODUCTION_TEST_USER_CONFIRMED"

if ($baseUrl -cne "https://api.pritset.com") {
    throw "PRITSET_BASE_URL must be exactly https://api.pritset.com for this launcher."
}
if ($allowProduction -cne "true") {
    throw "Set PRITSET_ALLOW_PRODUCTION=true in the .env file to allow this production run."
}
if ($testUserConfirmed -cne "true") {
    throw "Set PRITSET_PRODUCTION_TEST_USER_CONFIRMED=true in the .env file after confirming these credentials belong to the dedicated production test user."
}
if ($accessToken.StartsWith("Bearer ", [StringComparison]::OrdinalIgnoreCase)) {
    throw "PRITSET_ACCESS_TOKEN must be the raw Pritset access token without a Bearer prefix."
}
if ($accessToken -match "\s" -or $secret -match "\s") {
    throw "PRITSET_ACCESS_TOKEN and PRITSET_SECRET cannot contain whitespace. Check for accidental spaces or line breaks."
}

if ($environmentValues.ContainsKey("PRITSET_WEBHOOK_SETTLE_SECONDS")) {
    [int]$settleSeconds = 0
    if (-not [int]::TryParse([string]$environmentValues["PRITSET_WEBHOOK_SETTLE_SECONDS"], [ref]$settleSeconds) -or
        $settleSeconds -lt 0 -or $settleSeconds -gt 60) {
        throw "PRITSET_WEBHOOK_SETTLE_SECONDS must be a whole number between 0 and 60."
    }
}

[Uri]$parsedWebhookUrl = $null
if (-not [Uri]::TryCreate($webhookUrl, [UriKind]::Absolute, [ref]$parsedWebhookUrl) -or
    $parsedWebhookUrl.Scheme -cne "https" -or
    -not [string]::IsNullOrEmpty($parsedWebhookUrl.UserInfo)) {
    throw "PRITSET_WEBHOOK_URL must be an absolute HTTPS URL without embedded credentials."
}

$fixturePath = $defaultFixturePath
if ($environmentValues.ContainsKey("PRITSET_TEMPLATE_PATH") -and
    -not [string]::IsNullOrWhiteSpace([string]$environmentValues["PRITSET_TEMPLATE_PATH"])) {
    $fixturePath = [string]$environmentValues["PRITSET_TEMPLATE_PATH"]
    if (-not [IO.Path]::IsPathRooted($fixturePath)) {
        $fixturePath = Join-Path $repositoryRoot $fixturePath
    }
}
if (-not (Test-Path -LiteralPath $fixturePath -PathType Leaf)) {
    throw "Production test fixture was not found at $fixturePath."
}
$resolvedFixturePath = (Resolve-Path -LiteralPath $fixturePath).Path
if ([IO.Path]::GetExtension($resolvedFixturePath) -ine ".docx") {
    throw "PRITSET_TEMPLATE_PATH must point to a .docx file."
}
$fixtureInfo = Get-Item -LiteralPath $resolvedFixturePath
if ($fixtureInfo.Length -le 0 -or $fixtureInfo.Length -gt 5120000) {
    throw "Production test fixture must be between 1 byte and 5 MB."
}
$fixtureBytes = [IO.File]::ReadAllBytes($resolvedFixturePath)
if ($fixtureBytes.Length -lt 4 -or $fixtureBytes[0] -ne 0x50 -or $fixtureBytes[1] -ne 0x4B) {
    throw "Production test fixture is not a DOCX ZIP archive."
}
$hasZipEndMarker = $false
for ($index = [Math]::Max(0, $fixtureBytes.Length - 65557); $index -le $fixtureBytes.Length - 4; $index++) {
    if ($fixtureBytes[$index] -eq 0x50 -and $fixtureBytes[$index + 1] -eq 0x4B -and
        $fixtureBytes[$index + 2] -eq 0x05 -and $fixtureBytes[$index + 3] -eq 0x06) {
        $hasZipEndMarker = $true
        break
    }
}
if (-not $hasZipEndMarker) {
    throw "Production test fixture has no ZIP end marker."
}
$fixtureBytes = $null

$dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
}
if ($null -eq $dotnet) {
    throw ".NET SDK 8 or newer was not found. Install it and add dotnet to PATH before running this script."
}
$dotnetExecutable = $dotnet.Source
$sdkVersion = & $dotnetExecutable --version
if ($LASTEXITCODE -ne 0 -or $sdkVersion -notmatch '^(\d+)\.') {
    throw "The installed .NET SDK version could not be determined."
}
if ([int]$Matches[1] -lt 8) {
    throw ".NET SDK 8 or newer is required. Installed SDK: $sdkVersion"
}

Write-Host "Loaded production test settings from $resolvedEnvFile. Secret values will not be printed."
Write-Warning "This test targets https://api.pritset.com using a dedicated production test user."
Write-Warning "It creates and deletes a temporary template, generates PDFs, submits a webhook job, and may consume test-user credit."
$confirmation = Read-Host "Type RUN-PRODUCTION-TEST to continue"
if ($confirmation -cne "RUN-PRODUCTION-TEST") {
    throw "Production test was not explicitly confirmed."
}

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryPublishPath = Join-Path $temporaryRoot ("pritset-dotnet-production-lifecycle-{0}" -f [Guid]::NewGuid().ToString("N"))
$resolvedTemporaryPublishPath = [IO.Path]::GetFullPath($temporaryPublishPath)
if (-not $resolvedTemporaryPublishPath.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($resolvedTemporaryPublishPath) -notmatch '^pritset-dotnet-production-lifecycle-[a-f0-9]{32}$') {
    throw "Refusing to use an unexpected temporary publish path."
}

$locationChanged = $false
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}

try {
    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable($name, $null, "Process")
    }
    Push-Location -LiteralPath $repositoryRoot
    $locationChanged = $true

    & $dotnetExecutable restore tools/Pritset.ProductionLifecycle/Pritset.ProductionLifecycle.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) {
        throw "Production lifecycle restore failed with exit code $LASTEXITCODE."
    }
    & $dotnetExecutable publish tools/Pritset.ProductionLifecycle/Pritset.ProductionLifecycle.csproj -c Release --no-restore -o $resolvedTemporaryPublishPath
    if ($LASTEXITCODE -ne 0) {
        throw "Production lifecycle build failed with exit code $LASTEXITCODE."
    }

    $environmentValues["PRITSET_TEMPLATE_PATH"] = $resolvedFixturePath
    foreach ($name in $environmentValues.Keys) {
        [Environment]::SetEnvironmentVariable($name, [string]$environmentValues[$name], "Process")
    }

    & $dotnetExecutable (Join-Path $resolvedTemporaryPublishPath "Pritset.ProductionLifecycle.dll")
    if ($LASTEXITCODE -ne 0) {
        throw "Production lifecycle failed with exit code $LASTEXITCODE. Review cleanup output before retrying."
    }
    Write-Host "Production test-user lifecycle completed successfully."
}
finally {
    foreach ($name in $environmentNames) {
        $previousValue = $previousEnvironment[$name]
        [Environment]::SetEnvironmentVariable($name, $previousValue, "Process")
    }
    $accessToken = $null
    $secret = $null
    $webhookUrl = $null
    $environmentValues.Clear()
    $previousEnvironment.Clear()

    if ($locationChanged) {
        Pop-Location
    }
    if (Test-Path -LiteralPath $resolvedTemporaryPublishPath -PathType Container) {
        Remove-Item -LiteralPath $resolvedTemporaryPublishPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}
