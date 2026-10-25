# Downloads a portable Python 3.12 with tkinter (python-build-standalone, from GitHub) into the "python" folder next to this file.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Set-Location $PSScriptRoot
Start-Transcript -Path (Join-Path $PSScriptRoot 'portable-python-log.txt') -Force | Out-Null
$d = Join-Path $env:TEMP 'clip-python'
try {
    Write-Host 'Looking up the newest portable Python 3.12 (python-build-standalone, includes tkinter) ...'
    $rel = Invoke-RestMethod 'https://api.github.com/repos/astral-sh/python-build-standalone/releases/latest' -Headers @{ 'User-Agent' = 'ClipShrinker' }
    $asset = $rel.assets | Where-Object { $_.name -match '^cpython-3\.12\.\d+\+\d+-x86_64-pc-windows-msvc-install_only\.tar\.gz$' } |
             Select-Object -First 1
    if (-not $asset) { throw 'No Python 3.12 Windows build found in the latest python-build-standalone release' }
    Write-Host ("Downloading " + $asset.name + " (about " + [math]::Round($asset.size / 1MB) + " MB) ...")
    $z = Join-Path $env:TEMP 'clip-python.tar.gz'
    Invoke-WebRequest $asset.browser_download_url -OutFile $z
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
    New-Item -ItemType Directory $d | Out-Null
    Write-Host 'Unpacking ...'
    & tar.exe -xzf $z -C $d
    if ($LASTEXITCODE -ne 0) { throw 'tar.exe could not unpack the download' }
    $exe = Get-ChildItem $d -Recurse -Filter pythonw.exe | Select-Object -First 1
    if (-not $exe) { throw 'pythonw.exe was not found in the downloaded package' }
    if (Test-Path 'python') { Remove-Item 'python' -Recurse -Force }
    Copy-Item $exe.DirectoryName 'python' -Recurse
    Remove-Item $z, $d -Recurse -Force
    Write-Host 'Checking that tkinter works ...'
    & (Join-Path $PSScriptRoot 'python\python.exe') -c "import tkinter; print('tkinter OK')"
    if ($LASTEXITCODE -ne 0) {
        if (Test-Path 'python_failed') { Remove-Item 'python_failed' -Recurse -Force }
        Rename-Item 'python' 'python_failed'
        throw 'This Python package has no working tkinter (renamed to python_failed for inspection)'
    }
    foreach ($x in 'Lib\test', 'Lib\idlelib', 'Lib\turtledemo', 'Lib\ensurepip', 'Lib\venv', 'Lib\lib2to3', 'Lib\pydoc_data', 'Lib\site-packages', 'tcl\nmake', 'Lib\sqlite3', 'Lib\unittest', 'Lib\turtle.py', 'Lib\doctest.py', 'Lib\pdb.py', 'Lib\__phello__', 'Lib\__hello__.py', 'Lib\tkinter\tix.py', 'tcl\tix8.4.3', 'tcl\tcl8.6\tzdata', 'tcl\tk8.6\demos', 'DLLs\_sqlite3.pyd', 'DLLs\sqlite3.dll', 'DLLs\_msi.pyd', 'DLLs\_ctypes_test.pyd', 'Scripts', 'include', 'libs') {
        Remove-Item (Join-Path 'python' $x) -Recurse -Force -ErrorAction SilentlyContinue
    }
    Get-ChildItem 'python' -Recurse -Include *.pdb, *.lib | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem (Join-Path 'python' 'DLLs') -Filter '_test*.pyd' | Remove-Item -Force -ErrorAction SilentlyContinue
    & (Join-Path $PSScriptRoot 'python\python.exe') -c "import tkinter, json, wave, zlib; print('final check OK')"
    if ($LASTEXITCODE -ne 0) { throw 'Python stopped working after removing the unneeded files' }
    Write-Host 'Portable Python is ready.'
    $code = 0
} catch {
    Write-Host ''
    Write-Host ('FAILED: ' + $_.Exception.Message) -ForegroundColor Red
    $code = 1
} finally {
    Stop-Transcript | Out-Null
}
exit $code
