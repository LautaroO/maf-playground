#!/usr/bin/env sh
set -eu

repository_root=$(git rev-parse --show-toplevel)
git -C "$repository_root" config core.hooksPath .githooks

echo "Repository Git hooks enabled from .githooks."
