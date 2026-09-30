param([switch]$VerifyOnly)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$env:PSModulePath = Join-Path $PSHOME 'Modules'
$thumb = '668BEF56480FCA8EB1B0BC3B102A6A9CEC554CC4'
$origin = 'https://h4k.firawynix.com.br/downloads'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$temp = Join-Path $tempRoot ('FirawH4K-' + [Guid]::NewGuid().ToString('N'))
$exitCode = 1603
try {
    New-Item -ItemType Directory -Path $temp | Out-Null
    $systemDirectory = if ([Environment]::Is64BitOperatingSystem -and -not [Environment]::Is64BitProcess) { 'Sysnative' } else { 'System32' }
    $curl = Join-Path $env:WINDIR "$systemDirectory\curl.exe"
    if (-not (Test-Path -LiteralPath $curl -PathType Leaf)) { throw 'Windows curl.exe is required. Update Windows or use the offline installer.' }
    function Save-HttpsFile([string]$Url, [string]$Destination, [int]$Timeout, [long]$MaxBytes) {
        $errorFile = Join-Path $temp 'download-error.txt'
        # Ignore user curl configuration; redirects are intentionally not followed.
        & $curl --disable --fail --silent --show-error --proto '=https' --tlsv1.2 `
            --connect-timeout 30 --max-time $Timeout --max-filesize $MaxBytes `
            --stderr $errorFile --output $Destination $Url
        if ($LASTEXITCODE -ne 0) {
            $detail = if (Test-Path -LiteralPath $errorFile) { [IO.File]::ReadAllText($errorFile).Trim() } else { '' }
            throw "Download failed. $detail"
        }
        if ((Get-Item -LiteralPath $Destination).Length -gt $MaxBytes) { throw 'Download exceeds the allowed size.' }
    }
    $manifestFile = Join-Path $temp 'release.json'
    Save-HttpsFile "$origin/release.json" $manifestFile 60 65536
    $manifest = [IO.File]::ReadAllText($manifestFile) | ConvertFrom-Json
    if ([string]$manifest.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version.' }
    $arch = if ([Environment]::Is64BitOperatingSystem) { 'x64' } else { 'x86' }
    $entry = $manifest.architectures.$arch
    if (-not $entry) { throw "The manifest has no $arch package." }
    $expectedUrl = "$origin/Firaw-h4K-$($manifest.version)-$arch-Setup.exe"
    if ([string]$entry.url -cne $expectedUrl) { throw 'The package URL is not the canonical release URL.' }
    if ([string]$entry.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or
        [string]$entry.signerThumbprint -ne $thumb -or
        [string]$entry.size -notmatch '^[1-9][0-9]{0,9}$' -or
        [long]$entry.size -gt 2147483647) { throw 'Invalid size, hash, or pinned signer in the manifest.' }
    $setup = Join-Path $temp 'setup.exe'
    Save-HttpsFile $expectedUrl $setup 900 ([long]$entry.size)
    if ((Get-Item -LiteralPath $setup).Length -ne [long]$entry.size) { throw 'Downloaded size does not match the manifest.' }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $setup).Hash -ne [string]$entry.sha256) {
        throw 'Installer SHA-256 does not match the manifest.'
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $setup
    if (-not $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $thumb -or
        $signature.Status -notin @('Valid', 'NotTrusted') -or -not $signature.TimeStamperCertificate) {
        throw 'Installer does not have the pinned timestamped Firawynix signature.'
    }
    # Prevent a stale signed installer from being relabeled by a modified manifest.
    $fileVersion = (Get-Item -LiteralPath $setup).VersionInfo.FileVersion
    if ($fileVersion -ne [string]$manifest.version) { throw 'Signed installer version does not match the manifest.' }
    if ($VerifyOnly) {
        Write-Output "Verified $($manifest.version) $arch."
        $exitCode = 0
    } else {
        # Consent was collected by the bootstrapper, or explicitly via its silent flag.
        # The offline installer performs the definitive running-file check again.
        $process = Start-Process -FilePath $setup -ArgumentList '/S /ACCEPTCONSENT' -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -eq 1618) {
            $exitCode = 1618
            throw 'Firaw h4K is in use or another installation is running. Press Ctrl+Alt+F12, close the app, and retry.'
        }
        if ($process.ExitCode -ne 0) { throw "Offline installer failed with exit code $($process.ExitCode)." }
        $installDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\Firaw h4K'
        if (-not (Test-Path -LiteralPath (Join-Path $installDir 'Firaw-h4K.exe') -PathType Leaf)) { throw 'Installed executable is missing.' }
        # NSIS uses a stable 32-bit registry view for both package architectures.
        $hive = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry32)
        $key = $null
        try {
            $key = $hive.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall\FirawH4K')
            if (-not $key -or $key.GetValue('DisplayVersion') -ne [string]$manifest.version -or
                $key.GetValue('InstallLocation') -ne $installDir) { throw 'Installed release registration does not match the requested version.' }
        } finally {
            if ($key) { $key.Dispose() }
            $hive.Dispose()
        }
        $exitCode = 0
    }
} catch {
    Write-Output $_.Exception.Message
} finally {
    $resolved = [IO.Path]::GetFullPath($temp)
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notmatch '^FirawH4K-[a-f0-9]{32}$') {
        throw 'Refusing cleanup outside the unique installer temporary directory.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
exit $exitCode
