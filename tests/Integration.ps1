$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
$exe=Join-Path $base 'publish\cloudflarednew.exe'
$data=Join-Path $env:ProgramData ('CloudflaredNew-test-'+[guid]::NewGuid().ToString('N'))
$manager=$null
function Invoke-Cli([string[]]$Arguments) {
  & $exe @Arguments --data $data
  if($LASTEXITCODE -ne 0){throw "CLI failed: $($Arguments[0])"}
}
function Wait-For([scriptblock]$Condition,[string]$Description) {
  $deadline=(Get-Date).AddSeconds(20)
  do { if(& $Condition){Write-Output "PASS: $Description"; return}; Start-Sleep -Milliseconds 300 } while((Get-Date) -lt $deadline)
  throw "FAIL: $Description"
}
function State { if(Test-Path "$data\status.json"){Get-Content "$data\status.json" -Raw | ConvertFrom-Json} }
try {
 $dummy=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('{"a":"test-account","t":"11111111-1111-1111-1111-111111111111","s":"test-secret"}'))
 foreach($name in @('alpha','beta')) { $dummy | & $exe add $name --token-stdin --data $data; if($LASTEXITCODE){throw 'add failed'} }
 Invoke-Cli @('engine', (Join-Path $base 'tests\FakeEngine\bin\Release\net10.0\FakeEngine.exe'))
 $manager=Start-Process $exe -ArgumentList @('run','--data',('"' + $data + '"')) -PassThru -WindowStyle Hidden
 Wait-For { @((State).Workers | Where-Object State -eq connected).Count -eq 2 } 'two workers connected'
 $ids=@((State).Workers.Pid)
 $disk=Get-Content "$data\tunnels.json" -Raw
 if($disk.Contains($dummy)){throw 'plaintext token persisted'}
 $logs=(Get-Content "$data\logs\*.log" -Raw) -join ''
 if($logs.Contains($dummy) -or !$logs.Contains('[REDACTED]')){throw 'log redaction failed'}
 Write-Output 'PASS: tokens encrypted on disk and redacted from output'
 $cmds=Get-CimInstance Win32_Process | Where-Object ProcessId -in $ids
 if(@($cmds | Where-Object {$_.CommandLine -like "*$dummy*"}).Count){throw 'token in command line'}
 Write-Output 'PASS: no token in worker command lines'
 Invoke-Cli @('stop','alpha')
 Wait-For { @((State).Workers).Count -eq 1 -and !(Get-Process -Id $ids[0] -ErrorAction SilentlyContinue) } 'stop removes only requested worker'
 $beta=(State).Workers[0].Pid
 Stop-Process -Id $beta -Force
 Wait-For { $w=(State).Workers; $w.Count -eq 1 -and $w[0].Pid -and $w[0].Pid -ne $beta } 'crashed worker automatically restarts'
 Invoke-Cli @('start','alpha')
 Wait-For { @((State).Workers | Where-Object State -eq connected).Count -eq 2 } 'start adds worker without stopping peer'
 $old=((State).Workers | Where-Object Name -eq alpha).Pid
 Invoke-Cli @('restart','alpha')
 Wait-For { $w=(State).Workers | Where-Object Name -eq alpha; $w.Pid -and $w.Pid -ne $old } 'restart replaces selected worker'
 Invoke-Cli @('remove','beta')
 Wait-For { @((State).Workers).Count -eq 1 -and (State).Workers[0].Name -eq 'alpha' } 'remove hot reloads'
 $child=(State).Workers[0].Pid
 Stop-Process -Id $manager.Id -Force
 $manager=$null
 Wait-For { !(Get-Process -Id $child -ErrorAction SilentlyContinue) } 'job object cleans up workers on manager crash'
 Write-Output 'ALL INTEGRATION CHECKS PASSED'
} finally {
 if($manager -and !$manager.HasExited){Stop-Process -Id $manager.Id -Force}
 if(Test-Path $data){Remove-Item $data -Recurse -Force}
}
