$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
 dotnet publish .\src\cloudflarednew.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o .\publish
 if($LASTEXITCODE){throw 'Publish failed'}
 Copy-Item .\publish\cloudflarednew.exe .\cloudflarednew.exe -Force
 Copy-Item .\publish\cloudflarednew.exe .\cloudflared.exe -Force
 Write-Host 'Built cloudflarednew.exe (standalone Windows x64; no .NET install needed).'
} finally { Pop-Location }
