import assert from 'node:assert/strict';
import test from 'node:test';
import {
  buildBiographyEpisodes,
  groupJourneys,
  plotAt,
  undertakingAt,
  visibleBondAt,
  visibleMemoryAt,
} from './biography.ts';
import type {
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
