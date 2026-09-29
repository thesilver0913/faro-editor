#!/bin/sh
# Faro (tar.gz) needs the .NET 10 SDK. Without one, run this once: it goes into ./dotnet, where Faro's launcher looks first.
cd "$(dirname "$0")" && curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir dotnet
