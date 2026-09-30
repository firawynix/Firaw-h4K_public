param([string]$IdentityPath = (Join-Path $PSScriptRoot '..\packaging\store-identity.json'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\packaging\build-common.ps1')
# Fail before publishing or creating output if the real Partner Center identity is missing.
if (-not (Test-Path -LiteralPath $IdentityPath -PathType Leaf)) {
    throw 'Confirmed packaging/store-identity.json is required. Fill the example with actual Partner Center identity values; do not invent an identity.'
}
$identity = Get-Content -LiteralPath $IdentityPath -Raw | ConvertFrom-Json
if ($identity.confirmed -isnot [bool] -or $identity.confirmed -ne $true) {
    throw 'Store identity must have confirmed: true after verifying the values in Partner Center.'
}
foreach ($field in @('Name', 'Publisher', 'PublisherDisplayName', 'DisplayName', 'ApplicationId', 'StoreId')) {
    $value = $identity.$field
    if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value) -or $value -ne $value.Trim() -or
        $value -match '[<>\r\n]' -or $value -match '(?i)TODO|REPLACE|PLACEHOLDER|EXAMPLE') {
        throw "Store identity requires a confirmed, non-placeholder $field."
    }
}
if ($identity.Name -notmatch '^[A-Za-z0-9.-]{3,50}$' -or $identity.Publisher -notmatch '^CN=.{1,8190}$' -or
    $identity.ApplicationId -notmatch '^[A-Za-z][A-Za-z0-9.]{0,63}$' -or
    $identity.DisplayName.Length -gt 256 -or $identity.PublisherDisplayName.Length -gt 256 -or
    $identity.StoreId -notmatch '^[A-Za-z0-9]+$') { throw 'Invalid Store identity format.' }
Assert-BuildPrerequisites
$makeappx = Get-WindowsSdkTool 'makeappx.exe'
Add-Type -AssemblyName System.Drawing
$release = Join-Path $ProjectRoot 'release'
$stageRoot = Join-Path $release ('.store-staging-' + [Guid]::NewGuid().ToString('N'))
$bundleDir = Join-Path $stageRoot 'packages'
New-Item -ItemType Directory -Path $bundleDir -Force | Out-Null
function Xml-Escape([string]$Value) { return [Security.SecurityElement]::Escape($Value) }
$name = Xml-Escape $identity.Name
$publisher = Xml-Escape $identity.Publisher
$displayName = Xml-Escape $identity.DisplayName
$publisherDisplayName = Xml-Escape $identity.PublisherDisplayName
$appId = Xml-Escape $identity.ApplicationId
foreach ($arch in @('x64', 'x86')) {
    $stage = Join-Path $stageRoot $arch
    $app = Join-Path $stage 'app'
    $assets = Join-Path $stage 'Assets'
    New-Item -ItemType Directory -Path $assets -Force | Out-Null
    # Include the entire fresh, self-contained publish output in each MSIX.
    Publish-H4K $arch $app $false
    $image = [Drawing.Image]::FromFile((Join-Path $ProjectRoot 'Assets\firaw-beetle-v3.png'))
    try {
        foreach ($size in @(50, 44, 150)) {
            $bitmap = [Drawing.Bitmap]::new($size, $size)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::Transparent)
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $ratio = [Math]::Min($size / $image.Width, $size / $image.Height)
                $width = [int][Math]::Round($image.Width * $ratio)
                $height = [int][Math]::Round($image.Height * $ratio)
                $graphics.DrawImage($image, [int](($size - $width) / 2), [int](($size - $height) / 2), $width, $height)
                $logo = switch ($size) { 50 { 'StoreLogo.png' }; 44 { 'Square44x44Logo.png' }; 150 { 'Square150x150Logo.png' } }
                $bitmap.Save((Join-Path $assets $logo), [Drawing.Imaging.ImageFormat]::Png)
            } finally { $graphics.Dispose(); $bitmap.Dispose() }
        }
    } finally { $image.Dispose() }
    # The original ICO is embedded by the existing csproj; no source changes needed.
    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10" xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities" IgnorableNamespaces="uap uap10 rescap">
  <Identity Name="$name" Publisher="$publisher" Version="$ReleaseVersion.0" ProcessorArchitecture="$arch" />
  <Properties>
    <DisplayName>$displayName</DisplayName>
    <PublisherDisplayName>$publisherDisplayName</PublisherDisplayName>
    <Description>Efeitos visuais de entretenimento com tema de hack e controles opcionais.</Description>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Resources><Resource Language="pt-BR" /></Resources>
  <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" /></Dependencies>
  <Applications>
    <Application Id="$appId" Executable="app\Firaw-h4K.exe" uap10:RuntimeBehavior="packagedClassicApp" uap10:TrustLevel="mediumIL">
      <uap:VisualElements DisplayName="$displayName" Description="Efeitos visuais de entretenimento com tema de hack e controles opcionais." BackgroundColor="transparent" Square44x44Logo="Assets\Square44x44Logo.png" Square150x150Logo="Assets\Square150x150Logo.png" />
    </Application>
  </Applications>
  <Capabilities><rescap:Capability Name="runFullTrust" /></Capabilities>
</Package>
"@
    Write-Utf8 (Join-Path $stage 'AppxManifest.xml') $manifest
    & $makeappx pack /o /d $stage /p (Join-Path $bundleDir "Firaw-h4K-$ReleaseVersion-$arch.msix")
    if ($LASTEXITCODE -ne 0) { throw "MakeAppx validation/pack failed for $arch." }
}
$bundleName = "Firaw-h4K-$ReleaseVersion.msixbundle"
$bundle = Join-Path $stageRoot $bundleName
& $makeappx bundle /o /bv "$ReleaseVersion.0" /d $bundleDir /p $bundle
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx bundle failed.' }
$packageOutput = Join-Path $release "store-packages-$ReleaseVersion"
New-Item -ItemType Directory -Path $packageOutput -Force | Out-Null
foreach ($arch in @('x64', 'x86')) {
    $file = "Firaw-h4K-$ReleaseVersion-$arch.msix"
    Copy-Item -LiteralPath (Join-Path $bundleDir $file) -Destination (Join-Path $packageOutput $file) -Force
}
Copy-Item -LiteralPath $bundle -Destination (Join-Path $release $bundleName) -Force
Write-Output "Store submission bundle: $(Join-Path $release $bundleName)"
Write-Output 'The app EXEs are signed. MSIX packages/bundle are unsigned for Store submission; Microsoft applies the Store signature. No identity or certificate is substituted for sideloading.'
Write-Output "Build stage retained for inspection: $stageRoot"
