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
import { useLanguage } from '../i18n/LanguageProvider';
import { translateIn } from '../i18n/translations.ts';
import { href } from '../router';
import {
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
  const { language } = useLanguage();
  const tr = (key: string, values?: Readonly<Record<string, string | number | undefined>>) =>
    translateIn(language, 'knowledge', key, values);
  const index = readKnowledge(world);

  if (index.claims.length === 0) {
    return (
      <div className="space-y-5">
        <PageTitle eyebrow={tr('index')} title={tr('title')} />
        <Panel title={tr('claims')}>
          {world.schema.state === 'older' ? (
            <NotInThisExport what="claims" since={37} version={world.schema.version} />
          ) : (
            <p className="text-sm text-[var(--ink-soft)]">
              {tr('emptyWorld')}
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
        eyebrow={tr('index')}
        title={tr('title')}
        meta={
          <span className="text-[var(--ink-faint)]">
            {tr('subtitle')}
          </span>
        }
      />

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Stat label={tr('readings')} value={index.claims.length} hint={tr('everyClaim')} />
        <Stat
          label={tr('sightings')}
          value={index.observations.toLocaleString()}
          hint={tr('sightingsHint')}
        />
        <Stat
          label={tr('realmsHolding')}
          value={index.hasTransitions ? index.realmsHolding.length : '—'}
          hint={tr('realmsHoldingHint')}
        />
        <Stat
          label={tr('inWriting')}
          value={index.hasTransitions ? index.texts.length : '—'}
          hint={tr('inWritingHint')}
        />
      </div>

      {!index.hasTransitions && (
        // Not the same fact as "nobody held anything". This file was written before the engine
        // recorded where a reading went, and an empty column would read as a quiet world.
        <p className="text-sm text-[var(--ink-faint)]">
          {tr('noTransitions')}
        </p>
      )}

      <Panel title={tr('recordCoverage')}>
        <KnowledgeMatrix world={world} />
        <p className="mt-4 text-xs text-[var(--ink-faint)]">
          {tr('columnExplanation')}
          {!index.columns.includes('Practice') && (
            <>
              {' '}
              {tr('practiceNotRecorded')}
            </>
          )}
        </p>
      </Panel>

      <Panel title={tr('readings')}>
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
  const { language } = useLanguage();
  const index = readKnowledge(world);

  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[520px] border-collapse text-sm">
        <thead>
          <tr className="border-b border-[var(--rule)] text-left">
            <th className="he-label w-32 py-2 pr-4 font-normal" />
            {index.columns.map((column) => (
              <th key={column} className="py-2 pr-4 align-bottom">
                <div className="he-label">
                  {translateIn(language, 'knowledge', `column.${column}`)}
                </div>
                <p className="mt-1 max-w-[22rem] text-xs font-normal text-[var(--ink-faint)]">
                  {translateIn(language, 'knowledge', `columnNote.${column}`)}
                </p>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {index.domains.map((domain) => (
            <tr key={domain} className="border-b border-[var(--rule)] last:border-0 align-top">
              <th className="py-3 pr-4 text-left font-medium">
                {translateIn(language, 'knowledge', `domain.${domain}`)}
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
  const { language } = useLanguage();
  const tr = (key: string, values?: Readonly<Record<string, string | number | undefined>>) =>
    translateIn(language, 'knowledge', key, values);
  if (!cell) return <span className="text-[var(--ink-faint)]">—</span>;

  const earliest = cell.claims[0];
  const latest = cell.claims[cell.claims.length - 1];

  return (
    <div className="space-y-1">
      <p>
        <span className="he-data text-base">{cell.claims.length}</span>{' '}
        <span className="text-[var(--ink-soft)]">
          {tr(cell.claims.length === 1 ? 'reading.one' : 'reading.other')}
        </span>
        {column === 'Observation' && cell.observations > 0 && (
          <span className="text-[var(--ink-faint)]">
            {' '}
            {tr('sightingsFrom', { count: cell.observations.toLocaleString() })}
          </span>
        )}
      </p>
      <p className="text-xs text-[var(--ink-faint)]">
        {earliest.claim.year}
        {latest.claim.year !== earliest.claim.year && `–${latest.claim.year}`}
        {cell.realms > 0 && ` · ${tr(cell.realms === 1 ? 'heldInRealm.one' : 'heldInRealm.other', { count: cell.realms })}`}
      </p>
      <p className="text-sm">
        <a href={claimHref(earliest)} className="text-[var(--accent)] underline">
          {earliest.claim.reading}
        </a>
        {world.nameOf(earliest.claimantId) && (
          <span className="text-[var(--ink-faint)]">
            {' '}
            {tr('firstSetOutBy', { name: world.nameOf(earliest.claimantId) })}
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
  const { language } = useLanguage();
  return (
    <span className={verdictIsAJudgement(claim) ? '' : 'text-[var(--ink-faint)]'}>
      {translateIn(language, 'knowledge', `verdict.${claim.verdict}`)}
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
  const { language } = useLanguage();
  const tr = (key: string, values?: Readonly<Record<string, string | number | undefined>>) =>
    translateIn(language, 'knowledge', key, values);
  const columns: Column<ClaimRecord>[] = [
    {
      key: 'year',
      header: tr('year'),
      align: 'right',
      cell: (record) => record.claim.year,
      sort: (record) => record.claim.year,
    },
    {
      key: 'reading',
      header: tr('reading.label'),
      cell: (record) => (
        <a href={claimHref(record)} className="text-[var(--accent)] underline">
          {record.claim.reading}
        </a>
      ),
      sort: (record) => record.claim.reading,
    },
    {
      key: 'claimant',
      header: tr('setOutBy'),
      cell: (record) => <EntityLink world={world} id={record.claimantId} />,
      sort: (record) => world.nameOf(record.claimantId),
    },
    {
      key: 'realm',
      header: tr('in'),
      cell: (record) => <EntityLink world={world} id={record.claim.realmId} />,
      sort: (record) => (record.claim.realmId ? world.nameOf(record.claim.realmId) : ''),
    },
    {
      key: 'column',
      header: tr('settledBy'),
      cell: (record) => (
        <Badge tone={record.column === 'Observation' ? 'accent' : 'neutral'}>
          {tr(`column.${record.column}`)}
        </Badge>
      ),
      sort: (record) => record.column,
    },
    {
      key: 'verdict',
      header: tr('theSky'),
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
      header: tr('heldIn'),
      align: 'right',
      cell: (record) =>
        record.everHeld.length === 0 ? (
          <span className="text-[var(--ink-faint)]">—</span>
        ) : (
          <span title={record.everHeld.map((id) => world.nameOf(id)).join(', ')}>
            {record.heldNow.length}
            {record.everHeld.length !== record.heldNow.length && (
              <span className="text-[var(--ink-faint)]">{tr('ofCount', { count: record.everHeld.length })}</span>
            )}
          </span>
        ),
      sort: (record) => record.everHeld.length,
    });
  }

  const facets: Facet<ClaimRecord>[] = [
    {
      key: 'column',
      label: tr('settledBy'),
      options: [
        {
          value: 'observation',
          label: tr('column.Observation'),
          match: (record) => record.column === 'Observation',
        },
        { value: 'doctrine', label: tr('column.Doctrine'), match: (record) => record.column === 'Doctrine' },
      ],
    },
    {
      key: 'answer',
      label: tr('theSky'),
      options: [
        {
          value: 'confirmed',
          label: tr('facet.confirmed'),
          match: (record) => record.claim.verdict === 'Confirmed',
        },
        { value: 'refuted', label: tr('facet.refuted'), match: (record) => record.claim.verdict === 'Refuted' },
        {
          value: 'open',
          label: tr('facet.open'),
          match: (record) => !verdictIsAJudgement(record.claim),
        },
      ],
    },
  ];

  if (travels) {
    facets.push({
      key: 'fate',
      label: tr('whatBecameOfIt'),
      options: [
        { value: 'written', label: tr('facet.written'), match: (record) => record.texts.length > 0 },
        { value: 'lost', label: tr('facet.lost'), match: (record) => record.wasLost },
        { value: 'kept', label: tr('facet.stillHeld'), match: (record) => record.heldNow.length > 0 },
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
      placeholder={tr('search')}
      initialSort={{ key: 'year' }}
      emptyMessage={tr('noMatch')}
    />
  );
}

function SettledPanel({ records, world }: { records: ClaimRecord[]; world: World }) {
  const { language } = useLanguage();
  const tr = (key: string, values?: Readonly<Record<string, string | number | undefined>>) =>
    translateIn(language, 'knowledge', key, values);
  const settled = records
    .filter((record) => verdictIsAJudgement(record.claim))
    .sort((a, b) => (a.claim.settledYear ?? 0) - (b.claim.settledYear ?? 0));

  return (
    <Panel title={tr('skyAnswered')}>
      {settled.length === 0 ? (
        <p className="text-sm text-[var(--ink-faint)]">
          {tr('noReadingReachedYear')}
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
                    ? ` · ${tr('livedToHear')}`
                    : ` · ${tr('afterDeath')}`)}
              </p>
            </li>
          ))}
        </ul>
      )}
      {settled.length > 8 && (
        <p className="mt-3 text-xs text-[var(--ink-faint)]">
          {tr('moreInTable', { count: settled.length - 8 })}
        </p>
      )}
    </Panel>
  );
}

function LostPanel({ records, world }: { records: ClaimRecord[]; world: World }) {
  const { language } = useLanguage();
  const tr = (key: string, values?: Readonly<Record<string, string | number | undefined>>) =>
    translateIn(language, 'knowledge', key, values);
  const lost = records
    .map((record) => ({
      record,
      last: record.holdings.filter((holding) => holding.lost).at(-1),
    }))
    .filter((entry): entry is { record: ClaimRecord; last: Holding } => entry.last !== undefined)
    .sort((a, b) => (b.last.toYear ?? 0) - (a.last.toYear ?? 0));

  return (
    <Panel title={tr('letGo')}>
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
                  <EntityLink world={world} id={last.realmId} /> {tr('stoppedHolding')}{' '}
                </>
              ) : (
                tr('stoppedBeingHeld')
              )}
              {last.lost?.cause ?? tr('causeNotRecorded')}
              {record.heldNow.length > 0 &&
                ` · ${tr(record.heldNow.length === 1 ? 'stillHeldIn.one' : 'stillHeldIn.other', {
                  count: record.heldNow.length,
                })}`}
            </p>
          </li>
        ))}
      </ul>
      {lost.length > 8 && (
        <p className="mt-3 text-xs text-[var(--ink-faint)]">{tr('more', { count: lost.length - 8 })}</p>
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
  const { language } = useLanguage();
  const tr = (key: string, values?: Readonly<Record<string, string | number | undefined>>) =>
    translateIn(language, 'knowledge', key, values);
  const index = readKnowledge(world);
  const record = index.byKey.get(claimKey);

  if (!record) {
    const parsed = parseClaimKey(claimKey);
    return (
      <div className="space-y-5">
        <PageTitle eyebrow={tr('title')} title={tr('noSuchReading')} />
        <Panel>
          <p className="text-sm text-[var(--ink-soft)]">
            {tr('missingClaim')}
            {parsed ? (
              <>
                {tr('numberedClaim', { id: parsed.claimId })} <EntityLink world={world} id={parsed.claimantId} />
              </>
            ) : (
              <>{tr('atClaimKey', { key: claimKey })}</>
            )}
            .{' '}
            <a href={href(KNOWLEDGE_PATH)} className="text-[var(--accent)] underline">
              {tr('backToKnowledge')}
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
        eyebrow={`${tr('title')} · ${tr(`domain.${record.domain}`)}`}
        title={claim.reading.replace(/^that /, '')}
        meta={
          <>
            <Badge tone={record.column === 'Observation' ? 'accent' : 'neutral'}>
              {tr(`column.${record.column}`)}
            </Badge>
            <span className="text-[var(--ink-faint)]">
              {subjectLabel(claim)} · {tr('setOutIn', { year: claim.year })}
              {claim.realmId && (
                <>
                  {` ${tr('in')} `}
                  <EntityLink world={world} id={claim.realmId} />
                </>
              )}
            </span>
          </>
        }
      />

      <div className="grid gap-5 lg:grid-cols-3">
        <div className="space-y-5 lg:col-span-2">
          <Panel title={tr('theReading')}>
            <dl>
              <Field label={tr('setOutBy')}>
                <EntityLink world={world} id={record.claimantId} />
                {claimant?.deathYear !== undefined && (
                  <span className="text-[var(--ink-faint)]">
                    {' '}
                    ({claimant.birthYear}–{claimant.deathYear})
                  </span>
                )}
              </Field>
              <Field label={tr('in')}>
                <EntityLink world={world} id={claim.realmId} />
              </Field>
              <Field label={tr('year')}>{claim.year}</Field>
              <Field label={tr('register')}>
                {claim.register === 'Measured'
                  ? tr('register.measured')
                  : tr('register.mythic')}
              </Field>
              {claim.quantity && (
                <Field label={tr('theyStated')}>
                  <span className="he-data">
                    {claim.quantity.value}
                    {claim.quantity.unit === 'Years' &&
                      ` ${tr(claim.quantity.value === 1 ? 'year.one' : 'year.other')}`}
                  </span>
                </Field>
              )}
              <Field label={tr('restsOn')}>
                {claim.restsOnYears.length === 0 ? (
                  <span className="text-[var(--ink-faint)]">
                    {tr('nothingWrittenAsEvidence')}
                  </span>
                ) : (
                  <>
                    {tr('sightingsIn', { years: claim.restsOnYears.join(', ') })}
                    <span className="text-[var(--ink-faint)]">
                      {' '}
                      {tr(claim.restsOnYears.length === 1 ? 'recordsToReason.one' : 'recordsToReason.other', {
                        count: claim.restsOnYears.length,
                      })}
                    </span>
                  </>
                )}
              </Field>
              {claim.predictedYear !== undefined && (
                <Field label={tr('lookedForItIn')}>{claim.predictedYear}</Field>
              )}
            </dl>
          </Panel>

          {record.transitions.length > 0 ? (
            <Panel title={tr('whereItWent')}>
              <DiffusionStrip world={world} record={record} />
            </Panel>
          ) : (
            <Panel title={tr('whereItWent')}>
              {world.schema.version !== null && world.schema.version < 53 ? (
                <NotInThisExport
                  what={tr('datedHoldings')}
                  since={53}
                  version={world.schema.version}
                />
              ) : (
                <p className="text-sm text-[var(--ink-faint)]">
                  {tr('nothingCarried')}
                </p>
              )}
            </Panel>
          )}

          {record.texts.length > 0 && (
            <Panel title={tr('writtenDownIn')}>
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
                            {tr('writtenBy')}
                            <EntityLink world={world} id={text.creatorId} />
                          </>
                        )}
                      </span>
                    </p>
                    <p className="mt-0.5 text-xs text-[var(--ink-faint)]">
                      {text.holderId ? (
                        <>
                          {tr('keptAt')} <EntityLink world={world} id={text.holderId} />
                        </>
                      ) : (
                        `${tr('lost')}${text.lostYear !== undefined ? ` ${tr('inYear', { year: text.lostYear })}` : ''}`
                      )}
                      {(text.tomeContents?.copies?.length ?? 0) > 0 &&
                        ` · ${tr(text.tomeContents!.copies!.length === 1
                          ? 'copiedCount.one'
                          : 'copiedCount.other', { count: text.tomeContents!.copies!.length })}`}
                    </p>
                    {(text.tomeContents?.copies ?? []).length > 0 && (
                      <ul className="mt-1 space-y-0.5 text-xs text-[var(--ink-faint)]">
                        {text.tomeContents!.copies!.map((copy, position) => (
                          <li key={`${copy.year}:${copy.settlementId}:${position}`}>
                            {copy.year} · {tr('copiedTo')}{' '}
                            <EntityLink world={world} id={copy.settlementId} /> from{' '}
                            <EntityLink world={world} id={copy.sourceSettlementId} />
                            {copy.lostYear !== undefined &&
                              ` · ${tr('lostIn', { year: copy.lostYear })}${copy.lostCause ? `, ${copy.lostCause}` : ''}`}
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
          <Panel title={tr('skyMadeOfIt')}>
            <p className="text-sm">
              <VerdictLine claim={claim} />
            </p>
            <p className="mt-2 text-xs text-[var(--ink-faint)]">
              {claim.settledYear === undefined
                ? tr('noVerdict')
                : record.claimant?.deathYear !== undefined && !claim.claimantSawTheAnswer
                  ? tr('claimantDidNotHear', { name: world.nameOf(record.claimantId) })
                  : tr('claimantHeard', { name: world.nameOf(record.claimantId) })}
            </p>
            <p className="mt-3 border-t border-[var(--rule)] pt-3 text-xs text-[var(--ink-faint)]">
              {claim.verdict === 'Refuted'
                ? tr('refutedExplanation')
                : tr('verdictExplanation')}
            </p>
          </Panel>

          <Panel title={tr('heldBy')}>
            {record.everHeld.length === 0 ? (
              <p className="text-sm text-[var(--ink-faint)]">
                {tr('noRealmHeld')}
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
                            ? ` · ${tr(`standing.${last.standing}`)}`
                            : ''}
                          {open ? '' : ` · ${tr('letGoShort')}`}
                        </span>
                      </li>
                    );
                  })}
                </ul>
                <p className="mt-3 border-t border-[var(--rule)] pt-3 text-xs text-[var(--ink-faint)]">
                  {index.hasStandings
                    ? tr('holdingExplanation')
                    : tr('holdingsPredate')}
                </p>
              </>
            )}
          </Panel>

          {rivals.length > 0 && (
            <Panel title={tr('alsoSaidOf', { subject: subjectLabel(claim) })}>
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
                  {tr('more', { count: rivals.length - 10 })}
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
  const { language } = useLanguage();
  const tr = (key: string) => translateIn(language, 'knowledge', key);
  const marks: { year: number; node: React.ReactNode; kind: string }[] = [
    {
      year: record.claim.year,
      kind: tr('diffusion.proposed'),
      node: (
        <>
          {tr('setOutBy')} <EntityLink world={world} id={record.claimantId} />
          {record.claim.realmId && (
            <>
              {` ${tr('in')} `}
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
      kind: tr(transition.kind === 'Acquired' ? 'diffusion.acquired' : 'diffusion.lost'),
      node: <TransitionLine world={world} transition={transition} />,
    });
  }

  if (record.claim.settledYear !== undefined) {
    marks.push({
      year: record.claim.settledYear,
      kind: tr(record.claim.verdict === 'Confirmed' ? 'diffusion.borneOut' : 'diffusion.answered'),
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
  const { language } = useLanguage();
  const tr = (key: string) => translateIn(language, 'knowledge', key);
  const carrier = carrierOf(world, transition);
  const carried = tr(
    transition.carrier === 'Text' ? 'carrier.work' : 'carrier.person',
  );

  return (
    <>
      <EntityLink world={world} id={transition.realmId} />
      {transition.kind === 'Acquired' ? tr('cameToHold') : tr('stoppedHoldingTransition')}
      {transition.carrierId ? (
        <>
          {tr(transition.kind === 'Acquired' ? 'carriedBy' : 'with')}
          <EntityLink world={world} id={transition.carrierId} />
        </>
      ) : (
        <span className="text-[var(--ink-faint)]">
          {transition.kind === 'Acquired'
            ? `${tr('carriedBy')}${carrier.label}`
            : `${tr('with')}${carried}`}
        </span>
      )}
      {transition.settlementId && (
        <span className="text-[var(--ink-faint)]">
          {` ${tr('at')} `}
          <EntityLink world={world} id={transition.settlementId} />
        </span>
      )}
      <span className="text-[var(--ink-faint)]">
        {' · '}{transition.cause ?? tr('causeNotRecorded')}
      </span>
    </>
  );
}
