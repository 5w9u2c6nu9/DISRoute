param([string]$Out = "")
$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($Out)) { $Out = Join-Path $Root "artifacts\publish" }
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$restoreArgs = @("restore", (Join-Path $Root "DISRoute.slnx"), "-m:1")
if (-not [string]::IsNullOrWhiteSpace($env:DISROUTE_NUGET_CONFIG)) { $restoreArgs += @("--configfile", $env:DISROUTE_NUGET_CONFIG) }
& dotnet @restoreArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }
function Publish-Target([string]$Project,[string]$Rid,[string]$Name) {
    $args = @("publish", (Join-Path $Root $Project), "-c", "Release", "-r", $Rid, "--self-contained", "false", "-o", (Join-Path $Out $Name))
    if (-not [string]::IsNullOrWhiteSpace($env:DISROUTE_NUGET_CONFIG)) { $args += "-p:RestoreConfigFile=$($env:DISROUTE_NUGET_CONFIG)" }
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "publish failed: $Name" }
}
Publish-Target "src\DISRoute.Server\DISRoute.Server.csproj" "win-x64" "server-win-x64"
Publish-Target "src\DISRoute.Server\DISRoute.Server.csproj" "linux-x64" "server-linux-x64"
Publish-Target "src\DISRoute.CAD\DISRoute.CAD.csproj" "win-x64" "cad-win-x64"
Publish-Target "src\DISRoute.Station\DISRoute.Station.csproj" "win-x64" "station-win-x64"
Publish-Target "src\DISRoute.OperationsCenter\DISRoute.OperationsCenter.csproj" "win-x64" "operations-center-win-x64"
Publish-Target "src\DISRoute.Mobile\DISRoute.Mobile.csproj" "win-x64" "mobile-field-harness-win-x64"
Publish-Target "src\DISRoute.Station.Display\DISRoute.Station.Display.csproj" "linux-arm64" "station-display-linux-arm64"
Publish-Target "src\DISRoute.LegacyBridge\DISRoute.LegacyBridge.csproj" "win-x64" "legacy-bridge-win-x64"
Copy-Item (Join-Path $Root "deploy") (Join-Path $Out "deploy") -Recurse
Copy-Item (Join-Path $Root "docs\ops\PRODUCTION-DEPLOYMENT.md") $Out
Copy-Item (Join-Path $Root "docs\ops\UPGRADE-ROLLBACK.md") $Out
Write-Host "Published DISRoute production artifacts to $Out"
