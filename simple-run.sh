#!/bin/bash
cd "$(dirname "$0")"
dotnet clean
dotnet build -c Release
dotnet test -c Release
cd ReaderWriter.ConsoleApp
dotnet publish -c Release -r linux-x64 --self-contained true -o ./publish-linux
cp appsettings.json ./publish-linux/
chmod +x ./publish-linux/ReaderWriter.ConsoleApp
./publish-linux/ReaderWriter.ConsoleApp
