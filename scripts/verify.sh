#!/usr/bin/env sh
set -eu

repository_root=$(git rev-parse --show-toplevel)

dotnet restore "$repository_root/MafPlayground.slnx" -m:1 -p:UseSharedCompilation=false -nr:false
dotnet format "$repository_root/MafPlayground.slnx" --no-restore --verify-no-changes
dotnet build "$repository_root/MafPlayground.slnx" --no-restore -m:1 -p:UseSharedCompilation=false -nr:false
dotnet test "$repository_root/tests/MafPlayground.Tests/MafPlayground.Tests.csproj" \
  --no-build \
  --no-restore -m:1 -p:UseSharedCompilation=false -nr:false

dotnet test "$repository_root/tests/MafPlayground.Studio.Tests/MafPlayground.Studio.Tests.csproj" \
  --no-build --no-restore -m:1 -p:UseSharedCompilation=false -nr:false
dotnet test "$repository_root/tests/MafPlayground.Studio.IntegrationTests/MafPlayground.Studio.IntegrationTests.csproj" \
  --no-build --no-restore --filter 'Category!=StudioLiveModel&Category!=StudioTransport&Category!=StudioPackaging' \
  -m:1 -p:UseSharedCompilation=false -nr:false
# Frontend and opted-in transport/package/live checks are documented in tests/README.md.
