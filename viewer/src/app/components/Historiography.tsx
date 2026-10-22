import { translateIn } from '../i18n/translations.ts';
import { useLanguage } from '../i18n/LanguageProvider';
import { readHistoriography } from '../historiography';
import type { World } from '../store';
import type { Artifact, Battle } from '../types';
import { EntityLink, Panel } from './common';
import { EventList } from './EventList';

export function Historiography({ world, artifact }: { world: World; artifact: Artifact }) {
  const { language } = useLanguage();
  const account = readHistoriography(world).find((item) => item.artifact.id === artifact.id);
  if (!account) return null;

  return (
    <Panel title={translateIn(language, 'inline', "The account and the record")}>
      <p className="text-sm text-[var(--ink-soft)]">
        {translateIn(language, 'historiography', 'account.beforeSubject')}
        <EntityLink world={world} id={account.subjectId} />
        {translateIn(language, 'historiography', 'account.betweenEntities')}
        <EntityLink world={world} id={account.war.id} />
        {translateIn(language, 'historiography', 'account.afterWar', { year: account.war.startYear })}
      </p>

      {account.versions.map((version) => (
        <section key={version.year} className="mt-4 space-y-2 border-l border-[var(--line)] pl-3">
          <h3 className="font-medium">
            {translateIn(
              language,
              'historiography',
              version.continuation ? 'continuationEntered' : 'originalWritten',
            )}
            {' '}{version.year}
          </h3>
          <p className="text-xs text-[var(--ink-faint)]">
            {translateIn(
              language,
              'historiography',
              version.continuation ? 'continuationSections' : 'sections',
            )}
            {version.sections.map((section) => section.heading).join('; ')}.
          </p>
          <Engagements
            world={world}
            label={translateIn(language, 'inline', "Individually referenced by this date")}
            battles={version.described}
          />
          <Engagements
            world={world}
            label={translateIn(language, 'inline', "Within these years, without an individual account")}
            battles={version.omitted}
          />
          {version.summarized.length > 0 && (
            <p className="text-xs text-[var(--ink-faint)]">
              {translateIn(
                language,
                'historiography',
                version.summarized.length === 1 ? 'oneSummarizedEngagement' : 'manySummarizedEngagements',
                { count: version.summarized.length },
              )}
            </p>
          )}
          {version.laterEnding !== undefined && (
            <p className="text-sm">
              {translateIn(language, 'inline', "Written in {v0}; the war ran to {v1}.", { v0: version.year, v1: version.laterEnding })}
            </p>
          )}
          <details>
            <summary className="cursor-pointer text-sm">
              {translateIn(language, 'inline', "Later record of this war · {e0} events after {e1}", { e0: version.later.length, e1: version.year, f0: version.later.length, f1: version.later.length === 1 ? '' : 's', f2: version.year })}
            </summary>
            <EventList world={world} events={version.later} />
          </details>
        </section>
      ))}

      {account.laterRivals.length > 0 && (
        <div className="mt-4 text-sm">
          {translateIn(language, 'inline', "Later surviving accounts of the same commander and war with additional coverage:")}
          <ul className="mt-1 space-y-1">
            {account.laterRivals.map((rival) => (
              <li key={rival.id}>
                <EntityLink world={world} id={rival.id} /> · {translateIn(language, 'inline', "written")} {rival.createdYear}
              </li>
            ))}
          </ul>
        </div>
      )}
      {(artifact.tomeContents?.copies?.length ?? 0) > 0 && (
        <p className="mt-4 text-xs text-[var(--ink-faint)]">
          {translateIn(language, 'historiography', 'copyingNote')}
        </p>
      )}
    </Panel>
  );
}

function Engagements({ world, label, battles }: { world: World; label: string; battles: Battle[] }) {
  const { language } = useLanguage();

  return (
    <div className="text-sm">
      <p>
        {label}{translateIn(language, 'inline', ": ")}
        {battles.length === 0 ? (translateIn(language, 'inline', "none recorded")) : battles.length}.
      </p>
      {battles.length > 0 && (
        <ul className="mt-1 space-y-1">
          {battles.map((battle) => (
            <li key={battle.id}>
              {battle.year} · <EntityLink world={world} id={battle.id} />
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
