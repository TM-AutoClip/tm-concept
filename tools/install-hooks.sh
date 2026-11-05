#!/bin/sh
# Points git at the repository's hooks (.githooks: pre-commit secret scan, pre-push identity + message scan).
set -e
root=$(git rev-parse --show-toplevel)
git -C "$root" config core.hooksPath .githooks
chmod +x "$root/.githooks/pre-commit" "$root/.githooks/pre-push" 2>/dev/null || true
echo "git hooks enabled (.githooks)"
