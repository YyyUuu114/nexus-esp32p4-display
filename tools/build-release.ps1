#requires -Version 7.4

[CmdletBinding()]
param(
    [string]$DotNetPath = 'dotnet',

    [Parameter(Mandatory)]
    [string]$SigningKeyPath,

    [string]$AuthenticodeCertificateThumbprint
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$release = Get-Content -LiteralPath (Join-Path $repositoryRoot 'release.json') -Raw | ConvertFrom-Json -Depth 16
$trust = Get-Content -LiteralPath (Join-Path $repositoryRoot 'package\update-trust.json') -Raw | ConvertFrom-Json
$signingKey = (Resolve-Path -LiteralPath $SigningKeyPath).Path
$version = [string]$release.productVersion
$releaseTag = "desktop-v$version-dev"
$assetName = "NEXUS-Display-Windows-x64-v$version.zip"
$envelopeName = "NEXUS-Display-Windows-x64-v$version.nexus-update.json"
$artifactRoot = Join-Path $repositoryRoot 'artifacts'
$publishRoot = Join-Path $artifactRoot 'publish'
$containerRoot = Join-Path $artifactRoot 'NEXUS-Display-Windows-x64'
$packageRoot = Join-Path $containerRoot 'NEXUS Display'
$zipPath = Join-Path $artifactRoot $assetName
$envelopePath = Join-Path $artifactRoot $envelopeName
$checksumsPath = Join-Path $artifactRoot 'SHA256SUMS.txt'

& (Join-Path $PSScriptRoot 'validate-release.ps1') -Component desktop -Channel development
& (Join-Path $PSScriptRoot 'restore-lhm.ps1')

foreach ($target in @($publishRoot, $containerRoot, $zipPath, $envelopePath, $checksumsPath)) {
    if (-not (Test-Path -LiteralPath $target)) { continue }
    $resolvedArtifactRoot = [System.IO.Path]::GetFullPath($artifactRoot)
    $resolvedTarget = [System.IO.Path]::GetFullPath($target)
    if (-not $resolvedTarget.StartsWith($resolvedArtifactRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected artifact path: $resolvedTarget"
    }
    Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
}

New-Item -ItemType Directory -Path $publishRoot,$packageRoot -Force | Out-Null
& $DotNetPath publish (Join-Path $repositoryRoot 'NexusDisplayAgent.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishRoot `
    --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$publishedExecutable = Join-Path $publishRoot 'Nexus Display.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable)) {
    throw 'Published output does not contain Nexus Display.exe.'
}

if ($AuthenticodeCertificateThumbprint) {
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$AuthenticodeCertificateThumbprint"
    $authenticode = Set-AuthenticodeSignature -LiteralPath $publishedExecutable -Certificate $certificate `
        -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com'
    if ($authenticode.Status -ne 'Valid') {
        throw "Authenticode signing failed: $($authenticode.StatusMessage)"
    }
}

Copy-Item -LiteralPath $publishedExecutable -Destination $packageRoot
Copy-Item -Path (Join-Path $repositoryRoot 'package\*.cmd') -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'package\使用说明.txt'),
                       (Join-Path $repositoryRoot 'package\update-channel.json'),
                       (Join-Path $repositoryRoot 'package\update-trust.json') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE'),
                       (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md'),
                       (Join-Path $repositoryRoot 'COMPATIBILITY.md'),
                       (Join-Path $repositoryRoot 'SECURITY.md'),
                       (Join-Path $repositoryRoot 'release.json') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'licenses') -Destination $packageRoot -Recurse

Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal
$packageHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
$downloadUrl = "https://github.com/YyyUuu114/nexus-esp32p4-display/releases/download/$releaseTag/$assetName"
$payload = [ordered]@{
    schemaVersion = 1
    component = 'desktop'
    channel = [string]$release.channel
    version = $version
    downloadUrl = $downloadUrl
    sha256 = $packageHash
    protocolMajor = [int]$release.wireProtocol.major
    protocolRevision = [int]$release.wireProtocol.revision
    updateClass = [string]$release.updateClass
    peerUpdateRequired = [bool]$release.peerUpdateRequired
    pairedFirmwareVersion = [string]$release.pairedFirmwareVersion
    supportedFirmwareVersions = [ordered]@{
        minimum = [string]$release.supportedFirmwareVersions.minimum
        maximumExclusive = [string]$release.supportedFirmwareVersions.maximumExclusive
    }
}
$payloadJson = $payload | ConvertTo-Json -Depth 8 -Compress
$payloadBytes = [System.Text.UTF8Encoding]::new($false).GetBytes($payloadJson)

$key = [System.Security.Cryptography.ECDsa]::Create()
try {
    $key.ImportFromPem([System.IO.File]::ReadAllText($signingKey))
    if ($key.KeySize -ne 256) { throw 'The update-signing key is not ECDSA P-256.' }
    $publicKey = [Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo())
    if ($publicKey -cne [string]$trust.publicKeySpkiBase64) {
        throw 'The private signing key does not match package/update-trust.json.'
    }
    $signature = $key.SignData(
        $payloadBytes,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)
}
finally {
    $key.Dispose()
}

$envelope = [ordered]@{
    schemaVersion = 1
    keyId = [string]$trust.keyId
    payload = [Convert]::ToBase64String($payloadBytes)
    signature = [Convert]::ToBase64String($signature)
}
$envelope | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $envelopePath -Encoding utf8NoBOM
$envelopeHash = (Get-FileHash -LiteralPath $envelopePath -Algorithm SHA256).Hash
@(
    "$packageHash  $assetName"
    "$envelopeHash  $envelopeName"
) | Set-Content -LiteralPath $checksumsPath -Encoding ascii

Write-Host "Release package: $zipPath"
Write-Host "Signed envelope: $envelopePath"
Write-Host "Checksums: $checksumsPath"
