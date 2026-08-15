import { paramsFromExport, worldFileFromLocation } from '../generate';
import type { World } from '../store';
import { NewWorld } from './NewWorld';

/**
 * Prefills the generator from the export on screen.
 *
 * Mounted only on the Overview, and only under the dev server: a built viewer has
 * no generator endpoint, and this form would have nowhere to send a run.
 */
export function RerunWorld({ world }: { world: World }) {
  const file = worldFileFromLocation();

  return (
    <NewWorld
      title="Run this world again"
      initial={paramsFromExport(world.export, file)}
      sourceLabel={file ?? 'this world'}
      showContinue
    />
  );
}
