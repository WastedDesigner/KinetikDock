param([string]$SourceDirectory=(Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
$testExe=Join-Path $env:TEMP ('KinetikDock-benchmark-'+[Guid]::NewGuid().ToString('N')+'.exe')
try {
    $sources=@((Join-Path $SourceDirectory 'KinetikDock.cs'),(Join-Path $SourceDirectory 'SuiteSupport.cs'),(Join-Path $PSScriptRoot 'DockBenchmark.cs'))
    foreach($extra in @('DockFeatures.cs','DesktopOrganisation.cs')) { $path=Join-Path $SourceDirectory $extra; if(Test-Path -LiteralPath $path) { $sources+=$path } }
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /utf8output /codepage:65001 /target:exe /platform:x64 /main:DockBenchmark "/out:$testExe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:Accessibility.dll $sources
    if($LASTEXITCODE -ne 0) { throw 'Benchmark failed to compile' }
    & $testExe
    if($LASTEXITCODE -ne 0) { throw 'Benchmark failed' }
} finally { if(Test-Path -LiteralPath $testExe) { Remove-Item -LiteralPath $testExe -Force } }
