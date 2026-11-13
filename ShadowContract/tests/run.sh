#!/usr/bin/env bash
# Автотесты Shadow Contract (Playwright + Chromium).
# Создаёт отладочную копию игры с хуком __g (доступ к состоянию) и гоняет сценарии.
set -e
DIR="$(cd "$(dirname "$0")" && pwd)"
OUT="${1:-$(mktemp -d)}"
mkdir -p "$OUT/shots"
sed 's#/\* boot \*/#window.__g=function(c){return eval(c)};#' "$DIR/../Shadow_Contract.html" > "$OUT/dbg.html"
node "$DIR/scenarios.js" "$OUT"
node "$DIR/routes.js" "$OUT"
