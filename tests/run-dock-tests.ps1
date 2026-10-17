param([string]$Screenshot)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$testExe=Join-Path $env:TEMP ('KinetikDock-tests-'+[Guid]::NewGuid().ToString('N')+'.exe')
try {
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /utf8output /codepage:65001 /target:exe /platform:x64 /main:DockFeatureTests "/out:$testExe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:Accessibility.dll (Join-Path $root 'KinetikDock.cs') (Join-Path $root 'DockFeatures.cs') (Join-Path $root 'DesktopOrganisation.cs') (Join-Path $root 'SuiteSupport.cs') (Join-Path $PSScriptRoot 'DockFeatureTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Dock tests failed to compile' }
    if($Screenshot) { & $testExe $Screenshot } else { & $testExe }
    if ($LASTEXITCODE -ne 0) { throw 'Dock feature tests failed' }
} finally { if (Test-Path -LiteralPath $testExe) { Remove-Item -LiteralPath $testExe -Force } }
