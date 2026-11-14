# Phase 6 check in the real app: undo / redo, save project (Ctrl+S), bitrate presets editor, key rebinding,
# the close dialog and "Restore the last session". UI Automation for buttons and fields; real key presses only where
# a key must be captured (Ctrl+S, the new shortcut). Works on a copy of a test project in %TEMP%.
$ErrorActionPreference = "Stop"
$Root = Resolve-Path "$PSScriptRoot\.."
$Exe = "$Root\src\ClipShrinker.App\bin\Debug\net10.0-windows\ClipShrinker.exe"
$Lib = "$Root\src\ClipShrinker.App\bin\Debug\net10.0-windows\library"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class K {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, IntPtr extra);
}
"@
$A = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]; $P = [System.Windows.Automation.PropertyCondition]
$CT = [System.Windows.Automation.ControlType]
function Find($scope, $prop, $v, $timeout = 10) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.Elapsed.TotalSeconds -lt $timeout) { $e = $scope.FindFirst($TS::Descendants, (New-Object $P($prop, $v))); if ($e) { return $e }; Start-Sleep -Milliseconds 200 }; throw "not found: $v" }
function Win($name) { Find $A::RootElement $A::NameProperty $name }
function Invoke($e) { $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 400 }
function SetText($e, $t) { $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($t); Start-Sleep -Milliseconds 200 }
function Toggle($e) { $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 300 }
function Keys($target, [int[]]$vks) { $h = if ($target -is [System.Diagnostics.Process]) { $target.MainWindowHandle } else { [IntPtr]$target.Current.NativeWindowHandle }; [K]::SetForegroundWindow($h) | Out-Null; Start-Sleep -Milliseconds 300
    foreach ($v in $vks) { [K]::keybd_event([byte]$v, 0, 0, [IntPtr]::Zero) }; [array]::Reverse($vks); foreach ($v in $vks) { [K]::keybd_event([byte]$v, 0, 2, [IntPtr]::Zero) }; Start-Sleep -Milliseconds 500 }
function Clips($win) { @($win.FindAll($TS::Descendants, (New-Object $P($A::ControlTypeProperty, $CT::Text))) | Where-Object { $_.Current.Name -match '^(\S+ )?\d+\.  \S' }).Count }
function Start-App([string]$arg) {
    $p = if ($arg) { Start-Process $Exe -ArgumentList "`"$arg`"" -PassThru } else { Start-Process $Exe -PassThru }
    do { Start-Sleep -Milliseconds 300; $p.Refresh() } until ($p.MainWindowHandle -ne 0)
    Start-Sleep 3
    return $p
}
function Check($ok, $what) { if ($ok) { "  ok    $what" } else { "  FAIL  $what"; $script:fails++ } }
$fails = 0

if (Get-Process ClipShrinker -ErrorAction SilentlyContinue) { throw "Close ClipShrinker first: this check uses the same settings / presets files." }
# the user's own settings, presets and session are put aside and restored at the end (also after a failure)
$Bak = Join-Path $env:TEMP "cs_phase6_library_backup"; Remove-Item $Bak -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory $Bak | Out-Null
$LibFiles = 'settings.json', 'presets.json', 'last_session.json'
foreach ($f in $LibFiles) { if (Test-Path "$Lib\$f") { Copy-Item "$Lib\$f" $Bak } }
function Restore-Lib {
    if ($script:proc -and -not $script:proc.HasExited) { $script:proc.Kill(); $script:proc.WaitForExit(5000) | Out-Null }
    foreach ($f in $LibFiles) { Remove-Item "$Lib\$f" -ErrorAction SilentlyContinue; if (Test-Path "$Bak\$f") { Copy-Item "$Bak\$f" $Lib } }
}
trap { Restore-Lib; break }

$dir = Join-Path $env:TEMP "cs_phase6"; Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory $dir | Out-Null
$proj = Join-Path $dir "p.clipproj"
Copy-Item "$Root\tests\media\projects\single_effects.clipproj" $proj
Remove-Item "$Lib\last_session.json", "$Lib\presets.json" -ErrorAction SilentlyContinue

"1. undo / redo"
$proc = Start-App $proj
$win = $A::FromHandle($proc.MainWindowHandle)
Check ((Clips $win) -eq 1) "the project opened with 1 clip"
$undo = Find $win $A::NameProperty ([string][char]0xE7A7); $redo = Find $win $A::NameProperty ([string][char]0xE7A6)
Invoke (Find $win $A::NameProperty "Clear all");  Check ((Clips $win) -eq 0) "Clear all removed it (no question)"
Invoke $undo;  Check ((Clips $win) -eq 1) "Undo brought it back"
Invoke $redo;  Check ((Clips $win) -eq 0) "Redo removed it again"
Invoke $undo;  Check ((Clips $win) -eq 1) "Undo again"

"2. save project (Ctrl+S)"
SetText (Find $win $A::NameProperty "Max size (MB)") "17"
$before = (Get-Item $proj).LastWriteTime
Keys $proc @(0x11, 0x53)
$saved = Get-Content $proj -Raw | ConvertFrom-Json
Check ((Get-Item $proj).LastWriteTime -gt $before) "the project file was written"
Check ($saved.settings.size -eq "17" -and $saved.clips.Count -eq 1 -and $saved.clips[0].sections.Count -eq 2) "with the new size and the clip's sections"
Check ($saved.clips[0].rel -eq $null -or $saved.clips[0].rel -like "*a_1080p60.mp4") "relative path (only on the same drive)"

"3. bitrate presets"
Invoke (Find $win $A::NameProperty "Bitrate presets"); $pw = Win "Bitrate presets"
Invoke (Find $pw $A::NameProperty "+ Add profile"); $nd = Win "New profile"
$edits = @($nd.FindAll($TS::Descendants, (New-Object $P($A::ControlTypeProperty, $CT::Edit))))
SetText $edits[0] "Discord"; SetText $edits[1] "4,5"
Invoke (Find $nd $A::NameProperty "Save")
$pj = Get-Content "$Lib\presets.json" -Raw | ConvertFrom-Json
Check ($pj.presets.Count -eq 1 -and $pj.presets[0].name -eq "Discord" -and $pj.presets[0].mbps -eq 4.5) "presets.json has Discord 4.5 Mbps"
Check ((Find $pw $A::NameProperty "4.5 Mbps") -ne $null) "the list shows it"
$pw.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()

"4. key rebinding"
Invoke (Find $win $A::NameProperty "Shortcuts"); $sw = Win "Shortcuts"
Invoke (Find $sw $A::NameProperty "S"); $hd = Win "Change shortcut"   # the keycaps open the recording dialog
Keys $hd @(0x58)                                            # X
Invoke (Find $hd $A::NameProperty "Save")
Start-Sleep -Milliseconds 300
Check ((Find $sw $A::NameProperty "X" 3) -ne $null) "the speed keycaps show X"
Check ((Find $win $A::NameProperty "X = speed up" 3) -ne $null) "the hint under the preview says X = speed up"
Invoke (Find $sw $A::NameProperty "D"); $hd = Win "Change shortcut"
Keys $hd @(0x58)                                            # X again for slow: must be refused
Check ((Find $hd $A::NameProperty "X is already used: add a speed-up section (blue) at the playhead." 3) -ne $null) "X for slow motion shows the error"
Check (-not (Find $hd $A::NameProperty "Save").Current.IsEnabled) "and cannot be saved"
Invoke (Find $hd $A::NameProperty "Cancel")
$sw.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()

"5. session restore + close dialog"
Invoke (Find $win $A::NameProperty "Effects"); $fx = Win "Effects"
Toggle (Find $fx $A::NameProperty "Restore the last session on start")
Invoke (Find $fx $A::NameProperty "Done")
$proc.CloseMainWindow() | Out-Null
$cd = Win "Close Clip Shrinker"
Check ((Find $cd $A::NameProperty "1 clip is open in the timeline.") -ne $null) "the close dialog asks"
Invoke (Find $cd $A::NameProperty "Close without saving")
Check ($proc.WaitForExit(10000)) "the app closed"
Check (Test-Path "$Lib\last_session.json") "last_session.json was written"
$st = Get-Content "$Lib\settings.json" -Raw | ConvertFrom-Json
Check ($st.keys.speed -eq "x" -and $st.restore_session -eq $true) "settings.json has the new key and restore_session"
$proc = Start-App ""
$win = $A::FromHandle($proc.MainWindowHandle)
Check ((Clips $win) -eq 1) "started without a file: the last session came back"
Check ($win.Current.Name -notlike "*.clipproj") "(as a session, not as a project file)"

"6. clean up (back to the defaults)"
Invoke (Find $win $A::NameProperty "Effects"); $fx = Win "Effects"
Toggle (Find $fx $A::NameProperty "Restore the last session on start"); Invoke (Find $fx $A::NameProperty "Done")
Invoke (Find $win $A::NameProperty "Shortcuts"); $sw = Win "Shortcuts"
Invoke (Find $sw $A::NameProperty "X"); $hd = Win "Change shortcut"; Invoke (Find $hd $A::NameProperty "Reset"); Invoke (Find $hd $A::NameProperty "Save")
$sw.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
$proc.CloseMainWindow() | Out-Null
Invoke (Find (Win "Close Clip Shrinker") $A::NameProperty "Close without saving")
$proc.WaitForExit(10000) | Out-Null
$st = Get-Content "$Lib\settings.json" -Raw | ConvertFrom-Json
Check ($st.keys.speed -eq "s" -and -not (Test-Path "$Lib\last_session.json")) "keys back to default, no session file"
Restore-Lib                                                   # the user's own files back
""
if ($fails) { "$fails check(s) failed" } else { "all checks passed" }
