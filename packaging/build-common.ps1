$ErrorActionPreference = 'Stop'
$ReleaseVersion = '1.0.0'
$ReleaseThumbprint = '668BEF56480FCA8EB1B0BC3B102A6A9CEC554CC4'
$ProjectRoot = Split-Path -Parent $PSScriptRoot

function Get-WindowsSdkTool([string]$Name) {
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $tool = Get-ChildItem -LiteralPath $sdkRoot -Filter $Name -Recurse -File |
        Where-Object FullName -Match '\\x64\\' | Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $tool) { throw "$Name was not found in the official Windows SDK." }
    return $tool
}

function Assert-BuildPrerequisites {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET 10 SDK is required.' }
    $sdks = & dotnet --list-sdks
    if ($LASTEXITCODE -ne 0 -or -not ($sdks -match '^10\.')) { throw '.NET 10 SDK is required.' }
    foreach ($asset in @('firaw-beetle-v3.png', 'firaw-beetle-v3.ico')) {
        if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot "Assets\$asset") -PathType Leaf)) {
            throw "Missing existing asset: $asset"
        }
    }
    $null = Get-WindowsSdkTool 'signtool.exe'
    $cert = Get-Item -LiteralPath "Cert:\CurrentUser\My\$ReleaseThumbprint" -ErrorAction Stop
    if (-not $cert.HasPrivateKey -or $cert.Subject -ne 'CN=Firawynix' -or
        $cert.NotBefore -gt (Get-Date) -or $cert.NotAfter -le (Get-Date)) {
        throw 'The pinned Firawynix certificate is unavailable or expired.'
    }
}

function Assert-PinnedSignature([string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if (-not $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $ReleaseThumbprint -or
        $signature.Status -notin @('Valid', 'NotTrusted') -or -not $signature.TimeStamperCertificate) {
        throw "Missing or invalid pinned timestamped signature: $Path"
    }
}

function Publish-H4K([string]$Architecture, [string]$Destination, [bool]$SingleFile) {
    $single = $SingleFile.ToString().ToLowerInvariant()
    & dotnet publish (Join-Path $ProjectRoot 'FirawH4K.csproj') -c Release -f net10.0-windows `
        -r "win-$Architecture" --self-contained true "-p:PublishSingleFile=$single" `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false `
        -p:DebugType=None -p:DebugSymbols=false "-p:Version=$ReleaseVersion" `
        "-p:AssemblyVersion=$ReleaseVersion.0" "-p:FileVersion=$ReleaseVersion.0" -o $Destination
    if ($LASTEXITCODE -ne 0) { throw "Publishing failed for $Architecture." }
    $exe = Join-Path $Destination 'Firaw-h4K.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Missing published executable: $exe" }
    if ($SingleFile) {
        $extra = @(Get-ChildItem -LiteralPath $Destination -Recurse -File | Where-Object FullName -NE $exe)
        if ($extra.Count) { throw 'Offline packaging expects a single self-contained EXE; review additional publish content before releasing.' }
    }
    & (Join-Path $ProjectRoot 'tools\sign-windows.ps1') -File $exe
    Assert-PinnedSignature $exe
}

function Write-Utf8([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}
