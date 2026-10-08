param(
    [string]$GameRoot = 'E:\SteamLibrary\steamapps\common\Overcooked! 2',
    [string]$MSBuild = 'D:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe',
    [switch]$WithoutFastInit,
    [switch]$WithKitchenReturn,
    [switch]$WithRoundEnd
)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name Overcooked2 -ErrorAction SilentlyContinue) { throw 'A player game is running; close it before running Unity tests.' }
& (Join-Path $PSScriptRoot 'build.ps1') -GameRoot $GameRoot -MSBuild $MSBuild
& $MSBuild (Join-Path $PSScriptRoot '..\tests\UnityTests.csproj') /t:Rebuild "/p:GameRoot=$GameRoot" /p:PreBuildEvent= /p:PostBuildEvent= /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Unity test build failed ($LASTEXITCODE)." }
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$variant = if ($WithoutFastInit) { 'without-fastinit' } else { 'with-fastinit' }
if ($WithKitchenReturn) { $variant += '-kitchen-return' }
if ($WithRoundEnd) { $variant += '-round-end' }
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
    if ($WithKitchenReturn) { $arguments += '-oc2SortingTestKitchenReturn' }
    if ($WithRoundEnd) { $arguments += '-oc2SortingTestRoundEnd' }
    $steamPath = (Get-Process -Name steam -ErrorAction SilentlyContinue | Select-Object -First 1).Path
    if (-not $steamPath) { $steamPath = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamExe }
    if ($steamPath -and (Test-Path -LiteralPath $steamPath)) {
        # Starting the executable directly can make SteamAPI_RestartAppIfNecessary
        # open steam://run with the arguments and require a ShowGameArgs confirmation.
        # Ask the installed Steam client to launch instead; never own/stop that client.
        Start-Process -FilePath $steamPath -ArgumentList (@('-applaunch', '728880') + $arguments) -WindowStyle Hidden | Out-Null
    }
    else {
        $gameProcess = Start-Process -FilePath (Join-Path $GameRoot 'Overcooked2.exe') -WorkingDirectory $GameRoot -ArgumentList $arguments -WindowStyle Hidden -PassThru
    }
    $timeoutSeconds = if ($WithKitchenReturn -or $WithRoundEnd) { 300 } else { 150 }
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    $startupDeadline = (Get-Date).AddSeconds(45)
    # Steam may restart the bootstrap process. Adopt only a process with this exact
    # unique test directory in its command line, never an unrelated player process.
    while ($null -eq $gameProcess -or $gameProcess.HasExited -or -not (Test-Path -LiteralPath (Join-Path $runDirectory 'player.log'))) {
        $relaunched = Get-CimInstance Win32_Process -Filter "Name = 'Overcooked2.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($runDirectory) }
        if ($relaunched) { $gameProcess = Get-Process -Id ($relaunched | Select-Object -First 1).ProcessId -ErrorAction SilentlyContinue }
        if ((Get-Date) -ge $startupDeadline) { throw 'Steam test startup exceeded 45 seconds.' }
        if (Test-Path -LiteralPath (Join-Path $runDirectory 'result.txt')) { break }
        Start-Sleep -Milliseconds 500
        if ($null -ne $gameProcess) { $gameProcess.Refresh() }
    }
    while ($null -ne $gameProcess -and -not $gameProcess.HasExited -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $gameProcess.Refresh()
    }
    if ($null -ne $gameProcess -and -not $gameProcess.HasExited) { throw "Owned test game exceeded $timeoutSeconds seconds; see player.log." }
    $saveManifest = Join-Path $runDirectory 'save-manifest.tsv'
    if (Test-Path -LiteralPath $saveManifest) {
        foreach ($line in Get-Content -LiteralPath $saveManifest) {
            $parts = $line.Split(@("`t"), 2, [StringSplitOptions]::None)
            $actual = if (Test-Path -LiteralPath $parts[1]) { (Get-FileHash -LiteralPath $parts[1]).Hash } else { '-' }
            if ($actual -ne $parts[0]) { throw 'An original save changed during the isolated Unity test; see save-manifest.tsv.' }
        }
        Write-Output 'Original save hashes unchanged after test process exit.'
    }
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
        if (Test-Path -LiteralPath $fastPath) {
            if ((Get-FileHash -LiteralPath $fastPath).Hash -ne $fastHash) {
                # A user or updater may have installed a replacement while the
                # original DLL was out of the directory. Preserve both versions
                # and still restore every temporary sorting/config/log change.
                Write-Warning "FastInit was replaced during the test; keeping the replacement and original backup at $fastBackup"
            }
        }
        else {
            Move-Item -LiteralPath $fastBackup -Destination $fastPath
            if ((Get-FileHash -LiteralPath $fastPath).Hash -ne $fastHash) { throw 'FastInit restore hash mismatch.' }
        }
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
