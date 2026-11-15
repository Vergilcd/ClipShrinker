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
"1. publish (Release, win-x64, single file, self-contained)"
Remove-Item $Pub -Recurse -Force -ErrorAction SilentlyContinue
& $dotnet publish "$Root\src\ClipShrinker.App\ClipShrinker.App.csproj" -c Release -r win-x64 -o $Pub -p:DebugType=none --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
$extra = @(Get-ChildItem $Pub -File | Where-Object { $_.Name -ne "ClipShrinker.exe" })
if ($extra) { throw "publish made more than the exe: $($extra.Name -join ', ')" }

"2. assemble $Out"
Remove-Item $Out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory "$Out\library" | Out-Null
Copy-Item "$Pub\ClipShrinker.exe" $Out
foreach ($f in "README.txt", "CHANGELOG.txt", "LICENSE.txt", "THIRD-PARTY-NOTICES.txt", "ffmpeg-LICENSE.txt", "ffmpeg-build-settings.txt",
           "OpenCL-ICD-Loader-LICENSE.txt") {
    Copy-Item "$Root\dist\$f" $Out
}
Copy-Item "$Dev\library\ffmpeg.exe", "$Dev\library\ffprobe.exe" "$Out\library"
# 2.4: the built-in plugins (plugin.json + CHANGELOG.txt each); RIFE belongs to the Timeline effects plugin
Copy-Item "$Root\plugins" $Out -Recurse
$Rife = "$Out\plugins\timeline-effects\rife"
New-Item -ItemType Directory $Rife | Out-Null
# RIFE: only the parts the program uses (like the in-app download)
foreach ($f in "rife-ncnn-vulkan.exe", "vcomp140.dll", "LICENSE.txt") { Copy-Item "$Dev\library\rife\$f" $Rife }
Copy-Item "$Dev\library\rife\rife-v4.6" $Rife -Recurse

"3. zip $Zip"
Remove-Item $Zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path $Out -DestinationPath $Zip -CompressionLevel Optimal

$exe = Get-Item "$Out\ClipShrinker.exe"
$size = (Get-ChildItem $Out -Recurse -File | Measure-Object Length -Sum).Sum
"done: exe {0:N1} MB, folder {1:N1} MB, zip {2:N1} MB" -f ($exe.Length / 1MB), ($size / 1MB), ((Get-Item $Zip).Length / 1MB)
