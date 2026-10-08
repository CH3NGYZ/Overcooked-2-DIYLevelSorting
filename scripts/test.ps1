param([string]$MSBuild = 'D:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe')
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $MSBuild)) { $MSBuild = (Get-Command MSBuild.exe -ErrorAction Stop).Source }
& $MSBuild (Join-Path $PSScriptRoot '..\tests\BehaviorTests.csproj') /t:Rebuild /p:PreBuildEvent= /p:PostBuildEvent= /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Test build failed ($LASTEXITCODE)." }
& (Join-Path $PSScriptRoot '..\bin\Tests\BehaviorTests.exe')
if ($LASTEXITCODE -ne 0) { throw "Behavior tests failed ($LASTEXITCODE)." }
