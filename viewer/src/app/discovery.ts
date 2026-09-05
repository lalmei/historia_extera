import { historicalSignificance, type LifeContext, type Significance } from './biography.ts';
import { figureOf, type World } from './store.ts';
import type { EntityId, Figure } from './types.ts';

export const SIGNIFICANCE_BANDS: Significance['band'][] = [
  'Influential', 'Consequential', 'Notable', 'Recorded', 'Ordinary',
];

export interface FigureDiscovery {
  byId: ReadonlyMap<EntityId, Significance>;
  ranked: readonly Figure[];
}

const cache = new WeakMap<World, FigureDiscovery>();

/**
 * One reading per loaded world, shared by the overview and figure list. The export's event
 * index limits each reading to that person's records; artifact ownership is bucketed once.
 * This remains a viewer interpretation, using the same last living year as the life page.
 */
export function discoverFigures(world: World): FigureDiscovery {
  const existing = cache.get(world);
  if (existing) return existing;

  const artifacts = new Map<EntityId, number>();
  for (const artifact of world.export.artifacts) {
    if (artifact.ownerId) {
      artifacts.set(artifact.ownerId, (artifacts.get(artifact.ownerId) ?? 0) + 1);
    }
  }
  const context: LifeContext = {
    endYear: world.export.meta.endYear,
    figureOf: (id) => figureOf(world, id),
    eventsFor: world.eventsFor,
    nameOf: world.nameOf,
  };
  const byId = new Map<EntityId, Significance>();
  for (const figure of world.export.figures) {
    byId.set(figure.id, historicalSignificance(
      figure,
      world.eventsFor(figure.id),
      Math.min(figure.deathYear ?? context.endYear, context.endYear),
      context,
      artifacts.get(figure.id) ?? 0,
    ));
  }
  const ranked = [...world.export.figures].sort((a, b) =>
    byId.get(b.id)!.score - byId.get(a.id)!.score || a.id.localeCompare(b.id),
  );
  const result = { byId, ranked };
  cache.set(world, result);
  return result;
}
