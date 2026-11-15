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
Check (HasButton $win "Picture") "Picture button on the preview (2.6: every plugin on by default)"
Check (HasButton $win "Equalizer") "Equalizer button on the preview (2.6: every plugin on by default)"
$sw = [Diagnostics.Stopwatch]::StartNew(); while (-not (Has $win "GPU Encoding is ON") -and $sw.Elapsed.TotalSeconds -lt 10) { Start-Sleep -Milliseconds 300 }
Check (Has $win "GPU Encoding is ON") "top bar: GPU encoding ON (green) on this NVIDIA PC"
Invoke (Find $win $A::NameProperty "About")
$ab = Win "About ClipShrinker"
$sw = [Diagnostics.Stopwatch]::StartNew(); while ((Has $ab "Checking for updates...") -and $sw.Elapsed.TotalSeconds -lt 12) { Start-Sleep -Milliseconds 300 }
Check ((Has $ab "You have the latest version") -or (Has $ab "Could not check for updates (offline?)") -or @($ab.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -like "Version * is available" }).Count) "About: the update line answered"
Shot $ab "1c_about"
$ab.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep -Milliseconds 400
Check ((Find $win $A::NameProperty "Codec").GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0].Current.Name -eq "HEVC (hevc_nvenc)") "Codec: HEVC (hevc_nvenc) on this NVIDIA PC"
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
Check ((IsOn $te) -and (IsOn $bp) -and (IsOn $pa)) "switches: all on"

"3. switch Timeline effects off (Picture adjustments is on)"
Toggle $te
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
"3b. Sound equalizer (2.6)"
Check (HasButton $win "Equalizer") "Equalizer button on the preview"
Invoke (Find $win $A::NameProperty "Equalizer")
Check (Has $win "12 kHz") "Equalizer card opens over the preview"
Check (-not (Has $win "Brightness")) "the Picture card closed (one card at a time)"
$vb = Find $win $A::NameProperty "Volume dB"
$vb.SetFocus(); $vb.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("+6 dB"); Key 13
Check ((Range "Volume") -eq 6) "typed +6 dB moved the volume slider"
$b0 = Find $win $A::NameProperty "60 Hz dB"
$b0.SetFocus(); $b0.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("40"); Key 13
Check ((Range "60 Hz") -eq 12) "typed 40 is held at +12 dB"
Start-Sleep 1
Shot $win "3d_sound_card"
Invoke (Find $win $A::NameProperty "Reset")
Check ((Range "Volume") -eq 0 -and (Range "60 Hz") -eq 0) "Reset (sound)"
Invoke (Find $win $A::NameProperty "Equalizer")
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

"7. a PC without NVIDIA (2.6; NVENC / CUDA / OpenCL hidden by CLIPSHRINKER_HIDE_ENCODERS)"
$env:CLIPSHRINKER_HIDE_ENCODERS = "nvenc,cuda,opencl"
$proc = Start-Process $Exe -ArgumentList "`"$proj`"" -PassThru
$env:CLIPSHRINKER_HIDE_ENCODERS = $null
do { Start-Sleep -Milliseconds 300; $proc.Refresh() } until ($proc.MainWindowHandle -ne 0)
Start-Sleep 7
$win = $A::FromHandle($proc.MainWindowHandle)
$codec = (Find $win $A::NameProperty "Codec").GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0].Current.Name
Check ($codec -eq "HEVC (Windows, GPU)") "Codec switched to HEVC (Windows, GPU) (was: $codec)"
Shot $win "7_no_nvidia"
Check (Has $win "GPU Encoding is ON") "top bar: still GPU encoding ON (Windows GPU encoder)"
$proc.Kill(); $proc.WaitForExit(5000) | Out-Null
$env:CLIPSHRINKER_HIDE_ENCODERS = "nvenc,cuda,opencl,_hw"                 # (only the Windows CPU encoder left)
$proc = Start-Process $Exe -ArgumentList "`"$proj`"" -PassThru
$env:CLIPSHRINKER_HIDE_ENCODERS = $null
do { Start-Sleep -Milliseconds 300; $proc.Refresh() } until ($proc.MainWindowHandle -ne 0)
Start-Sleep 7
$win = $A::FromHandle($proc.MainWindowHandle)
Check (Has $win "GPU Encoding is OFF") "top bar: GPU encoding OFF (orange) with only the CPU encoder"
Shot $win "7b_cpu_only"
$proc.Kill(); $proc.WaitForExit(5000) | Out-Null
Restore-Lib
""
"screenshots: $Shots"
if ($fails) { "$fails check(s) FAILED"; exit 1 } else { "all checks passed" }
