param([string]$MakeNsis = (Join-Path $env:LOCALAPPDATA 'tauri\NSIS\makensis.exe'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\packaging\build-common.ps1')
Assert-BuildPrerequisites
if (-not (Test-Path -LiteralPath $MakeNsis -PathType Leaf)) { throw 'NSIS 3 is required; use -MakeNsis to specify makensis.exe.' }
$MakeNsis = (Resolve-Path -LiteralPath $MakeNsis).Path
$powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$release = Join-Path $ProjectRoot 'release'
$downloads = Join-Path $ProjectRoot 'site\public\downloads'
# A fresh stage prevents old architecture payloads from leaking into a new release.
$stage = Join-Path $release ('.staging-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$packages = @()
$manifest = [ordered]@{ version = $ReleaseVersion; architectures = [ordered]@{} }
foreach ($arch in @('x64', 'x86')) {
    $payload = Join-Path $stage "app-$arch"
    Publish-H4K $arch $payload $true
    $name = "Firaw-h4K-$ReleaseVersion-$arch-Setup.exe"
    $package = Join-Path $stage $name
    & $MakeNsis /INPUTCHARSET UTF8 "/DARCH=$arch" "/DVERSION=$ReleaseVersion" `
        "/DROOT=$ProjectRoot" "/DPAYLOAD=$payload" "/DOUTPUT=$package" `
        "/DSIGN_SCRIPT=$PSScriptRoot\sign-windows.ps1" "/DPOWERSHELL=$powershell" `
        (Join-Path $ProjectRoot 'packaging\offline.nsi')
    if ($LASTEXITCODE -ne 0) { throw "NSIS failed for $arch." }
    & "$PSScriptRoot\sign-windows.ps1" -File $package
    Assert-PinnedSignature $package
    $manifest.architectures[$arch] = [ordered]@{
        url = "https://h4k.firawynix.com.br/downloads/$name"
        size = (Get-Item -LiteralPath $package).Length
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $package).Hash.ToLowerInvariant()
        signerThumbprint = $ReleaseThumbprint
    }
    $packages += $name
}
$onlineName = 'Firaw-h4K-Instalador-Online.exe'
$online = Join-Path $stage $onlineName
& $MakeNsis /INPUTCHARSET UTF8 "/DVERSION=$ReleaseVersion" "/DROOT=$ProjectRoot" "/DOUTPUT=$online" `
    (Join-Path $ProjectRoot 'packaging\online.nsi')
if ($LASTEXITCODE -ne 0) { throw 'NSIS failed for the universal online installer.' }
& "$PSScriptRoot\sign-windows.ps1" -File $online
Assert-PinnedSignature $online
$packages += $onlineName
Write-Utf8 (Join-Path $stage 'release.json') ($manifest | ConvertTo-Json -Depth 6)
$packages += 'release.json'
$checksums = foreach ($name in $packages) {
    '{0}  {1}' -f (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $stage $name)).Hash.ToLowerInvariant(), $name
}
Write-Utf8 (Join-Path $stage 'SHA256SUMS.txt') (($checksums -join "`n") + "`n")
$packages += 'SHA256SUMS.txt'
# Publish the manifest only after all signed installers have succeeded.
New-Item -ItemType Directory -Path $downloads -Force | Out-Null
foreach ($name in $packages) {
    Copy-Item -LiteralPath (Join-Path $stage $name) -Destination (Join-Path $release $name) -Force
    Copy-Item -LiteralPath (Join-Path $stage $name) -Destination (Join-Path $downloads $name) -Force
}
Write-Output "Signed release ${ReleaseVersion}: $release"
Write-Output "Website download files: $downloads (no deployment performed)."
Write-Output "Build stage retained for inspection: $stage"
