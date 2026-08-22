param([string]$Out = "")
$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($Out)) { $Out = Join-Path $Root "artifacts\publish" }
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$restoreArgs = @("restore", (Join-Path $Root "DISRoute.slnx"), "--locked-mode", "-m:1")
if (-not [string]::IsNullOrWhiteSpace($env:DISROUTE_NUGET_CONFIG)) { $restoreArgs += @("--configfile", $env:DISROUTE_NUGET_CONFIG) }
& dotnet @restoreArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }
$RidLockRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("disroute-ridlocks-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $RidLockRoot | Out-Null
try {
    function Publish-Target([string]$Project,[string]$Rid,[string]$Name) {
        $lockDir = Join-Path $RidLockRoot $Name
        New-Item -ItemType Directory -Force -Path $lockDir | Out-Null
        $ridRestore = @("restore", (Join-Path $Root $Project), "-r", $Rid, "--force-evaluate", "--disable-parallel", "-m:1", "-p:DISROUTE_RID_RESTORE_DIR=$lockDir")
        if (-not [string]::IsNullOrWhiteSpace($env:DISROUTE_NUGET_CONFIG)) { $ridRestore += @("--configfile", $env:DISROUTE_NUGET_CONFIG) }
        & dotnet @ridRestore
        if ($LASTEXITCODE -ne 0) { throw "RID restore failed: $Name" }
        $args = @("publish", (Join-Path $Root $Project), "-c", "Release", "-r", $Rid, "--self-contained", "false", "-o", (Join-Path $Out $Name), "--no-restore", "-m:1")
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
} finally {
    if (Test-Path $RidLockRoot) { Remove-Item $RidLockRoot -Recurse -Force }
}
Copy-Item (Join-Path $Root "deploy") (Join-Path $Out "deploy") -Recurse
Copy-Item (Join-Path $Root "docs\ops\PRODUCTION-DEPLOYMENT.md") $Out
Copy-Item (Join-Path $Root "docs\ops\UPGRADE-ROLLBACK.md") $Out
Copy-Item (Join-Path $Root "docs\ops\RELEASE-CANDIDATE-ACCEPTANCE.md") (Join-Path $Out "RELEASE-CANDIDATE-ACCEPTANCE.md") -Force
Copy-Item (Join-Path $Root "docs\ops\KNOWN-LIMITATIONS.md") (Join-Path $Out "KNOWN-LIMITATIONS.md") -Force
Copy-Item (Join-Path $Root "docs\ops\RELEASE-CHECKLIST.md") (Join-Path $Out "RELEASE-CHECKLIST.md") -Force
Copy-Item (Join-Path $Root "docs\ops\RELEASE-NOTES-1.0.0-rc.1.md") (Join-Path $Out "RELEASE-NOTES-1.0.0-rc.1.md") -Force
& python (Join-Path $Root "scripts/verify-published-dependencies.py") $Root $Out
if ($LASTEXITCODE -ne 0) { throw "published dependency verification failed" }
Write-Host "Published DISRoute production artifacts to $Out"
