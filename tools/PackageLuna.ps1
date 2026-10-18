param(
    [Parameter(Mandatory = $true)][string]$ToolboxPath,
    [string]$OutputDirectory = "artifacts/packages",
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$toolbox = (Resolve-Path -LiteralPath $ToolboxPath).Path
$hostDirectory = Split-Path -Parent $toolbox
$sdkVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $hostDirectory "Playnite.SDK.dll")).Version
if ($sdkVersion.Major -ne 6 -or $sdkVersion -lt [Version]"6.17.0.0") { throw "Use Playnite 10 with SDK 6.17 or later." }
$jsonIdentity = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $hostDirectory "Newtonsoft.Json.dll"))
if ($jsonIdentity.Name -ne "Newtonsoft.Json" -or $jsonIdentity.Version -ne [Version]"10.0.0.0") { throw "Use a Playnite 10 host providing Newtonsoft.Json 10 (verified with 10.62)." }
Push-Location $repoRoot
try {
    dotnet build src/LunaPlaynite/LunaPlaynite.csproj --no-restore --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Plugin build failed." }
    $stage = Join-Path $repoRoot "src/LunaPlaynite/bin/$Configuration/net462/extension"
    $expected = @("extension.yaml", "LunaPlaynite.dll", "LunaCatalogBrowser.dll", "LunaCatalog.dll", "LICENSE", "THIRD-PARTY-NOTICES.txt")
    $actual = @(Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object {
        $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
    })
    $difference = Compare-Object ($expected | Sort-Object) ($actual | Sort-Object)
    if ($difference) { throw "Unexpected or missing extension files in the staging directory." }
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $destination = (Resolve-Path -LiteralPath $OutputDirectory).Path
    & $toolbox pack $stage $destination
    if ($LASTEXITCODE -ne 0) { throw "Playnite Toolbox packaging failed." }
    $package = Join-Path $destination "LunaPlaynite_6f4229ca-70a9-4c65-ae9f-047a367beda9_0_2_4.pext"
    if (-not (Test-Path -LiteralPath $package)) { throw "Toolbox did not produce the expected package." }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        if (Compare-Object ($expected | Sort-Object) ($entries | Sort-Object)) { throw "Package contents differ from the expected runtime files." }
    } finally { $archive.Dispose() }
    Write-Output "Verified package: $package"
} finally { Pop-Location }
