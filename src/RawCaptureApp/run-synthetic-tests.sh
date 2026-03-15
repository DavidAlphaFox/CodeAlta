#!/usr/bin/env sh
set -eu

configuration="${1:-Debug}"
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
output_dir="$script_dir/bin/$configuration/net10.0"
agent_discovery_dir="$output_dir/Tests/agent-discovery"
fibonacci_dir="$output_dir/Tests/readme-fibonacci"

dotnet run --project "$script_dir/RawCaptureApp.csproj" --configuration "$configuration" -- \
  "Explain what this folder is about." \
  "$agent_discovery_dir" \
  "agent-discovery"

dotnet run --project "$script_dir/RawCaptureApp.csproj" --configuration "$configuration" -- \
  "Modify the readme.md to implement the code asked in the fenced code block." \
  "$fibonacci_dir" \
  "readme-fibonacci"
