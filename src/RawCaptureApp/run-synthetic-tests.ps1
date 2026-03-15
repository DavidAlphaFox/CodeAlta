param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputDir = Join-Path $projectDir "bin\$Configuration\net10.0"
$agentDiscoveryDir = Join-Path $outputDir "Tests\agent-discovery"
$fibonacciDir = Join-Path $outputDir "Tests\readme-fibonacci"

dotnet run --project (Join-Path $projectDir "RawCaptureApp.csproj") --configuration $Configuration -- `
    "Explain what this folder is about." `
    $agentDiscoveryDir `
    "agent-discovery"

dotnet run --project (Join-Path $projectDir "RawCaptureApp.csproj") --configuration $Configuration -- `
    "Modify the readme.md to implement the code asked in the fenced code block." `
    $fibonacciDir `
    "readme-fibonacci"
