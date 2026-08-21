param([Parameter(Mandatory=$true)][string]$PublishDirectory,[string]$InstallDirectory="$env:ProgramFiles\DISRoute\Server")
$ErrorActionPreference="Stop"
$serviceName="DISRouteServer"
$svc=Get-Service -Name $serviceName -ErrorAction Stop
if ($svc.Status -ne 'Stopped') { Stop-Service $serviceName -Force; $svc.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(45)) }
$backup="$InstallDirectory.rollback-$(Get-Date -Format yyyyMMddHHmmss)"
Copy-Item $InstallDirectory $backup -Recurse -Force
try {
  Remove-Item (Join-Path $InstallDirectory '*') -Recurse -Force
  Copy-Item (Join-Path $PublishDirectory '*') $InstallDirectory -Recurse -Force
  Start-Service $serviceName
  (Get-Service $serviceName).WaitForStatus('Running',[TimeSpan]::FromSeconds(60))
  Write-Host "Upgrade complete. Rollback copy: $backup"
} catch {
  Remove-Item $InstallDirectory -Recurse -Force -ErrorAction SilentlyContinue
  Move-Item $backup $InstallDirectory -Force
  Start-Service $serviceName -ErrorAction SilentlyContinue
  throw
}
