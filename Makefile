# Historia Extera — CLI + viewer + docs
#
#   make generate          # run legends → viewer/public/worlds/world.json
#   make viewer            # Astro dev server
#   make docs-serve        # ProperDocs live reload
#   make test              # xunit suite
#   make generate SEED=7 YEARS=500 CIVS=12

.DEFAULT_GOAL := help

CLI_PROJECT := src/HistoryEngine.Cli
VIEWER      := viewer
GOLDEN      := src/HistoryEngine.Tests/Goldens/standard-seed42.sha256
UV          ?= uv
UV_RUN      := $(UV) run

SEED   ?= 42
YEARS  ?= 300
CIVS   ?= 8
SIZE   ?= 4096
RASTER ?= 256
OUT    ?=
SAMPLE ?=
ARGS   ?=

CLI_FLAGS := --seed $(SEED) --years $(YEARS) --civs $(CIVS) --size $(SIZE) --raster $(RASTER)
ifneq ($(OUT),)
  CLI_FLAGS += --out $(OUT)
endif
ifneq ($(SAMPLE),)
  CLI_FLAGS += --sample $(SAMPLE)
endif
CLI_FLAGS += $(ARGS)

.PHONY: help generate legends fingerprint test build viewer install preview docs-build docs-serve clean

help:
	@echo "Historia Extera"
	@echo
	@echo "  make generate [SEED=42 YEARS=300 CIVS=8 SIZE=4096 RASTER=256]"
	@echo "  make fingerprint   # regenerate golden digest for seed 42"
	@echo "  make test"
	@echo "  make build"
	@echo "  make viewer        # npm run dev in viewer/"
	@echo "  make install       # npm install in viewer/"
	@echo "  make preview       # npm run preview in viewer/"
	@echo "  make docs-build    # ProperDocs → site/ (uv)"
	@echo "  make docs-serve    # ProperDocs live reload (uv)"
	@echo "  make clean"
	@echo
	@echo "Extra CLI flags: make generate ARGS='--pretty --sample 20'"
	@echo "Docs deps: uv sync  (pyproject.toml / uv.lock)"

generate legends:
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

docs-build docs-serve: SHELL := /bin/sh

docs-build:
	@$(UV_RUN) properdocs build -f properdocs.yml --strict

docs-serve:
	@$(UV_RUN) properdocs serve -f properdocs.yml

clean:
	dotnet clean
	rm -rf $(VIEWER)/dist $(VIEWER)/.astro site
