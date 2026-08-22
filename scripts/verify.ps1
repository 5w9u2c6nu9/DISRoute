$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    python scripts/verify-source.py
    dotnet --info
    $restoreArgs = @('restore', 'DISRoute.slnx', '--locked-mode', '-m:1', '-p:Platform=Any CPU')
    if ($env:DISROUTE_NUGET_CONFIG) {
        $restoreArgs += @('--configfile', $env:DISROUTE_NUGET_CONFIG)
    }
    & dotnet @restoreArgs
    dotnet build DISRoute.slnx -c Release --no-restore '-p:Platform=Any CPU'
    dotnet src/DISRoute.SelfTest/bin/Release/net10.0/DISRoute.SelfTest.dll
    Write-Host 'DISRoute solution build and self-test completed successfully.' -ForegroundColor Green
} finally {
    Pop-Location
}
