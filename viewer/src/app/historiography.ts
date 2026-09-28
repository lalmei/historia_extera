import type { World } from './store.ts';
import type { Artifact, Battle, EntityId, HistoryEvent, TomeSection, War } from './types.ts';

/** A bounded comparison of a commander's campaign account, not a verdict on its truth. */
export interface CampaignVersion {
  year: number;
  continuation: boolean;
  sections: TomeSection[];
  described: Battle[];
  /** Engagements inside the account's dated horizon, without an individual account. */
  omitted: Battle[];
  /** Of those omissions, engagements still referenced by an aggregate service passage. */
  summarized: Battle[];
  later: HistoryEvent[];
  laterEnding?: number;
  recordsEnding: boolean;
}

export interface CampaignAccount {
  artifact: Artifact;
  war: War;
  subjectId: EntityId;
  survives: boolean;
  versions: CampaignVersion[];
  /** Later surviving works about the same commander in the same war with additional coverage. */
  laterRivals: Artifact[];
}

const cache = new WeakMap<World, CampaignAccount[]>();

/** Uses recorded entity references and dates. Never rewrites or parses the prose into facts. */
export function readHistoriography(world: World): CampaignAccount[] {
  const cached = cache.get(world);
  if (cached) return cached;
  const battlesByWar = new Map<EntityId, Battle[]>();
  for (const battle of world.export.battles) {
    const group = battlesByWar.get(battle.warId) ?? [];
    group.push(battle);
    battlesByWar.set(battle.warId, group);
  }
  const accounts: CampaignAccount[] = [];
  for (const artifact of world.export.artifacts) {
    const contents = artifact.tomeContents;
    if (contents?.kind !== 'Campaign' || !contents.contextId || !contents.sections.length) continue;
    const war = world.byId.get(contents.contextId) as War | undefined;
    if (!war || !('battleIds' in war)) continue;
    const subjectId = contents.subjectId;
    const engagements = (battlesByWar.get(war.id) ?? []).filter(
      (battle) => battle.attackerCommanderId === subjectId || battle.defenderCommanderId === subjectId,
    ).sort((a, b) => a.year - b.year || a.id.localeCompare(b.id));
    const sectionYear = (section: TomeSection) => section.year || artifact.createdYear;
    const years = [...new Set([artifact.createdYear, ...contents.sections.map(sectionYear)])]
      .filter((year) => year >= artifact.createdYear).sort((a, b) => a - b);
    const versions = years.map((year): CampaignVersion => {
      const available = contents.sections.filter((section) => sectionYear(section) <= year);
      // Campaign's Service passage cites every engagement behind its aggregate totals.
      // Those references are evidence of a summary, not an individual account of each battle.
      const individual = new Set(available.filter((section) => section.heading !== 'Service')
        .flatMap((section) => section.references));
      const aggregate = new Set(available.filter((section) => section.heading === 'Service')
        .flatMap((section) => section.references));
      const within = engagements.filter((battle) => battle.year <= year);
      return {
        year,
        continuation: year > artifact.createdYear,
        sections: contents.sections.filter((section) => sectionYear(section) === year),
        described: within.filter((battle) => individual.has(battle.id)),
        omitted: within.filter((battle) => !individual.has(battle.id)),
        summarized: within.filter((battle) => !individual.has(battle.id) && aggregate.has(battle.id)),
        later: world.eventsFor(war.id).filter((event) => event.year > year),
        recordsEnding: war.endYear !== undefined && available.some((section) =>
          section.heading === 'Aftermath' && sectionYear(section) >= war.endYear! && section.references.includes(war.id)),
        laterEnding: war.endYear !== undefined && war.endYear > year ? war.endYear : undefined,
      };
    });
    accounts.push({ artifact, war, subjectId, versions, laterRivals: [],
      survives: artifact.lostYear === undefined || (contents.copies ?? []).some((copy) => copy.lostYear === undefined),
    });
  }
  for (const account of accounts) {
    const last = account.versions.at(-1)!;
    const described = new Set(last.described.map((battle) => battle.id));
    account.laterRivals = accounts.filter((other) => {
      if (!other.survives || other.artifact.id === account.artifact.id || other.war.id !== account.war.id ||
          other.subjectId !== account.subjectId || other.artifact.createdYear <= last.year) return false;
      const newer = other.versions.at(-1)!;
      return newer.described.some((battle) => !described.has(battle.id)) ||
        (!last.recordsEnding && newer.recordsEnding);
    }).map((other) => other.artifact);
  }
  cache.set(world, accounts);
  return accounts;
}
