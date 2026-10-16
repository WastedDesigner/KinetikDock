@echo off
cd /d "%~dp0"
rem Builds KinetikDock.exe with the .NET Framework compiler that ships with Windows. No SDK needed.
rem "build.bat package" also makes KinetikDock.zip (the exe, README and licence) for a release.
if not exist icon.ico powershell -NoProfile -ExecutionPolicy Bypass -File make-icon.ps1 || exit /b 1
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /utf8output /codepage:65001 /platform:x64 /target:winexe /out:KinetikDock.new.exe /win32icon:icon.ico /resource:icon.ico,icon.ico ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Web.Extensions.dll ^
  KinetikDock.cs || exit /b 1
rem Built under a new name first, so this works while the app is running. The exe it replaces goes to ..\Old Versions
rem (when that folder is there), stamped with the date and time, to go back to if a build misbehaves.
if exist KinetikDock.exe if exist "..\Old Versions\" (
  for /f %%t in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd-HHmmss"') do move /y KinetikDock.exe "..\Old Versions\KinetikDock-%%t.exe" >nul
)
if exist KinetikDock.exe move /y KinetikDock.exe KinetikDock.old.exe >nul
move /y KinetikDock.new.exe KinetikDock.exe >nul || exit /b 1
if exist KinetikDock.old.exe del KinetikDock.old.exe 2>nul
echo Built KinetikDock.exe
if /i not "%~1"=="package" exit /b 0
if exist dist rmdir /s /q dist
mkdir dist\KinetikDock
copy /y KinetikDock.exe dist\KinetikDock\ >nul
copy /y README.md dist\KinetikDock\ >nul
copy /y LICENSE dist\KinetikDock\ >nul
if exist KinetikDock.zip del KinetikDock.zip
powershell -NoProfile -Command "Compress-Archive -Path dist\KinetikDock\* -DestinationPath KinetikDock.zip"
rmdir /s /q dist
echo Made KinetikDock.zip
