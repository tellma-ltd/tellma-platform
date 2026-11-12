// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { mkdir, readFile, rm, stat, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { test, type TestInfo } from '@playwright/test';

/**
 * Playwright runs spec files across parallel workers (`fullyParallel`), and
 * two tests doing `Control+C/X/V` at once must not share a clipboard: one
 * clobbers the other's payload, and a same-grid cut→paste then fails to
 * recognize its own cut (the fingerprint no longer matches) and pastes values
 * instead of moving the row. A headed browser writes the OS clipboard, which
 * every browser on the machine shares; Playwright's default headless shell
 * was observed to keep one per browser process. This module gives the
 * real-clipboard specs a cross-process mutex so that, whichever applies,
 * exactly one runs at a time within a run; every other spec keeps running in
 * parallel.
 *
 * The lock lives in this checkout's own ignored output directory, not in the
 * machine's temp directory: several checkouts of the repository on one
 * machine each run their own suite, and a lock they all shared would queue
 * every run behind the others for a resource that does not cross runs.
 *
 * The lock is an atomically-created directory (mkdir fails for all but the
 * creator) carrying an owner record, so a release only removes a lock this
 * process still holds. A lock whose owner process is gone, or older than
 * {@link STALE_MS} (well past the longest a live holder keeps it), is treated
 * as abandoned by a crashed or killed worker and reclaimed, so one hard crash
 * cannot wedge the suite.
 */
const LOCK_DIR = fileURLToPath(new URL('../../.artifacts/e2e/clipboard.lock', import.meta.url));

/** The owner record inside {@link LOCK_DIR}: the holder's token, pid and start time. */
const OWNER_FILE = join(LOCK_DIR, 'owner');

/**
 * Age past which a lock is reclaimed even if its owner process still seems
 * alive (a reused pid). A live holder keeps the lock for at most its test
 * timeout plus teardown, which Playwright bounds by the same timeout again.
 */
const STALE_MS = 300_000;

/**
 * How long a lock directory may lack its owner record before it counts as
 * orphaned; writing the record takes milliseconds.
 */
const OWNERLESS_GRACE_MS = 10_000;

/**
 * Budget of each serialized test, not counting the time spent waiting for the
 * lock: that time is added on top as it accrues.
 */
const TEST_TIMEOUT_MS = 60_000;

/**
 * Longest a test waits for the lock before failing with a message naming the
 * holder. Far longer than a queue of every real-clipboard test on every
 * worker, so it trips only when something is wrong with the lock itself.
 */
const MAX_WAIT_MS = 600_000;

/** The owner record written into the lock directory. */
interface LockOwner {
  /** Unique per acquisition, so a release never removes another holder's lock. */
  readonly token: string;
  /** The holder's process id, checked for liveness before reclaiming. */
  readonly pid: number;
  /** When the lock was taken, in epoch milliseconds. */
  readonly ts: number;
}

/** The token of the lock this worker holds, or null when it holds none. */
let heldToken: string | null = null;

/** Reads the current owner record, or null while it is mid-write or gone. */
async function readOwner(): Promise<LockOwner | null> {
  try {
    return JSON.parse(await readFile(OWNER_FILE, 'utf8')) as LockOwner;
  } catch {
    return null;
  }
}

/**
 * Whether a process with this id exists. Signal 0 checks without signalling,
 * on Windows and Linux alike; EPERM means it exists under another account.
 */
function isAlive(pid: number): boolean {
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return (error as NodeJS.ErrnoException).code === 'EPERM';
  }
}

/** Whether a lock with a readable owner record was left behind by its holder. */
function isAbandoned(owner: LockOwner): boolean {
  return !Number.isFinite(owner.ts) || !isAlive(owner.pid) || Date.now() - owner.ts > STALE_MS;
}

/**
 * Whether a lock without a readable owner record was left behind: a holder
 * that died between creating the directory and writing its record. A record
 * still being written is younger than {@link OWNERLESS_GRACE_MS}.
 */
async function isOrphaned(): Promise<boolean> {
  try {
    return Date.now() - (await stat(LOCK_DIR)).mtimeMs > OWNERLESS_GRACE_MS;
  } catch {
    return false; // already gone: the next attempt simply takes it
  }
}

/**
 * Takes the lock, waiting behind any other holder. Waiting does not count
 * against the test's timeout: each poll adds the time it took to the timeout
 * of `info`, so the test keeps its whole budget for its own work.
 */
async function acquire(info: TestInfo): Promise<void> {
  const token = `${process.pid}.${Date.now()}.${Math.random()}`;
  const start = Date.now();
  let credited = start;
  // Moves the test's deadline forward by the time spent since the last call.
  const creditWait = (): void => {
    const now = Date.now();
    info.setTimeout(info.timeout + (now - credited));
    credited = now;
  };
  await mkdir(dirname(LOCK_DIR), { recursive: true });
  for (;;) {
    try {
      await mkdir(LOCK_DIR);
    } catch {
      // Held elsewhere (or being removed): reclaim it only if it looks
      // abandoned, else wait and retry.
      const owner = await readOwner();
      if (owner === null ? await isOrphaned() : isAbandoned(owner)) {
        await rm(LOCK_DIR, { recursive: true, force: true });
        continue;
      }
      if (Date.now() - start > MAX_WAIT_MS) {
        const holder =
          owner === null
            ? 'an owner whose record could not be read'
            : `process ${owner.pid} since ${new Date(owner.ts).toISOString()}`;
        throw new Error(
          `Waited ${Math.round(MAX_WAIT_MS / 1000)} s for the real-clipboard lock at ${LOCK_DIR}, ` +
            `held by ${holder}. Delete the directory if no test run is in progress.`,
        );
      }
      await new Promise((resolve) => setTimeout(resolve, 25 + Math.floor(Math.random() * 50)));
      creditWait();
      continue;
    }
    // The directory is ours: record the owner, then credit the last poll.
    const owner: LockOwner = { token, pid: process.pid, ts: Date.now() };
    await writeFile(OWNER_FILE, JSON.stringify(owner), 'utf8');
    heldToken = token;
    creditWait();
    return;
  }
}

/** Releases the lock if this worker still holds it. */
async function release(): Promise<void> {
  if (heldToken === null) {
    return;
  }
  const owner = await readOwner();
  // A lock reclaimed as stale belongs to someone else now: leave it alone.
  if (owner?.token === heldToken) {
    await rm(LOCK_DIR, { recursive: true, force: true });
  }
  heldToken = null;
}

/**
 * Serializes every test in the enclosing `describe` against all other
 * real-clipboard describes of this run (across files and workers). Call it
 * once at the top of a describe that drives the real OS clipboard, after any
 * `test.use({ permissions })`. The lock is held for the test body and
 * teardown; the time spent waiting for it is added to the test's timeout, so
 * a long queue never times a test out.
 */
export function useExclusiveClipboard(): void {
  test.beforeEach(async () => {
    const info = test.info();
    info.setTimeout(TEST_TIMEOUT_MS);
    await acquire(info);
  });
  test.afterEach(async () => {
    await release();
  });
}
