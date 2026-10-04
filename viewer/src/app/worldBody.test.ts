import { test } from 'node:test';
import assert from 'node:assert/strict';
import { gzipSync } from 'node:zlib';
import { isWorldFileName, readWorldJson, worldBodyStream } from './worldBody.ts';

const document = { schemaVersion: 62, names: Array.from({ length: 200 }, (_, i) => `name-${i}`) };

/** A response that hands its body over in small pieces, as a network would. */
function chunked(bytes: Uint8Array, size = 1): Response {
  let offset = 0;
  return new Response(new ReadableStream<Uint8Array>({
    pull(controller) {
      if (offset >= bytes.length) return controller.close();
      controller.enqueue(bytes.subarray(offset, offset + size));
      offset += size;
    },
  }));
}

test('a plain world reads as itself', async () => {
  const bytes = new TextEncoder().encode(JSON.stringify(document));
  assert.deepEqual(await readWorldJson(chunked(bytes)), document);
});

test('a gzipped world served without Content-Encoding is inflated by its magic bytes', async () => {
  // One byte per chunk, so the two-byte sniff has to read across a chunk boundary.
  const bytes = new Uint8Array(gzipSync(JSON.stringify(document)));
  assert.deepEqual(await readWorldJson(chunked(bytes)), document);
});

test('a reader can stop early without draining the world', async () => {
  const bytes = new Uint8Array(gzipSync(JSON.stringify(document)));
  const reader = (await worldBodyStream(chunked(bytes, 64))).getReader();
  const { value } = await reader.read();
  assert.ok(value && value.length > 0);
  await reader.cancel();
});

test('world filenames are the plain export and its gzipped form', () => {
  assert.ok(isWorldFileName('world.json'));
  assert.ok(isWorldFileName('world-s1-y2-c3-z4096.json.gz'));
  assert.ok(!isWorldFileName('world.gz'));
  assert.ok(!isWorldFileName('terrain.png'));
});
