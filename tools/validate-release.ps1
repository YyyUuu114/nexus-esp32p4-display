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

function Get-SourceConstant([string]$Content, [string]$Name) {
    $pattern = '(?m)^\s*(?:public\s+const\s+(?:string|int)|#define)\s+{0}\s+(?:=\s*)?"?([^";\s]+)' -f [regex]::Escape($Name)
    $match = [regex]::Match($Content, $pattern)
    if (-not $match.Success) {
        throw "Required source constant was not found: $Name"
    }
    return $match.Groups[1].Value
}

if ($Component -eq 'desktop') {
    [xml]$project = Get-Content -LiteralPath (Join-Path $repositoryRoot 'NexusDisplayAgent.csproj') -Raw
    $projectVersion = [string]$project.Project.PropertyGroup.Version
    if ($projectVersion -ne $release.productVersion) {
        throw "Project version '$projectVersion' does not match release.json '$($release.productVersion)'."
    }

    $buildInfo = Get-Content -LiteralPath (Join-Path $repositoryRoot 'BuildInfo.cs') -Raw
    if ((Get-SourceConstant $buildInfo 'ReleaseChannel') -ne $Channel) {
        throw 'BuildInfo release channel does not match release.json.'
    }
    if ([int](Get-SourceConstant $buildInfo 'ProtocolMajor') -ne $release.wireProtocol.major -or
        [int](Get-SourceConstant $buildInfo 'ProtocolRevision') -ne $release.wireProtocol.revision) {
        throw 'BuildInfo protocol version does not match release.json.'
    }

    $updateChannel = Get-Content -LiteralPath (Join-Path $repositoryRoot 'package\update-channel.json') -Raw | ConvertFrom-Json
    if ($updateChannel.channel -ne $Channel) {
        throw 'Packaged update channel does not match release.json.'
    }
    if ($Channel -eq 'stable' -and
        (-not [Uri]::IsWellFormedUriString($updateChannel.manifestUrl, [UriKind]::Absolute) -or
         -not $updateChannel.manifestUrl.StartsWith('https://', [System.StringComparison]::OrdinalIgnoreCase))) {
        throw 'Stable builds require an absolute HTTPS update manifest URL.'
    }
}
else {
    $cmake = Get-Content -LiteralPath (Join-Path $repositoryRoot 'CMakeLists.txt') -Raw
    $cmakeVersion = [regex]::Match($cmake, 'set\(PROJECT_VER\s+"([^"]+)"\)').Groups[1].Value
    if ($cmakeVersion -ne $release.productVersion) {
        throw "CMake project version '$cmakeVersion' does not match release.json '$($release.productVersion)'."
    }

    $versionHeader = Get-Content -LiteralPath (Join-Path $repositoryRoot 'main\version.h') -Raw
    if ((Get-SourceConstant $versionHeader 'NEXUS_FIRMWARE_VERSION') -ne $release.productVersion -or
        (Get-SourceConstant $versionHeader 'NEXUS_RELEASE_CHANNEL') -ne $Channel) {
        throw 'Firmware source version or channel does not match release.json.'
    }
    if ([int](Get-SourceConstant $versionHeader 'NEXUS_PROTOCOL_VERSION') -ne $release.wireProtocol.major -or
        [int](Get-SourceConstant $versionHeader 'NEXUS_PROTOCOL_REVISION') -ne $release.wireProtocol.revision) {
        throw 'Firmware protocol version does not match release.json.'
    }
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
