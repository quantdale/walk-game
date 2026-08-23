@echo off
rem Windows convenience wrapper around scripts\verify-domain.ps1.
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0verify-domain.ps1"
exit /b %ERRORLEVEL%
