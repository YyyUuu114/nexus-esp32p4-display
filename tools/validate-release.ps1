#requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('firmware', 'desktop')]
    [string]$Component,

    [Parameter(Mandatory)]
    [ValidateSet('stable', 'development')]
    [string]$Channel
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$releasePath = Join-Path $repositoryRoot 'release.json'
$compatibilityPath = Join-Path $repositoryRoot 'compatibility.json'

$release = Get-Content -LiteralPath $releasePath -Raw | ConvertFrom-Json
$null = Get-Content -LiteralPath $compatibilityPath -Raw | ConvertFrom-Json

if ($release.component -ne $Component) {
    throw "release.json component '$($release.component)' does not match '$Component'."
}
if ($release.channel -ne $Channel) {
    throw "release.json channel '$($release.channel)' does not match '$Channel'."
}

$version = [Version]$release.productVersion
$channelNumber = $version.Build
if ($Channel -eq 'stable' -and $channelNumber -ne 0) {
    throw 'Stable releases must use product version channel 0.'
}
if ($Channel -eq 'development' -and ($channelNumber -lt 1 -or $channelNumber -gt 9)) {
    throw 'Development releases must use product version channel 1 through 9.'
}

$trackedFiles = @(& git -c core.quotePath=false -C $repositoryRoot ls-files)
if ($LASTEXITCODE -ne 0 -or $trackedFiles.Count -eq 0) {
    throw 'No tracked release files were found.'
}

$forbiddenPathFragments = @('/build/', '/managed_components/', '/bin/', '/obj/', '/vendor/', '/artifacts/')
foreach ($path in $trackedFiles) {
    $normalized = '/' + $path.Replace('\', '/')
    foreach ($fragment in $forbiddenPathFragments) {
        if ($normalized.Contains($fragment, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Generated path is tracked: $path"
        }
    }
    if ($path -match '(?i)(^|/)(sdkconfig|sdkconfig\.old)$') {
        throw "Local SDK configuration is tracked: $path"
    }

    $file = Join-Path $repositoryRoot $path
    if ((Get-Item -LiteralPath $file).Length -gt 10MB) {
        throw "Tracked file exceeds the 10 MiB source-tree limit: $path"
    }
}

$textExtensions = @('.c', '.h', '.cs', '.json', '.md', '.ps1', '.txt', '.yml', '.yaml', '.xml', '.csproj', '.cmd')
$sensitivePatterns = @(
    '(?i)-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----',
    '(?i)\bgh[pousr]_[A-Za-z0-9_]{20,}\b',
    '(?i)\bAKIA[0-9A-Z]{16}\b',
    '(?i)(password|secret|api[_-]?key)\s*[:=]\s*["''][^"'']{8,}["'']',
    '(?i)[A-Z]:[\\/]Users[\\/][^\\/\s]+',
    '(?i)[A-Z]:[\\/](IDF|Projects?)[\\/]'
)

foreach ($path in $trackedFiles) {
    if ($path -eq 'tools/validate-release.ps1') {
        continue
    }
    $extension = [System.IO.Path]::GetExtension($path)
    if ($textExtensions -notcontains $extension) {
        continue
    }
    $content = Get-Content -LiteralPath (Join-Path $repositoryRoot $path) -Raw
    foreach ($pattern in $sensitivePatterns) {
        if ($content -match $pattern) {
            throw "Potential sensitive or machine-specific content in $path."
        }
    }
}

Write-Host "Release validation passed: $Component $($release.productVersion) ($Channel)."
