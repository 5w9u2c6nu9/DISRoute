param(
    [Parameter(Mandatory=$true)][string]$PublishDirectory,
    [string]$InstallDirectory = "$env:ProgramFiles\DISRoute\Server",
    [string]$DataDirectory = "$env:ProgramData\DISRoute"
)
$ErrorActionPreference = "Stop"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Run from an elevated PowerShell session." }
$sourceExe = Join-Path $PublishDirectory "DISRoute.Server.exe"
if (-not (Test-Path $sourceExe)) { throw "Published DISRoute.Server.exe not found in $PublishDirectory" }
$serviceName = "DISRouteServer"
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) { throw "$serviceName already exists. Upgrade it with Update-DISRouteServer.ps1." }
New-Item -ItemType Directory -Force -Path $InstallDirectory, "$DataDirectory\config", "$DataDirectory\data", "$DataDirectory\logs", "$DataDirectory\secrets" | Out-Null
Copy-Item (Join-Path $PublishDirectory '*') $InstallDirectory -Recurse -Force
$config = Join-Path $DataDirectory "config\appsettings.Production.json"
if (-not (Test-Path $config)) { Copy-Item (Join-Path $PSScriptRoot "appsettings.Production.template.json") $config }
$exe = Join-Path $InstallDirectory "DISRoute.Server.exe"
$binPath = '"' + $exe + '" --windows-service'
& sc.exe create $serviceName binPath= $binPath start= auto DisplayName= "DISRoute Server" obj= "NT SERVICE\DISRouteServer" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed with $LASTEXITCODE" }
& sc.exe description $serviceName "DISRoute authoritative backend" | Out-Null
& sc.exe sidtype $serviceName unrestricted | Out-Null
& sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/15000/none/0 | Out-Null
$serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
New-ItemProperty -Path $serviceKey -Name Environment -PropertyType MultiString -Value @(
  "ASPNETCORE_ENVIRONMENT=Production",
  "DOTNET_ENVIRONMENT=Production",
  "DISROUTE_CONFIG_FILE=$config"
) -Force | Out-Null
& icacls.exe $InstallDirectory /inheritance:r /grant:r "Administrators:(OI)(CI)F" "SYSTEM:(OI)(CI)F" "NT SERVICE\DISRouteServer:(OI)(CI)RX" | Out-Null
& icacls.exe $DataDirectory /inheritance:r /grant:r "Administrators:(OI)(CI)F" "SYSTEM:(OI)(CI)F" "NT SERVICE\DISRouteServer:(OI)(CI)M" | Out-Null
& icacls.exe (Join-Path $DataDirectory 'secrets') /inheritance:r /grant:r "Administrators:(OI)(CI)F" "SYSTEM:(OI)(CI)F" "NT SERVICE\DISRouteServer:(OI)(CI)R" | Out-Null
Write-Host "Installed $serviceName. Provision $config and secret files, then Start-Service $serviceName."
