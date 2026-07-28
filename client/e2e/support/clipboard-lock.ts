// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { test } from '@playwright/test';

/**
 * The real OS clipboard is a single machine-global resource, but Playwright
 * runs spec files across parallel workers (`fullyParallel`). Two tests doing
 * `Control+C/X/V` at once clobber each other's payload — a same-grid
 * cut→paste then fails to recognize its own cut (the fingerprint no longer
 * matches) and pastes values instead of moving the row. This module gives
 * the real-clipboard specs a cross-process mutex so exactly one runs at a
 * time; every other spec keeps running in parallel.
 *
 * The lock is an atomically-created directory (mkdir fails with EEXIST for
 * all but the creator) carrying an owner token, so a release only removes a
 * lock this process still holds. A lock older than {@link STALE_MS} — well
 * past any test timeout — is treated as abandoned by a crashed worker and
 * reclaimed, so one hard crash can't wedge the whole suite.
 */
const LOCK_DIR = join(tmpdir(), 'tm-e2e-clipboard.lock');
const OWNER_FILE = join(LOCK_DIR, 'owner');
const STALE_MS = 120_000;
const TEST_TIMEOUT_MS = 60_000;

let heldToken: string | null = null;

async function acquire(): Promise<void> {
  const token = `${process.pid}.${Date.now()}.${Math.random()}`;
  for (;;) {
    try {
      await mkdir(LOCK_DIR);
      await writeFile(OWNER_FILE, JSON.stringify({ token, ts: Date.now() }), 'utf8');
      heldToken = token;
      return;
    } catch {
      // Held elsewhere — reclaim it only if it looks abandoned, else wait.
      try {
        const { ts } = JSON.parse(await readFile(OWNER_FILE, 'utf8')) as { ts: number };
        if (Number.isFinite(ts) && Date.now() - ts > STALE_MS) {
          await rm(LOCK_DIR, { recursive: true, force: true });
          continue;
        }
      } catch {
        // The owner file is mid-write or already gone; fall through and retry.
      }
      await new Promise((resolve) => setTimeout(resolve, 25 + Math.floor(Math.random() * 50)));
    }
  }
}

async function release(): Promise<void> {
  if (heldToken === null) {
    return;
  }
  try {
    const { token } = JSON.parse(await readFile(OWNER_FILE, 'utf8')) as { token: string };
    if (token === heldToken) {
      await rm(LOCK_DIR, { recursive: true, force: true });
    }
  } catch {
    // Already reclaimed by a stale takeover — nothing of ours to remove.
  }
  heldToken = null;
}

/**
 * Serializes every test in the enclosing `describe` against all other
 * real-clipboard describes (across files and workers). Call it once at the
 * top of a describe that drives the real OS clipboard, after any
 * `test.use({ permissions })`. The lock is held only for the test body and
 * teardown; the timeout is widened to absorb the wait behind other holders.
 */
export function useExclusiveClipboard(): void {
  test.beforeEach(async () => {
    test.setTimeout(TEST_TIMEOUT_MS);
    await acquire();
  });
  test.afterEach(async () => {
    await release();
  });
}
