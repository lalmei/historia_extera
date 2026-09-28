/** Shared by the catalog and the loaded-world generator: filenames are never provenance. */
export const MISSING_RECIPE = 'This export has no supported generation recipe. Rerun the original CLI command or library invocation to create a new export.';
export const UNSAFE_SEED = 'This seed exceeds the viewer’s safe integer range (0–9007199254740991). Use the CLI with the original seed from the JSON file.';

/** @param {any} meta @param {any} world */
export function rerunSettings(meta, world) {
  const refuse = (reason) => ({ params: null, reason });
  if (!Number.isSafeInteger(meta?.seed) || meta.seed < 0) return refuse(UNSAFE_SEED);
  const recipe = meta.generation;
  if (recipe?.version !== 1) return refuse(MISSING_RECIPE);
  if (recipe.unsupportedReason) return refuse(String(recipe.unsupportedReason));
  if (recipe.terrainSource !== '') return refuse('External terrain is required. Use the original CLI command and terrain files.');
  const params = {
    seed: meta.seed,
    years: meta.yearsSimulated,
    civs: recipe.initialCivilizations,
    size: world?.width,
    eastWestPeriodic: world?.eastWestPeriodic,
  };
  for (const [name, min, max] of [['years', 1, 5000], ['civs', 1, 64], ['size', 512, 8192]]) {
    if (!Number.isSafeInteger(params[name]) || params[name] < min || params[name] > max) {
      return refuse(`The recorded ${name} is outside the generator's supported range. Use the original CLI command or library invocation.`);
    }
  }
  if (meta.startYear !== 1 || world.height !== params.size || params.size % 256 !== 0 ||
      Math.floor(params.size / 128) ** 2 < 16 * params.civs || typeof params.eastWestPeriodic !== 'boolean') {
    return refuse('The recorded world settings are not supported by the generator. Use the original CLI command or library invocation.');
  }
  return { params, reason: null };
}
