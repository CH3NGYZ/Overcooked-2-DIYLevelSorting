param(
    [string]$GameRoot = 'E:\SteamLibrary\steamapps\common\Overcooked! 2',
    [string]$MSBuild = 'D:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe',
    [switch]$WithoutFastInit
)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name Overcooked2 -ErrorAction SilentlyContinue) { throw 'A player game is running; close it before running Unity tests.' }
& (Join-Path $PSScriptRoot 'build.ps1') -GameRoot $GameRoot -MSBuild $MSBuild
& $MSBuild (Join-Path $PSScriptRoot '..\tests\UnityTests.csproj') /t:Rebuild "/p:GameRoot=$GameRoot" /p:PreBuildEvent= /p:PostBuildEvent= /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Unity test build failed ($LASTEXITCODE)." }
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$variant = if ($WithoutFastInit) { 'without-fastinit' } else { 'with-fastinit' }
$runDirectory = Join-Path $workspace ('.validation\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + $variant)
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$backupDirectory = Join-Path $runDirectory 'backup'
New-Item -ItemType Directory -Path $backupDirectory | Out-Null
$pluginDirectory = Join-Path $GameRoot 'BepInEx\plugins'
$pluginSource = Get-Content -LiteralPath (Join-Path $workspace 'src\SortingPlugin.cs') -Raw
$pluginGuid = [regex]::Match($pluginSource, 'public const string PluginGuid = "([^"]+)"').Groups[1].Value
if (-not $pluginGuid) { throw 'Cannot resolve the current plugin config GUID for safe backup.' }
$targets = @(
    (Join-Path $pluginDirectory 'OC2DIYLevelSorting.dll'),
    (Join-Path $pluginDirectory 'OC2DIYLevelSorting.UnityTests.dll'),
    (Join-Path $GameRoot ("BepInEx\config\$pluginGuid.cfg")),
    (Join-Path $GameRoot 'BepInEx\config\oc2.diylevel.sorting.unitytests.cfg'),
    (Join-Path $GameRoot 'BepInEx\LogOutput.log')
)
$manifest = @()
foreach ($target in $targets) {
    $exists = Test-Path -LiteralPath $target
    $backup = Join-Path $backupDirectory ([IO.Path]::GetFileName($target))
    $hash = $null
    if ($exists) {
        Copy-Item -LiteralPath $target -Destination $backup
        $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        if ((Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -ne $hash) { throw "Backup mismatch: $target" }
    }
    $manifest += [pscustomobject]@{ Target = $target; Existed = $exists; Backup = $backup; Hash = $hash }
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDirectory 'manifest.json')
$previousOutput = $env:OC2_SORTING_TEST_OUTPUT
$gameProcess = $null
$fastPath = Join-Path $pluginDirectory 'DIYLevelFastInit.dll'
$fastBackup = Join-Path $backupDirectory 'DIYLevelFastInit.dll'
$fastMoved = $false
$fastHash = $null
try {
    # Check again immediately before temporary deployment.
    if (Get-Process -Name Overcooked2 -ErrorAction SilentlyContinue) { throw 'A game started during test preparation.' }
    Copy-Item -LiteralPath (Join-Path $workspace 'bin\Release\OC2DIYLevelSorting.dll') -Destination $targets[0]
    Copy-Item -LiteralPath (Join-Path $workspace 'bin\UnityTests\OC2DIYLevelSorting.UnityTests.dll') -Destination $targets[1]
    if ($WithoutFastInit -and (Test-Path -LiteralPath $fastPath)) {
        $fastHash = (Get-FileHash -LiteralPath $fastPath).Hash
        Move-Item -LiteralPath $fastPath -Destination $fastBackup
        $fastMoved = $true
    }
    $env:OC2_SORTING_TEST_OUTPUT = $runDirectory
    $arguments = @('-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720', '-logFile', ('"' + (Join-Path $runDirectory 'player.log') + '"'), '-oc2SortingTestOutput', ('"' + $runDirectory + '"'))
    $gameProcess = Start-Process -FilePath (Join-Path $GameRoot 'Overcooked2.exe') -WorkingDirectory $GameRoot -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $deadline = (Get-Date).AddSeconds(150)
    $startupDeadline = (Get-Date).AddSeconds(45)
    # Steam may restart the bootstrap process. Adopt only a process with this exact
    # unique test directory in its command line, never an unrelated player process.
    while ($gameProcess.HasExited -or -not (Test-Path -LiteralPath (Join-Path $runDirectory 'player.log'))) {
        $relaunched = Get-CimInstance Win32_Process -Filter "Name = 'Overcooked2.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($runDirectory) }
        if ($relaunched) { $gameProcess = Get-Process -Id ($relaunched | Select-Object -First 1).ProcessId }
        if ((Get-Date) -ge $startupDeadline) { throw 'Steam test startup exceeded 45 seconds.' }
        if (Test-Path -LiteralPath (Join-Path $runDirectory 'result.txt')) { break }
        Start-Sleep -Milliseconds 500
        $gameProcess.Refresh()
    }
    while (-not $gameProcess.HasExited -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $gameProcess.Refresh()
    }
    if (-not $gameProcess.HasExited) { throw 'Owned test game exceeded 150 seconds; see player.log.' }
    $resultPath = Join-Path $runDirectory 'result.txt'
    if (-not (Test-Path -LiteralPath $resultPath)) { throw "Test plugin produced no result. Evidence: $runDirectory" }
    $result = Get-Content -LiteralPath $resultPath -Raw
    Write-Output $result
    if (-not $result.StartsWith('PASS:')) { throw "Unity tests failed. Evidence: $runDirectory" }
}
finally {
    # Only terminate the exact process launched by this script, never another player game.
    if ($null -ne $gameProcess -and -not $gameProcess.HasExited) { Stop-Process -Id $gameProcess.Id; $gameProcess.WaitForExit() }
    $remainingOwned = Get-CimInstance Win32_Process -Filter "Name = 'Overcooked2.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($runDirectory) }
    foreach ($owned in $remainingOwned) {
        Stop-Process -Id $owned.ProcessId
        Wait-Process -Id $owned.ProcessId -Timeout 10 -ErrorAction SilentlyContinue
    }
    $env:OC2_SORTING_TEST_OUTPUT = $previousOutput
    if (Test-Path -LiteralPath $targets[4]) { Copy-Item -LiteralPath $targets[4] -Destination (Join-Path $runDirectory 'bepinex.log') }
    if ($fastMoved) {
        Move-Item -LiteralPath $fastBackup -Destination $fastPath
        if ((Get-FileHash -LiteralPath $fastPath).Hash -ne $fastHash) { throw 'FastInit restore hash mismatch.' }
    }
    foreach ($entry in $manifest) {
        if ($entry.Existed) {
            Copy-Item -LiteralPath $entry.Backup -Destination $entry.Target
            if ((Get-FileHash -LiteralPath $entry.Target).Hash -ne $entry.Hash) { throw "Restore mismatch: $($entry.Target)" }
        }
        elseif (Test-Path -LiteralPath $entry.Target) {
            for ($attempt = 0; $attempt -lt 10; $attempt++) {
                try { Remove-Item -LiteralPath $entry.Target; break }
                catch { if ($attempt -eq 9) { throw }; Start-Sleep -Milliseconds 500 }
            }
        }
    }
    Write-Output "Original DLL/config/log state restored. Evidence: $runDirectory"
}
