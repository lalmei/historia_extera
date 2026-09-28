# Campaign accounts and the historical record

Issue #209 begins with `Campaign` tomes. Each account has a checkable commander and war.
The viewer derives its comparison from those identifiers, section dates and references,
battle records, and the war's chronicle. It does not change the tome, add a correctness field,
or assign a score to a writer or realm.

The original account and each later section date have separate comparisons. A later passage
can add coverage without altering the original comparison. Undated legacy sections use the
work's creation year. Other tome kinds are not compared by this first slice.

## What the comparison means

- The account covers the named commander's engagements in this war up to its writing date.
- Individual passages reference particular engagements. The `Service` passage instead
  references every engagement behind its aggregate totals. An engagement named only there
  is shown as summarized, without an individual account; it is not called wholly absent.
- Later events are the war's recorded events after that writing date. A war that ended later
  is explicitly dated. These events can be about the wider war, beyond the named commander.
- A later rival must be a surviving work about the same commander and war, written after
  this account's latest passage. It must name an additional engagement individually or
  contain the war's ending in a dated aftermath when the earlier account does not.
- Survival includes settlement copies even when the original artifact is lost. A copy's
  manufacture date never advances its account's writing date. The export does not preserve
  separate copy editions or say which continuations reached them, so no such propagation
  is inferred.

The current engine only continues annals and realm chronicles. Separate continuation
comparisons for campaign data are covered by synthetic tests; the measured campaign panel
therefore does not establish naturally generated campaign continuations or competing editions.

## Five-seed measurement

Measured with 1,000 years, eight initial civilizations, size 4096 and raster 64 in the composed
workspace. The existing orientation, hardship and rerun branches were applied. For this
bounded report, “incomplete” means at least one engagement lacking an individual account,
a later war event, or a later war ending. It is a coverage count, not a truth assessment.

| Seed | Campaign tomes | Surviving accounts | Incomplete by this definition | With a later fuller rival |
| --- | ---: | ---: | ---: | ---: |
| 2 | 3 | 3 | 0 | 0 |
| 7 | 7 | 7 | 0 | 0 |
| 11 | 8 | 5 | 0 | 0 |
| 42 | 4 | 4 | 0 | 0 |
| 99 | 13 | 11 | 4 | 0 |

All four incomplete cases are in seed 99 and contain summary-only engagements. No later
rival was observed. The 300-year panel had only two campaign tomes, both in seed 2, with no
incomplete cases. The longer panel makes the distinction observable without changing book
production to force it.

For example, seed 99's `art:64`, *Book of Rodoslava*, was written in 420 about the War of
Bengasniesuo (382–387). It individually describes three of Rodoslava's four engagements.
The Third Battle of Tervolajarvi (`bat:65`, 387) appears in the service summary but has no
individual account. The original is lost in 618, but one copy survives. Its comparison is
still dated 420, rather than the copying year or the final year of the world.

Reproduce the count with:

```bash
node tools/measure_historiography.mjs path/to/seed-2.json path/to/seed-7.json \
  path/to/seed-11.json path/to/seed-42.json path/to/seed-99.json
```

The command emits one JSON summary per supplied world. Synthetic tests separately cover a
war ending after writing, later rival coverage, dated continuations, old undated sections,
and a lost original surviving in copies. The original export is checked for mutation.
