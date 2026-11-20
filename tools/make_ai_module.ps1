# 2.8: builds the AI editing module's download files for the GitHub release "ai-editing" (the base package from
# make_release.ps1 has none of them; the app downloads them under Modules > AI editing > Install):
#   ai-editing.json                       the list the app reads: each part's version, size and SHA-256
#   ai-editing-helper-<version>.zip       clipshrinker-mcp.exe (the MCP helper Claude Code starts)
#   ai-editing-ffmpeg-<date>.zip          ffmpeg.exe with whisper + its DLLs (from Dev\library-ai), only used for speech to text
#   ggml-large-v3-turbo-q5_0.bin, ggml-silero-v5.1.2.bin   the speech model and VAD (optional parts)
#   powershell -ExecutionPolicy Bypass -File tools\make_ai_module.ps1 [-Models <folder with the two .bin files>]
# Output: ..\Release\ai-editing\  (try it with CLIPSHRINKER_AI_SOURCE=<that folder> before uploading)
param([string]$Models = "", [string]$MinApp = "2.9.0")
$ErrorActionPreference = "Stop"
$Root = Resolve-Path "$PSScriptRoot\.."
$Dev = Resolve-Path "$Root\..\Dev"
$RelDir = Join-Path (Split-Path $Root -Parent) "Release"
$Out = Join-Path $RelDir "ai-editing"
$Pub = Join-Path $env:TEMP "clipshrinker_mcp_publish"
$Stage = Join-Path $env:TEMP "clipshrinker_ai_stage"
$dotnet = if (Test-Path "C:\Program Files\dotnet\dotnet.exe") { "C:\Program Files\dotnet\dotnet.exe" } else { "dotnet" }
$ModuleJson = Get-Content "$Root\modules\ai-agent\module.json" -Raw | ConvertFrom-Json
$Version = $ModuleJson.version
$AiFfmpeg = "$Dev\library-ai\ffmpeg.exe"
if (-not $Models) {
    $Models = @(Get-ChildItem $RelDir -Directory -Filter "ClipShrinker*" | ForEach-Object { "$($_.FullName)\modules\ai-agent\models" } |
                Where-Object { Test-Path "$_\ggml-large-v3-turbo-q5_0.bin" }) | Select-Object -First 1
}
$ModelFiles = "ggml-large-v3-turbo-q5_0.bin", "ggml-silero-v5.1.2.bin"
foreach ($f in $ModelFiles) { if (-not $Models -or -not (Test-Path "$Models\$f")) { throw "speech model $f not found: give -Models <folder>" } }
if (-not (Test-Path $AiFfmpeg)) { throw "$AiFfmpeg not found (the ffmpeg build with whisper)" }
if (-not ((& $AiFfmpeg -hide_banner -filters 2>$null) -match "\bwhisper\b")) { throw "$AiFfmpeg has no whisper filter" }

"AI editing module $Version (needs Clip Shrinker $MinApp)"
"1. publish the MCP helper (Release, win-x64, single file, self-contained, compressed)"
Remove-Item $Pub, $Stage -Recurse -Force -ErrorAction SilentlyContinue
& $dotnet publish "$Root\src\ClipShrinker.Cli\ClipShrinker.Cli.csproj" -c Release -r win-x64 -o $Pub --self-contained `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

"2. zip the parts into $Out"
Remove-Item $Out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $Out, "$Stage\helper", "$Stage\ffmpeg" | Out-Null
Copy-Item "$Pub\ClipShrinker.Cli.exe" "$Stage\helper\clipshrinker-mcp.exe"
Copy-Item "$Root\dist\LICENSE.txt" "$Stage\helper"
$FfDate = (Get-Item $AiFfmpeg).LastWriteTime.ToString("yyyy-MM-dd")
Copy-Item $AiFfmpeg "$Stage\ffmpeg"
# 2.8.0-beta2: whisper.dll, ggml*.dll and the run-time backends (ggml-vulkan.dll = graphics card, ggml-cpu-*.dll)
Get-ChildItem "$Dev\library-ai" -Filter *.dll | Copy-Item -Destination "$Stage\ffmpeg"
foreach ($f in "ffmpeg-LICENSE.txt", "ffmpeg-build-settings.txt", "whisper.cpp-LICENSE.txt") { Copy-Item "$Root\dist\$f" "$Stage\ffmpeg" }
$HelperZip = "ai-editing-helper-$Version.zip"
$FfmpegZip = "ai-editing-ffmpeg-$FfDate.zip"
Compress-Archive -Path "$Stage\helper\*" -DestinationPath "$Out\$HelperZip" -CompressionLevel Optimal
Compress-Archive -Path "$Stage\ffmpeg\*" -DestinationPath "$Out\$FfmpegZip" -CompressionLevel Optimal
foreach ($f in $ModelFiles) { Copy-Item "$Models\$f" $Out }
Remove-Item $Stage -Recurse -Force

"3. write ai-editing.json"
function Part($id, $name, $ver, $file, $kind, $dest, $optional, $fallbacks) {
    $fi = Get-Item "$Out\$file"
    [ordered]@{ id = $id; name = $name; version = $ver; file = $file; url = $file; size = $fi.Length
                sha256 = (Get-FileHash $fi.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); kind = $kind; dest = $dest
                optional = $optional; fallbacks = @($fallbacks) }
}
$hf = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin"
$hfVad = "https://huggingface.co/ggml-org/whisper-vad/resolve/main/ggml-silero-v5.1.2.bin"
$manifest = [ordered]@{
    version = $Version; min_app = $MinApp
    parts = @(
        (Part "ffmpeg" "ffmpeg with speech recognition" $FfDate $FfmpegZip "zip" "ffmpeg" $false @()),
        (Part "vad" "Voice activity model (Silero VAD)" "5.1.2" "ggml-silero-v5.1.2.bin" "file" "models" $true @($hfVad)),
        (Part "model" "Speech model (Whisper large-v3-turbo)" "large-v3-turbo-q5_0" "ggml-large-v3-turbo-q5_0.bin" "file" "models" $true @($hf)),
        (Part "helper" "AI editing helper (MCP)" $Version $HelperZip "zip" "helper" $false @())
    )
}
# UTF-8 without BOM (the app's JSON reader takes either, other tools may not)
[IO.File]::WriteAllText("$Out\ai-editing.json", ($manifest | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))

Get-ChildItem $Out | ForEach-Object { "   {0,-40} {1,10:N1} MB" -f $_.Name, ($_.Length / 1MB) }
""
"Test from these files:   set CLIPSHRINKER_AI_SOURCE=$Out   then start Clip Shrinker > Modules > AI editing > Install"
"Publish (the owner does this):"
"   gh release create ai-editing --prerelease --title ""AI editing module"" --notes ""Downloaded by Clip Shrinker 2.8+ (Modules > AI editing)."""
"   gh release upload ai-editing ""$Out\ai-editing.json"" ""$Out\$HelperZip"" ""$Out\$FfmpegZip"" --clobber"
"   (the speech model only once, or when it changes:)"
"   gh release upload ai-editing ""$Out\ggml-large-v3-turbo-q5_0.bin"" ""$Out\ggml-silero-v5.1.2.bin"" --clobber"
