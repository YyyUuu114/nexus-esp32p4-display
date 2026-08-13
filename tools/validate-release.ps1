#requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('firmware', 'desktop')]
    [string]$Component,

    [Parameter(Mandatory)]
    [ValidateSet('stable', 'development')]
    [string]$Channel,

    [switch]$SkipPublishedEnvelope
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path

function Read-JsonObject([string]$Path) {
    try { return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 32 }
    catch { throw "Invalid JSON document: $Path. $($_.Exception.Message)" }
}

function Read-ProductVersion([string]$Text, [string]$FieldName) {
    if ($Text -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.([0-9])$') {
        throw "$FieldName must use canonical MAJOR.LINE.CHANNEL form with CHANNEL 0 through 9."
    }
    return [pscustomobject]@{
        Major = [uint64]$Matches[1]
        Line = [uint64]$Matches[2]
        Channel = [uint64]$Matches[3]
    }
}

function Compare-ProductVersion($Left, $Right) {
    foreach ($property in @('Major', 'Line')) {
        if ($Left.$property -lt $Right.$property) { return -1 }
        if ($Left.$property -gt $Right.$property) { return 1 }
    }
    if ($Left.Channel -eq $Right.Channel) { return 0 }
    if ($Left.Channel -eq 0) { return 1 }
    if ($Right.Channel -eq 0) { return -1 }
    return $Left.Channel.CompareTo($Right.Channel)
}

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$release = Read-JsonObject (Join-Path $repositoryRoot 'release.json')
$compatibility = Read-JsonObject (Join-Path $repositoryRoot 'compatibility.json')
Require ($release.schemaVersion -eq 1) 'release.json schemaVersion must be 1.'
Require ($compatibility.schemaVersion -eq 1) 'compatibility.json schemaVersion must be 1.'
Require ($release.component -ceq $Component) "release.json component does not match $Component."
Require ($release.channel -ceq $Channel) "release.json channel does not match $Channel."
Require ($compatibility.productVersion.format -ceq 'MAJOR.LINE.CHANNEL') 'Unexpected product-version policy.'
Require ($compatibility.wireProtocol.format -ceq 'WIRE_MAJOR.WIRE_REVISION') 'Unexpected wire-protocol policy.'
Require ($compatibility.wireProtocol.runtimeHandshakeRequired -eq $true) 'Runtime handshake must be required.'
Require ($compatibility.wireProtocol.sessionNonceRequired -eq $true) 'Session nonce must be required.'

$version = Read-ProductVersion ([string]$release.productVersion) 'release.json productVersion'
if ($Channel -eq 'stable') {
    Require ($version.Channel -eq 0) 'Stable releases must use channel 0.'
}
else {
    Require ($version.Channel -ge 1 -and $version.Channel -le 9) 'Development releases must use channel 1 through 9.'
}
Require ($release.branch -ceq "$Component-$(@{ stable = 'stable'; development = 'dev' }[$Channel])") 'release.json branch does not match component/channel.'
Require ($release.wireProtocol.major -is [int64] -and $release.wireProtocol.major -ge 1) 'Wire major must be a positive integer.'
Require ($release.wireProtocol.revision -is [int64] -and $release.wireProtocol.revision -ge 0) 'Wire revision must be a non-negative integer.'

$knownUpdateClasses = @('single-endpoint', 'coordinated-additive', 'coordinated-breaking', 'paired-baseline')
Require ($knownUpdateClasses -ccontains [string]$release.updateClass) 'Unknown release updateClass.'
$classPolicy = $compatibility.updateClasses.([string]$release.updateClass)
Require ($null -ne $classPolicy) 'updateClass is absent from compatibility.json.'
Require ($release.peerUpdateRequired -is [bool] -and
         $release.peerUpdateRequired -eq $classPolicy.peerUpdateRequired) 'peerUpdateRequired conflicts with updateClass policy.'

$peerName = if ($Component -eq 'firmware') { 'Desktop' } else { 'Firmware' }
$pairedProperty = "paired${peerName}Version"
$supportedProperty = "supported${peerName}Versions"
$pairedVersion = Read-ProductVersion ([string]$release.$pairedProperty) "release.json $pairedProperty"
$minimumVersion = Read-ProductVersion ([string]$release.$supportedProperty.minimum) "release.json $supportedProperty.minimum"
$maximumVersion = Read-ProductVersion ([string]$release.$supportedProperty.maximumExclusive) "release.json $supportedProperty.maximumExclusive"
Require ((Compare-ProductVersion $minimumVersion $maximumVersion) -lt 0) 'Peer support range is empty or reversed.'
Require ((Compare-ProductVersion $pairedVersion $minimumVersion) -ge 0 -and
         (Compare-ProductVersion $pairedVersion $maximumVersion) -lt 0) 'Paired peer version is outside the support range.'
if ($release.updateClass -eq 'coordinated-breaking') {
    Require ($release.peerUpdateRequired -eq $true -and $release.wireProtocol.major -ge 2) 'Breaking update metadata is inconsistent.'
}

$safeRoot = $repositoryRoot.Replace('\', '/')
$branch = (& git -c "safe.directory=$safeRoot" -C $repositoryRoot branch --show-current).Trim()
if ($LASTEXITCODE -eq 0 -and $branch) {
    Require ($branch -ceq [string]$release.branch) "Current branch '$branch' does not match release.json."
}

$candidateFiles = @(& git -c "safe.directory=$safeRoot" -c core.quotePath=false -C $repositoryRoot ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0 -or $candidateFiles.Count -eq 0) { throw 'No candidate release files were found.' }
$candidateFiles = @($candidateFiles | Sort-Object -Unique)
$forbiddenPathFragments = @('/build/', '/managed_components/', '/bin/', '/obj/', '/vendor/', '/artifacts/', '/.vs/', '/.idea/', '/.vscode/')
$textExtensions = @('.c', '.h', '.cs', '.json', '.md', '.ps1', '.txt', '.yml', '.yaml', '.xml', '.csproj', '.cmd')
$sensitivePatterns = @(
    '(?i)-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----',
    '(?i)\bgh[pousr]_[A-Za-z0-9_]{20,}\b',
    '(?i)\bAKIA[0-9A-Z]{16}\b',
    '(?i)(password|secret|api[_-]?key)\s*[:=]\s*["''][^"'']{8,}["'']',
    '(?i)[A-Z]:[\\/]Users[\\/][^\\/\s]+',
    '(?i)[A-Z]:[\\/](IDF|Projects?)[\\/]'
)

foreach ($path in $candidateFiles) {
    $normalized = '/' + $path.Replace('\', '/')
    foreach ($fragment in $forbiddenPathFragments) {
        Require (-not $normalized.Contains($fragment, [System.StringComparison]::OrdinalIgnoreCase)) "Generated path is included: $path"
    }
    Require ($path -notmatch '(?i)(^|/)(sdkconfig|sdkconfig\.old)$') "Local SDK configuration is included: $path"
    $file = Join-Path $repositoryRoot $path
    Require (Test-Path -LiteralPath $file -PathType Leaf) "Candidate path is missing or not a regular file: $path"
    $item = Get-Item -LiteralPath $file -Force
    Require (-not ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) "Reparse points are not allowed: $path"
    Require ($item.Length -le 10MB) "Candidate file exceeds the 10 MiB source-tree limit: $path"
    if ($path -ne 'tools/validate-release.ps1' -and $textExtensions -contains $item.Extension) {
        $content = Get-Content -LiteralPath $file -Raw
        foreach ($pattern in $sensitivePatterns) {
            Require ($content -notmatch $pattern) "Potential sensitive or machine-specific content in $path."
        }
    }
}

if ($Component -eq 'desktop') {
    [xml]$project = Get-Content -LiteralPath (Join-Path $repositoryRoot 'NexusDisplayAgent.csproj') -Raw
    Require ([string]$project.Project.PropertyGroup.Version -ceq [string]$release.productVersion) 'Project version does not match release.json.'
    $buildInfo = Get-Content -LiteralPath (Join-Path $repositoryRoot 'BuildInfo.cs') -Raw
    Require ($buildInfo -match "public const string ReleaseChannel = `"$Channel`";") 'BuildInfo release channel does not match.'
    Require ($buildInfo -match "public const int ProtocolMajor = $($release.wireProtocol.major);") 'BuildInfo protocol major does not match.'
    Require ($buildInfo -match "public const int ProtocolRevision = $($release.wireProtocol.revision);") 'BuildInfo protocol revision does not match.'

    $updateChannel = Read-JsonObject (Join-Path $repositoryRoot 'package\update-channel.json')
    $trust = Read-JsonObject (Join-Path $repositoryRoot 'package\update-trust.json')
    Require ($updateChannel.channel -ceq $Channel) 'Packaged update channel does not match.'
    Require ($updateChannel.manifestUrl -ceq 'https://raw.githubusercontent.com/YyyUuu114/nexus-esp32p4-display/desktop-dev/package/latest-update.json') 'Packaged manifest URL is not the fixed official source.'
    Require ($trust.schemaVersion -eq 1 -and $trust.keyId -ceq 'nexus-dev-2026-01' -and
             $trust.algorithm -ceq 'ECDSA_P256_SHA256_DER') 'Update trust metadata is invalid.'
    $publicMatch = [regex]::Match($buildInfo, 'UpdateSigningPublicKey\s*=\s*\r?\n?\s*"([A-Za-z0-9+/=]+)";')
    Require ($publicMatch.Success -and $publicMatch.Groups[1].Value -ceq [string]$trust.publicKeySpkiBase64) 'Compiled update public key does not match update-trust.json.'

    if (-not $SkipPublishedEnvelope) {
    $envelope = Read-JsonObject (Join-Path $repositoryRoot 'package\latest-update.json')
    Require ($envelope.schemaVersion -eq 1 -and $envelope.keyId -ceq [string]$trust.keyId) 'Published envelope header is invalid.'
    $payloadBytes = [Convert]::FromBase64String([string]$envelope.payload)
    $signatureBytes = [Convert]::FromBase64String([string]$envelope.signature)
    $publicBytes = [Convert]::FromBase64String([string]$trust.publicKeySpkiBase64)
    $ecdsa = [System.Security.Cryptography.ECDsa]::Create()
    try {
        $bytesRead = 0
        $ecdsa.ImportSubjectPublicKeyInfo($publicBytes, [ref]$bytesRead)
        Require ($bytesRead -eq $publicBytes.Length) 'Update public key contains trailing data.'
        $signatureValid = $ecdsa.VerifyData(
            $payloadBytes, $signatureBytes,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)
    }
    finally { $ecdsa.Dispose() }
    Require $signatureValid 'Published update envelope signature is invalid.'
    $payload = [System.Text.UTF8Encoding]::new($false, $true).GetString($payloadBytes) | ConvertFrom-Json -Depth 16
    Require ($payload.component -ceq 'desktop' -and $payload.channel -ceq $Channel -and
             $payload.version -ceq [string]$release.productVersion) 'Signed payload identity does not match release.json.'
    Require ($payload.protocolMajor -eq $release.wireProtocol.major -and
             $payload.protocolRevision -eq $release.wireProtocol.revision -and
             $payload.updateClass -ceq [string]$release.updateClass -and
             $payload.peerUpdateRequired -eq $release.peerUpdateRequired -and
             $payload.pairedFirmwareVersion -ceq [string]$release.pairedFirmwareVersion) 'Signed compatibility metadata does not match release.json.'
    Require ($payload.downloadUrl -ceq "https://github.com/YyyUuu114/nexus-esp32p4-display/releases/download/desktop-v$($release.productVersion)-dev/NEXUS-Display-Windows-x64-v$($release.productVersion).zip") 'Signed package URL is unexpected.'
    Require ([string]$payload.sha256 -match '^[A-F0-9]{64}$') 'Signed package hash is invalid.'
    }

    $startupSource = Get-Content -LiteralPath (Join-Path $repositoryRoot 'StartupManager.cs') -Raw
    $installationSource = Get-Content -LiteralPath (Join-Path $repositoryRoot 'InstallationManager.cs') -Raw
    $updateSource = Get-Content -LiteralPath (Join-Path $repositoryRoot 'UpdateManager.cs') -Raw
    $projectSource = Get-Content -LiteralPath (Join-Path $repositoryRoot 'NexusDisplayAgent.csproj') -Raw
    $restoreSource = Get-Content -LiteralPath (Join-Path $repositoryRoot 'tools\restore-lhm.ps1') -Raw
    $testSource = Get-Content -LiteralPath (Join-Path $repositoryRoot 'tests\Program.cs') -Raw
    $buildSource = Get-Content -LiteralPath (Join-Path $repositoryRoot 'tools\build-release.ps1') -Raw
    Require ($startupSource.Contains('InstallationManager.CurrentExecutable', [System.StringComparison]::Ordinal)) 'Startup task must target the protected installed executable.'
    Require ($installationSource.Contains('SpecialFolder.ProgramFiles', [System.StringComparison]::Ordinal)) 'Installation must use Program Files.'
    Require (-not $updateSource.Contains('powershell.exe', [System.StringComparison]::OrdinalIgnoreCase) -and
             -not $updateSource.Contains('ExecutionPolicy', [System.StringComparison]::OrdinalIgnoreCase)) 'Updater must not generate or invoke PowerShell.'
    Require ($updateSource.Contains('ApplyAtomicSwap', [System.StringComparison]::Ordinal) -and
             $updateSource.Contains('VerifyPackageHash', [System.StringComparison]::Ordinal)) 'Atomic update and integrity gates are required.'
    Require ($projectSource.Contains('SelectPinnedWindowsRuntime', [System.StringComparison]::Ordinal) -and
             $projectSource.Contains('VerifyPinnedWindowsRuntime', [System.StringComparison]::Ordinal) -and
             $restoreSource.Contains('NexusRuntime', [System.StringComparison]::Ordinal) -and
             $restoreSource.Contains('331FF3528809BF54382461FDF0FBDFDCB6C221D770DA7C755EF531801A80554A', [System.StringComparison]::Ordinal)) 'Pinned Windows serial runtime selection is required.'
    Require ($testSource.Contains('SerialPort.GetPortNames()', [System.StringComparison]::Ordinal) -and
             $buildSource.Contains('--runtime-self-test', [System.StringComparison]::Ordinal)) 'Windows serial runtime tests are required.'
    Require ($candidateFiles -contains 'tests/NexusDisplay.Tests.csproj') 'Desktop core tests are required.'
    Require ($candidateFiles -contains '.github/workflows/desktop-dev-ci.yml') 'Desktop development CI is required.'
}

Write-Host "Release validation passed: $Component $($release.productVersion) ($Channel), protocol $($release.wireProtocol.major).$($release.wireProtocol.revision)."
