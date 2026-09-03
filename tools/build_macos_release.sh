#!/bin/sh

set -eu

script_dir=$(CDPATH= cd -- "$(dirname "$0")" && pwd)
repo_root=$(CDPATH= cd -- "$script_dir/.." && pwd)
version=$(sed -nE 's/^version = "([0-9]+\.[0-9]+\.[0-9]+)"$/\1/p' "$repo_root/pyproject.toml" | head -n1)
node_version=${NODE_RUNTIME_VERSION:-26.3.0}

if [ -z "$version" ]; then
    echo "Could not read the project version from pyproject.toml." >&2
    exit 1
fi

if [ ! -f "$repo_root/viewer/node_modules/astro/bin/astro.mjs" ]; then
    echo "Viewer dependencies are missing. Run 'make install' first." >&2
    exit 1
fi

case "$(uname -m)" in
    arm64)
        runtime_id=osx-arm64
        node_arch=arm64
        artifact_arch=arm64
        ;;
    x86_64)
        runtime_id=osx-x64
        node_arch=x64
        artifact_arch=x64
        ;;
    *)
        echo "Unsupported macOS architecture: $(uname -m)" >&2
        exit 1
        ;;
esac

build_root="$repo_root/build/macos-release"
download_dir="$build_root/downloads"
release_dir="$repo_root/build/release"
swift_scratch="$build_root/swift"
dotnet_publish="$build_root/dotnet-$runtime_id"
node_name="node-v$node_version-darwin-$node_arch"
node_archive="$download_dir/$node_name.tar.gz"
node_shasums="$download_dir/SHASUMS256-v$node_version.txt"
archive="$release_dir/Historia-Extera-v$version-macos-$artifact_arch.zip"
checksum="$archive.sha256"
dmg="$release_dir/Historia-Extera-v$version-macos-$artifact_arch.dmg"
dmg_checksum="$dmg.sha256"
bundle="$release_dir/Historia Extera.app"

mkdir -p "$download_dir" "$release_dir"

curl -fL --retry 3 \
    "https://nodejs.org/dist/v$node_version/SHASUMS256.txt" \
    -o "$node_shasums"

if [ ! -f "$node_archive" ]; then
    curl -fL --retry 3 \
        "https://nodejs.org/dist/v$node_version/$node_name.tar.gz" \
        -o "$node_archive"
fi

expected=$(awk -v name="$node_name.tar.gz" '$2 == name { print $1 }' "$node_shasums")
actual=$(shasum -a 256 "$node_archive" | awk '{ print $1 }')
if [ -z "$expected" ] || [ "$actual" != "$expected" ]; then
    echo "Node runtime checksum mismatch for $node_name.tar.gz." >&2
    exit 1
fi

swift build \
    --package-path "$repo_root/macos/HistoriaExteraApp" \
    --scratch-path "$swift_scratch" \
    -c release

dotnet publish "$repo_root/src/HistoryEngine.Cli/HistoryEngine.Cli.csproj" \
    -c Release \
    -r "$runtime_id" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$dotnet_publish"

staging=$(mktemp -d "${TMPDIR:-/tmp}/historia-extera-release.XXXXXX")
trap 'rm -rf "$staging"' EXIT HUP INT TERM

node_dist="$staging/node"
mkdir -p "$node_dist"
tar -xzf "$node_archive" -C "$node_dist" --strip-components=1

staged_app="$staging/Historia Extera.app"
contents="$staged_app/Contents"
runtime="$contents/Resources/runtime"
viewer_source="$runtime/viewer-source"

mkdir -p \
    "$contents/MacOS" \
    "$runtime/bin" \
    "$runtime/licenses" \
    "$viewer_source/public"

cp "$swift_scratch/release/HistoriaExtera" "$contents/MacOS/HistoriaExtera"
cp "$repo_root/macos/HistoriaExteraApp/Info.plist" "$contents/Info.plist"
plutil -replace CFBundleShortVersionString -string "$version" "$contents/Info.plist"

cp "$node_dist/bin/node" "$runtime/bin/node"
cp "$node_dist/LICENSE" "$runtime/licenses/Node.txt"
cp "$dotnet_publish/historia-extera" "$runtime/bin/historia-extera"

ditto "$repo_root/viewer/src" "$viewer_source/src"
ditto "$repo_root/viewer/dev" "$viewer_source/dev"
cp "$repo_root/viewer/astro.config.mjs" "$viewer_source/astro.config.mjs"
cp "$repo_root/viewer/package.json" "$viewer_source/package.json"
cp "$repo_root/viewer/package-lock.json" "$viewer_source/package-lock.json"
cp "$repo_root/viewer/tsconfig.json" "$viewer_source/tsconfig.json"

find "$repo_root/viewer/public" -maxdepth 1 -type f -exec cp {} "$viewer_source/public/" \;
ditto "$repo_root/viewer/node_modules" "$runtime/node_modules"

# Key the extracted viewer cache to its packaged source and dependency manifest. This lets a
# corrected draft build invalidate an older cache even when the app version has not changed.
viewer_cache_id=$(
    find "$viewer_source" -type f -print |
        LC_ALL=C sort |
        while IFS= read -r file; do shasum -a 256 "$file"; done |
        shasum -a 256 |
        awk '{ print substr($1, 1, 12) }'
)
printf '%s\n' "$viewer_cache_id" > "$runtime/viewer-cache-id"

chmod 755 \
    "$contents/MacOS/HistoriaExtera" \
    "$runtime/bin/node" \
    "$runtime/bin/historia-extera"

# No Developer ID is installed on this builder. Nested executables and the bundle receive an
# ad-hoc signature so macOS can validate their structure; notarization is a separate release gate.
codesign --force --sign - "$runtime/bin/node"
codesign --force --sign - "$runtime/bin/historia-extera"
codesign --force --sign - "$contents/MacOS/HistoriaExtera"
codesign --force --sign - "$staged_app"
codesign --verify --deep --strict "$staged_app"

case "$bundle" in
    "$repo_root"/build/release/*.app) ;;
    *)
        echo "Refusing to replace unexpected bundle path: $bundle" >&2
        exit 1
        ;;
esac

if [ -e "$bundle" ]; then
    rm -rf "$bundle"
fi
rm -f "$archive" "$checksum" "$dmg" "$dmg_checksum"

ditto "$staged_app" "$bundle"
ditto -c -k --sequesterRsrc --keepParent "$bundle" "$archive"
shasum -a 256 "$archive" > "$checksum"

# The disk image is built from its own staging directory so the volume holds exactly the app
# and the drag-to-install shortcut, and nothing that happens to sit next to the bundle.
dmg_staging="$staging/dmg"
mkdir -p "$dmg_staging"
ditto "$staged_app" "$dmg_staging/Historia Extera.app"
ln -s /Applications "$dmg_staging/Applications"

hdiutil create \
    -volname "Historia Extera $version" \
    -srcfolder "$dmg_staging" \
    -fs HFS+ \
    -format UDZO \
    -ov \
    -quiet \
    "$dmg"

# Ad-hoc, for the same reason the bundle is: it lets macOS validate the image's structure,
# while Developer ID signing and notarization stay a separate release gate.
codesign --force --sign - "$dmg"
shasum -a 256 "$dmg" > "$dmg_checksum"

echo "Built release app: $bundle"
echo "Release archive:   $archive"
echo "SHA-256:           $checksum"
echo "Disk image:        $dmg"
echo "SHA-256:           $dmg_checksum"
