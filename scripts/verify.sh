#!/usr/bin/env sh
set -eu

repository_root=$(git rev-parse --show-toplevel)

dotnet restore "$repository_root/MafPlayground.slnx"
dotnet format "$repository_root/MafPlayground.slnx" --no-restore --verify-no-changes
dotnet build "$repository_root/MafPlayground.slnx" --no-restore
dotnet test "$repository_root/tests/MafPlayground.Tests/MafPlayground.Tests.csproj" \
  --no-build \
  --no-restore
