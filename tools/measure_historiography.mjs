// Usage: node tools/measure_historiography.mjs path/to/world.json [...]
import { readFileSync } from 'node:fs';
import { buildWorld } from '../viewer/src/app/store.ts';
import { readHistoriography } from '../viewer/src/app/historiography.ts';

if (process.argv.length < 3) throw new Error('Provide one or more exported world paths.');
for (const file of process.argv.slice(2)) {
  const world = buildWorld(JSON.parse(readFileSync(file, 'utf8')));
  const all = readHistoriography(world);
  const surviving = all.filter((account) => account.survives);
  const incomplete = surviving.filter((account) => {
    const latest = account.versions.at(-1);
    return latest.omitted.length > 0 || latest.later.length > 0 || latest.laterEnding !== undefined;
  });
  console.log(JSON.stringify({
    file, seed: world.export.meta.seed, years: world.export.meta.yearsSimulated,
    campaigns: all.length, surviving: surviving.length, materiallyIncomplete: incomplete.length,
    withLaterRival: surviving.filter((account) => account.laterRivals.length > 0).length,
    examples: incomplete.slice(0, 3).map((account) => ({
      id: account.artifact.id, name: account.artifact.name, written: account.artifact.createdYear,
      latest: account.versions.at(-1).year, omitted: account.versions.at(-1).omitted.length,
      summarized: account.versions.at(-1).summarized.length,
      laterEvents: account.versions.at(-1).later.length, warEnd: account.war.endYear,
    })),
  }));
}
