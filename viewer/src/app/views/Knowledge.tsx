/**
 * The Knowledge section: what people said about the world, and what became of the saying.
 *
 * Deliberately not part of Cosmology. That page is the objective physical description of the
 * generated system — the numbers the world was built from, which nobody in the world knows.
 * This is the opposite register: what was claimed, on what evidence, by whom, and how the sky
 * answered. Folding the second into the first would pollute the page that has to stay
 * trustworthy as the truth table, so the two never share a screen and this one never reads the
 * true value of anything.
 *
 * The rule that shapes every cell below: a verdict is not a belief, and doctrine is not graded.
 * `Refuted` states a claim's relationship to measurable reality at a named year and nothing at
 * all about whether anyone stopped holding it — so a refuted reading with two centuries of
 * copies must read as exactly that, not as a dead one.
 */

import type React from 'react';
import { href } from '../router';
import {
  KNOWLEDGE_COLUMN_LABELS,
  KNOWLEDGE_COLUMN_NOTES,
  KNOWLEDGE_DOMAIN_LABELS,
  carrierOf,
  claimsAboutTheSame,
  parseClaimKey,
  readKnowledge,
  subjectLabel,
  type ClaimRecord,
  type Holding,
  type KnowledgeCell,
  type KnowledgeColumn,
} from '../knowledge';
import { figureOf, type World } from '../store';
import {
  CLAIM_STANDING_LABELS,
  CLAIM_VERDICT_LABELS,
  type Claim,
  type ClaimTransition,
} from '../types';
import {
  Badge,
  DataTable,
  EntityLink,
  Field,
  NotInThisExport,
  PageTitle,
  Panel,
  Stat,
  type Column,
  type Facet,
} from '../components/common';

/** Route prefix for the section, so a claim is linkable without knowing whose life to open. */
export const KNOWLEDGE_PATH = '/knowledge';

export function claimHref(record: ClaimRecord): string {
  return href(`${KNOWLEDGE_PATH}/${record.key}`);
}

/**
 * The section index: what the world has claims about, and every one of them.
 *
 * The matrix is not a tech tree and carries no levels. Its columns say what could settle a
 * thing, and only the rows and columns this export populates appear — at the first slice that
 * is one row and two cells, and that is the honest picture.
 */
export function KnowledgePage({ world }: { world: World }) {
  const index = readKnowledge(world);

  if (index.claims.length === 0) {
    return (
      <div className="space-y-5">
        <PageTitle eyebrow="Index" title="Knowledge" />
        <Panel title="Claims">
          {world.schema.state === 'older' ? (
            <NotInThisExport what="claims" since={37} version={world.schema.version} />
          ) : (
            <p className="text-sm text-[var(--ink-soft)]">
              Nobody in this world wrote down a reading of anything. Observation needs a comet
              somebody lived to see twice, and a scribe in the town that saw it.
            </p>
          )}
        </Panel>
      </div>
    );
  }

  const measured = index.claims.filter((record) => record.column === 'Observation');
  const lost = index.claims.filter((record) => record.wasLost);

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow="Index"
        title="Knowledge"
        meta={
          <span className="text-[var(--ink-faint)]">
            What people said about the world, and what became of the saying
          </span>
        }
      />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label="Readings" value={index.claims.length} hint="Every claim anybody set out" />
        <Stat
          label="Sightings"
          value={index.observations.toLocaleString()}
          hint="Written down, and the evidence every measured reading rests on"
        />
        <Stat
          label="Realms holding"
          value={index.hasTransitions ? index.realmsHolding.length : '—'}
          hint="Realms that ever held one of these readings"
        />
        <Stat
          label="In writing"
          value={index.hasTransitions ? index.texts.length : '—'}
          hint="Works that set a reading down, so it could outlive whoever had it"
        />
      </div>

      {!index.hasTransitions && (
        // Not the same fact as "nobody held anything". This file was written before the engine
        // recorded where a reading went, and an empty column would read as a quiet world.
        <p className="text-sm text-[var(--ink-faint)]">
          This export was written before the engine recorded where a reading travelled, so nothing
          below can say who came to hold one, what carried it, or when it was let go. The readings
          themselves are all here. Run the seed again for the rest.
        </p>
      )}

      <Panel title="What the record covers">
        <KnowledgeMatrix world={world} />
        <p className="mt-4 text-xs text-[var(--ink-faint)]">
          Columns say what could settle a thing, not how right anyone was. Doctrine is
          permanently unanswerable by the sky and is never graded here — a mythic reading is a
          held position with its own patrons, not a wrong answer.
          {!index.columns.includes('Practice') && (
            <>
              {' '}
              Practice — what could actually be done — is not a record this engine writes yet, so
              it has no column.
            </>
          )}
        </p>
      </Panel>

      <Panel title="Readings">
        <ClaimTable world={world} records={index.claims} travels={index.hasTransitions} />
      </Panel>

      {(measured.length > 0 || lost.length > 0) && (
        <div className="grid gap-5 lg:grid-cols-2">
          {measured.length > 0 && <SettledPanel records={measured} world={world} />}
          {lost.length > 0 && <LostPanel records={lost} world={world} />}
        </div>
      )}
    </div>
  );
}

function KnowledgeMatrix({ world }: { world: World }) {
  const index = readKnowledge(world);

  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[520px] border-collapse text-sm">
        <thead>
          <tr className="border-b border-[var(--rule)] text-left">
            <th className="he-label w-32 py-2 pr-4 font-normal" />
            {index.columns.map((column) => (
              <th key={column} className="py-2 pr-4 align-bottom">
                <div className="he-label">{KNOWLEDGE_COLUMN_LABELS[column]}</div>
                <p className="mt-1 max-w-[22rem] text-xs font-normal text-[var(--ink-faint)]">
                  {KNOWLEDGE_COLUMN_NOTES[column]}
                </p>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {index.domains.map((domain) => (
            <tr key={domain} className="border-b border-[var(--rule)] last:border-0 align-top">
              <th className="py-3 pr-4 text-left font-medium">
                {KNOWLEDGE_DOMAIN_LABELS[domain]}
              </th>
              {index.columns.map((column) => (
                <td key={column} className="py-3 pr-4">
                  <MatrixCell world={world} cell={index.cellAt(domain, column)} column={column} />
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function MatrixCell({
  world,
  cell,
  column,
}: {
  world: World;
  cell: KnowledgeCell | undefined;
  column: KnowledgeColumn;
}) {
  if (!cell) return <span className="text-[var(--ink-faint)]">—</span>;

  const earliest = cell.claims[0];
  const latest = cell.claims[cell.claims.length - 1];

  return (
    <div className="space-y-1">
      <p>
        <span className="he-data text-base">{cell.claims.length}</span>{' '}
        <span className="text-[var(--ink-soft)]">
          {cell.claims.length === 1 ? 'reading' : 'readings'}
        </span>
        {column === 'Observation' && cell.observations > 0 && (
          <span className="text-[var(--ink-faint)]">
            {' '}
            · from {cell.observations.toLocaleString()} sightings
          </span>
        )}
      </p>
      <p className="text-xs text-[var(--ink-faint)]">
        {earliest.claim.year}
        {latest.claim.year !== earliest.claim.year && `–${latest.claim.year}`}
        {cell.realms > 0 && ` · held in ${cell.realms} ${cell.realms === 1 ? 'realm' : 'realms'}`}
      </p>
      <p className="text-sm">
        <a href={claimHref(earliest)} className="text-[var(--accent)] underline">
          {earliest.claim.reading}
        </a>
        {world.nameOf(earliest.claimantId) && (
          <span className="text-[var(--ink-faint)]">
            {' '}
            — first set out by {world.nameOf(earliest.claimantId)}
          </span>
        )}
      </p>
    </div>
  );
}

/** Whether a claim's verdict is a judgement at all. Doctrine's never is. */
function verdictIsAJudgement(claim: Claim): boolean {
  return claim.verdict === 'Confirmed' || claim.verdict === 'Refuted';
}

function VerdictLine({ claim }: { claim: Claim }) {
  return (
    <span className={verdictIsAJudgement(claim) ? '' : 'text-[var(--ink-faint)]'}>
      {CLAIM_VERDICT_LABELS[claim.verdict] ?? claim.verdict}
      {claim.settledYear !== undefined && `, ${claim.settledYear}`}
    </span>
  );
}

function ClaimTable({
  world,
  records,
  travels,
}: {
  world: World;
  records: ClaimRecord[];
  /** Whether this export says where a reading went. Without it the fate facet asks nothing. */
  travels: boolean;
}) {
  const columns: Column<ClaimRecord>[] = [
    {
      key: 'year',
      header: 'Year',
      align: 'right',
      cell: (record) => record.claim.year,
      sort: (record) => record.claim.year,
    },
    {
      key: 'reading',
      header: 'Reading',
      cell: (record) => (
        <a href={claimHref(record)} className="text-[var(--accent)] underline">
          {record.claim.reading}
        </a>
      ),
      sort: (record) => record.claim.reading,
    },
    {
      key: 'claimant',
      header: 'Set out by',
      cell: (record) => <EntityLink world={world} id={record.claimantId} />,
      sort: (record) => world.nameOf(record.claimantId),
    },
    {
      key: 'realm',
      header: 'In',
      cell: (record) => <EntityLink world={world} id={record.claim.realmId} />,
      sort: (record) => (record.claim.realmId ? world.nameOf(record.claim.realmId) : ''),
    },
    {
      key: 'column',
      header: 'Settled by',
      cell: (record) => (
        <Badge tone={record.column === 'Observation' ? 'accent' : 'neutral'}>
          {KNOWLEDGE_COLUMN_LABELS[record.column]}
        </Badge>
      ),
      sort: (record) => record.column,
    },
    {
      key: 'verdict',
      header: 'The sky',
      cell: (record) => (
        <span className="text-sm">
          <VerdictLine claim={record.claim} />
        </span>
      ),
      sort: (record) => record.claim.verdict,
    },
  ];

  if (travels) {
    columns.push({
      key: 'held',
      header: 'Held in',
      align: 'right',
      cell: (record) =>
        record.everHeld.length === 0 ? (
          <span className="text-[var(--ink-faint)]">—</span>
        ) : (
          <span title={record.everHeld.map((id) => world.nameOf(id)).join(', ')}>
            {record.heldNow.length}
            {record.everHeld.length !== record.heldNow.length && (
              <span className="text-[var(--ink-faint)]"> of {record.everHeld.length}</span>
            )}
          </span>
        ),
      sort: (record) => record.everHeld.length,
    });
  }

  const facets: Facet<ClaimRecord>[] = [
    {
      key: 'column',
      label: 'Settled by',
      options: [
        {
          value: 'observation',
          label: 'Observation',
          match: (record) => record.column === 'Observation',
        },
        { value: 'doctrine', label: 'Doctrine', match: (record) => record.column === 'Doctrine' },
      ],
    },
    {
      key: 'answer',
      label: 'The sky',
      options: [
        {
          value: 'confirmed',
          label: 'Bore them out',
          match: (record) => record.claim.verdict === 'Confirmed',
        },
        { value: 'refuted', label: 'Did not', match: (record) => record.claim.verdict === 'Refuted' },
        {
          value: 'open',
          label: 'Never answered',
          match: (record) => !verdictIsAJudgement(record.claim),
        },
      ],
    },
  ];

  if (travels) {
    facets.push({
      key: 'fate',
      label: 'What became of it',
      options: [
        { value: 'written', label: 'Written down', match: (record) => record.texts.length > 0 },
        { value: 'lost', label: 'Lost somewhere', match: (record) => record.wasLost },
        { value: 'kept', label: 'Still held', match: (record) => record.heldNow.length > 0 },
      ],
    });
  }

  return (
    <DataTable
      rows={records}
      columns={columns}
      facets={facets}
      searchText={(record) =>
        `${record.claim.reading} ${world.nameOf(record.claimantId)} ${
          record.claim.realmId ? world.nameOf(record.claim.realmId) : ''
        } ${subjectLabel(record.claim)}`
      }
      placeholder="Search readings, claimants, realms…"
      initialSort={{ key: 'year' }}
      emptyMessage="No reading matches that."
    />
  );
}

function SettledPanel({ records, world }: { records: ClaimRecord[]; world: World }) {
  const settled = records
    .filter((record) => verdictIsAJudgement(record.claim))
    .sort((a, b) => (a.claim.settledYear ?? 0) - (b.claim.settledYear ?? 0));

  return (
    <Panel title="What the sky answered">
      {settled.length === 0 ? (
        <p className="text-sm text-[var(--ink-faint)]">
          No measured reading named a year the record reached.
        </p>
      ) : (
        <ul className="space-y-2 text-sm">
          {settled.slice(0, 8).map((record) => (
            <li key={record.key} className="border-l border-[var(--line)] pl-3">
              <p>
                <a href={claimHref(record)} className="text-[var(--accent)] underline">
                  {record.claim.reading}
                </a>
                <span className="text-[var(--ink-faint)]">
                  {' '}
                  — <EntityLink world={world} id={record.claimantId} />, {record.claim.year}
                </span>
              </p>
              <p className="mt-0.5 text-xs text-[var(--ink-faint)]">
                <VerdictLine claim={record.claim} />
                {record.claim.settledYear !== undefined &&
                  (record.claim.claimantSawTheAnswer
                    ? ' · they lived to hear it'
                    : ' · after their death')}
              </p>
            </li>
          ))}
        </ul>
      )}
      {settled.length > 8 && (
        <p className="mt-3 text-xs text-[var(--ink-faint)]">
          and {settled.length - 8} more in the table above.
        </p>
      )}
    </Panel>
  );
}

function LostPanel({ records, world }: { records: ClaimRecord[]; world: World }) {
  const lost = records
    .map((record) => ({
      record,
      last: record.holdings.filter((holding) => holding.lost).at(-1),
    }))
    .filter((entry): entry is { record: ClaimRecord; last: Holding } => entry.last !== undefined)
    .sort((a, b) => (b.last.toYear ?? 0) - (a.last.toYear ?? 0));

  return (
    <Panel title="What was let go">
      <ul className="space-y-2 text-sm">
        {lost.slice(0, 8).map(({ record, last }) => (
          <li key={record.key} className="border-l border-[var(--line)] pl-3">
            <p>
              <a href={claimHref(record)} className="text-[var(--accent)] underline">
                {record.claim.reading}
              </a>
              <span className="text-[var(--ink-faint)]"> — {last.toYear}</span>
            </p>
            <p className="mt-0.5 text-xs text-[var(--ink-faint)]">
              {last.realmId ? (
                <>
                  <EntityLink world={world} id={last.realmId} /> stopped holding it when{' '}
                </>
              ) : (
                'stopped being held when '
              )}
              {last.lost?.carrier === 'Text'
                ? 'the last copy in reach stopped surviving'
                : 'the person carrying it died'}
              {record.heldNow.length > 0 &&
                ` · still held in ${record.heldNow.length} ${
                  record.heldNow.length === 1 ? 'realm' : 'realms'
                }`}
            </p>
          </li>
        ))}
      </ul>
      {lost.length > 8 && (
        <p className="mt-3 text-xs text-[var(--ink-faint)]">and {lost.length - 8} more.</p>
      )}
    </Panel>
  );
}

// ---------------------------------------------------------------------------
// One claim
// ---------------------------------------------------------------------------

/**
 * One reading, what it rests on, and everywhere it went.
 *
 * The page answers four questions in order: what was said, what they had to go on, what the sky
 * made of it, and how anybody else came to hold it. The fourth is the provenance walk — this
 * realm holds it because this copy survived, copied from this text, written by this figure,
 * after these sightings — and it folds transitions rather than inferring anything.
 */
export function ClaimPage({ world, claimKey }: { world: World; claimKey: string }) {
  const index = readKnowledge(world);
  const record = index.byKey.get(claimKey);

  if (!record) {
    const parsed = parseClaimKey(claimKey);
    return (
      <div className="space-y-5">
        <PageTitle eyebrow="Knowledge" title="No such reading" />
        <Panel>
          <p className="text-sm text-[var(--ink-soft)]">
            This chronicle holds no reading{' '}
            {parsed ? (
              <>
                numbered {parsed.claimId} by <EntityLink world={world} id={parsed.claimantId} />
              </>
            ) : (
              <>at {claimKey}</>
            )}
            .{' '}
            <a href={href(KNOWLEDGE_PATH)} className="text-[var(--accent)] underline">
              Back to Knowledge
            </a>
            .
          </p>
        </Panel>
      </div>
    );
  }

  const { claim } = record;
  const claimant = figureOf(world, record.claimantId);
  const rivals = claimsAboutTheSame(index, record).filter((other) => other.key !== record.key);

  return (
    <div className="space-y-5">
      <PageTitle
        eyebrow={`Knowledge · ${KNOWLEDGE_DOMAIN_LABELS[record.domain]}`}
        title={claim.reading.replace(/^that /, '')}
        meta={
          <>
            <Badge tone={record.column === 'Observation' ? 'accent' : 'neutral'}>
              {KNOWLEDGE_COLUMN_LABELS[record.column]}
            </Badge>
            <span className="text-[var(--ink-faint)]">
              {subjectLabel(claim)} · set out in {claim.year}
              {claim.realmId && (
                <>
                  {' in '}
                  <EntityLink world={world} id={claim.realmId} />
                </>
              )}
            </span>
          </>
        }
      />

      <div className="grid gap-5 lg:grid-cols-3">
        <div className="space-y-5 lg:col-span-2">
          <Panel title="The reading">
            <dl>
              <Field label="Set out by">
                <EntityLink world={world} id={record.claimantId} />
                {claimant?.deathYear !== undefined && (
                  <span className="text-[var(--ink-faint)]">
                    {' '}
                    ({claimant.birthYear}–{claimant.deathYear})
                  </span>
                )}
              </Field>
              <Field label="In">
                <EntityLink world={world} id={claim.realmId} />
              </Field>
              <Field label="Year">{claim.year}</Field>
              <Field label="Register">
                {claim.register === 'Measured'
                  ? 'Measured — it states a number the sky can meet or miss'
                  : 'Mythic — it says what the light is for, which nothing measures'}
              </Field>
              {claim.quantity && (
                <Field label="They stated">
                  <span className="he-data">
                    {claim.quantity.value}
                    {claim.quantity.unit === 'Years' &&
                      ` year${claim.quantity.value === 1 ? '' : 's'}`}
                  </span>
                </Field>
              )}
              <Field label="Rests on">
                {claim.restsOnYears.length === 0 ? (
                  <span className="text-[var(--ink-faint)]">
                    Nothing written down — it was not that kind of reading
                  </span>
                ) : (
                  <>
                    Sightings in {claim.restsOnYears.join(', ')}
                    <span className="text-[var(--ink-faint)]">
                      {' '}
                      — {claim.restsOnYears.length}{' '}
                      {claim.restsOnYears.length === 1 ? 'record' : 'records'} to reason over
                    </span>
                  </>
                )}
              </Field>
              {claim.predictedYear !== undefined && (
                <Field label="Looked for it in">{claim.predictedYear}</Field>
              )}
            </dl>
          </Panel>

          {record.transitions.length > 0 ? (
            <Panel title="Where it went">
              <DiffusionStrip world={world} record={record} />
            </Panel>
          ) : (
            <Panel title="Where it went">
              {world.schema.version !== null && world.schema.version < 53 ? (
                <NotInThisExport
                  what="dated holdings"
                  since={53}
                  version={world.schema.version}
                />
              ) : (
                <p className="text-sm text-[var(--ink-faint)]">
                  Nothing carried this reading anywhere. It stayed with whoever set it out.
                </p>
              )}
            </Panel>
          )}

          {record.texts.length > 0 && (
            <Panel title="Written down in">
              <ul className="space-y-3 text-sm">
                {record.texts.map((text) => (
                  <li key={text.id} className="border-l border-[var(--line)] pl-3">
                    <p>
                      <EntityLink world={world} id={text.id} />
                      <span className="text-[var(--ink-faint)]">
                        {' '}
                        · {text.createdYear}
                        {text.creatorId && (
                          <>
                            {', written by '}
                            <EntityLink world={world} id={text.creatorId} />
                          </>
                        )}
                      </span>
                    </p>
                    <p className="mt-0.5 text-xs text-[var(--ink-faint)]">
                      {text.holderId ? (
                        <>
                          Kept at <EntityLink world={world} id={text.holderId} />
                        </>
                      ) : (
                        `Lost${text.lostYear !== undefined ? ` in ${text.lostYear}` : ''}`
                      )}
                      {(text.tomeContents?.copies?.length ?? 0) > 0 &&
                        ` · copied ${text.tomeContents!.copies!.length} time${
                          text.tomeContents!.copies!.length === 1 ? '' : 's'
                        }`}
                    </p>
                    {(text.tomeContents?.copies ?? []).length > 0 && (
                      <ul className="mt-1 space-y-0.5 text-xs text-[var(--ink-faint)]">
                        {text.tomeContents!.copies!.map((copy, position) => (
                          <li key={`${copy.year}:${copy.settlementId}:${position}`}>
                            {copy.year} · copied to{' '}
                            <EntityLink world={world} id={copy.settlementId} /> from{' '}
                            <EntityLink world={world} id={copy.sourceSettlementId} />
                            {copy.lostYear !== undefined &&
                              ` · lost in ${copy.lostYear}${copy.lostCause ? `, ${copy.lostCause}` : ''}`}
                          </li>
                        ))}
                      </ul>
                    )}
                  </li>
                ))}
              </ul>
            </Panel>
          )}
        </div>

        <div className="space-y-5">
          <Panel title="What the sky made of it">
            <p className="text-sm">
              <VerdictLine claim={claim} />
            </p>
            <p className="mt-2 text-xs text-[var(--ink-faint)]">
              {claim.settledYear === undefined
                ? 'A verdict is what happened in the sky, not what anybody believed. This one has none.'
                : record.claimant?.deathYear !== undefined && !claim.claimantSawTheAnswer
                  ? `${world.nameOf(record.claimantId)} did not live to hear it.`
                  : `${world.nameOf(record.claimantId)} lived to hear it.`}
            </p>
            <p className="mt-3 border-t border-[var(--rule)] pt-3 text-xs text-[var(--ink-faint)]">
              {claim.verdict === 'Refuted'
                ? 'The sky answering says nothing about who went on holding it. Whoever kept teaching this after that year is below, and is not wrong for it.'
                : 'What the sky settled and what anybody believed are separate records. Nothing on this page grades a held position.'}
            </p>
          </Panel>

          <Panel title="Held by">
            {record.everHeld.length === 0 ? (
              <p className="text-sm text-[var(--ink-faint)]">
                No realm is recorded as having held it.
              </p>
            ) : (
              <>
                <ul className="space-y-1.5 text-sm">
                  {record.everHeld.map((realmId) => {
                    const spans = record.holdings.filter((holding) => holding.realmId === realmId);
                    const open = spans.some((holding) => holding.toYear === undefined);
                    const last = spans.at(-1);
                    return (
                      <li key={realmId} className="flex items-baseline justify-between gap-3">
                        <EntityLink world={world} id={realmId} />
                        <span className="he-data text-xs text-[var(--ink-faint)]">
                          {spans
                            .map((holding) =>
                              holding.toYear === undefined
                                ? `${holding.fromYear}–`
                                : `${holding.fromYear}–${holding.toYear}`,
                            )
                            .join(', ')}
                          {index.hasStandings && last
                            ? ` · ${CLAIM_STANDING_LABELS[last.standing]}`
                            : ''}
                          {open ? '' : ' · let go'}
                        </span>
                      </li>
                    );
                  })}
                </ul>
                <p className="mt-3 border-t border-[var(--rule)] pt-3 text-xs text-[var(--ink-faint)]">
                  {index.hasStandings
                    ? 'Having a reading and holding with it are two facts. A realm can keep one it argues with, or shelve one the sky confirmed.'
                    : 'This export predates the record of what a realm made of a reading; these are realms that had it.'}
                </p>
              </>
            )}
          </Panel>

          {rivals.length > 0 && (
            <Panel title={`Also said of ${subjectLabel(claim)}`}>
              <ul className="space-y-2 text-sm">
                {rivals.slice(0, 10).map((other) => (
                  <li key={other.key}>
                    <a href={claimHref(other)} className="text-[var(--accent)] underline">
                      {other.claim.reading}
                    </a>
                    <span className="text-[var(--ink-faint)]">
                      {' '}
                      — {other.claim.year}
                      {other.claim.realmId && (
                        <>
                          {', '}
                          <EntityLink world={world} id={other.claim.realmId} />
                        </>
                      )}
                    </span>
                  </li>
                ))}
              </ul>
              {rivals.length > 10 && (
                <p className="mt-2 text-xs text-[var(--ink-faint)]">
                  and {rivals.length - 10} more.
                </p>
              )}
            </Panel>
          )}
        </div>
      </div>
    </div>
  );
}

/**
 * The life of one reading, in the order it happened.
 *
 * Every mark names the carrier that caused it, because a claim in a realm nothing carried it to
 * is the failure the transition record exists to make impossible.
 */
function DiffusionStrip({ world, record }: { world: World; record: ClaimRecord }) {
  const marks: { year: number; node: React.ReactNode; kind: string }[] = [
    {
      year: record.claim.year,
      kind: 'Proposed',
      node: (
        <>
          Set out by <EntityLink world={world} id={record.claimantId} />
          {record.claim.realmId && (
            <>
              {' in '}
              <EntityLink world={world} id={record.claim.realmId} />
            </>
          )}
        </>
      ),
    },
  ];

  for (const transition of record.transitions) {
    marks.push({
      year: transition.year,
      kind: transition.kind === 'Acquired' ? 'Acquired' : 'Lost',
      node: <TransitionLine world={world} transition={transition} />,
    });
  }

  if (record.claim.settledYear !== undefined) {
    marks.push({
      year: record.claim.settledYear,
      kind: record.claim.verdict === 'Confirmed' ? 'Borne out' : 'Answered',
      node: <VerdictLine claim={record.claim} />,
    });
  }

  marks.sort((a, b) => a.year - b.year);

  return (
    <ol className="space-y-2.5 text-sm">
      {marks.map((mark, position) => (
        <li key={`${mark.year}:${position}`} className="flex gap-3">
          <span className="he-data w-12 shrink-0 text-right text-[var(--ink-faint)]">
            {mark.year}
          </span>
          <span className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-[var(--rule)]" />
          <span className="min-w-0">
            <span className="he-label mr-2">{mark.kind}</span>
            {mark.node}
          </span>
        </li>
      ))}
    </ol>
  );
}

function TransitionLine({
  world,
  transition,
}: {
  world: World;
  transition: ClaimTransition;
}) {
  const carrier = carrierOf(world, transition);
  const carried =
    transition.carrier === 'Text' ? 'the work that carried it' : 'the person carrying it';

  return (
    <>
      <EntityLink world={world} id={transition.realmId} />
      {transition.kind === 'Acquired' ? ' came to hold it — ' : ' stopped holding it — '}
      {transition.carrierId ? (
        <>
          {transition.kind === 'Acquired' ? 'carried by ' : 'with '}
          <EntityLink world={world} id={transition.carrierId} />
        </>
      ) : (
        <span className="text-[var(--ink-faint)]">
          {transition.kind === 'Acquired' ? `carried by ${carrier.label}` : `with ${carried}`}
        </span>
      )}
      {transition.settlementId && (
        <span className="text-[var(--ink-faint)]">
          {' at '}
          <EntityLink world={world} id={transition.settlementId} />
        </span>
      )}
    </>
  );
}
