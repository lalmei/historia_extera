# Historia Extera — CLI + viewer + docs
#
#   make generate          # run the history engine → viewer/public/worlds/world.json
#   make viewer            # Astro dev server
#   make docs-serve        # ProperDocs live reload
#   make test              # xunit suite
#   make generate SEED=7 YEARS=500 CIVS=12

.DEFAULT_GOAL := help

CLI_PROJECT := src/HistoryEngine.Cli
VIEWER      := viewer
GOLDEN      := src/HistoryEngine.Tests/Goldens/standard-seed42.sha256
MAC_PACKAGE := macos/HistoriaExteraApp
MAC_BUILD   := build/swift
MAC_APP     := build/Historia Extera.app
VERSION     := $(shell sed -nE 's/^version = "([0-9]+\.[0-9]+\.[0-9]+)"$$/\1/p' pyproject.toml | head -n1)
MAC_ARCH    := $(shell uname -m)
MAC_RELEASE := build/release/Historia-Extera-v$(VERSION)-macos-$(MAC_ARCH).zip
UV          ?= uv
UV_RUN      := $(UV) run

# Semantic version bumps across every file that mirrors the one version (see the
# [tool.bumpversion] block in pyproject.toml). Run through uv in a throwaway
# environment, so bump-my-version never lands in this project's dependencies.
BUMP        := $(UV) run --no-project --with bump-my-version==1.5.1 bump-my-version
PART        ?= patch

SEED   ?= 42
YEARS  ?= 300
CIVS   ?= 8
SIZE   ?= 4096
RASTER ?= 256
OUT    ?=
SAMPLE ?=
ARGS   ?=

# Phase 2 terrain: where a baked raster set lands, and at what resolution.
TERRAIN     ?= build/terrain
TERRAIN_RES ?= 512

# The Phase 2 trial: terrain from a generator that has never heard of this engine.
# WorldEngine (MIT) is run through uv in a throwaway environment, so nothing it needs
# lands in this project's pyproject.toml and nothing lands in HistoryEngine at all.
WE_SEED     ?= 4242
WE_RES      ?= 512
WE_WORLD    ?= build/worldengine
WE_ARGS     ?=
TERRAIN_WE  ?= build/terrain-worldengine

CLI_FLAGS := --seed $(SEED) --years $(YEARS) --civs $(CIVS) --size $(SIZE) --raster $(RASTER)
ifneq ($(OUT),)
  CLI_FLAGS += --out $(OUT)
endif
ifneq ($(SAMPLE),)
  CLI_FLAGS += --sample $(SAMPLE)
endif
CLI_FLAGS += $(ARGS)

.PHONY: help bump bump-patch bump-minor bump-major bump-dry generate fingerprint terrain-bake terrain-worldengine terrain-generate test build viewer install preview macos-app macos-run macos-release macos-release-upload docs-build docs-serve clean

help:
	@echo "Historia Extera"
	@echo
	@echo "  make generate [SEED=42 YEARS=300 CIVS=8 SIZE=4096 RASTER=256]"
	@echo "  make fingerprint   # regenerate golden digest for seed 42"
	@echo "  make terrain-bake  # bake the noise world to rasters (TERRAIN, TERRAIN_RES)"
	@echo "  make terrain-worldengine  # convert a WorldEngine world into a raster set"
	@echo "  make terrain-generate  # then run a history over them"
	@echo "  make test"
	@echo "  make build"
	@echo "  make viewer        # npm run dev in viewer/"
	@echo "  make install       # npm install in viewer/"
	@echo "  make preview       # npm run preview in viewer/"
	@echo "  make macos-app     # build the native SwiftUI shell"
	@echo "  make macos-run     # build and open the macOS app"
	@echo "  make macos-release # self-contained app archive for this Mac architecture"
	@echo "  make docs-build    # ProperDocs → site/ (uv)"
	@echo "  make docs-serve    # ProperDocs live reload (uv)"
	@echo "  make bump-patch | bump-minor | bump-major   # semantic version bump"
	@echo "  make bump-dry PART=minor   # show what a bump would change"
	@echo "  make clean"
	@echo
	@echo "Extra CLI flags: make generate ARGS='--pretty --sample 20'"
	@echo "Docs deps: uv sync  (pyproject.toml / uv.lock)"

generate:
	dotnet run --project $(CLI_PROJECT) -- $(CLI_FLAGS)

# Written through a temp file so a failed run leaves the committed golden intact.
# The chmod is not cosmetic: mktemp creates 0600, and mv would carry that onto a
# file the repository tracks as 0644.
fingerprint:
	@set -eu; \
		tmp="$$(mktemp "$(GOLDEN).tmp.XXXXXX")"; \
		trap 'rm -f "$$tmp"' 0 1 2 3 15; \
		dotnet run --project $(CLI_PROJECT) -- \
			--seed 42 --years 300 --civs 8 --size 4096 --raster 64 --fingerprint \
			> "$$tmp"; \
		chmod 644 "$$tmp"; \
		mv "$$tmp" "$(GOLDEN)"; \
		trap - 0 1 2 3 15; \
		echo "wrote $(GOLDEN)"

# Phase 2: bake this seed's noise world out as a raster set, then read it back in.
# The round trip is the reference for wiring up a real generator's export.
terrain-bake:
	dotnet run --project $(CLI_PROJECT) -- \
		--seed $(SEED) --size $(SIZE) --emit-terrain $(TERRAIN) --terrain-res $(TERRAIN_RES)

# The external half of the same route: generate a world with WorldEngine, then convert
# its protobuf into PGM planes and a manifest. See docs/dev/terrain-trial.md for what the
# conversion has to decide on the generator's behalf, and what that costs.
terrain-worldengine:
	$(UV) run --no-project --with worldengine==0.20.0 \
		worldengine world -s $(WE_SEED) -x $(WE_RES) -y $(WE_RES) -n trial -o $(WE_WORLD)
	$(UV) run --no-project --script tools/terrain/worldengine_to_raster.py \
		$(WE_WORLD)/trial.world --out $(TERRAIN_WE) $(WE_ARGS)
	@echo
	@echo "Then:  make terrain-generate TERRAIN=$(TERRAIN_WE)"

terrain-generate:
	dotnet run --project $(CLI_PROJECT) -- $(CLI_FLAGS) --terrain $(TERRAIN)/terrain.json

test:
	dotnet test

build:
	dotnet build
	npm run build --prefix $(VIEWER)

viewer:
	npm run dev --prefix $(VIEWER)

install:
	npm install --prefix $(VIEWER)

preview:
	npm run preview --prefix $(VIEWER)

# The native app is a deliberately thin shell around the existing dev-only generator and
# viewer. Keeping the bundle in build/ lets it find the repository without baking one
# developer's absolute path into the executable.
macos-app:
	swift build --package-path $(MAC_PACKAGE) --scratch-path $(MAC_BUILD) -c release
	mkdir -p "$(MAC_APP)/Contents/MacOS"
	cp "$(MAC_BUILD)/release/HistoriaExtera" "$(MAC_APP)/Contents/MacOS/HistoriaExtera"
	cp "$(MAC_PACKAGE)/Info.plist" "$(MAC_APP)/Contents/Info.plist"
	codesign --force --deep --sign - "$(MAC_APP)"
	@echo "built $(MAC_APP)"

macos-run: macos-app
	open "$(MAC_APP)"

macos-release:
	sh tools/build_macos_release.sh

# Explicit publication step: build first, then attach both the archive and its digest to the
# matching GitHub draft release. `--clobber` makes a corrected draft build repeatable.
macos-release-upload: macos-release
	gh release upload "v$(VERSION)" "$(MAC_RELEASE)" "$(MAC_RELEASE).sha256" --clobber

docs-build docs-serve: SHELL := /bin/sh

docs-build:
	@$(UV_RUN) properdocs build -f properdocs.yml --strict

docs-serve:
	@$(UV_RUN) properdocs serve -f properdocs.yml

# Version bumps rewrite pyproject.toml, Directory.Build.props, WorldExporter.cs,
# Info.plist, and the viewer's package.json/package-lock.json in one step. The commit
# and tag are left to you.
bump:
	$(BUMP) bump $(PART)
	@echo "bumped to $$($(BUMP) show current_version)"

bump-patch:
	@$(MAKE) bump PART=patch

bump-minor:
	@$(MAKE) bump PART=minor

bump-major:
	@$(MAKE) bump PART=major

bump-dry:
	$(BUMP) bump --dry-run --verbose $(PART)

clean:
	dotnet clean
	rm -rf $(VIEWER)/dist $(VIEWER)/.astro site build
