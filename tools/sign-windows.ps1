param([Parameter(Mandatory = $true)][string]$File)
$ErrorActionPreference = 'Stop'
$env:PSModulePath = Join-Path $PSHOME 'Modules'
Import-Module Microsoft.PowerShell.Security
# Adapted from firaw-work-assistant/tools/sign-windows.ps1. Keys stay in the store.
$thumb = '668BEF56480FCA8EB1B0BC3B102A6A9CEC554CC4'
$cert = Get-Item -LiteralPath "Cert:\CurrentUser\My\$thumb" -ErrorAction Stop
if (-not $cert.HasPrivateKey -or $cert.Subject -ne 'CN=Firawynix' -or
    $cert.NotBefore -gt (Get-Date) -or $cert.NotAfter -le (Get-Date)) {
    throw 'The pinned Firawynix signing certificate is unavailable or expired.'
}
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$tool = Get-ChildItem -LiteralPath $sdkRoot -Filter signtool.exe -Recurse -File |
    Where-Object FullName -Match '\\x64\\' | Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $tool) { throw 'SignTool was not found in the Windows SDK.' }
$target = (Resolve-Path -LiteralPath $File).Path
& $tool sign /s My /fd SHA256 /sha1 $thumb /tr http://timestamp.digicert.com /td SHA256 $target
if ($LASTEXITCODE -ne 0) { throw "Signing failed: $target" }
$signature = Get-AuthenticodeSignature -LiteralPath $target
# NotTrusted permits the existing privately trusted certificate, never a hash mismatch.
if (-not $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $thumb -or
    $signature.Status -notin @('Valid', 'NotTrusted') -or -not $signature.TimeStamperCertificate) {
    throw "The pinned, timestamped signature could not be verified: $target"
}
