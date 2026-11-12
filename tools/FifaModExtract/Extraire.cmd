@echo off
setlocal
set "extract_python="
for /d %%D in ("%LocalAppData%\Programs\Python\Python*") do if exist "%%~fD\python.exe" set "extract_python=%%~fD\python.exe"
if defined extract_python goto run
where py >nul 2>nul
if not errorlevel 1 (
    py -3 "%~dp0extract.py" %*
    goto done
)
echo Python 3 est requis. Installez Python pour utiliser cet outil.
goto done
:run
"%extract_python%" "%~dp0extract.py" %*
:done
pause
