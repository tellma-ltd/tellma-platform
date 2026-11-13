// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

import { tmContentHash } from '../common/files.js';
import { TmBuildFailure } from '../common/problems.js';

// The metadata the emit wrote (spec 0024 §3.2), as the build reads it back:
// generated/assets/{core,entities,contracts}-<h>.json and one
// strings-<lang>-<h>.json per language, where <h> is the content hash of the
// file's bytes. The build reads only the members it needs, so these shapes
// are the subset of @tellma/core's metadata types the commands depend on.

/** A language the distribution offers, as the build reads it. */
export interface TmEmittedLanguage {
  /** The language code, such as `ar`. */
  readonly code: string;
  /** The language's writing direction. */
  readonly dir: 'ltr' | 'rtl';
}

/** The reserved route words of the Core chunk's `routing` section. */
export interface TmEmittedRouting {
  /** The first segments the server owns, such as `api` and `bff`. */
  readonly serverFirstSegments: readonly string[];
  /** The segments the server owns under a tenant id, such as `api` and `hub`. */
  readonly serverTenantSegments: readonly string[];
}

/** The members of the Core chunk the build reads. */
export interface TmEmittedCore {
  /** Who the distribution is. */
  readonly distribution: {
    /** The distribution's slug, such as `acme`. */
    readonly slug: string;
    /** The distribution's name, the fallback of every localized display name. */
    readonly displayName: string;
    /** The UI version. */
    readonly version: string;
    /** The platform's version. */
    readonly platformVersion: string;
  };
  /** The offered languages; the first is the default. */
  readonly languages: readonly TmEmittedLanguage[];
  /** The reserved route words. */
  readonly routing: TmEmittedRouting;
  /** The coded reports, whose JSON has a budget of its own. */
  readonly codedReports?: readonly unknown[];
}

/** One emitted file: its name under `assets/` and its parsed content. */
export interface TmEmittedFile<T> {
  /** The file's name, such as `core-K3J7Q2M9X1BA.json`. */
  readonly name: string;
  /** The file's parsed JSON. */
  readonly value: T;
}

/** Everything the emit wrote that the build reads. */
export interface TmEmittedMetadata {
  /** The emit's output folder, `generated/`, absolute. */
  readonly dir: string;
  /** The Core chunk, whose members the build reads. */
  readonly core: TmEmittedFile<TmEmittedCore>;
  /** The Core chunk exactly as emitted, for the config check. */
  readonly coreValue: unknown;
  /** The Entities chunk, for the config check. */
  readonly entities: TmEmittedFile<unknown>;
  /** The Contracts chunk. */
  readonly contracts: TmEmittedFile<unknown>;
  /** The string packs per language code, each a flat table of strings. */
  readonly strings: ReadonlyMap<string, TmEmittedFile<Readonly<Record<string, string>>>>;
}

/** An emitted file's name: its kind, the language for a string pack, and the hash. */
const EMITTED_NAME = /^(core|entities|contracts|strings-([A-Za-z0-9-]+))-([0-9A-HJKMNP-TV-Z]{12})\.json$/;

/** Fails the step for emitted metadata the build cannot read. */
function invalid(reason: string): never {
  throw new TmBuildFailure(`The emitted client metadata is invalid: ${reason} Run the emit again.`);
}

/** Checks the members of the Core chunk the build reads. */
function checkCore(value: unknown, name: string): TmEmittedCore {
  const core = value as Partial<TmEmittedCore> | null;
  const distribution = core?.distribution;
  if (
    typeof distribution?.slug !== 'string' ||
    typeof distribution.displayName !== 'string' ||
    typeof distribution.version !== 'string' ||
    typeof distribution.platformVersion !== 'string'
  ) {
    invalid(`${name} lacks the distribution's slug, display name or versions.`);
  }
  if (
    !Array.isArray(core?.languages) ||
    core.languages.length === 0 ||
    !core.languages.every((language) => typeof language?.code === 'string' && (language.dir === 'ltr' || language.dir === 'rtl'))
  ) {
    invalid(`${name} offers no languages, or a language lacks its code or direction.`);
  }
  const routing = core.routing;
  if (
    !Array.isArray(routing?.serverFirstSegments) ||
    !Array.isArray(routing.serverTenantSegments) ||
    ![...routing.serverFirstSegments, ...routing.serverTenantSegments].every((word) => typeof word === 'string' && /^[A-Za-z0-9._-]+$/.test(word))
  ) {
    invalid(`${name} lacks the routing words, or a word is not a plain path segment.`);
  }
  return core as TmEmittedCore;
}

/**
 * Reads the emitted client metadata back from `generated/assets/`, checking
 * that each file's name carries the hash of its bytes.
 *
 * @param generatedDir - The emit's output folder.
 * @returns The emitted metadata.
 * @throws TmBuildFailure when a chunk is missing, twice present, stale or not valid JSON.
 */
export function tmReadEmittedMetadata(generatedDir: string): TmEmittedMetadata {
  const assets = join(generatedDir, 'assets');
  if (!existsSync(assets)) {
    invalid(`${assets} does not exist.`);
  }
  const found = new Map<string, TmEmittedFile<unknown>>();
  const strings = new Map<string, TmEmittedFile<Readonly<Record<string, string>>>>();
  for (const name of readdirSync(assets).sort()) {
    const match = EMITTED_NAME.exec(name);
    if (match === null) {
      continue;
    }
    // --- The name's hash must be the hash of the bytes ---
    const bytes = readFileSync(join(assets, name));
    if (tmContentHash(bytes) !== match[3]) {
      invalid(`the hash in ${name} is not the hash of its content.`);
    }
    let value: unknown;
    try {
      value = JSON.parse(bytes.toString('utf8')) as unknown;
    } catch (error) {
      throw new TmBuildFailure(`The emitted client metadata is invalid: ${name} is not valid JSON.`, { cause: error });
    }
    // --- A string pack per language, else one file per chunk ---
    const kind = match[2] === undefined ? match[1]! : `strings:${match[2]}`;
    if (found.has(kind)) {
      invalid(`two files hold the ${kind} chunk; the emit removes stale files, so another process wrote one.`);
    }
    found.set(kind, { name, value });
    if (match[2] !== undefined) {
      if (
        typeof value !== 'object' ||
        value === null ||
        Array.isArray(value) ||
        !Object.values(value).every((text) => typeof text === 'string')
      ) {
        invalid(`${name} is not a flat table of strings.`);
      }
      strings.set(match[2], { name, value: value as Readonly<Record<string, string>> });
    }
  }
  const chunk = (kind: string): TmEmittedFile<unknown> => found.get(kind) ?? invalid(`the ${kind} chunk is missing.`);
  const core = chunk('core');
  return {
    dir: generatedDir,
    core: { name: core.name, value: checkCore(core.value, core.name) },
    coreValue: core.value,
    entities: chunk('entities'),
    contracts: chunk('contracts'),
    strings,
  };
}
