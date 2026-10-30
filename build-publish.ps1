param([switch]$SkipChecks)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw '.NET Framework 4.8 compiler is missing. Enable/install .NET Framework 4.8 from https://dotnet.microsoft.com/download/dotnet-framework/net48 then rebuild.'
}
$release = Join-Path $projectRoot 'release\LgSwitch-1.0.1'
$checkRoot = Join-Path $projectRoot 'obj\checks'
New-Item -ItemType Directory -Force -Path $release,$checkRoot | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$references = @('/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Xml.dll')
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warn:4 /warnaserror+ "/out:$release\LgSwitch.exe" "/win32icon:$projectRoot\assets\LgSwitch.ico" "/win32manifest:$projectRoot\app.manifest" @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Production compilation failed.' }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warn:4 /warnaserror+ /main:LgSwitch.CheckRunner "/out:$checkRoot\LgSwitch.Checks.exe" "/win32manifest:$projectRoot\checks.manifest" @references @sources (Join-Path $projectRoot 'scripts\CheckRunner.cs')
if ($LASTEXITCODE -ne 0) { throw 'Check runner compilation failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination (Join-Path $release 'LgSwitch.exe.config') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination (Join-Path $checkRoot 'LgSwitch.Checks.exe.config') -Force
foreach ($name in @('README.md','BUILD.md','FEASIBILITY.md','VALIDATION.md','CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $release -Force
}
New-Item -ItemType Directory -Force -Path (Join-Path $release 'docs'), (Join-Path $release 'assets') | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\CURRENT_STATE.md') -Destination (Join-Path $release 'docs') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\screenshot.png') -Destination (Join-Path $release 'assets') -Force
if (-not $SkipChecks) {
    $runner = Join-Path $checkRoot 'LgSwitch.Checks.exe'
    $test = Start-Process -FilePath $runner -ArgumentList '--self-test' -WindowStyle Hidden -PassThru
    if (-not $test.WaitForExit(20000)) { $test.Kill(); throw 'Self-check timed out.' }
    if ($test.ExitCode -ne 0) { throw "Self-check failed. Read $checkRoot\last-error.txt" }
    Get-Content -LiteralPath (Join-Path $checkRoot 'self-test.txt')
    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((Join-Path $release 'LgSwitch.exe')))
    $manifest = Start-Process -FilePath $runner -ArgumentList "--verify-manifest $encoded" -WindowStyle Hidden -PassThru
    if (-not $manifest.WaitForExit(10000)) { $manifest.Kill(); throw 'Manifest check timed out.' }
    if ($manifest.ExitCode -ne 0) { throw "Manifest check failed. Read $checkRoot\last-error.txt" }
    Get-Content -LiteralPath (Join-Path $checkRoot 'manifest-check.txt')
}
Write-Output "Portable executable: $release\LgSwitch.exe"
