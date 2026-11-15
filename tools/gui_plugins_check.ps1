# 2.4 plugins check in the real (Debug) app: the Plugins window, switching plugins off / on, delete + restore,
# the moved sidebar options. UI Automation for the controls; screenshots go to %TEMP%\cs_plugins_shots.
# Backs up and restores library\settings.json / presets.json / last_session.json and the plugins folder.
#   powershell -ExecutionPolicy Bypass -File tools\gui_plugins_check.ps1
$ErrorActionPreference = "Stop"
$Root = Resolve-Path "$PSScriptRoot\.."
$Bin = "$Root\src\ClipShrinker.App\bin\Debug\net10.0-windows"
$Exe = "$Bin\ClipShrinker.exe"; $Lib = "$Bin\library"; $Plug = "$Bin\plugins"
$Shots = Join-Path $env:TEMP "cs_plugins_shots"; Remove-Item $Shots -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory $Shots | Out-Null
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

if (Get-Process ClipShrinker -ErrorAction SilentlyContinue) { throw "Close ClipShrinker first: this check uses the same settings files." }
$Bak = Join-Path $env:TEMP "cs_plugins_library_backup"; Remove-Item $Bak -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory $Bak | Out-Null
$LibFiles = 'settings.json', 'presets.json', 'last_session.json'
foreach ($f in $LibFiles) { if (Test-Path "$Lib\$f") { Copy-Item "$Lib\$f" $Bak } }
$hadPlugins = Test-Path $Plug
if ($hadPlugins) { Copy-Item $Plug "$Bak\plugins" -Recurse }
function Restore-Lib {
    if ($script:proc -and -not $script:proc.HasExited) { $script:proc.Kill(); $script:proc.WaitForExit(5000) | Out-Null }
    foreach ($f in $LibFiles) { Remove-Item "$Lib\$f" -ErrorAction SilentlyContinue; if (Test-Path "$Bak\$f") { Copy-Item "$Bak\$f" $Lib } }
    Remove-Item $Plug -Recurse -Force -ErrorAction SilentlyContinue
    if ($hadPlugins) { Copy-Item "$Bak\plugins" $Plug -Recurse }
}
trap { Restore-Lib; break }

Remove-Item $Plug -Recurse -Force -ErrorAction SilentlyContinue      # a first start: the built-in plugins are written
foreach ($f in $LibFiles) { Remove-Item "$Lib\$f" -ErrorAction SilentlyContinue }   # (default settings; the user's are put back at the end)
$dir = Join-Path $env:TEMP "cs_plugins"; Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory $dir | Out-Null
$proj = Join-Path $dir "p.clipproj"
Copy-Item "$Root\tests\media\projects\single_effects.clipproj" $proj
$proc = Start-Process $Exe -ArgumentList "`"$proj`"" -PassThru
do { Start-Sleep -Milliseconds 300; $proc.Refresh() } until ($proc.MainWindowHandle -ne 0)
Start-Sleep 4
$win = $A::FromHandle($proc.MainWindowHandle)

"1. first start"
foreach ($id in 'timeline-effects', 'bitrate-presets', 'picture-adjust') { Check (Test-Path "$Plug\$id\plugin.json") "plugins\$id\plugin.json written" }
Check (Has $win "Plugins") "Plugins button"
Check (Has $win "Effects") "Effects button (Timeline effects on by default)"
Check (Has $win "Speed Up") "effect buttons under the preview"
Check (Has $win "Bitrate presets") "Bitrate presets button (on by default)"
Check (-not (HasButton $win "Picture")) "no Picture button on the preview (Picture adjustments off by default)"
Check (Has $win "Save session on exit") "Save session on exit in the Export panel"
Check (-not (Has $win "Transition")) "no Transition button with one clip"
Shot $win "1_main"
function Key($vk) { [W]::keybd_event($vk, 0, 0, [IntPtr]::Zero); [W]::keybd_event($vk, 0, 2, [IntPtr]::Zero); Start-Sleep -Milliseconds 400 }
[W]::SetForegroundWindow([IntPtr]$win.Current.NativeWindowHandle) | Out-Null; Start-Sleep -Milliseconds 400
Key 0x70                                                          # F1: Shortcuts
$sc = Win "Shortcuts"
Check (Has $sc "EFFECTS (plugin)") "Shortcuts: EFFECTS (plugin) section while the plugin is on"
Shot $sc "1b_shortcuts_on"
$sc.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()

"2. Plugins window"
Invoke (Find $win $A::NameProperty "Plugins")
$pw = Win "Plugins"
Toggle (@($pw.FindAll($TS::Descendants, (New-Object $P($A::NameProperty, "Changelog")))) | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Button' })[0]
Shot $pw "2_plugins"
$te = @($pw.FindAll($TS::Descendants, (New-Object $P($A::NameProperty, "Timeline effects"))) | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.CheckBox' })[0]
$bp = @($pw.FindAll($TS::Descendants, (New-Object $P($A::NameProperty, "Bitrate presets"))) | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.CheckBox' })[0]
$pa = @($pw.FindAll($TS::Descendants, (New-Object $P($A::NameProperty, "Picture adjustments"))) | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.CheckBox' })[0]
Check ((IsOn $te) -and (IsOn $bp) -and -not (IsOn $pa)) "switches: effects on, presets on, picture off"

"3. switch Timeline effects off, Picture adjustments on"
Toggle $te; Toggle $pa
Check (-not (HasButton $win "Effects")) "Effects button hidden"
Check (-not (Has $win "Speed Up")) "effect buttons hidden"
Check (HasButton $win "Picture") "Picture button on the preview"
$pw.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep -Milliseconds 500
[W]::SetForegroundWindow([IntPtr]$win.Current.NativeWindowHandle) | Out-Null; Start-Sleep -Milliseconds 400
Key 0x70
$sc = Win "Shortcuts"
Check (-not (Has $sc "EFFECTS (plugin)")) "Shortcuts: no EFFECTS section while the plugin is off"
Shot $sc "3a_shortcuts_off"
$sc.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
Invoke (Find $win $A::NameProperty "Picture")
Check (Has $win "Brightness") "Picture card opens over the preview"
function Slider($name) { @($win.FindAll($TS::Descendants, (New-Object $P($A::NameProperty, $name))) | Where-Object { $_.Current.ControlType.ProgrammaticName -eq "ControlType.Slider" })[0] }
function SetRange($name, $v) { (Slider $name).GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue($v); Start-Sleep -Milliseconds 300 }
function Range($name) { (Slider $name).GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value }
SetRange "Brightness" 60
$sat = Find $win $A::NameProperty "Saturation %"                  # typed value with a % sign, Enter confirms
$sat.SetFocus(); $sat.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("-100%"); Key 13
Check ((Range "Saturation") -eq -100) "typed -100% moved the saturation slider"
$br = Find $win $A::NameProperty "Brightness %"
Check ($br.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq "+60%") "the slider shows +60% in its box"
$dn = Find $win $A::NameProperty "Denoise %"                       # 2.5: denoise 0 .. 100, no negative values
$dn.SetFocus(); $dn.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("-20"); Key 13
Check ((Range "Denoise") -eq 0) "typed -20 gives denoise 0"
$dn.SetFocus(); $dn.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("45 %"); Key 13
Check ((Range "Denoise") -eq 45) "typed 45 % moved the denoise slider"
Start-Sleep 2
Shot $win "3b_picture_overlay"
Invoke (Find $win $A::NameProperty "Full screen")                 # full screen (the card is open there too)
Start-Sleep 2
$fs = Win "Preview"
Check (HasButton $fs "Picture") "full screen: Picture button"
Shot $fs "3c_fullscreen"
Key 0x1B
Start-Sleep 1
Invoke (Find $win $A::NameProperty "Reset")
Check ((Range "Brightness") -eq 0 -and (Range "Saturation") -eq 0 -and (Range "Denoise") -eq 0) "Reset"
Invoke (Find $win $A::NameProperty "Plugins")
$pw = Win "Plugins"

"4. delete Bitrate presets, then restore"
$del = @($pw.FindAll($TS::Descendants, (New-Object $P($A::NameProperty, "Delete"))) | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Button' })
"  (found $($del.Count) delete buttons)"
$del = $del[1]                                                     # the second card
Invoke $del                                                       # (UIA returns at once; the question box opens)
$box = $null; $sw = [Diagnostics.Stopwatch]::StartNew()
while (-not $box -and $sw.Elapsed.TotalSeconds -lt 8) {
    Start-Sleep -Milliseconds 300
    $box = @($A::RootElement.FindAll($TS::Descendants, (New-Object $P($A::NameProperty, "Delete plugin"))) | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Window' })[0]
}
[W]::SetForegroundWindow([IntPtr]$box.Current.NativeWindowHandle) | Out-Null; Start-Sleep -Milliseconds 400
[W]::keybd_event(13, 0, 0, [IntPtr]::Zero); [W]::keybd_event(13, 0, 2, [IntPtr]::Zero)   # Enter = Yes (the default button)
Start-Sleep 1
Start-Sleep 1
Check (-not (Test-Path "$Plug\bitrate-presets")) "plugins\bitrate-presets deleted"
Check (-not (HasButton $win "Bitrate presets")) "Bitrate presets button hidden"
Check (Has $pw "Restore") "greyed row with Restore"
Shot $pw "4_deleted"
Invoke (Find $pw $A::NameProperty "Restore")
$sw = [Diagnostics.Stopwatch]::StartNew(); while (-not (Test-Path "$Plug\bitrate-presets\plugin.json") -and $sw.Elapsed.TotalSeconds -lt 25) { Start-Sleep -Milliseconds 300 }
Start-Sleep 1
Check (Test-Path "$Plug\bitrate-presets\plugin.json") "restored plugin.json"
Check (HasButton $win "Bitrate presets") "Bitrate presets button back"
Shot $pw "5_restored"

"5. settings kept on close"
$pw.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
$proc.CloseMainWindow() | Out-Null
Start-Sleep 2
$closeBox = $null; $sw = [Diagnostics.Stopwatch]::StartNew()
while (-not $closeBox -and $sw.Elapsed.TotalSeconds -lt 6) { Start-Sleep -Milliseconds 300; $closeBox = $A::RootElement.FindFirst($TS::Descendants, (New-Object $P($A::NameProperty, "Close Clip Shrinker"))) }
if ($closeBox) { Invoke (Find $closeBox $A::NameProperty "Close without saving") }
$proc.WaitForExit(10000) | Out-Null
$s = Get-Content "$Lib\settings.json" -Raw | ConvertFrom-Json
Check ($s.plugins.'timeline-effects' -eq $false -and $s.plugins.'picture-adjust' -eq $true -and $s.plugins.'bitrate-presets' -eq $true) "plugin switches saved"


"6. Transition drop-down (2.5: app feature, shown with 2 or more clips; Timeline effects is still off)"
$proj2 = Join-Path $dir "two.clipproj"
Copy-Item "$Root\tests\media\projects\join_hardcut_mix_mutemic.clipproj" $proj2
$proc = Start-Process $Exe -ArgumentList "`"$proj2`"" -PassThru
do { Start-Sleep -Milliseconds 300; $proc.Refresh() } until ($proc.MainWindowHandle -ne 0)
Start-Sleep 4
$win = $A::FromHandle($proc.MainWindowHandle)
Check (Has $win "Clear all clips") "Clear all clips button"
Check (-not (HasButton $win "Effects")) "Effects button still hidden (plugin off)"
$tb = Find $win $A::NameProperty "Transition"
Check (-not $tb.Current.IsOffscreen) "Transition button with 2+ clips"
Toggle $tb
$combo = Find $A::RootElement $A::NameProperty "Transition effect"
Check ($combo -ne $null) "the drop-down shows the transition effect"
$len = Find $A::RootElement $A::NameProperty "Transition length"
$len.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("0.8"); Start-Sleep -Milliseconds 400
$combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 600
$item = Find $combo $A::NameProperty "Crossfade (gradient)"
$item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 400
$combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse(); Start-Sleep -Milliseconds 400
Shot $win "6_transition"
Check ($combo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0].Current.Name -eq "Crossfade (gradient)") "picked Crossfade (gradient)"
Invoke (Find $win $A::NameProperty "Clear all clips")
Start-Sleep 1
$tbs = @($win.FindAll($TS::Descendants, (New-Object $P($A::NameProperty, "Transition"))) | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Button' -and -not $_.Current.IsOffscreen })
Check ($tbs.Count -eq 0) "Transition button hidden after Clear all clips"
$proc.CloseMainWindow() | Out-Null
Start-Sleep 2
$closeBox = $null; $sw = [Diagnostics.Stopwatch]::StartNew()
while (-not $closeBox -and $sw.Elapsed.TotalSeconds -lt 6) { Start-Sleep -Milliseconds 300; $closeBox = $A::RootElement.FindFirst($TS::Descendants, (New-Object $P($A::NameProperty, "Close Clip Shrinker"))) }
if ($closeBox) { Invoke (Find $closeBox $A::NameProperty "Close without saving") }
$proc.WaitForExit(10000) | Out-Null
$s = Get-Content "$Lib\settings.json" -Raw | ConvertFrom-Json
Check ($s.trans -eq "Crossfade (gradient)" -and $s.tlen -eq "0.8") "transition and length saved"
Restore-Lib
""
"screenshots: $Shots"
if ($fails) { "$fails check(s) FAILED"; exit 1 } else { "all checks passed" }
