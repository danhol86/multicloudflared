$ErrorActionPreference='Stop'
$exe=Join-Path (Split-Path $PSScriptRoot -Parent) 'publish\cloudflarednew.exe'
$data=Join-Path $env:ProgramData ('CloudflaredNew-livecheck-'+[guid]::NewGuid().ToString('N'))
$installed=$false
try {
 if(Get-Service CloudflaredNew -ErrorAction SilentlyContinue){throw 'CloudflaredNew already installed; refusing to alter it.'}
 $original=Get-CimInstance Win32_Service -Filter "Name='Cloudflared'"
 $originalPid=$original.ProcessId
 if($original.PathName -notmatch '--token\s+("?)([A-Za-z0-9+/=_-]+)\1'){throw 'Existing service does not expose a supported token argument; skipped live check.'}
 $token=$matches[2]
 $token | & $exe add live-check --token-stdin --data $data
 $token=$null; $matches=$null
 if($LASTEXITCODE){throw 'Could not add live-check'}
 & $exe service install --data $data
 if($LASTEXITCODE){throw 'Could not install test service'}
 $installed=$true
 $deadline=(Get-Date).AddSeconds(35)
 $connected=$false
 do {
   Start-Sleep -Seconds 1
   if(Test-Path "$data\status.json") {
     $status=Get-Content "$data\status.json" -Raw | ConvertFrom-Json
     if(@($status.Workers | Where-Object State -eq connected).Count -eq 1){$connected=$true;break}
   }
 } while((Get-Date) -lt $deadline)
 if(!$connected){ & $exe logs live-check --data $data; throw 'Live connection was not registered'}
 Write-Output 'PASS: Windows service decrypted token as LocalSystem and registered a real Cloudflare connection'
 & $exe service restart --data $data
 if($LASTEXITCODE){throw 'Service restart failed'}
 Write-Output 'PASS: Windows service restart'
 $current=Get-CimInstance Win32_Service -Filter "Name='Cloudflared'"
 if($current.State -ne 'Running' -or $current.ProcessId -ne $originalPid){throw 'Original service changed unexpectedly'}
 Write-Output 'PASS: original Cloudflared service remained running with same PID'
} finally {
 if($installed){ & $exe service uninstall --data $data }
 if(Test-Path $data){Remove-Item $data -Recurse -Force}
}
