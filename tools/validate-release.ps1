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

function Read-JsonObject([string]$Path) {
    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 32
    }
    catch {
        throw "Invalid JSON document: $Path. $($_.Exception.Message)"
    }
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

$releasePath = Join-Path $repositoryRoot 'release.json'
$compatibilityPath = Join-Path $repositoryRoot 'compatibility.json'
$release = Read-JsonObject $releasePath
$compatibility = Read-JsonObject $compatibilityPath

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
    Require ($version.Channel -eq 0) 'Stable releases must use product version channel 0.'
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
         (Compare-ProductVersion $pairedVersion $maximumVersion) -lt 0) 'Paired peer version is outside the declared support range.'
if ($release.updateClass -eq 'coordinated-breaking') {
    Require ($release.peerUpdateRequired -eq $true) 'Breaking updates must require a peer update.'
    Require ($release.wireProtocol.major -ge 2) 'This breaking development line must use protocol major 2 or later.'
}

$branch = (& git -c "safe.directory=$($repositoryRoot.Replace('\', '/'))" -C $repositoryRoot branch --show-current).Trim()
if ($LASTEXITCODE -eq 0 -and $branch) {
    Require ($branch -ceq [string]$release.branch) "Current branch '$branch' does not match release.json."
}

$candidateFiles = @(& git -c "safe.directory=$($repositoryRoot.Replace('\', '/'))" -C $repositoryRoot ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0 -or $candidateFiles.Count -eq 0) {
    throw 'No candidate release files were found.'
}
$candidateFiles = @($candidateFiles | Sort-Object -Unique)
$forbiddenPathFragments = @('/build/', '/managed_components/', '/bin/', '/obj/', '/.vs/', '/.idea/', '/.vscode/')
$textExtensions = @('.c', '.h', '.cs', '.json', '.md', '.ps1', '.txt', '.yml', '.yaml', '.xml', '.csproj', '.props')
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

if ($Component -eq 'firmware') {
    $versionHeader = Get-Content -LiteralPath (Join-Path $repositoryRoot 'main/version.h') -Raw
    $cmake = Get-Content -LiteralPath (Join-Path $repositoryRoot 'CMakeLists.txt') -Raw
    $sdkDefaults = Get-Content -LiteralPath (Join-Path $repositoryRoot 'sdkconfig.defaults') -Raw
    $partitions = Get-Content -LiteralPath (Join-Path $repositoryRoot 'partitions.csv') -Raw
    Require ($versionHeader -match "#define NEXUS_FIRMWARE_VERSION `"$([regex]::Escape($release.productVersion))`"") 'Firmware version header does not match release.json.'
    Require ($versionHeader -match "#define NEXUS_RELEASE_CHANNEL `"$Channel`"") 'Firmware release channel does not match release.json.'
    Require ($versionHeader -match "#define NEXUS_PROTOCOL_VERSION $($release.wireProtocol.major)(\r?\n|$)") 'Firmware protocol major does not match release.json.'
    Require ($versionHeader -match "#define NEXUS_PROTOCOL_REVISION $($release.wireProtocol.revision)(\r?\n|$)") 'Firmware protocol revision does not match release.json.'
    Require ($cmake -match "set\(PROJECT_VER `"$([regex]::Escape($release.productVersion))`"\)") 'CMake project version does not match release.json.'
    Require ($sdkDefaults -match '(?m)^CONFIG_PARTITION_TABLE_OFFSET=0x10000$') 'Partition table offset must be 0x10000.'
    Require ($sdkDefaults -match '(?m)^CONFIG_PM_ENABLE=y$') 'Dynamic power management must be enabled.'
    Require ($sdkDefaults -match '(?m)^CONFIG_PM_DFS_INIT_AUTO=y$') 'Automatic DFS initialization must be enabled.'
    Require ($sdkDefaults -notmatch '(?m)^CONFIG_PM_POWER_DOWN_CPU_IN_LIGHT_SLEEP=y$') 'Automatic CPU light sleep is not permitted on the validated USB path.'
    Require ($partitions -match '(?m)^factory,\s*app,\s*factory,\s*0x20000,') 'Factory application offset must be 0x20000.'
    Require ($candidateFiles -contains 'tests/protocol_validation_test.c') 'Protocol validation tests are required.'
    Require ($candidateFiles -contains '.github/workflows/firmware-dev-ci.yml') 'Firmware development CI is required.'
}

Write-Host "Release validation passed: $Component $($release.productVersion) ($Channel), protocol $($release.wireProtocol.major).$($release.wireProtocol.revision)."
