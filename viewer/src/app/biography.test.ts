import assert from 'node:assert/strict';
import test from 'node:test';
import {
  affinityAt,
  buildBiographyEpisodes,
  buildConstellation,
  buildLifeArc,
  groupJourneys,
  historicalSignificance,
  knownFor,
  plotAt,
  ripplesAfter,
  standingAt,
  undertakingAt,
  visibleBondAt,
  visibleMemoryAt,
  type LifeContext,
} from './biography.ts';
import type {
  Affinity,
  Figure,
  FigureBond,
  HistoryEvent,
  Journey,
  Plot,
  SalientMemory,
  Undertaking,
} from './types.ts';

test('routine journeys aggregate without swallowing a notable outcome', () => {
  const journeys: Journey[] = [
    trip(12, 'Returned'),
    trip(10, 'Returned'),
    trip(13, 'Waylaid'),
    trip(18, 'Returned'),
  ];

  const groups = groupJourneys(journeys, 15);
  assert.equal(groups.length, 2);
  assert.deepEqual(groups[0].journeys.map((journey) => journey.year), [10, 12]);
  assert.equal(groups[0].firstYear, 10);
  assert.equal(groups[0].lastYear, 12);
  assert.equal(groups[1].journeys[0].outcome, 'Waylaid');
});

test('plot visibility waits for public knowledge and hides a future ending', () => {
  const targetPlot = plot('Target');
  assert.equal(plotAt(targetPlot, 'fig:2', 14), undefined);
  assert.equal(plotAt(targetPlot, 'fig:2', 15)?.outcome, 'Ongoing');
  assert.equal(plotAt(targetPlot, 'fig:2', 18)?.outcome, 'Failed');

  const memberPlot = plot('Member');
  assert.equal(plotAt(memberPlot, 'fig:3', 11), undefined);
  assert.equal(plotAt(memberPlot, 'fig:3', 12), undefined);
  assert.equal(plotAt(memberPlot, 'fig:3', 15)?.outcome, 'Ongoing');

  const neverPublic = { ...memberPlot, publicYear: undefined };
  assert.equal(plotAt(neverPublic, 'fig:3', 18), undefined);
});

test('a future bond mutation is not leaked into an earlier biography', () => {
  const bond = {
    sinceYear: 4,
    lastChangedYear: 9,
  } as FigureBond;
  assert.equal(visibleBondAt(bond, 8), false);
  assert.equal(visibleBondAt(bond, 9), true);
});

test('future memory reinforcement and undertaking outcomes stay hidden', () => {
  const memory = { year: 6, lastReinforcedYear: 11 } as SalientMemory;
  assert.equal(visibleMemoryAt(memory, 10), false);
  assert.equal(visibleMemoryAt(memory, 11), true);

  const undertaking = {
    startYear: 8,
    endYear: 14,
    state: 'Succeeded',
    outcome: 'reached the destination',
    requiredProgress: 2,
    progress: 2,
    steps: [
      { year: 9 },
      { year: 14 },
    ],
  } as Undertaking;
  const earlier = undertakingAt(undertaking, 10);
  assert.equal(earlier?.state, 'Active');
  assert.equal(earlier?.outcome, undefined);
  assert.deepEqual(earlier?.steps.map((step) => step.year), [9]);
  assert.equal(undertakingAt(undertaking, 14)?.outcome, 'reached the destination');
});

test('episode selection is deterministic and retains source event ids', () => {
  const figure = {
    id: 'fig:1',
    undertakings: [
      {
        id: 3,
        kind: 'Pilgrimage',
        state: 'Succeeded',
        startYear: 20,
        endYear: 23,
        objective: 'reach the shrine',
        destinationId: 'hol:1',
        progress: 2,
        requiredProgress: 2,
        motive: 'Journey',
        motiveSourceKind: 'JourneyMade',
        deadlineYear: 25,
        lastProgressYear: 23,
        participantIds: [],
        steps: [
          {
            year: 21,
            sourceKind: 'JourneyMade',
            placeId: 'set:2',
            outcome: 'arrived',
          },
        ],
      },
    ],
    disputes: [],
    plots: [],
    campaigns: [],
  } as unknown as Figure;
  const events = [
    {
      id: 41,
      year: 21,
      kind: 'JourneyMade',
      significance: 'Routine',
      subject: 'fig:1',
      object: 'hol:1',
    },
  ] as HistoryEvent[];

  const episodes = buildBiographyEpisodes(figure, events, 23);
  assert.equal(episodes.length, 1);
  assert.equal(episodes[0].key, 'undertaking:3:20');
  assert.deepEqual(episodes[0].sourceEventIds, [41]);
  assert.deepEqual(episodes[0].relatedIds, ['hol:1']);
});

function trip(year: number, outcome: Journey['outcome']): Journey {
  return {
    kind: 'Trade',
    year,
    day: 0,
    fromSettlementId: 'set:1',
    toSettlementId: 'set:2',
    viaId: 'rte:1',
    durationDays: 40,
    outcome,
    returnSettlementId: 'set:1',
    returnYear: year,
    returnDay: 40,
  };
}

function plot(viewpoint: Plot['viewpoint']): Plot {
  return {
    id: 1,
    leaderId: 'fig:1',
    targetId: 'fig:2',
    viewpoint,
    objective: 'Assassinate',
    cause: 'OfficeRevoked',
    sourceKind: 'OfficeRevoked',
    phase: 'Ready',
    outcome: 'Failed',
    resolution: 'the attempt missed',
    startYear: 10,
    endYear: 18,
    publicYear: 15,
    progress: 2,
    requiredProgress: 2,
    secrecy: 0.3,
    suspicion: 0.7,
    access: 0.8,
    members: [
      {
        figureId: 'fig:3',
        joinedYear: 12,
        tie: 'TrustInLeader',
        witting: true,
      },
    ],
    acts: [
      {
        year: 10,
        sourceKind: 'OfficeRevoked',
        phase: 'Gathering',
        actorId: 'fig:1',
        detail: 'resolved on an attempt',
        known: false,
      },
      {
        year: 16,
        sourceKind: 'UndertakingStarted',
        phase: 'Access',
        actorId: 'fig:1',
        detail: 'moved a step closer',
        known: false,
      },
      {
        year: 18,
        sourceKind: 'ConspiracyAttempted',
        phase: 'Ready',
        actorId: 'fig:1',
        detail: 'the attempt missed',
        known: true,
      },
    ],
  };
}

test('a friendship read before its ending carries neither the ending nor the betrayal', () => {
  const friendship = {
    id: 1,
    otherId: 'fig:2',
    sought: true,
    origin: 'SharedResidence',
    stage: 'Friendship',
    outcome: 'Betrayed',
    resolution: 'one of them turned on the other',
    betrayerId: 'fig:2',
    placeId: 'set:9',
    startYear: 10,
    endYear: 20,
    lastActionYear: 20,
    acts: [
      { year: 10, stage: 'Acquaintance', detail: 'they lived in the same town' },
      { year: 14, stage: 'Kindness', detail: 'a good turn was done' },
      { year: 20, stage: 'Friendship', detail: 'turned on them' },
    ],
  } as Affinity;

  assert.equal(affinityAt(friendship, 9), undefined);

  const during = affinityAt(friendship, 15);
  assert.equal(during?.outcome, 'Open');
  assert.equal(during?.stage, 'Kindness');
  assert.equal(during?.betrayerId, undefined);
  assert.equal(during?.resolution, undefined);
  // The town is where the turn happened, and the turn has not happened yet.
  assert.equal(during?.placeId, undefined);
  assert.equal(during?.lastActionYear, 14);
  assert.deepEqual(during?.acts.map((act) => act.year), [10, 14]);

  const after = affinityAt(friendship, 20);
  assert.equal(after?.outcome, 'Betrayed');
  assert.equal(after?.betrayerId, 'fig:2');
  assert.equal(after?.acts.length, 3);
});

// ---------------------------------------------------------------------------
// The whole-life derivations.
// ---------------------------------------------------------------------------

function person(overrides: Partial<Figure>): Figure {
  return {
    id: 'fig:1',
    name: 'Kullerwa',
    sex: 'Female',
    civilizationId: 'civ:1',
    cultureId: 'cul:1',
    birthYear: 10,
    deathYear: undefined,
    deathCause: 'Unknown',
    origin: 'Unrecorded',
    residences: [],
    titles: [],
    service: [],
    campaigns: [],
    journeys: [],
    bonds: [],
    memories: [],
    injuries: [],
    undertakings: [],
    disputes: [],
    affinities: [],
    plots: [],
    guardianships: [],
    mentorships: [],
    observations: [],
    claims: [],
    childIds: [],
    spouseIds: [],
    ...overrides,
  } as Figure;
}

function bond(otherId: string, overrides: Partial<FigureBond> = {}): FigureBond {
  return {
    otherId,
    kinds: ['Kin'],
    sinceYear: 0,
    lastChangedYear: 0,
    lastCause: 'SharedResidence',
    originEventKind: 'FigureBorn',
    lastEventKind: 'FigureBorn',
    affection: 0.5,
    trust: 0.5,
    obligation: 0.2,
    fear: 0,
    grievance: 0,
    ...overrides,
  } as FigureBond;
}

function context(figures: Figure[], events: Record<string, HistoryEvent[]> = {}): LifeContext {
  const byId = new Map(figures.map((figure) => [figure.id, figure]));
  return {
    endYear: 200,
    figureOf: (id) => byId.get(id),
    eventsFor: (id) => events[id] ?? [],
    nameOf: (id) => byId.get(id)?.name ?? id,
  };
}

test('the life arc keeps birth and death, collapses the children, and reads in order', () => {
  const children = [
    person({ id: 'fig:2', name: 'Olaw', birthYear: 40 }),
    person({ id: 'fig:3', name: 'Elinamo', birthYear: 43 }),
    person({ id: 'fig:4', name: 'Ainikka', birthYear: 47 }),
  ];
  const figure = person({
    deathYear: 80,
    childIds: ['fig:2', 'fig:3', 'fig:4'],
    bonds: [bond('fig:9', { kinds: ['Spouse'], sinceYear: 35, lastChangedYear: 35 })],
    titles: [
      { kind: 'Governor', title: 'Warden', civilizationId: 'civ:1', fromYear: 55 },
      { kind: 'Ruler', title: 'Consul', civilizationId: 'civ:1', fromYear: 60, toYear: 65 },
      { kind: 'Ruler', title: 'Consul', civilizationId: 'civ:1', fromYear: 70 },
    ] as Figure['titles'],
  });

  const arc = buildLifeArc(figure, [], context([figure, ...children]));
  const kinds = arc.moments.map((moment) => moment.kind);
  assert.equal(kinds[0], 'Birth');
  assert.equal(kinds.at(-1), 'Death');
  assert.deepEqual(
    arc.moments.map((moment) => moment.year),
    [...arc.moments.map((moment) => moment.year)].sort((a, b) => a - b),
  );

  // Three children are one turn in a life, dated to the first of them.
  const born = arc.moments.filter((moment) => moment.kind === 'Children');
  assert.equal(born.length, 1);
  assert.equal(born[0].year, 40);
  assert.equal(born[0].label, 'First of 3');
  assert.equal(born[0].age, 30);

  // The height of a repeated office is the first time it was reached, not the last renewal.
  const consul = arc.moments.find((moment) => moment.label === 'Consul');
  assert.equal(consul?.year, 60);
});

test('the arc weighs a busy year above a quiet one, and a notable year above a busy one', () => {
  const figure = person({ deathYear: 30 });
  const events = [12, 12, 12, 20].map(
    (year, index) => ({ id: index, year, kind: 'FigureMoved', significance: 'Routine' }) as HistoryEvent,
  );

  const arc = buildLifeArc(figure, events, context([figure]));
  assert.equal(arc.busiestYear, 12);
  assert.deepEqual(arc.density, [
    { year: 12, weight: 1 },
    { year: 20, weight: 1 / 3 },
  ]);

  // One siege outweighs three journeys: the chronicle's own significance is what makes a year
  // stand tall, rather than the number of lines it happened to take.
  const withASiege = buildLifeArc(
    figure,
    [...events, { id: 9, year: 20, kind: 'SiegeBegan', significance: 'Notable' } as HistoryEvent],
    context([figure]),
  );
  assert.equal(withASiege.busiestYear, 20);
});

test('standing past a death reports the year they stopped, and says the year asked for', () => {
  const spouse = person({ id: 'fig:9', name: 'Ainikka', birthYear: 8, deathYear: 60 });
  const figure = person({
    deathYear: 70,
    childIds: ['fig:2'],
    bonds: [bond('fig:9', { kinds: ['Spouse'], sinceYear: 30, lastChangedYear: 30 })],
    titles: [
      { kind: 'Ruler', title: 'Consul', civilizationId: 'civ:1', fromYear: 50, toYear: 70 },
    ] as Figure['titles'],
  });
  const child = person({ id: 'fig:2', birthYear: 35 });
  const ctx = context([figure, spouse, child]);

  const atEnd = standingAt(figure, 200, ctx);
  assert.equal(atEnd.year, 200, 'the year asked for is what the reader chose');
  assert.equal(atEnd.age, 60, 'the age is the one they died at');
  assert.equal(atEnd.alive, false);
  assert.equal(atEnd.position, 'Consul', 'not "no recorded position" six centuries later');
  assert.equal(atEnd.household, 'widowed');
  assert.equal(atEnd.childCount, 1);

  const before = standingAt(figure, 40, ctx);
  assert.equal(before.position, undefined, 'the office was not held yet');
  assert.equal(before.household, 'married');
  assert.equal(before.childCount, 1);
});

test('the constellation drops whoever is not alive in the year drawn', () => {
  const living = person({ id: 'fig:2', name: 'Ainikka', birthYear: 5 });
  const dead = person({ id: 'fig:3', name: 'Olaw', birthYear: 5, deathYear: 40 });
  const unborn = person({ id: 'fig:4', name: 'Elinamo', birthYear: 60 });
  const rival = person({ id: 'fig:5', name: 'Maelan', birthYear: 5 });
  const figure = person({
    bonds: [
      bond('fig:2', { kinds: ['Friend'] }),
      bond('fig:3', { kinds: ['Sibling'] }),
      bond('fig:4', { kinds: ['Child'] }),
      bond('fig:5', { kinds: ['Rival'], grievance: 0.9 }),
    ],
  });
  const ctx = context([figure, living, dead, unborn, rival]);

  const at30 = buildConstellation(figure, 30, ctx).map((node) => node.id);
  assert.deepEqual(at30.sort(), ['fig:2', 'fig:3', 'fig:5']);

  const at50 = buildConstellation(figure, 50, ctx);
  assert.deepEqual(
    at50.map((node) => node.id).sort(),
    ['fig:2', 'fig:5'],
    'the sibling died in 40 and their line goes out',
  );
  assert.equal(at50.find((node) => node.id === 'fig:5')?.tie, 'rival');
  assert.equal(at50.find((node) => node.id === 'fig:2')?.tie, 'friend');
  assert.equal(Math.max(...at50.map((node) => node.strength)), 1);
});

test('what someone is known for is counted through the selected year alone', () => {
  const child = person({ id: 'fig:2', birthYear: 40 });
  const late = person({ id: 'fig:3', birthYear: 55 });
  const figure = person({
    deathYear: 90,
    childIds: ['fig:2', 'fig:3'],
    service: [
      { rank: 'Soldier', title: 'Of the line', civilizationId: 'civ:1', year: 30 },
      { rank: 'Captain', title: 'Centurion', civilizationId: 'civ:1', year: 62 },
    ] as Figure['service'],
  });
  const ctx = context([figure, child, late]);

  const early = knownFor(figure, [], 50, ctx).map((line) => `${line.before}${line.after}`);
  assert.ok(early.includes('Raised 1 child'), early.join(' / '));
  // Only one year under arms is on the record by then: a span it cannot defend is not claimed.
  assert.ok(early.some((line) => line === 'Took to arms in 30'), early.join(' / '));

  const whole = knownFor(figure, [], 90, ctx).map((line) => `${line.before}${line.after}`);
  assert.ok(whole.includes('Raised 2 children'), whole.join(' / '));
  assert.ok(whole.some((line) => line === '32 years under arms'), whole.join(' / '));
});

test('significance leaves an ordinary life ordinary and puts a ruler above a soldier', () => {
  const townsman = person({ childIds: [], journeys: [] });
  const soldier = person({
    id: 'fig:2',
    service: [{ rank: 'Captain', title: 'Centurion', civilizationId: 'civ:1', year: 30 }] as Figure['service'],
    campaigns: [
      { warId: 'war:1', battleId: 'bat:1', sideId: 'civ:1', year: 31, role: 'Fought', fate: 'ReturnedUnharmed', renownGained: 1, traumatized: false, deserted: false },
    ] as Figure['campaigns'],
  });
  const ruler = person({
    id: 'fig:3',
    titles: [
      { kind: 'Ruler', title: 'Consul', civilizationId: 'civ:1', fromYear: 40 },
      { kind: 'Marshal', title: 'War-leader', civilizationId: 'civ:1', fromYear: 35 },
    ] as Figure['titles'],
    campaigns: [
      { warId: 'war:1', battleId: 'bat:1', sideId: 'civ:1', year: 41, role: 'Commanded', fate: 'ReturnedUnharmed', renownGained: 3, traumatized: false, deserted: false },
    ] as Figure['campaigns'],
  });
  const ctx = context([townsman, soldier, ruler]);

  assert.equal(historicalSignificance(townsman, [], 100, ctx).band, 'Ordinary');
  const soldierScore = historicalSignificance(soldier, [], 100, ctx);
  const rulerScore = historicalSignificance(ruler, [], 100, ctx);
  assert.ok(rulerScore.score > soldierScore.score);
  assert.ok(soldierScore.score > historicalSignificance(townsman, [], 100, ctx).score);
  assert.ok(rulerScore.reasons.length > 0);
});

test('ripples name the line that continued and the successor who took the seat', () => {
  const heir = person({ id: 'fig:2', name: 'Olaw', birthYear: 40, childIds: ['fig:5', 'fig:6'] });
  const successor = person({ id: 'fig:7', name: 'Elinamo', birthYear: 45 });
  const grandchildren = [
    person({ id: 'fig:5', birthYear: 70 }),
    person({ id: 'fig:6', birthYear: 72 }),
  ];
  const figure = person({
    deathYear: 80,
    childIds: ['fig:2'],
    titles: [{ kind: 'Ruler', title: 'Consul', civilizationId: 'civ:1', fromYear: 60 }] as Figure['titles'],
  });
  const ctx = context([figure, heir, successor, ...grandchildren], {
    'civ:1': [
      { id: 1, year: 60, kind: 'RulerCrowned', significance: 'Notable', subject: 'fig:1' },
      { id: 2, year: 81, kind: 'RulerCrowned', significance: 'Notable', subject: 'fig:7' },
    ] as HistoryEvent[],
    'fig:5': [
      { id: 3, year: 95, kind: 'FigureMarried', significance: 'Notable', subject: 'fig:5' },
    ] as HistoryEvent[],
  });

  const ripples = ripplesAfter(figure, ctx);
  const rendered = ripples.map((ripple) => `${ripple.before}[${ripple.aboutId ?? ''}]${ripple.after}`);
  assert.ok(rendered.includes('[fig:2] continued the line — 2 children'), rendered.join(' / '));
  assert.ok(rendered.includes('[fig:7] took the seat in civ:1, in 81'), rendered.join(' / '));
  assert.ok(
    rendered.includes('Descendants stood in 1 later recorded event[]'),
    rendered.join(' / '),
  );

  assert.deepEqual(ripplesAfter(person({ deathYear: undefined }), ctx), [], 'nothing follows the living');
});
