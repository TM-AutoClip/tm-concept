# Points git at the repository's hooks (.githooks: pre-commit secret scan, pre-push identity + message scan).
$ErrorActionPreference = 'Stop'
$root = (& git -C $PSScriptRoot rev-parse --show-toplevel).Trim()
& git -C $root config core.hooksPath .githooks
Write-Host "git hooks enabled (.githooks)"
