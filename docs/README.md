# Historia Extera Docs

Documentation for the `historia-extera` history generator and viewer.

The site builds with [ProperDocs](https://properdocs.org/) and MaterialX.
Dependencies live in the root `pyproject.toml` / `uv.lock` under the `docs` group.

```bash
uv sync
make docs-serve   # or: make docs-build
```

## Contents

- [Getting started](guide/getting-started.md)
- [CLI](guide/cli.md)
- [Viewer](guide/viewer.md)
- [Makefile](guide/makefile.md)
- [Developer overview](dev/index.md)
     