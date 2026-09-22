#!/bin/sh
dotnet tool restore
dotnet docfx "$(dirname "$0")/site/docfx.json" --serve
