// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { mkdir, readFile, rename, rm, stat, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { test, type TestInfo } from '@playwright/test';

/**
 * Two tests doing `Control+C/X/V` at once must not share a clipboard: one
 * clobbers the other's payload, and a same-grid cut→paste then fails to
 * recognize its own cut (the fingerprint no longer matches) and pastes values
 * instead of moving the row.
 *
 * Headless Chromium keeps an in-memory clipboard per browser process, and
 * Playwright gives each worker its own browser whose tests run one after
 * another, so headless runs (every default and CI run) cannot interfere and
 * take no lock. A headed browser writes the OS clipboard, which every browser
 * on the machine shares; there this module gives the real-clipboard specs a
 * cross-process mutex so exactly one runs at a time, while every other spec
 * keeps running in parallel.
 *
 * The lock lives in this checkout's own ignored output directory, so it
 * serializes the workers of one run; two checkouts running headed at once
 * still share the OS clipboard, which is a debugging session's concern.
 *
 * The lock is an atomically-created directory (mkdir fails for all but the
 * creator) carrying an owner record, so a release only removes a lock this
 * process still holds. A lock whose owner process is gone, or older than
 * {@link STALE_MS} (well past the longest a live holder keeps it), is treated
 * as abandoned by a crashed or killed worker and reclaimed, so one hard crash
 * cannot wedge the suite. A reclaim first renames the lock aside, which only
 * one waiter can do, and puts it back if it turns out to be a fresh holder's.
 */
const LOCK_DIR = fileURLToPath(new URL('../../.artifacts/e2e/clipboard.lock', import.meta.url));

/** The owner record inside {@link LOCK_DIR}: the holder's token, pid and start time. */
const OWNER_FILE = 'owner';

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
 * Budget of each real-clipboard test, not counting the time spent waiting for
 * the lock: that time is added on top as it accrues.
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

/** The error code of a failed file-system call, if it carries one. */
function codeOf(error: unknown): string | undefined {
  return (error as NodeJS.ErrnoException | null)?.code;
}

/**
 * Reads the owner record of a lock directory (the live lock by default), or
 * null while it is mid-write or gone.
 */
async function readOwner(dir = LOCK_DIR): Promise<LockOwner | null> {
  try {
    return JSON.parse(await readFile(join(dir, OWNER_FILE), 'utf8')) as LockOwner;
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
    return codeOf(error) === 'EPERM';
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
async function isOrphaned(dir = LOCK_DIR): Promise<boolean> {
  try {
    return Date.now() - (await stat(dir)).mtimeMs > OWNERLESS_GRACE_MS;
  } catch {
    return false; // already gone: the next attempt simply takes it
  }
}

/**
 * Removes the lock judged abandoned, whose owner token was `staleToken` (null
 * for an ownerless lock). The lock is first renamed to a name unique to this
 * waiter, which only one of several waiters judging the same lock can do.
 * If the renamed lock turns out to carry another token, another waiter has
 * already reclaimed the stale lock and this one moved that waiter's fresh
 * lock aside, so it is put back. Any failure (a racing waiter, a Windows
 * delete-pending directory) is swallowed: the caller waits and retries.
 */
async function reclaim(staleToken: string | null, token: string): Promise<void> {
  const aside = `${LOCK_DIR}.${token}.stale`;
  try {
    await rename(LOCK_DIR, aside);
  } catch {
    return; // another waiter got there first, or the lock is gone
  }
  try {
    const owner = await readOwner(aside);
    // The lock moved aside is the one judged abandoned only if it carries the
    // same token; an ownerless one must also still be old, since a fresh
    // holder that has not yet written its record is ownerless too.
    const same =
      staleToken === null
        ? owner === null && (await isOrphaned(aside))
        : owner?.token === staleToken;
    if (same) {
      await rm(aside, { recursive: true, force: true });
    } else {
      await rename(aside, LOCK_DIR);
    }
  } catch {
    // A stale copy left aside is harmless; one put back late is reclaimed
    // again by the pid and age checks.
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
  for (;;) {
    try {
      await mkdir(LOCK_DIR);
    } catch (error) {
      const code = codeOf(error);
      if (code === 'ENOENT') {
        // The output directory is missing (first run, or wiped mid-run):
        // create it and try again.
        await mkdir(dirname(LOCK_DIR), { recursive: true });
        continue;
      }
      // EEXIST is a held lock; on Windows a directory still being deleted
      // answers EPERM or EACCES. Anything else is a real failure.
      if (code !== 'EEXIST' && code !== 'EPERM' && code !== 'EACCES') {
        throw error;
      }
      // Held elsewhere: reclaim it if it looks abandoned, then (whether or
      // not that freed it) wait a moment and retry.
      const owner = await readOwner();
      if (owner === null ? await isOrphaned() : isAbandoned(owner)) {
        await reclaim(owner?.token ?? null, token);
      } else if (Date.now() - start > MAX_WAIT_MS) {
        const holder =
          owner === null
            ? 'an owner whose record could not be read'
            : `process ${owner.pid} since ${new Date(owner.ts).toISOString()}`;
        throw new Error(
          `Waited ${Math.round(MAX_WAIT_MS / 1000)} s for the real-clipboard lock at ${LOCK_DIR}, ` +
            `held by ${holder}. Delete the directory if no test run is in progress.`,
          { cause: error },
        );
      }
      await new Promise((resolve) => setTimeout(resolve, 25 + Math.floor(Math.random() * 50)));
      creditWait();
      continue;
    }
    // The directory is ours: record the owner, then credit the last poll.
    const owner: LockOwner = { token, pid: process.pid, ts: Date.now() };
    await writeFile(join(LOCK_DIR, OWNER_FILE), JSON.stringify(owner), 'utf8');
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
 * Gives every test in the enclosing `describe` a {@link TEST_TIMEOUT_MS}
 * budget and, in a headed run, serializes it against all other
 * real-clipboard describes of the run (across files and workers). Call it
 * once at the top of a describe that drives the real clipboard, after any
 * `test.use({ permissions })`. The lock is held for the test body and
 * teardown; the time spent waiting for it is added to the test's timeout, so
 * a long queue never times a test out.
 */
export function useExclusiveClipboard(): void {
  test.beforeEach(async ({ headless }) => {
    const info = test.info();
    info.setTimeout(TEST_TIMEOUT_MS);
    // A headless browser's clipboard is its own: nothing to serialize.
    if (!headless) {
      await acquire(info);
    }
  });
  test.afterEach(async () => {
    await release();
  });
}
