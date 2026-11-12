// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken } from '@angular/core';

import type { TmClientMetadata, TmContracts, TmEntitiesChunk } from './tm-client-metadata';

// The loaders of the client metadata (spec 0024 §3.2, §3.3). The emit writes a
// module that calls `tmMetadataLoaders` with the hashed paths of the chunks it
// wrote, so the app knows every file's name at build time and fetches each
// one at the moment it first needs it. Each file's name changes whenever its
// bytes do, so a fetched file never goes stale.

/** One language's string pack: server string keys and their messages. */
export type TmStringPack = Readonly<Record<string, string>>;

/** The loaders of the client metadata, as the emitted module exports them. */
export interface TmMetadataLoaders {
  /** Loads the Core chunk. */
  readonly core: () => Promise<TmClientMetadata>;
  /** Loads the Entities chunk. */
  readonly entities: () => Promise<TmEntitiesChunk>;
  /** Loads the Contracts chunk. */
  readonly contracts: () => Promise<TmContracts>;
  /** Loads each offered language's string pack, by language code. */
  readonly strings: Readonly<Record<string, () => Promise<TmStringPack>>>;
}

/**
 * The app's metadata loaders, as `provideTellma` received them. Services that
 * load a chunk on demand, such as the HTTP layer with the Contracts chunk,
 * inject it optionally: an injector without `provideTellma` has none.
 */
export const TM_METADATA_LOADERS = new InjectionToken<TmMetadataLoaders>('TM_METADATA_LOADERS');

/** The paths of the client metadata's files, relative to the app's base URL. */
export interface TmMetadataPaths {
  /** The Core chunk's path, such as `tm-metadata/core-K3J7Q2M9X1BA.json`. */
  readonly core: string;
  /** The Entities chunk's path. */
  readonly entities: string;
  /** The Contracts chunk's path. */
  readonly contracts: string;
  /** Each offered language's string pack's path, by language code. */
  readonly strings: Readonly<Record<string, string>>;
}

/**
 * Fetches one metadata file and parses it as plain JSON.
 *
 * @param path - The file's path.
 * @returns The parsed file.
 * @throws Error when the request fails or answers anything but a success.
 */
async function fetchJson(path: string): Promise<unknown> {
  const response = await fetch(path, { credentials: 'same-origin' });
  if (!response.ok) {
    throw new Error(`The client metadata file '${path}' could not be loaded: the server answered ${response.status}.`);
  }
  return JSON.parse(await response.text()) as unknown;
}

/**
 * Wraps a load so that it runs at most once while it succeeds: every call
 * shares the first call's promise, and a failed load is tried again on the
 * next call.
 *
 * @param load - The load to share.
 * @returns The shared load.
 */
function once<T>(load: () => Promise<T>): () => Promise<T> {
  let pending: Promise<T> | null = null;
  return () => {
    pending ??= load().catch((error: unknown) => {
      pending = null;
      throw error;
    });
    return pending;
  };
}

/**
 * The loaders of the client metadata files at `paths`. Each loader fetches
 * its file with `fetch` and parses it with `JSON.parse` on its first call,
 * and every later call shares that result; a failed load rejects, and the
 * next call tries again.
 *
 * The emit writes the one call a distribution needs; nothing else calls it.
 *
 * @param paths - The files' paths, relative to the app's base URL.
 * @returns The loaders.
 *
 * @example
 * ```ts
 * // generated/tellma-metadata.ts
 * export const tellmaMetadata = tmMetadataLoaders({
 *   core: 'tm-metadata/core-K3J7Q2M9X1BA.json',
 *   entities: 'tm-metadata/entities-7D2LQ4NV8C1M.json',
 *   contracts: 'tm-metadata/contracts-Q8B3XK2M6W4T.json',
 *   strings: { en: 'tm-metadata/strings-en-P4D8N2K6R3VY.json' },
 * });
 * ```
 */
export function tmMetadataLoaders(paths: TmMetadataPaths): TmMetadataLoaders {
  // --- The Core chunk, refused when its format is not the one this build reads ---
  const core = once(async () => {
    const chunk = (await fetchJson(paths.core)) as TmClientMetadata;
    if (chunk.formatVersion !== 1) {
      throw new Error(
        `The client metadata file '${paths.core}' has format ${String(chunk.formatVersion)}, but this app reads format 1.`,
      );
    }
    return chunk;
  });

  // --- The other chunks and the string packs ---
  const strings: Record<string, () => Promise<TmStringPack>> = {};
  for (const [language, path] of Object.entries(paths.strings)) {
    strings[language] = once(async () => (await fetchJson(path)) as TmStringPack);
  }

  return Object.freeze({
    core,
    entities: once(async () => (await fetchJson(paths.entities)) as TmEntitiesChunk),
    contracts: once(async () => (await fetchJson(paths.contracts)) as TmContracts),
    strings: Object.freeze(strings),
  });
}
