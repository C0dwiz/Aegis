#!/bin/bash
dotnet restore
dotnet build --configuration Release
dotnet test tests/Aegis.Tests --verbosity normal
