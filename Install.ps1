[CmdletBinding()]
param([switch]$Update, [switch]$NoPrompt)
$ErrorActionPreference='Stop'
if(-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
 $arguments='-NoProfile -ExecutionPolicy Bypass -File "'+$PSCommandPath+'"'
 if($Update){$arguments+=' -Update'}
 if($NoPrompt){$arguments+=' -NoPrompt'}
 $child=Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait -PassThru
 exit $child.ExitCode
}
if(-not [Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64'){throw 'This release supports Windows x64 (Intel/AMD) only.'}
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$install=Join-Path $env:ProgramFiles 'CloudflaredNew'
$temp=Join-Path $env:TEMP ('multicloudflared-setup-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
 $payloadRoot=$PSScriptRoot
 $candidate=Join-Path $payloadRoot 'cloudflared.exe'
 if($Update -or !(Test-Path $candidate)) {
   Write-Host 'Downloading latest MultiCloudflared release...'
   $release=Invoke-RestMethod 'https://api.github.com/repos/danhol86/multicloudflared/releases/latest'
   $asset=$release.assets | Where-Object name -eq 'multicloudflared-windows-x64.zip' | Select-Object -First 1
   if(!$asset -or $asset.digest -notmatch '^sha256:([a-fA-F0-9]{64})$'){throw 'Release is missing a SHA-256 verified package.'}
   $expected=$matches[1]
   if($asset.browser_download_url -notlike 'https://github.com/danhol86/multicloudflared/releases/download/*/multicloudflared-windows-x64.zip'){throw 'Unexpected release download address.'}
   $archive=Join-Path $temp 'release.zip'
   Invoke-WebRequest $asset.browser_download_url -UseBasicParsing -OutFile $archive
   if((Get-FileHash $archive -Algorithm SHA256).Hash -ne $expected){throw 'Download checksum mismatch.'}
   Write-Host 'Release package SHA-256 verified.'
   $payloadRoot=Join-Path $temp 'package'
   Expand-Archive -Path $archive -DestinationPath $payloadRoot
   $candidate=Join-Path $payloadRoot 'cloudflared.exe'
 }
 $data=Join-Path $env:ProgramData 'CloudflaredNew'
 $service=Get-Service CloudflaredNew -ErrorAction SilentlyContinue
 if($service) {
   $image=(Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\CloudflaredNew').ImagePath
   if($image -notmatch '--data\s+"([^"]+)"'){throw 'Cannot identify installed service configuration.'}
   $data=$matches[1]
   & $candidate service upgrade --data $data
 } else {
   & $candidate service install --data $data
 }
 if($LASTEXITCODE -ne 0){throw 'Service setup failed.'}
 foreach($name in @('Manage.cmd','Manage.ps1','Update.cmd','Install.ps1','Uninstall.cmd','Uninstall.ps1','README.md','LICENSE','THIRD-PARTY-NOTICES.md')) {
   $source=Join-Path $payloadRoot $name
   $destination=Join-Path $install $name
   if((Test-Path $source) -and [IO.Path]::GetFullPath($source) -ne [IO.Path]::GetFullPath($destination)){Copy-Item $source $destination -Force}
 }
 foreach($name in @('DOTNET-LICENSE.TXT','DOTNET-THIRD-PARTY-NOTICES.TXT')) {
   $source=Join-Path $payloadRoot $name
   if((Test-Path $source) -and [IO.Path]::GetFullPath($source) -ne [IO.Path]::GetFullPath((Join-Path $install $name))){Copy-Item $source $install -Force}
 }
 if((Test-Path (Join-Path $payloadRoot 'docs')) -and [IO.Path]::GetFullPath($payloadRoot) -ne [IO.Path]::GetFullPath($install)){
   Copy-Item (Join-Path $payloadRoot 'docs') $install -Recurse -Force
 }
 if(Test-Path (Join-Path $install 'Manage.cmd')) {
   $shell=New-Object -ComObject WScript.Shell
   $shortcut=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'MultiCloudflared.lnk'))
   $shortcut.TargetPath=Join-Path $install 'Manage.cmd'
   $shortcut.WorkingDirectory=$install
   $shortcut.Save()
 }
 Write-Host ''
 Write-Host 'MultiCloudflared is installed and running.' -ForegroundColor Green
 Write-Host 'Open MultiCloudflared from the Start menu to add tunnels, or run:'
 Write-Host ('& "'+(Join-Path $install 'cloudflarednew.exe')+'" service install <TUNNEL_TOKEN>')
 if(!$NoPrompt){Read-Host 'Press Enter to close'}
} finally { Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue }
