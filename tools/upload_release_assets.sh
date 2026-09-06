#!/bin/sh

# Attach the built release artifacts to this version's GitHub draft release.
#
# A draft whose tag has never been pushed reports a synthetic "untagged-..." slug
# as its tag, so `gh release upload v$VERSION` cannot find it. The draft's name is
# what actually identifies it, the same way tools/append_release_notes.py resolves
# it, so the tag is looked up from the name before anything is uploaded.

set -eu

script_dir=$(CDPATH= cd -- "$(dirname "$0")" && pwd)
repo_root=$(CDPATH= cd -- "$script_dir/.." && pwd)
version=$(sed -nE 's/^version = "([0-9]+\.[0-9]+\.[0-9]+)"$/\1/p' "$repo_root/pyproject.toml" | head -n1)

if [ -z "$version" ]; then
    echo "Could not read the project version from pyproject.toml." >&2
    exit 1
fi

if [ "$#" -eq 0 ]; then
    echo "Usage: $0 <asset> [asset...]" >&2
    exit 1
fi

for asset in "$@"; do
    if [ ! -f "$asset" ]; then
        echo "Missing release asset: $asset" >&2
        echo "Run 'make macos-release' first." >&2
        exit 1
    fi
done

tag="v$version"

if ! gh release view "$tag" >/dev/null 2>&1; then
    tag=$(
        gh api --paginate /repos/{owner}/{repo}/releases \
            --jq "map(select(.draft and .name == \"v$version\")) | .[0].tag_name // empty"
    )
    if [ -z "$tag" ]; then
        echo "No release or draft named v$version. Create the draft first." >&2
        exit 1
    fi
    echo "Draft v$version has no pushed tag yet; uploading to $tag."
fi

# --clobber makes a corrected draft build repeatable.
gh release upload "$tag" "$@" --clobber

echo "Uploaded $# asset(s) to the v$version draft."
