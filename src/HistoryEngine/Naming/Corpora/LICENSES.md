# Corpus provenance

Every file in this directory is free of attribution and share-alike obligations, so
the corpora can be redistributed inside a published Vintage Story mod indefinitely.

This was a deliberate choice over the faster route. Wiktionary and Wikipedia name
lists would have given broader coverage sooner, but they are CC BY-SA 4.0, and
share-alike is awkward to unwind once it is inside a shipped mod.

## What these files are

Each file is a **name list authored for this project**, written to reflect the
phonology and morphology of a historical naming tradition. The names are drawn from
or modelled on the public-domain historical record — sagas, chronicles, inscriptions,
prosopographies, and census records, all long out of copyright.

Individual names are facts and not copyrightable. These particular compilations are
original to this repository, so there is no third-party compilation right either.

## Licence

All files: **CC0 1.0 Universal** (public domain dedication).

## What the engine does with them

Corpora are training data for order-3 character Markov models, never emitted verbatim
— `MarkovNameModel` rejects any generated name that appears in its training set. Each
culture draws on a weighted blend of one to three of these files plus its own phoneme
mutations, so cultures come out invented-but-coherent rather than as recognisable
copies of a real language.

A name family is not a culture. There is no "the Norse civilization" in a generated
world; there are civilizations whose names lean on Norse phonology, mutated.

## File format

```
# comments start with hash
[given]        personal names
[place]        settlement and geographic names
[placesuffix]  suffixes that form place names, leading hyphen
[peoplesuffix] suffixes that form ethnonyms, leading hyphen
```

Adding a family means dropping a file here and adding it to `NameCorpus.FamilyNames`.
Nothing else needs to change.
