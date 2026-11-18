# 2.7 Sound track check in the real (Debug) app: a project with sounds opens with its Sounds lane, the Sounds list
# shows the library, a sound's + ("Add at the playhead") adds it, undo takes it away, and the project saves the sounds.
# Backs up and restores library\settings.json / last_session.json and the modules folder. Screenshots: %TEMP%\cs_sounds_shots.
#   powershell -ExecutionPolicy Bypass -File tools\gui_sounds_check.ps1
$ErrorActionPreference = "Stop"
$Root = Resolve-Path "$PSScriptRoot\.."
$Bin = "$Root\src\ClipShrinker.App\bin\Debug\net10.0-windows"
$Exe = "$Bin\ClipShrinker.exe"; $Lib = "$Bin\library"; $Plug = "$Bin\modules"
$Shots = Join-Path $env:TEMP "cs_sounds_shots"; Remove-Item $Shots -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory $Shots | Out-Null
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, IntPtr extra);
}
"@
[W]::SetProcessDPIAware() | Out-Null
$A = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]; $P = [System.Windows.Automation.PropertyCondition]
function Find($scope, $prop, $v, $timeout = 10) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.Elapsed.TotalSeconds -lt $timeout) { $e = $scope.FindFirst($TS::Descendants, (New-Object $P($prop, $v))); if ($e) { return $e }; Start-Sleep -Milliseconds 200 }; throw "not found: $v" }
function Has($scope, $name) { return [bool](@($scope.FindAll($TS::Descendants, (New-Object $P($A::NameProperty, $name))) | Where-Object { -not $_.Current.IsOffscreen }).Count) }
function HasButton($scope, $name) { return [bool](@($scope.FindAll($TS::Descendants, (New-Object $P($A::NameProperty, $name))) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.ControlType.ProgrammaticName -eq "ControlType.Button" }).Count) }
function Win($name) { Find $A::RootElement $A::NameProperty $name }
function Invoke($e) { $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 600 }
function Toggle($e) { $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 800 }
function IsOn($e) { $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq 'On' }
function Shot($el, $name) {
    $h = [IntPtr]$el.Current.NativeWindowHandle; [W]::SetForegroundWindow($h) | Out-Null; Start-Sleep -Milliseconds 700
    $r = New-Object W+RECT; [W]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size); $g.Dispose()
    $bmp.Save("$Shots\$name.png"); $bmp.Dispose()
}
function Check($ok, $what) { if ($ok) { "  ok    $what" } else { "  FAIL  $what"; $script:fails++ } }
$fails = 0

function Key($vk) { [W]::keybd_event($vk, 0, 0, [IntPtr]::Zero); [W]::keybd_event($vk, 0, 2, [IntPtr]::Zero); Start-Sleep -Milliseconds 400 }
function Ctrl($vk) { [W]::keybd_event(0x11, 0, 0, [IntPtr]::Zero); Key $vk; [W]::keybd_event(0x11, 0, 2, [IntPtr]::Zero); Start-Sleep -Milliseconds 600 }
function SoundCount($path) { $j = Get-Content $path -Raw | ConvertFrom-Json; return @($j.clips | ForEach-Object { @($_.sounds).Where({ $_ }).Count } | Measure-Object -Sum).Sum }

if (Get-Process ClipShrinker -ErrorAction SilentlyContinue) { throw "Close ClipShrinker first: this check uses the same settings files." }
$Bak = Join-Path $env:TEMP "cs_sounds_library_backup"; Remove-Item $Bak -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory $Bak | Out-Null
$LibFiles = 'settings.json', 'last_session.json'
foreach ($f in $LibFiles) { if (Test-Path "$Lib\$f") { Copy-Item "$Lib\$f" $Bak } }
$hadModules = Test-Path $Plug
if ($hadModules) { Copy-Item $Plug "$Bak\modules" -Recurse }
function Restore-Lib {
    if ($script:proc -and -not $script:proc.HasExited) { $script:proc.Kill(); $script:proc.WaitForExit(5000) | Out-Null }
    foreach ($f in $LibFiles) { Remove-Item "$Lib\$f" -ErrorAction SilentlyContinue; if (Test-Path "$Bak\$f") { Copy-Item "$Bak\$f" $Lib } }
    Remove-Item $Plug -Recurse -Force -ErrorAction SilentlyContinue
    if ($hadModules) { Copy-Item "$Bak\modules" $Plug -Recurse }
}
trap { Restore-Lib; break }

# test material: two short sounds made with the bundled ffmpeg, and a project with one sound on each clip
$dir = Join-Path $env:TEMP "cs_sounds"; Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory $dir | Out-Null
$ff = (Resolve-Path "$Root\..\Dev\library\ffmpeg.exe").Path
& $ff -hide_banner -v error -y -filter_complex "aevalsrc='0.5*sin(2*PI*440*t)*exp(-2*t)':s=44100:d=2" -c:a pcm_s16le "$dir\beep.wav"
& $ff -hide_banner -v error -y -filter_complex "aevalsrc='0.3*sin(2*PI*220*t)':s=44100:d=5" -c:a pcm_s16le "$dir\hum.wav"
foreach ($f in $LibFiles) { Remove-Item "$Lib\$f" -ErrorAction SilentlyContinue }
Remove-Item $Plug -Recurse -Force -ErrorAction SilentlyContinue      # first start: every module on
$media = (Resolve-Path "$Root\tests\media").Path.Replace('\', '/')
$d = $dir.Replace('\', '/')
$proj = Join-Path $dir "p.clipproj"
@"
{"app":"Clip Shrinker","format":1,"active":0,"clips":[
 {"path":"$media/a_1080p60.mp4","start":2,"end":12,"sections":[],"zooms":[],"sounds":[{"path":"$d/hum.wav","at":4,"in":0,"len":5,"dur":5,"vol":-3,"fade":0.5}]},
 {"path":"$media/c_720p_ntsc_noaudio.mp4","start":0,"end":5,"sections":[],"zooms":[],"sounds":[{"path":"$d/beep.wav","at":1,"in":0,"len":2,"dur":2,"vol":0,"fade":0}]}],
 "settings":{"size":"8"}}
"@ | Set-Content $proj -Encoding UTF8

$proc = Start-Process $Exe -ArgumentList "`"$proj`"" -PassThru
do { Start-Sleep -Milliseconds 300; $proc.Refresh() } until ($proc.MainWindowHandle -ne 0)
Start-Sleep 5
$win = $A::FromHandle($proc.MainWindowHandle)

"1. a project with sounds"
Check (-not (HasButton $win "Add sound")) "no Sound button among the effects (2.7: sounds are dragged from the Sounds list)"
Check (Has $win "Sounds") "Sounds drop-down button"
Check (Test-Path "$Plug\sound-track\module.json") "modules\sound-track\module.json written"
Shot $win "1_lanes"

"2. the Sounds list"
New-Item -ItemType Directory -Force "$Plug\sound-track\sounds" | Out-Null
Copy-Item "$dir\beep.wav" "$Plug\sound-track\sounds\"
Toggle (Find $win $A::NameProperty "Sounds")
$list = Find $A::RootElement $A::NameProperty "Sound library"
$items = @($list.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition))
Check ($items.Count -eq 1) "the library lists the sound in modules\sound-track\sounds"
Shot $win "2_sounds_list"
Invoke (Find $A::RootElement $A::NameProperty "Add at the playhead")
Start-Sleep 1
[W]::SetForegroundWindow([IntPtr]$win.Current.NativeWindowHandle) | Out-Null; Start-Sleep -Milliseconds 400
Key 0x1B
Ctrl 0x53                                                           # Ctrl+S
Check ((SoundCount $proj) -eq 3) "Add at the playhead: the project now has 3 sounds"
Shot $win "3_added"

"3. undo"
Ctrl 0x5A                                                           # Ctrl+Z
Ctrl 0x53
Check ((SoundCount $proj) -eq 2) "Ctrl+Z took the new sound away"

"4. the A key"
Key 0x41                                                            # A
Ctrl 0x53
Check ((SoundCount $proj) -eq 3) "A added the picked sound"
Key 0x2E                                                            # Delete: the new sound is selected
Ctrl 0x53
Check ((SoundCount $proj) -eq 2) "Delete removed the selected sound"

$proc.Kill(); $proc.WaitForExit(5000) | Out-Null
Restore-Lib
""
"screenshots: $Shots"
if ($fails) { "$fails check(s) FAILED"; exit 1 } else { "all checks passed" }
