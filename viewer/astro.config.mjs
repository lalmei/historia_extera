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
// `worldGenerator` adds one dev-only exception to that: under `astro dev` it injects a
// /new page and the endpoint behind it, so a seed can be simulated from the browser
// instead of a second terminal. It contributes nothing to a build, which is what keeps
// the built viewer serverless — see dev/world-generator.mjs.
export default defineConfig({
  integrations: [react(), worldGenerator()],
  devToolbar: { enabled: process.env.HISTORIA_NATIVE_APP !== '1' },
  vite: {
    plugins: [tailwindcss()],
    // A release app runs viewer sources from a writable cache while node_modules stays in
    // the signed bundle. Keep Vite's generated dependency cache beside those sources.
    ...(process.env.HISTORIA_CACHE_DIR
      ? { cacheDir: process.env.HISTORIA_CACHE_DIR }
      : {}),
    ...(process.env.HISTORIA_MODULE_DIR
      ? {
          server: {
            fs: {
              allow: [process.cwd(), process.env.HISTORIA_MODULE_DIR],
            },
          },
        }
      : {}),
  },
});
