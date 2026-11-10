[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
$exe=Join-Path $base 'publish\cloudflarednew.exe'
if(!(Test-Path $exe)){throw 'Run build.ps1 before packaging.'}
$dist=Join-Path $base 'dist'
$stage=Join-Path $dist 'package'
if(Test-Path $dist){Remove-Item $dist -Recurse -Force}
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item $exe (Join-Path $stage 'cloudflared.exe')
foreach($name in @('Install.cmd','Install.ps1','Manage.cmd','Manage.ps1','Update.cmd','Uninstall.cmd','Uninstall.ps1','README.md','LICENSE','THIRD-PARTY-NOTICES.md')) {
 Copy-Item (Join-Path $base $name) $stage
}
Copy-Item (Join-Path $base 'docs') (Join-Path $stage 'docs') -Recurse
$deps=Get-Content (Join-Path $base 'src\bin\Release\net10.0-windows\win-x64\cloudflarednew.deps.json') -Raw | ConvertFrom-Json
$runtime=($deps.libraries.PSObject.Properties.Name | Where-Object {$_ -like 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/*'} | Select-Object -First 1).Split('/')[-1]
if($runtime -notmatch '^\d+\.\d+\.\d+$'){throw 'Cannot determine packaged .NET runtime license version.'}
foreach($notice in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
 Invoke-WebRequest "https://raw.githubusercontent.com/dotnet/runtime/v$runtime/$notice" -UseBasicParsing -OutFile (Join-Path $stage "DOTNET-$notice")
}
Copy-Item $exe (Join-Path $dist 'cloudflared.exe')
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath (Join-Path $dist 'multicloudflared-windows-x64.zip') -CompressionLevel Optimal
$lines=foreach($name in @('cloudflared.exe','multicloudflared-windows-x64.zip')) {
 $hash=(Get-FileHash (Join-Path $dist $name) -Algorithm SHA256).Hash.ToLowerInvariant()
 "$hash  $name"
}
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'),[string[]]$lines,[Text.UTF8Encoding]::new($false))
Write-Host 'Packaged Windows x64 ZIP, standalone executable and SHA256SUMS.txt.'
