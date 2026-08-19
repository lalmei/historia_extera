import { useEffect } from 'react';
import { SiteShell } from '../components/SiteChrome';
import { NewWorld } from './NewWorld';

/**
 * The generator, on its own page, behind the library's Generate button.
 *
 * Lives at `/new` under `astro dev` only — see `viewer/dev/new.astro`.
 */
export function GeneratePage() {
  useEffect(() => {
    const previous = document.title;
    document.title = 'Initialize Engine — Historia Extera';
    return () => {
      document.title = previous;
    };
  }, []);

  return (
    <SiteShell active="worlds">
      <div className="mx-auto w-full max-w-xl">
        <h1 className="he-headline">Initialize Engine</h1>
        <p className="mt-3 text-[var(--ink-soft)]">
          Configure parameters for procedural generation. High civilization counts on smaller map
          sizes may result in unstable early-era boundary conflicts.
        </p>

        <div className="mt-8">
          <NewWorld />
        </div>
      </div>
    </SiteShell>
  );
}
