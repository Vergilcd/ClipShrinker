param(
    [string]$Exe = "$PSScriptRoot\..\src\ClipShrinker.App\bin\Debug\net10.0-windows\ClipShrinker.exe",
    [string]$Project = "$PSScriptRoot\..\tests\media\projects\join_fade_hdr.clipproj",
    [string]$OutDir = "$PSScriptRoot\..\tests\media\out\gui\join_fade_hdr",
    [string]$Choice = "Join into one video",
    [string]$Shot,
    [string]$ShotScript
)
# Phase 3 check: drives the WPF app with UI Automation the way a user would:
# Project > Open project..., set the save folder, pick One video / Separate files (2.9.1: "Export as" in the Export panel
# instead of a question after Export), Export, wait for "Done in ...". -Choice still takes the old answers too.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$P = [System.Windows.Automation.PropertyCondition]

function Find($scope, $prop, $value, $timeout = 15) {
    $cond = New-Object $P($prop, $value)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeout) {
        $el = $scope.FindFirst($TS::Descendants, $cond)
        if ($el) { return $el }
        Start-Sleep -Milliseconds 200
    }
    throw "not found: $value"
}
function Invoke($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function SetText($el, $text) { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text) }

Remove-Item $OutDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
# the project is passed on the command line (the new Windows file dialog does not expose its name field to UI Automation)
$proc = Start-Process (Resolve-Path $Exe) -ArgumentList "`"$((Resolve-Path $Project).Path)`"" -PassThru
try {
    $root = $A::RootElement
    $sw = [Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 300; $proc.Refresh() } until ($proc.MainWindowHandle -ne 0 -or $sw.Elapsed.TotalSeconds -gt 20)
    $win = $A::FromHandle($proc.MainWindowHandle)
    "window: $($win.Current.Name)"

    $sw = [Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 300; $win = $A::FromHandle($proc.MainWindowHandle) } until ($win.Current.Name -like "* - *.clipproj" -or $sw.Elapsed.TotalSeconds -gt 30)
    "opened project: $($win.Current.Name)"

    SetText (Find $win $A::NameProperty "Export folder") $OutDir
    Start-Sleep -Milliseconds 800
    $mode = if ($Choice -like "Each*" -or $Choice -like "Separate*") { "Separate files" } else { "One video" }
    $combo = Find $win $A::NameProperty "Export as"                 # 2.9.1: the "Export as" drop-down (2 or more clips)
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 400
    (Find $combo $A::NameProperty $mode).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Start-Sleep -Milliseconds 300
    Invoke (Find $win $A::AutomationIdProperty "GoButton")
    "export started ($mode)"

    $cap = Find $win $A::AutomationIdProperty "ProgressCaption"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $last = ""
    while ($sw.Elapsed.TotalSeconds -lt 300) {
        $t = $cap.Current.Name
        if ($t -ne $last) { "  progress: $t"; $last = $t }
        if ($t -like "Done in*" -or $t -like "Failed*" -or $t -eq "Aborted") { break }
        Start-Sleep -Milliseconds 500
    }
    "message: " + (Find $win $A::AutomationIdProperty "ExportMessage").Current.Name
    if ($Shot) { & $ShotScript -Out $Shot | Out-Null }
    Get-ChildItem $OutDir -Filter *.mp4 | ForEach-Object { "file: $($_.Name) $($_.Length) bytes" }
}
finally {
    if (-not $proc.HasExited) {
        $proc.CloseMainWindow() | Out-Null
        # clips are open: answer the close dialog (Save / Close without saving / Cancel)
        try { Invoke (Find (Find $A::RootElement $A::NameProperty "Close Clip Shrinker" 5) $A::NameProperty "Close without saving" 3) } catch { }
        if (-not $proc.WaitForExit(5000)) { $proc.Kill() }
    }
}
