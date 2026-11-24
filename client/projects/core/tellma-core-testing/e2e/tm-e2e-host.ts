// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="node" />

// The facts of the e2e host a distribution's Playwright suite runs on (spec
// 0024 §13). The suite's configuration starts the host and publishes these
// facts as environment variables, which every Playwright worker inherits, so
// the helpers read them wherever a test runs.

/** The environment variables the e2e host's facts travel in. */
export const TM_E2E_ENV = {
  /** The host's own origin, such as `https://localhost:7160`. */
  origin: 'TELLMA_E2E_ORIGIN',
  /** The absolute directory the host's development mail drop writes each message to. */
  mailDrop: 'TELLMA_E2E_MAIL_DROP',
  /** The absolute directory where signed-in states are kept between tests and runs. */
  stateDir: 'TELLMA_E2E_STATE_DIR',
} as const;

/** The e2e host a suite runs on. */
export interface TmE2eHost {
  /** The host's origin, which is also the app's public origin. */
  readonly origin: string;
  /** The directory of the host's development mail drop. */
  readonly mailDrop: string;
  /** The directory signed-in states are kept in. */
  readonly stateDir: string;
}

/**
 * The e2e host the current run started, read from {@link TM_E2E_ENV}.
 *
 * @returns The host's facts.
 * @throws Error when the suite's configuration did not publish them.
 */
export function tmE2eHost(): TmE2eHost {
  const read = (name: string): string => {
    const value = process.env[name];
    if (!value) {
      throw new Error(
        `${name} is not set. Run the suite through its e2e script, which starts the e2e host and publishes its facts.`,
      );
    }
    return value;
  };
  return {
    origin: read(TM_E2E_ENV.origin).replace(/\/+$/, ''),
    mailDrop: read(TM_E2E_ENV.mailDrop),
    stateDir: read(TM_E2E_ENV.stateDir),
  };
}
