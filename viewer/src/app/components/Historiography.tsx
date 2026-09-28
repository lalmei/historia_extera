import { readHistoriography } from '../historiography';
import type { World } from '../store';
import type { Artifact, Battle } from '../types';
import { EntityLink, Panel } from './common';
import { EventList } from './EventList';

export function Historiography({ world, artifact }: { world: World; artifact: Artifact }) {
  const account = readHistoriography(world).find((item) => item.artifact.id === artifact.id);
  if (!account) return null;
  return (
    <Panel title="The account and the record">
      <p className="text-sm text-[var(--ink-soft)]">
        This account follows <EntityLink world={world} id={account.subjectId} /> in{' '}
        <EntityLink world={world} id={account.war.id} />, beginning in {account.war.startYear}.
        The comparison uses dated passages and their references; it does not grade the text's accuracy.
        Service totals can summarize engagements that have no individual description.
      </p>
      {account.versions.map((version) => (
        <section key={version.year} className="mt-4 space-y-2 border-l border-[var(--line)] pl-3">
          <h3 className="font-medium">
            {version.continuation ? 'Continuation entered' : 'Original account written'} {version.year}
          </h3>
          <p className="text-xs text-[var(--ink-faint)]">
            {version.continuation ? 'Coverage including the earlier passages. Added passages: ' : 'Passages: '}
            {version.sections.map((section) => section.heading).join('; ')}.
          </p>
          <Engagements world={world} label="Individually referenced by this date" battles={version.described} />
          <Engagements world={world} label="Within these years, without an individual account" battles={version.omitted} />
          {version.summarized.length > 0 && (
            <p className="text-xs text-[var(--ink-faint)]">
              {version.summarized.length === 1 ? 'This engagement appears' : `${version.summarized.length} of these appear`} in the service summary.
            </p>
          )}
          {version.laterEnding !== undefined && (
            <p className="text-sm">Written in {version.year}; the war ran to {version.laterEnding}.</p>
          )}
          <details>
            <summary className="cursor-pointer text-sm">
              Later record of this war · {version.later.length} events after {version.year}
            </summary>
            <EventList world={world} events={version.later} />
          </details>
        </section>
      ))}
      {account.laterRivals.length > 0 && (
        <div className="mt-4 text-sm">
          Later surviving accounts of the same commander and war with additional coverage:
          <ul className="mt-1 space-y-1">
            {account.laterRivals.map((rival) => (
              <li key={rival.id}><EntityLink world={world} id={rival.id} /> · written {rival.createdYear}</li>
            ))}
          </ul>
        </div>
      )}
      {(artifact.tomeContents?.copies?.length ?? 0) > 0 && (
        <p className="mt-4 text-xs text-[var(--ink-faint)]">
          Copying dates below are not new dates of authorship. A later copy does not make the original
          account later. The record does not say which continuations reached each copy.
        </p>
      )}
    </Panel>
  );
}

function Engagements({ world, label, battles }: { world: World; label: string; battles: Battle[] }) {
  return (
    <div className="text-sm">
      <p>{label}: {battles.length === 0 ? 'none recorded' : battles.length}.</p>
      {battles.length > 0 && <ul className="mt-1 space-y-1">
        {battles.map((battle) => <li key={battle.id}>
          {battle.year} · <EntityLink world={world} id={battle.id} />
        </li>)}
      </ul>}
    </div>
  );
}
