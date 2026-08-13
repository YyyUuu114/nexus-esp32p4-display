#requires -Version 7.4

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string]$OutputPath,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$fullPath = [System.IO.Path]::GetFullPath($OutputPath)
if ((Test-Path -LiteralPath $fullPath) -and -not $Force) {
    throw "The signing key already exists: $fullPath"
}
if (-not $PSCmdlet.ShouldProcess($fullPath, 'Create an ECDSA P-256 private update-signing key')) {
    return
}

$parent = Split-Path -Parent $fullPath
New-Item -ItemType Directory -Path $parent -Force | Out-Null

$key = [System.Security.Cryptography.ECDsa]::Create(
    [System.Security.Cryptography.ECCurve+NamedCurves]::nistP256)
try {
    [System.IO.File]::WriteAllText(
        $fullPath,
        $key.ExportPkcs8PrivateKeyPem(),
        [System.Text.UTF8Encoding]::new($false))

    $currentSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
    if ($null -eq $currentSid) { throw 'Could not determine the current Windows security identifier.' }
    $acl = [System.Security.AccessControl.FileSecurity]::new()
    $acl.SetOwner($currentSid)
    $acl.SetAccessRuleProtection($true, $false)
    $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
        $currentSid,
        [System.Security.AccessControl.FileSystemRights]::FullControl,
        [System.Security.AccessControl.AccessControlType]::Allow)
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $fullPath -AclObject $acl

    $publicKey = [Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo())
    Write-Output $publicKey
}
catch {
    Remove-Item -LiteralPath $fullPath -Force -ErrorAction SilentlyContinue
    throw
}
finally {
    $key.Dispose()
}
