if(-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
 Start-Process powershell.exe -Verb RunAs -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $PSCommandPath)
 exit
}
$exe=Join-Path $PSScriptRoot 'cloudflarednew.exe'
if(!(Test-Path $exe)){$exe=Join-Path $PSScriptRoot 'cloudflared.exe'}
while($true) {
 Clear-Host
 Write-Host 'MultiCloudflared - Tunnel Manager' -ForegroundColor Cyan
 & $exe status
 Write-Host ''
 Write-Host '1 Add tunnel    2 Remove tunnel    3 Start tunnel'
 Write-Host '4 Stop tunnel   5 Restart tunnel   6 View logs'
 Write-Host '7 Install service   8 Start service   9 Stop service'
 Write-Host 'T Replace token   E Set engine path   U Uninstall service'
 Write-Host 'R Refresh   Q Quit'
 $choice=Read-Host 'Choose'
 switch($choice.ToUpperInvariant()) {
  '1' { & $exe add (Read-Host 'Tunnel name') }
  '2' { & $exe remove (Read-Host 'Tunnel name') }
  '3' { & $exe start (Read-Host 'Tunnel name') }
  '4' { & $exe stop (Read-Host 'Tunnel name') }
  '5' { & $exe restart (Read-Host 'Tunnel name') }
  '6' { & $exe logs (Read-Host 'Tunnel name') }
  '7' { & $exe service install }
  '8' { & $exe service start }
  '9' { & $exe service stop }
  'T' { & $exe token (Read-Host 'Tunnel name') }
  'E' { & $exe engine (Read-Host 'Full path to official cloudflared.exe') }
  'U' { & $exe service uninstall }
  'R' { continue }
  'Q' { exit }
  default { continue }
 }
 Read-Host 'Press Enter to continue'
}
