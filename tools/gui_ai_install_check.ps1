# 2.8 check of the AI editing install in the packaged app: a copy of the base package (make_release.ps1) in
# %TEMP%\cs_ai_install installs the module from local files (make_ai_module.ps1 output) through the Modules window,
# switches it on, runs speech to text through the installed helper, then uninstalls it. Claude Code's own
# "clipshrinker" entry is not changed (Connect is answered No; Uninstall only removes an entry pointing here).
#   powershell -ExecutionPolicy Bypass -File tools\gui_ai_install_check.ps1 [-Package <folder>] [-Assets <folder>]
# UI Automation (no keyboard or mouse); a picture of the Modules window goes to %TEMP%\cs_ai_install\*.png.
param([string]$Package = "", [string]$Assets = "")
$ErrorActionPreference = "Stop"
$Root = Resolve-Path "$PSScriptRoot\.."
$RelDir = Join-Path (Split-Path $Root -Parent) "Release"
$Version = ([regex]::Match((Get-Content "$Root\src\ClipShrinker.Core\Constants.cs" -Raw), 'AppVersion = "([^"]+)"')).Groups[1].Value
if (-not $Package) { $Package = Join-Path $RelDir "ClipShrinker-$Version" }
if (-not $Assets) { $Assets = Join-Path $RelDir "ai-editing" }
$Work = Join-Path $env:TEMP "cs_ai_install"; $App = "$Work\ClipShrinker"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
}
"@
[W]::SetProcessDPIAware() | Out-Null
$A = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]; $P = [System.Windows.Automation.PropertyCondition]
function FindAll($scope, $name) { @($scope.FindAll($TS::Descendants, (New-Object $P($A::NameProperty, $name))) | Where-Object { -not $_.Current.IsOffscreen }) }
function Find($scope, $name, $timeout = 15) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.Elapsed.TotalSeconds -lt $timeout) { $e = FindAll $scope $name | Select-Object -First 1; if ($e) { return $e }; Start-Sleep -Milliseconds 250 }; throw "not found: $name" }
function FindLike($scope, $pattern, $timeout = 15) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeout) {
        $e = @($scope.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -like $pattern }) | Select-Object -First 1
        if ($e) { return $e }; Start-Sleep -Milliseconds 250
    }
    throw "not found: $pattern"
}
function Win($name, $timeout = 15) {
    $c = New-Object System.Windows.Automation.AndCondition (New-Object $P($A::NameProperty, $name)), (New-Object $P($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeout) { $e = $A::RootElement.FindFirst($TS::Descendants, $c); if ($e) { return $e }; Start-Sleep -Milliseconds 250 }
    throw "window not found: $name"
}
function Invoke($e) {
    # (a name can match the text inside a button: go up to the button)
    $ip = [System.Windows.Automation.InvokePattern]::Pattern; $done = $false
    if ($env:CS_TRACE) { Write-Host "    click: $(if ($e) { "$($e.Current.ControlType.ProgrammaticName) '$($e.Current.Name)'" } else { 'null' })" }
    while ($null -ne $e -and -not $done) {
        try { $e.GetCurrentPattern($ip).Invoke(); $done = $true }
        catch [InvalidOperationException] { $e = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($e) }
    }
    if (-not $done) { throw "nothing to click" }
    Start-Sleep -Milliseconds 700
}
function Button($scope, $name, $timeout = 15) {
    $c = New-Object System.Windows.Automation.AndCondition (New-Object $P($A::NameProperty, $name)), (New-Object $P($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeout) { $e = $scope.FindFirst($TS::Descendants, $c); if ($e) { return $e }; Start-Sleep -Milliseconds 250 }
    return $null
}
# MessageBox buttons by id (6 = Yes, 7 = No, 1/2 = OK): their names follow the Windows language
# A MessageBox (Yes / No) is answered with WM_COMMAND IDYES (6) / IDNO (7): its buttons have no Invoke pattern here
function Answer($box, $id) {
    [W]::PostMessage([IntPtr]$box.Current.NativeWindowHandle, 0x111, [IntPtr]$id, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 700
}
function Shot($el, $name) {
    # the AI editing card is the last one: scroll the list down first
    $sc = $el.FindFirst($TS::Descendants, (New-Object $P($A::IsScrollPatternAvailableProperty, $true)))
    if ($sc) { $sc.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, 100); Start-Sleep -Milliseconds 400 }
    $h = [IntPtr]$el.Current.NativeWindowHandle; $r = New-Object W+RECT; [W]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T); $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc(); [W]::PrintWindow($h, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save("$Work\$name.png"); $bmp.Dispose()
}
function Check($ok, $what) { if ($ok) { "  ok    $what" } else { "  FAIL  $what"; $script:fails++ } }
$fails = 0

if (-not (Test-Path "$Package\ClipShrinker.exe")) { throw "no package in $Package (run make_release.ps1)" }
if (-not (Test-Path "$Assets\ai-editing.json")) { throw "no ai-editing.json in $Assets (run make_ai_module.ps1)" }
if (Get-Process ClipShrinker -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Work*" }) { throw "close the test copy of ClipShrinker first" }
Remove-Item $Work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $Work | Out-Null
Copy-Item $Package $App -Recurse
$proj = "$Work\p.clipproj"; Copy-Item "$Root\tests\media\projects\single_effects.clipproj" $proj

"1. base package"
Check (-not (Test-Path "$App\clipshrinker-mcp.exe") -and -not (Test-Path "$App\modules\ai-agent\helper")) "no MCP helper in the package"
Check (-not ((& "$App\library\ffmpeg.exe" -hide_banner -filters 2>$null) -match "\bwhisper\b")) "library\ffmpeg.exe is the base build (no whisper)"
$env:CLIPSHRINKER_AI_SOURCE = $Assets
$proc = Start-Process "$App\ClipShrinker.exe" -ArgumentList "`"$proj`"" -PassThru
do { Start-Sleep -Milliseconds 300; $proc.Refresh() } until ($proc.MainWindowHandle -ne 0)
Start-Sleep 3
$win = $A::FromHandle($proc.MainWindowHandle)
try {
    Check (-not (FindAll $win "AI agent not connected")) "no AI agent pill before the install"
    Invoke (Find $win "Modules")
    $mw = Win "Modules"
    $by = FindLike $mw "Not installed*"
    Check ($by.Current.Name -like "*for speech to text*") "AI editing card: '$($by.Current.Name)'"

    "2. install from $Assets"
    Invoke (Find $mw "Install")
    $dlg = Win "Install AI editing"
    Invoke (FindLike $dlg "Install all*")
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path "$App\modules\ai-agent\installed.json") -and $sw.Elapsed.TotalSeconds -lt 300) { Start-Sleep 1 }
    Check (Test-Path "$App\modules\ai-agent\installed.json") ("installed in {0:N0} s" -f $sw.Elapsed.TotalSeconds)
    $notice = Win "AI editing: your clips leave this PC" 30
    $btn = Button $notice "I understand, switch on"
    if (-not $btn) {
        "  (dialog: $($notice.Current.ControlType.ProgrammaticName) '$($notice.Current.Name)')"
        $notice.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { "    $($_.Current.ControlType.ProgrammaticName) '$($_.Current.Name)'" }
    }
    Invoke $btn
    $conn = Win "Connect to Claude Code"
    # No: keep the owner's Claude Code entry (without claude.exe the dialog offers the command to copy: Close)
    if ($close = Button $conn "Close" 1) { "  ok    claude.exe not found: the dialog offers the command to copy"; Invoke $close }
    else { Answer $conn 7; "  ok    'Register in Claude Code now?' asked (answered No)" }
    $m = Get-Content "$Assets\ai-editing.json" -Raw | ConvertFrom-Json
    foreach ($f in "helper\clipshrinker-mcp.exe", "ffmpeg\ffmpeg.exe", "models\ggml-large-v3-turbo-q5_0.bin", "models\ggml-silero-v5.1.2.bin") {
        Check (Test-Path "$App\modules\ai-agent\$f") "modules\ai-agent\$f"
    }
    $model = $m.parts | Where-Object id -eq "model"
    Check ((Get-FileHash "$App\modules\ai-agent\models\$($model.file)").Hash.ToLower() -eq $model.sha256) "speech model SHA-256 matches the manifest"
    Check (-not (Test-Path "$App\modules\ai-agent\.download")) "download folder cleaned up"
    # (a Claude session that already runs a clipshrinker helper connects at once: then the pill says connected)
    Check ([bool]((FindAll $win "AI agent not connected") + (FindAll $win "AI agent connected"))) "AI agent pill shows after switching on"
    Check ([bool](FindAll $mw "Connect to Claude Code")) "Connect to Claude Code button"
    Check ([bool](FindLike $mw "v$($m.version)*downloaded from GitHub*" 3)) "card shows v$($m.version), downloaded from GitHub"
    Shot $mw "modules_installed"

    "3. speech to text through the installed helper (live, in the open window)"
    $psi = New-Object Diagnostics.ProcessStartInfo "$App\modules\ai-agent\helper\clipshrinker-mcp.exe"
    $psi.Arguments = "mcp --app `"$App`" --tools `"$App\library`""
    $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $h = [Diagnostics.Process]::Start($psi)
    function Rpc($id, $method, $params) {
        $h.StandardInput.WriteLine((@{ jsonrpc = "2.0"; id = $id; method = $method; params = $params } | ConvertTo-Json -Depth 6 -Compress))
        $h.StandardInput.Flush()
        while ($true) { $line = $h.StandardOutput.ReadLine(); if ($null -eq $line) { return $null }; $o = $line | ConvertFrom-Json; if ($o.id -eq $id) { return $o } }
    }
    Rpc 1 "initialize" @{ protocolVersion = "2025-06-18"; capabilities = @{}; clientInfo = @{ name = "check"; version = "1" } } | Out-Null
    $h.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $list = Rpc 2 "tools/call" @{ name = "clip_list"; arguments = @{} }
    Check ($list.result.content[0].text -like "*open Clip Shrinker window*") "helper connected to the open window"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $tr = Rpc 3 "tools/call" @{ name = "transcribe"; arguments = @{ clip = 1; from = 0; to = 6 } }
    $text = "$($tr.result.content[0].text)$($tr.error.message)"
    Check ($text -like "Speech *" -or $text -like "No speech found*") ("transcribe ran with the module's ffmpeg in {0:N0} s: {1}" -f $sw.Elapsed.TotalSeconds, ($text -split "`n")[0])
    $h.StandardInput.Close(); if (-not $h.WaitForExit(5000)) { $h.Kill() }

    "4. uninstall"
    $del = @(FindAll $mw "Delete") | Select-Object -Last 1              # AI editing is the last card
    Invoke $del
    $un = Win "Uninstall AI editing"
    Answer $un 6
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ((Test-Path "$App\modules\ai-agent\installed.json") -and $sw.Elapsed.TotalSeconds -lt 30) { Start-Sleep 1 }
    Start-Sleep 2
    Check (-not (Test-Path "$App\modules\ai-agent\helper") -and -not (Test-Path "$App\modules\ai-agent\models") -and -not (Test-Path "$App\modules\ai-agent\ffmpeg")) "module files deleted"
    Check (Test-Path "$App\modules\ai-agent\module.json") "module.json kept (Install is offered again)"
    Check ([bool](FindAll $mw "Install")) "Install button back"
    Check (-not ((FindAll $win "AI agent not connected") + (FindAll $win "AI agent connected"))) "AI agent pill gone"
    Shot $mw "modules_uninstalled"
}
finally {
    if (-not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(5000) | Out-Null }
    Remove-Item Env:CLIPSHRINKER_AI_SOURCE -ErrorAction SilentlyContinue
}
""
if ($fails) { "$fails check(s) FAILED" } else { "all checks passed (pictures in $Work)" }
