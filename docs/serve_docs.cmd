@echo off
dotnet tool restore
dotnet docfx "%~dp0site\docfx.json" --serve
