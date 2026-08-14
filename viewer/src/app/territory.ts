import type { EntityId, ExportWorld, Region } from './types';

/**
 * Realm shapes, drawn as realms rather than as the cells they are stored in.
 *
 * Territory is held per region, and a region is a square of the world grid. Drawing one
 * translucent square per region — which is what this replaced — makes every realm look like
 * graph paper and makes a border between two realms indistinguishable from the seam between
 * two provinces of the same realm. Since the border *is* the thing a political map is for,
 * the squares are merged: one filled path per realm, and an outline that follows only the
 * edges where the neighbouring cell belongs to somebody else.
 *
 * Everything is emitted in the 0–100 space the map's SVG overlay uses, so the geometry does
 * not care how large the world is or what resolution the terrain raster was rendered at.
 */

export interface RealmShape {
  civilizationId: EntityId;
  /** Every region the realm holds, as one filled path. */
  fill: string;
  /** Only the edges that face somebody else, or the sea. */
  border: string;
  regions: number;
}

export interface RegionGrid {
  cols: number;
  rows: number;
  /** Cell size in the 0–100 overlay space. */
  cellWidth: number;
  cellHeight: number;
  /** Row-major, `cols × rows`. Undefined where the world has no region — never, in practice. */
  cells: (Region | undefined)[];
  at: (col: number, row: number) => Region | undefined;
  /** The region under a point given in 0–100 overlay space, if any. */
  atPoint: (x: number, y: number) => Region | undefined;
}

export function buildGrid(world: ExportWorld, regions: Region[]): RegionGrid {
  const cols = Math.max(1, Math.round(world.width / world.regionSize));
  const rows = Math.max(1, Math.round(world.height / world.regionSize));

  const cells = new Array<Region | undefined>(cols * rows);

  for (const region of regions) {
    const col = Math.round((region.minX - world.minX) / world.regionSize);
    const row = Math.round((region.minZ - world.minZ) / world.regionSize);
    if (col < 0 || col >= cols || row < 0 || row >= rows) continue;

    cells[row * cols + col] = region;
  }

  const at = (col: number, row: number) =>
    col < 0 || col >= cols || row < 0 || row >= rows ? undefined : cells[row * cols + col];

  return {
    cols,
    rows,
    cellWidth: 100 / cols,
    cellHeight: 100 / rows,
    cells,
    at,
    atPoint: (x, y) => at(Math.floor((x / 100) * cols), Math.floor((y / 100) * rows)),
  };
}

/**
 * One shape per realm holding land, in a stable order.
 *
 * `owners` is the map for a single year, so this is called again on every step of the year
 * slider — hence a single pass over the grid emitting strings, rather than any kind of
 * polygon union. A world grid is a thousand cells; the honest algorithm would cost more to
 * write and more to run.
 */
export function buildRealms(
  grid: RegionGrid,
  owners: Map<EntityId, EntityId>,
  order: EntityId[],
): RealmShape[] {
  const fills = new Map<EntityId, string[]>();
  const borders = new Map<EntityId, string[]>();
  const counts = new Map<EntityId, number>();

  const ownerAt = (col: number, row: number): EntityId | undefined => {
    const region = grid.at(col, row);
    return region ? owners.get(region.id) : undefined;
  };

  const { cellWidth: w, cellHeight: h } = grid;

  for (let row = 0; row < grid.rows; row++) {
    for (let col = 0; col < grid.cols; col++) {
      const region = grid.at(col, row);
      const owner = region && owners.get(region.id);
      if (!region || !owner) continue;

      const x = col * w;
      const y = row * h;

      let fill = fills.get(owner);
      if (!fill) fills.set(owner, (fill = []));

      let border = borders.get(owner);
      if (!border) borders.set(owner, (border = []));

      counts.set(owner, (counts.get(owner) ?? 0) + 1);

      fill.push(`M${n(x)} ${n(y)}h${n(w)}v${n(h)}h${n(-w)}z`);

      // Four edges, each drawn only when what lies across it is not this realm. Beyond the
      // grid counts as somebody else, which is what closes a realm that runs to the map edge.
      if (ownerAt(col - 1, row) !== owner) border.push(`M${n(x)} ${n(y)}v${n(h)}`);
      if (ownerAt(col + 1, row) !== owner) border.push(`M${n(x + w)} ${n(y)}v${n(h)}`);
      if (ownerAt(col, row - 1) !== owner) border.push(`M${n(x)} ${n(y)}h${n(w)}`);
      if (ownerAt(col, row + 1) !== owner) border.push(`M${n(x)} ${n(y + h)}h${n(w)}`);
    }
  }

  const shapes: RealmShape[] = [];

  // Ordered by the caller's list rather than by discovery, so a realm keeps its stacking
  // position as the years are scrubbed and the map does not flicker.
  for (const civilizationId of order) {
    const fill = fills.get(civilizationId);
    if (!fill) continue;

    shapes.push({
      civilizationId,
      fill: fill.join(''),
      border: (borders.get(civilizationId) ?? []).join(''),
      regions: counts.get(civilizationId) ?? 0,
    });
  }

  return shapes;
}

/** Three decimals is under a tenth of a pixel at any size this map is drawn at. */
function n(value: number): string {
  return Number(value.toFixed(3)).toString();
}
