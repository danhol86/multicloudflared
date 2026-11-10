[CmdletBinding()]
param([switch]$Purge, [switch]$NoPrompt)
$ErrorActionPreference='Stop'
if(-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
 $arguments='-NoProfile -ExecutionPolicy Bypass -File "'+$PSCommandPath+'"'
 if($Purge){$arguments+=' -Purge'}
 if($NoPrompt){$arguments+=' -NoPrompt'}
 $child=Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait -PassThru
 exit $child.ExitCode
}
$install=Join-Path $env:ProgramFiles 'CloudflaredNew'
$data=Join-Path $env:ProgramData 'CloudflaredNew'
if(Get-Service CloudflaredNew -ErrorAction SilentlyContinue) {
 $image=(Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\CloudflaredNew').ImagePath
 if($image -notmatch '--data\s+"([^"]+)"'){throw 'Cannot identify service data directory.'}
 $data=$matches[1]
 $exe=Join-Path $install 'cloudflarednew.exe'
 & $exe service uninstall --data $data
 if($LASTEXITCODE){throw 'Service removal failed; files were retained.'}
}
$shortcut=Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'MultiCloudflared.lnk'
if(Test-Path $shortcut){Remove-Item $shortcut -Force}
Set-Location $env:TEMP
if(Test-Path $install){Remove-Item $install -Recurse -Force}
if($Purge) {
 # Only purge the product's standard data folder automatically.
 $standard=Join-Path $env:ProgramData 'CloudflaredNew'
 if([IO.Path]::GetFullPath($data).TrimEnd('\') -ne $standard.TrimEnd('\')){throw "Application removed. Custom data directory retained: $data"}
 if(Test-Path $data){Remove-Item $data -Recurse -Force}
 Write-Host 'MultiCloudflared and its stored tokens/logs removed.'
} else { Write-Host "MultiCloudflared removed. Settings and encrypted tokens retained at $data" }
Write-Host 'Cloudflare tunnels/DNS and the original Cloudflared service were not deleted.'
if(!$NoPrompt){Read-Host 'Press Enter to close'}
