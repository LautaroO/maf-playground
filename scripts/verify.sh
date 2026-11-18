#!/usr/bin/env sh
set -eu

repository_root=$(git rev-parse --show-toplevel)

dotnet restore "$repository_root/MafPlayground.slnx"
dotnet format "$repository_root/MafPlayground.slnx" --no-restore --verify-no-changes
dotnet build "$repository_root/MafPlayground.slnx" --no-restore
dotnet test "$repository_root/tests/MafPlayground.Tests/MafPlayground.Tests.csproj" \
  --no-build \
  --no-restore
dotnet test "$repository_root/tests/MafPlayground.Studio.Tests/MafPlayground.Studio.Tests.csproj" --no-build --no-restore
dotnet test "$repository_root/tests/MafPlayground.Studio.IntegrationTests/MafPlayground.Studio.IntegrationTests.csproj" \
  --no-build --no-restore --filter 'Category!=StudioLiveModel&Category!=StudioTransport&Category!=StudioPackaging'
