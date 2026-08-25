#!/usr/bin/env bash
set -euo pipefail

echo "== .NET =="
dotnet --info || true

echo
echo "== solution/project files =="
find . -maxdepth 4 \( -name '*.sln' -o -name '*.slnx' -o -name '*.csproj' -o -name 'Directory.Packages.props' -o -name 'global.json' \) -print | sort

echo
echo "== MAF / AI package references =="
grep -RIn --include='*.csproj' --include='Directory.Packages.props' -E 'Microsoft\.Agents\.AI|Microsoft\.Extensions\.AI|Azure\.AI\.Projects|OpenAI|Anthropic|Ollama' . || true

echo
echo "== likely MAF abstractions =="
grep -RIn --include='*.cs' -E 'AIAgent|ChatClientAgent|WorkflowBuilder|Executor|MessageHandler|AIFunction|AIContextProvider|AgentSession|IChatClient|AddWorkflow|AddAIAgent' . || true
