// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { readFileSync, writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

import { register } from 'tsx/esm/api';

// The config check's own Node process (spec 0024 §3.6). It loads the
// application's src/app.config.ts through tsx, with the application's
// tsconfig, so its imports resolve as the application's build resolves them,
// then calls @tellma/core's tmCheckAppConfig on the configuration
// provideTellma received. Every module loads in one tsx namespace, so
// app.config.ts and this file share one instance of @tellma/core, whose
// tmAppConfigOf finds the configuration. The process is separate because the
// application's code (Angular among it) must not load into the command's.
//
// It reads a request file named by its one argument and writes the answer
// to the file the request names: { problems } or { failure }.

/** What the build asks of the check. */
interface ConfigCheckRequest {
  /** The application's tsconfig, absolute. */
  readonly tsconfig: string;
  /** The application's src/app.config.ts, absolute. */
  readonly appConfig: string;
  /** The emitted Core chunk, absolute. */
  readonly core: string;
  /** The emitted Entities chunk, absolute. */
  readonly entities: string;
  /** The emitted string packs per language code, absolute. */
  readonly strings: Readonly<Record<string, string>>;
  /** Where to write the answer, absolute. */
  readonly result: string;
}

/** The members of @tellma/core this process calls. */
interface TellmaCore {
  /** The Tellma configuration a `provideTellma` call recorded in an application configuration. */
  readonly tmAppConfigOf: (appConfig: unknown) => unknown;
}

/** The members of @tellma/core/config-check this process calls. */
interface TellmaConfigCheck {
  /** Checks the Tellma configuration against the emitted metadata and string packs. */
  readonly tmCheckAppConfig: (config: unknown, metadata: unknown, entities: unknown, strings: unknown) => readonly unknown[];
}

/** Reads and parses a JSON file. */
function readJson(file: string): unknown {
  return JSON.parse(readFileSync(file, 'utf8')) as unknown;
}

/** The request the build wrote, named by the first argument. */
const request = readJson(process.argv[2]!) as ConfigCheckRequest;
/** Writes the answer where the request asks. */
const answer = (value: unknown): void => writeFileSync(request.result, JSON.stringify(value));

try {
  const api = register({ namespace: 'tellma-config-check', tsconfig: request.tsconfig });
  const parent = pathToFileURL(request.appConfig).href;

  // Partially compiled Angular code calls the JIT facade as its modules
  // evaluate, so the compiler loads before anything that imports Angular.
  await api.import('@angular/compiler', parent);
  const app = (await api.import(parent, import.meta.url)) as { readonly appConfig?: unknown };
  const core = (await api.import('@tellma/core', parent)) as TellmaCore;
  const check = (await api.import('@tellma/core/config-check', parent)) as TellmaConfigCheck;

  // --- The configuration provideTellma received ---
  if (app.appConfig === undefined) {
    answer({ failure: 'src/app.config.ts does not export appConfig.' });
  } else {
    const config = core.tmAppConfigOf(app.appConfig);
    if (config === null) {
      answer({ failure: 'appConfig in src/app.config.ts does not hold provideTellma(…).' });
    } else {
      // --- The check, with the string packs for the key-collision rule ---
      const strings = Object.fromEntries(Object.entries(request.strings).map(([code, file]) => [code, readJson(file)]));
      answer({ problems: check.tmCheckAppConfig(config, readJson(request.core), readJson(request.entities), strings) });
    }
  }
} catch (error) {
  answer({ failure: `src/app.config.ts could not be loaded: ${error instanceof Error ? (error.stack ?? error.message) : String(error)}` });
}

// The answer is written; a timer the application's modules started must not
// keep the process alive.
process.exit(0);
