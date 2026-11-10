$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
$exe=Join-Path $base 'publish\cloudflarednew.exe'
if(Get-Service CloudflaredNew -ErrorAction SilentlyContinue){throw 'Refusing to test over an existing CloudflaredNew service.'}
$data=Join-Path $env:ProgramData ('MultiCloudflared-service-test-'+[guid]::NewGuid().ToString('N'))
function Invoke-ManagerCli([string[]]$a) { & $exe @a --data $data; if($LASTEXITCODE){throw "Command failed: $($a[0])"} }
function Snapshot { if(Test-Path "$data\status.json"){Get-Content "$data\status.json" -Raw | ConvertFrom-Json} }
function WaitFor([scriptblock]$check,[string]$label) {
 $until=(Get-Date).AddSeconds(20)
 do { if(& $check){Write-Host "PASS: $label"; return}; Start-Sleep -Milliseconds 300 } while((Get-Date) -lt $until)
 throw "FAIL: $label"
}
function FakeToken([string]$id,[string]$secret='fake-secret') {
 [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((@{a='fake-account';t=$id;s=$secret}|ConvertTo-Json -Compress)))
}
try {
 Invoke-ManagerCli @('engine',(Join-Path $base 'tests\FakeEngine\bin\Release\net10.0\FakeEngine.exe'))
 $one=FakeToken '11111111-1111-1111-1111-111111111111'
 $two=FakeToken '22222222-2222-2222-2222-222222222222'
 Invoke-ManagerCli @('service','install',$one)
 WaitFor { @((Snapshot).Workers | Where-Object State -eq connected).Count -eq 1 } 'fresh service install TOKEN'
 $pidBefore=(Snapshot).Workers[0].Pid
 Invoke-ManagerCli @('service','install',$one)
 Start-Sleep -Seconds 2
 if((Snapshot).Workers[0].Pid -ne $pidBefore){throw 'Repeated install interrupted worker'}
 Write-Host 'PASS: identical token install is idempotent'
 Invoke-ManagerCli @('service','install',$two)
 WaitFor { @((Snapshot).Workers | Where-Object State -eq connected).Count -eq 2 } 'second token added to running service'
 if(((Snapshot).Workers | Where-Object Name -eq '11111111-1111-1111-1111-111111111111').Pid -ne $pidBefore){throw 'Adding second tunnel interrupted first'}
 $rotated=FakeToken '11111111-1111-1111-1111-111111111111' 'rotated-fake-secret'
 Invoke-ManagerCli @('service','install',$rotated)
 WaitFor { $w=(Snapshot).Workers | Where-Object Name -eq '11111111-1111-1111-1111-111111111111'; $w.Pid -and $w.Pid -ne $pidBefore } 'rotated token replaced worker'
 Invoke-ManagerCli @('service','stop')
 Invoke-ManagerCli @('service','install',$rotated)
 WaitFor { @((Snapshot).Workers | Where-Object State -eq connected).Count -eq 2 } 'install starts stopped service'
 Invoke-ManagerCli @('service','upgrade')
 WaitFor { @((Snapshot).Workers | Where-Object State -eq connected).Count -eq 2 } 'upgrade preserved both tunnels'
 & (Join-Path $base 'Install.ps1') -NoPrompt
 WaitFor { @((Snapshot).Workers | Where-Object State -eq connected).Count -eq 2 } 'ZIP installer upgrades existing service'
 $shortcut=Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'MultiCloudflared.lnk'
 if(!(Test-Path $shortcut)){throw 'Installer did not create Start menu shortcut'}
 & (Join-Path $base 'Uninstall.ps1') -NoPrompt
 if(Get-Service CloudflaredNew -ErrorAction SilentlyContinue){throw 'Uninstaller left service installed'}
 if(!(Test-Path "$data\tunnels.json")){throw 'Uninstaller removed configuration'}
 if(Test-Path $shortcut){throw 'Uninstaller left shortcut'}
 Invoke-ManagerCli @('service','install',$rotated)
 WaitFor { @((Snapshot).Workers | Where-Object State -eq connected).Count -eq 2 } 'reinstall recovers preserved configuration'
 Invoke-ManagerCli @('service','uninstall')
 Invoke-ManagerCli @('service','uninstall')
 if(!(Test-Path "$data\tunnels.json")){throw 'Uninstall lost config'}
 Write-Host 'PASS: uninstall is idempotent and preserves configuration'
} finally {
 if(Get-Service CloudflaredNew -ErrorAction SilentlyContinue){& $exe service uninstall --data $data}
 if(Test-Path $data){Remove-Item $data -Recurse -Force}
}
