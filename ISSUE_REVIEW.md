# GitHub issue review — 5 September 2026

Reviewed all 17 open issues against the composed workspace and GitHub `main`
(`0451dbb0a832d3e940bed8ca9605913a8eb68548`). Existing local branches and
uncommitted work are not evidence that a fix has shipped. This review leaves
GitHub issue state unchanged.

Implementation checkpoints are on `leo/issue-review-batch`, stacked above the
existing `leo/betrayal-record`. Validation and the golden refer to the composed
workspace, including its pre-existing posthumous-occupation fix; they do not
claim an isolated checkout of this branch is the complete integration.

## This batch

| Issue | Result | Evidence |
| --- | --- | --- |
| [#191](https://github.com/lalmei/historia_extera/issues/191) Figure discovery | Implemented significance sorting and band facets, plus five leading lives on the overview. | `viewer/src/app/discovery.ts`, `discovery.test.ts`, and `views/Lists.tsx`. Scores agree with the life page at its last living year. |
| [#185](https://github.com/lalmei/historia_extera/issues/185) Slow structural tests | Both invariants now use their existing five-seed panels at 300 years. | `TerritoryTests.NoLandIsHeldByARealmThatHasEnded` still requires an actual fallen realm; `SettlementHierarchyTests.NoSettlementHoldsMoreThanItsPeak` still checks inhabited and abandoned settlements. The millennium distribution test is unchanged. |
| [#183](https://github.com/lalmei/historia_extera/issues/183) Acquaintance event volume | Meetings remain in shared affinity acts and bonds; their duplicate chronicle entries are omitted. Later rungs and endings retain their events. | `Affinities.Consider` and `AcquaintanceRecordTests`; five seeds exercise actual meetings and later rungs. Existing exports remain readable. |

### Discovery decision and measurements

The score stays a viewer interpretation. The export already indexes events by
entity, so discovery need not scan the entire chronicle for each figure. It
buckets artifact ownership once and caches the completed ranking per loaded
world with a `WeakMap`; sorting and filtering reuse it. This deliberately does
**not** add an engine score or an export-schema field proposed in #191.

Profiling found repeated locale formatting was the largest avoidable cost.
Reusing one `Intl.NumberFormat` also speeds up ordinary biography prose without
changing its wording. In the final measured saved-world run, indexing took
114 ms for 4,407 figures, 127 ms for 9,808, and 500 ms for 21,490. These are local
measurements, not a performance guarantee. Each cached score was compared with
the individual life-page calculation. A synthetic older-schema case runs even
when no saved worlds are present and excludes mentions after death.

### Chronicle measurement

Seed 42, 300 years, eight civilizations: **16,413 → 14,489 events**, removing
exactly 1,924 acquaintance entries (11.7%). Figure, settlement, civilization,
war, battle, route, and yearly-series exports were unchanged. Every retained
event matched its previous contents except for its renumbered ID. The prior
fingerprint matched before the change; the new golden is regenerated for this
intentional event-log change.

| Seed | Meetings retained in affinity acts | Later rung acts | Chronicle events after change |
| --- | ---: | ---: | ---: |
| 2 | 1,480 | 1,722 | 11,906 |
| 7 | 1,666 | 1,744 | 19,937 |
| 11 | 1,943 | 2,159 | 15,672 |
| 42 | 1,924 | 2,076 | 14,489 |
| 99 | 1,290 | 1,363 | 18,179 |

## Existing implementation

| Issue | Actual state | Evidence and remaining boundary |
| --- | --- | --- |
| [#176](https://github.com/lalmei/historia_extera/issues/176) Friendship panel | On GitHub main. | `EntityPages.Friendship`, `affinityAt`, typed labels and schema-gap handling show dated acts, endings, and both sides of betrayal. |
| [#174](https://github.com/lalmei/historia_extera/issues/174) Wrongs between peers | On GitHub main. | `Offices.NotePassedOver` writes `OfficePassedOver` and opens `PassedOverForOffice` disputes; the affinity seed panel covers resulting betrayals. |
| [#159](https://github.com/lalmei/historia_extera/issues/159) Journey duration | On GitHub main. | `Journey.DurationDays`, dated returns, configurable pace, road-at-departure tests, and wintering-over guards are implemented. |
| [#114](https://github.com/lalmei/historia_extera/issues/114) Dedications cite deeds | Dedication correctness is on GitHub main. | `HolySites.RecordedDeed` and `FlavourTests.DedicationsCiteARecordedDeedOrNameTheirLegend` enforce recorded evidence or explicit legend. New craft/composition undertakings remain #144. |
| [#193](https://github.com/lalmei/historia_extera/issues/193) Keyboard life arc | Existing local branch `leo/life-arc-keyboard`. | Slider semantics, keyboard handling, focus treatment, and density-bar event counts are already implemented. Not this batch's work. |
| [#192](https://github.com/lalmei/historia_extera/issues/192) Rulers lived under | Existing [PR #194](https://github.com/lalmei/historia_extera/pull/194), `leo/rulers-lived-under`. | `accessionsLivedUnder` reads residence spans and `Timeline.realmAt`. Not merged at review time. |
| [#184](https://github.com/lalmei/historia_extera/issues/184) Durable betrayal | Primary record exists on local `leo/betrayal-record`. | `FigureBetrayal`, shared storage, export, and viewer are implemented. The issue's secondary proposal for a betrayal-specific personal-quarrel cause remains open. |

## Unfinished work

| Issue | Missing work / next dependency |
| --- | --- |
| [#190](https://github.com/lalmei/historia_extera/issues/190) Bond history | Dated bond readings and replay; current bonds only carry their latest dimensions, so historical views still hide later mutations. Requires coordinated engine/export/viewer work. |
| [#175](https://github.com/lalmei/historia_extera/issues/175) Courtship | A courtship ladder, marriage selection consuming affection/trust, and displaced-lover outcomes. `HouseholdSystem.FindPartner` still finishes with a random candidate. |
| [#113](https://github.com/lalmei/historia_extera/issues/113) Road itineraries | Ordered route IDs, intermediate settlement nodes, a shared deterministic route-graph search, and a measured waypoint consumer. Road-derived duration is implemented, but a journey still has endpoints rather than a stored path. |
| [#115](https://github.com/lalmei/historia_extera/issues/115) Campaign marches | Route-aware levy reach and recorded marches. Reuse #113's pathfinder rather than invent a second one. |
| [#144](https://github.com/lalmei/historia_extera/issues/144) Makers' undertakings | Scribe compositions and guild commissions driven by motives, with terminal outcomes and linked products. Existing tome commissions are not these figure undertakings. |
| [#148](https://github.com/lalmei/historia_extera/issues/148) Transmitted knowledge | Claim-bearing copies, transport provenance, continuation, loss and divergence. Existing tome copies do not carry the claim model. |
| [#149](https://github.com/lalmei/historia_extera/issues/149) Other knowledge domains | Explicitly waits for #148 to close the astronomy transmission/loss loop; it is a candidate list, not an implementation-ready milestone. |

## Validation

- The two shortened structural tests passed together in 24 seconds before the
  chronicle change. This is not a controlled speed comparison with the older
  timing quoted in #185.
- All 22 focused affinity, acquaintance, and determinism tests passed.
- All 28 viewer tests and the production build passed; Astro check reported
  zero errors and three existing unused-symbol hints.
- Running browser checks verified default significance ordering, the
  Influential facet (42 people), combined Influential + Still living facets
  (two people), and the overview's five linked lives and contributing reasons. Reverse sorting
  and the figure controls were also exercised at 390 px width.
- Full engine suite: **531 passed, zero failures**, in 16.04 minutes. The
  unchanged millennium distribution check, `MostSettlementsAreSmallerThanTowns`,
  took 11 minutes 41 seconds and is now the longest test. Its duration remains
  justified by the distribution it tests.
- Browser verification does not claim a rebuilt or restarted packaged macOS app.
