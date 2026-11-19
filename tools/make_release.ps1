# Builds the release package: one self-contained ClipShrinker.exe (.NET built in), the text files from dist\ and
# a library\ folder with ffmpeg, ffprobe and RIFE (taken from the 1.0.8 Dev folder), then zips it.
#   powershell -ExecutionPolicy Bypass -File tools\make_release.ps1
# Output: ..\Release\ClipShrinker\  and  ..\Release\ClipShrinker-<version>.zip  (next to the CSharp folder)
$ErrorActionPreference = "Stop"
$Root = Resolve-Path "$PSScriptRoot\.."
$Dev = Resolve-Path "$Root\..\Dev"
$Version = ([regex]::Match((Get-Content "$Root\src\ClipShrinker.Core\Constants.cs" -Raw), 'AppVersion = "([^"]+)"')).Groups[1].Value
if (-not $Version) { throw "AppVersion not found in Constants.cs" }
$RelDir = Join-Path (Split-Path $Root -Parent) "Release"
$Out = Join-Path $RelDir "ClipShrinker"
$Zip = Join-Path $RelDir "ClipShrinker-$Version.zip"
$Pub = Join-Path $env:TEMP "clipshrinker_publish"
$dotnet = if (Test-Path "C:\Program Files\dotnet\dotnet.exe") { "C:\Program Files\dotnet\dotnet.exe" } else { "dotnet" }

"Clip Shrinker $Version"
# the release folder cannot be rebuilt while its exe runs (the old build stopped halfway, with the kept files moved out)
if (Get-Process ClipShrinker -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($Out, [StringComparison]::OrdinalIgnoreCase) }) {
    throw "Close ClipShrinker first: it is running from $Out"
}
"1. publish (Release, win-x64, single file, self-contained)"
Remove-Item $Pub -Recurse -Force -ErrorAction SilentlyContinue
& $dotnet publish "$Root\src\ClipShrinker.App\ClipShrinker.App.csproj" -c Release -r win-x64 -o $Pub -p:DebugType=none --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
$extra = @(Get-ChildItem $Pub -File | Where-Object { $_.Name -ne "ClipShrinker.exe" })
if ($extra) { throw "publish made more than the exe: $($extra.Name -join ', ')" }

"2. assemble $Out"
# the owner's own files in the release folder (example sounds, settings from testing) are kept there but never zipped
$Keep = Join-Path $RelDir "ClipShrinker-kept-files"
if (Test-Path $Keep) { throw "$Keep is left from an interrupted run: move its files back into $Out, then delete it" }
$KeepPaths = @("modules\sound-track\sounds") + @(Get-ChildItem "$Out\library" -Filter *.json -ErrorAction SilentlyContinue | ForEach-Object { "library\$($_.Name)" })
if ((Test-Path "$Out\plugins\sound-track\sounds") -and -not (Test-Path "$Out\modules\sound-track\sounds")) {   # (made before 2.7's rename)
    New-Item -ItemType Directory -Force "$Out\modules\sound-track" | Out-Null; Move-Item "$Out\plugins\sound-track\sounds" "$Out\modules\sound-track\sounds"
}
foreach ($k in $KeepPaths) {
    if (Test-Path "$Out\$k") { New-Item -ItemType Directory -Force (Split-Path "$Keep\$k" -Parent) | Out-Null; Move-Item "$Out\$k" "$Keep\$k" }
}
Remove-Item $Out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory "$Out\library" | Out-Null
Copy-Item "$Pub\ClipShrinker.exe" $Out
foreach ($f in "README.txt", "CHANGELOG.txt", "LICENSE.txt", "THIRD-PARTY-NOTICES.txt", "ffmpeg-LICENSE.txt", "ffmpeg-build-settings.txt",
           "OpenCL-ICD-Loader-LICENSE.txt", "AMF-LICENSE.txt", "oneVPL-LICENSE.txt") {
    Copy-Item "$Root\dist\$f" $Out
}
Copy-Item "$Dev\library\ffmpeg.exe", "$Dev\library\ffprobe.exe" "$Out\library"
# 2.4: the built-in modules (module.json + CHANGELOG.txt each); RIFE belongs to the Timeline effects module
Copy-Item "$Root\modules" $Out -Recurse
$Rife = "$Out\modules\timeline-effects\rife"
New-Item -ItemType Directory $Rife | Out-Null
# RIFE: only the parts the program uses (like the in-app download)
foreach ($f in "rife-ncnn-vulkan.exe", "vcomp140.dll", "LICENSE.txt") { Copy-Item "$Dev\library\rife\$f" $Rife }
Copy-Item "$Dev\library\rife\rife-v4.6" $Rife -Recurse

"3. zip $Zip"
Remove-Item $Zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path $Out -DestinationPath $Zip -CompressionLevel Optimal
foreach ($k in $KeepPaths) {
    if (Test-Path "$Keep\$k") { New-Item -ItemType Directory -Force (Split-Path "$Out\$k" -Parent) | Out-Null; Move-Item "$Keep\$k" "$Out\$k" -Force; "   kept (not zipped): $k" }
}
Remove-Item $Keep -Recurse -Force -ErrorAction SilentlyContinue

$exe = Get-Item "$Out\ClipShrinker.exe"
$size = (Get-ChildItem $Out -Recurse -File | Measure-Object Length -Sum).Sum
"done: exe {0:N1} MB, folder {1:N1} MB, zip {2:N1} MB" -f ($exe.Length / 1MB), ($size / 1MB), ((Get-Item $Zip).Length / 1MB)
