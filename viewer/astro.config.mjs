// @ts-check
import { defineConfig } from 'astro/config';
import react from '@astrojs/react';
import tailwindcss from '@tailwindcss/vite';

// The viewer is a static shell around one client-rendered app.
//
// The alternative — having Astro read world.json at build time and statically
// generate a page per entity — would give faster first paint and real URLs, but
// it would couple viewer builds to world generation: every regenerated history
// would need a site rebuild before it could be looked at. The export is meant to
// be the whole contract between engine and viewer, so the viewer is built once
// and any world file can be dropped into public/worlds/ and opened.
export default defineConfig({
  integrations: [react()],
  vite: { plugins: [tailwindcss()] },
});
