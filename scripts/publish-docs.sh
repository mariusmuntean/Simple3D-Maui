#!/usr/bin/env bash
# Publish a locally built DocFX site; no hosted MAUI build is required.
set -euo pipefail
cd "$(dirname "$0")/.."
test -f _site/index.html || { echo 'Build the DocFX site first' >&2; exit 1; }
test -f _site/api/Simple3D.Maui.SceneView.html || { echo 'API documentation is missing' >&2; exit 1; }
repository="$(git remote get-url origin)"
publication="$(mktemp -d "${TMPDIR:-/tmp}/simple3d-pages.XXXXXX")"
trap 'rm -r "$publication"' EXIT
git -C "$publication" init -b gh-pages
git -C "$publication" remote add origin "$repository"
if git ls-remote --exit-code --heads origin gh-pages >/dev/null 2>&1; then
    git -C "$publication" fetch --depth=1 origin gh-pages
    git -C "$publication" reset --hard FETCH_HEAD
fi
rsync -a --delete --exclude=.git _site/ "$publication/"
touch "$publication/.nojekyll"
git -C "$publication" add -A
if git -C "$publication" diff --cached --quiet; then
    echo 'Documentation is already published.'
    exit 0
fi
git -C "$publication" commit -m 'Publish documentation site'
git -C "$publication" push origin HEAD:gh-pages
