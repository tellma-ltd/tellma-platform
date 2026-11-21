// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmTestEntitiesChunk, tmTestMetadata } from '@tellma/core-testing';

import { tmMetadataLoaders } from './tm-metadata-loaders';

/** The paths the loaders under test read. */
const PATHS = {
  core: 'tm-metadata/core-AAAA.json',
  entities: 'tm-metadata/entities-BBBB.json',
  contracts: 'tm-metadata/contracts-CCCC.json',
  strings: { en: 'tm-metadata/strings-en-DDDD.json' },
};

/** A fake `fetch` answering each path with its body, or with a 404. */
function fakeFetch(bodies: Readonly<Record<string, unknown>>) {
  return vi.fn<typeof fetch>((input) => {
    const path = String(input);
    const body = bodies[path];
    return Promise.resolve(
      body === undefined ? new Response('missing', { status: 404 }) : new Response(JSON.stringify(body), { status: 200 }),
    );
  });
}

describe('tmMetadataLoaders', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('fetches each file once, on its first load', async () => {
    const fetch = fakeFetch({
      [PATHS.core]: tmTestMetadata(),
      [PATHS.entities]: tmTestEntitiesChunk(),
      [PATHS.contracts]: { fingerprints: {}, long: [], idempotent: [] },
      [PATHS.strings.en]: { Acme_Customer: 'Customer' },
    });
    vi.stubGlobal('fetch', fetch);
    const loaders = tmMetadataLoaders(PATHS);
    expect(fetch).not.toHaveBeenCalled();

    const [first, second] = await Promise.all([loaders.core(), loaders.core()]);
    expect(first).toBe(second);
    expect(first.distribution.slug).toBe('acme');
    expect((await loaders.entities()).entities.length).toBe(tmTestEntitiesChunk().entities.length);
    expect((await loaders.contracts()).long).toEqual([]);
    expect(await loaders.strings['en']?.()).toEqual({ Acme_Customer: 'Customer' });
    await loaders.core();

    expect(fetch.mock.calls.map(([path]) => String(path))).toEqual([PATHS.core, PATHS.entities, PATHS.contracts, PATHS.strings.en]);
    expect(fetch.mock.calls[0]?.[1]).toEqual({ credentials: 'same-origin' });
    expect(Object.isFrozen(loaders)).toBe(true);
  });

  it('rejects a failed load and tries again on the next', async () => {
    const bodies: Record<string, unknown> = {};
    const fetch = fakeFetch(bodies);
    vi.stubGlobal('fetch', fetch);
    const loaders = tmMetadataLoaders(PATHS);

    await expect(loaders.core()).rejects.toThrow(`The client metadata file '${PATHS.core}' could not be loaded: the server answered 404.`);
    bodies[PATHS.core] = tmTestMetadata();
    await expect(loaders.core()).resolves.toEqual(tmTestMetadata());
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it('refuses a Core chunk of another format', async () => {
    vi.stubGlobal('fetch', fakeFetch({ [PATHS.core]: { ...tmTestMetadata(), formatVersion: 2 } }));
    await expect(tmMetadataLoaders(PATHS).core()).rejects.toThrow(
      `The client metadata file '${PATHS.core}' has format 2, but this app reads format 1.`,
    );
  });
});
