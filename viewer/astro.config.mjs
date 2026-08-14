// @ts-check
import { defineConfig } from 'astro/config';
import react from '@astrojs/react';
import tailwindcss from '@tailwindcss/vite';
import { worldGenerator } from './dev/world-generator.mjs';

// The viewer is a static shell around one client-rendered app.
//
// The alternative — having Astro read world.json at build time and statically
// generate a page per entity — would give faster first paint and real URLs, but
// it would couple viewer builds to world generation: every regenerated history
// would need a site rebuild before it could be looked at. The export is meant to
// be the whole contract between engine and viewer, so the viewer is built once
// and any world file can be dropped into public/worlds/ and opened.
//
// `worldGenerator` adds one dev-only exception to that: a middleware that runs the CLI
// on request, so a seed can be simulated from the page instead of a second terminal. It
// is a dev-server plugin rather than an Astro route precisely to keep the built bundle
// serverless — see dev/world-generator.mjs.
export default defineConfig({
  integrations: [react()],
  vite: { plugins: [tailwindcss(), worldGenerator()] },
});
