param(
    [string]$GameRoot = 'E:\SteamLibrary\steamapps\common\Overcooked! 2',
    [string]$MSBuild = 'D:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe'
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $MSBuild)) {
    $MSBuild = (Get-Command MSBuild.exe -ErrorAction Stop).Source
}
$project = Join-Path $PSScriptRoot '..\OC2DIYLevelSorting.csproj'
& $MSBuild $project /t:Rebuild /p:Configuration=Release "/p:GameRoot=$GameRoot" /p:PreBuildEvent= /p:PostBuildEvent= /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }
