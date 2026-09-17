@echo off
echo "This script uses docfx (https://dotnet.github.io/docfx/) to build and serve the documentation."
docfx site/docfx.json
docfx serve site/_site
