/**
 * A world file's body as JSON text, whether it arrived plain or gzipped.
 *
 * The engine writes `*.json.gz` for the library, and the dev server serves those bytes with
 * `Content-Encoding: gzip`, so the browser has already inflated them by the time `fetch`
 * hands the body over. A static host that serves the same file without that header does not —
 * so rather than trust the name or the headers, the first two bytes decide: gzip's `1f 8b` can
 * never open a JSON document.
 */

const GZIP_MAGIC = [0x1f, 0x8b] as const;

/** World filenames the viewer will open: the plain export and its gzipped form. */
export function isWorldFileName(name: string): boolean {
  return name.endsWith('.json') || name.endsWith('.json.gz');
}

/** The response body as a stream of JSON bytes, inflated when it is still gzipped. */
export async function worldBodyStream(response: Response): Promise<ReadableStream<Uint8Array>> {
  if (!response.body) throw new Error(`no body for ${response.url}`);

  const reader = response.body.getReader();
  const first = await readAtLeast(reader, GZIP_MAGIC.length);
  const replayed = prepend(first, reader);

  const gzipped = first.length >= 2 && first[0] === GZIP_MAGIC[0] && first[1] === GZIP_MAGIC[1];
  return gzipped
    ? replayed.pipeThrough(new DecompressionStream('gzip') as ReadableWritablePair<Uint8Array, Uint8Array>)
    : replayed;
}

/** The whole document, parsed. */
export async function readWorldJson(response: Response): Promise<unknown> {
  return new Response(await worldBodyStream(response)).json();
}

/** Reads until at least `count` bytes are in hand or the stream ends; never drops a byte. */
async function readAtLeast(
  reader: ReadableStreamDefaultReader<Uint8Array>,
  count: number,
): Promise<Uint8Array> {
  const chunks: Uint8Array[] = [];
  let length = 0;
  while (length < count) {
    const { done, value } = await reader.read();
    if (done) break;
    chunks.push(value);
    length += value.length;
  }

  const joined = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) {
    joined.set(chunk, offset);
    offset += chunk.length;
  }
  return joined;
}

/** Puts the peeked bytes back in front of the rest of the stream. */
function prepend(
  head: Uint8Array,
  rest: ReadableStreamDefaultReader<Uint8Array>,
): ReadableStream<Uint8Array> {
  return new ReadableStream<Uint8Array>({
    start(controller) {
      if (head.length > 0) controller.enqueue(head);
    },
    async pull(controller) {
      const { done, value } = await rest.read();
      if (done) controller.close();
      else controller.enqueue(value);
    },
    cancel(reason) {
      return rest.cancel(reason);
    },
  });
}
