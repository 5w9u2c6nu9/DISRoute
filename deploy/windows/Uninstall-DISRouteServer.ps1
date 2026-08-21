param([switch]$RemoveData)
$ErrorActionPreference="Stop"
$serviceName="DISRouteServer"
$svc=Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($svc) { if ($svc.Status -ne 'Stopped') { Stop-Service $serviceName -Force }; & sc.exe delete $serviceName | Out-Null }
if ($RemoveData) { Remove-Item "$env:ProgramData\DISRoute" -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host "DISRoute Server service removed. Program files/data are preserved unless explicitly removed."
