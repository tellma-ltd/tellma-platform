// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The e2e host's SQL Server containers: the labels each carries, and which
// of them a starting run may remove. A run removes only a container that is
// provably stale: the host that started it is gone, or it is older than any
// run lasts. A container another live run uses is never removed, whichever
// worktree started it.

/** The label every e2e container carries, set to its worktree's database suffix or `default`. */
export const CONTAINER_LABEL = 'tellma-e2e';

/** The label that holds the process id of the host that started the container. */
export const PID_LABEL = `${CONTAINER_LABEL}.pid`;

/** The label that holds when the container started, in epoch milliseconds. */
export const STARTED_LABEL = `${CONTAINER_LABEL}.started`;

/** Longer than any run lasts: a container this old outlived its run, even if its host's process id was reused. */
export const MAX_RUN_MS = 12 * 60 * 60 * 1000;

/** The `docker ps --format` template that {@link parseContainers} reads: id, owner and start, tab-separated. */
export const CONTAINER_FORMAT = `{{.ID}}\t{{.Label "${PID_LABEL}"}}\t{{.Label "${STARTED_LABEL}"}}`;

/**
 * @typedef {object} E2eContainer
 * @property {string} id The container's id.
 * @property {string} pid The owning host's process id, as labelled; empty when unlabelled.
 * @property {string} started When it started, as labelled; empty when unlabelled.
 */

/**
 * The labels of a container this run starts.
 *
 * @param {string} suffix The worktree's database suffix, or `default`.
 * @param {number} pid This host's process id.
 * @param {number} now The current time, in epoch milliseconds.
 * @returns {string[]} The `key=value` labels.
 */
export function containerLabels(suffix, pid, now) {
  return [`${CONTAINER_LABEL}=${suffix}`, `${PID_LABEL}=${pid}`, `${STARTED_LABEL}=${now}`];
}

/**
 * Reads `docker ps -a --format` output written with {@link CONTAINER_FORMAT}.
 *
 * @param {string} output The command's output.
 * @returns {E2eContainer[]} The containers listed.
 */
export function parseContainers(output) {
  return output
    .split(/\r?\n/)
    .filter((line) => line.trim() !== '')
    .map((line) => {
      const [id = '', pid = '', started = ''] = line.trim().split('\t');
      return { id, pid: pid.trim(), started: started.trim() };
    });
}

/**
 * The containers a starting run may remove. One without both labels (from an
 * older host script) cannot be proved stale, so it stays.
 *
 * @param {E2eContainer[]} containers The e2e containers on the machine.
 * @param {{ now: number, alive: (pid: number) => boolean }} facts The time, and whether a process lives.
 * @returns {string[]} The ids of the stale containers.
 */
export function staleContainers(containers, { now, alive }) {
  return containers
    .filter(({ pid, started }) => {
      const owner = Number(pid);
      const since = Number(started);
      if (
        pid === '' ||
        started === '' ||
        !Number.isInteger(owner) ||
        owner <= 0 ||
        !Number.isFinite(since)
      ) {
        return false;
      }
      return !alive(owner) || now - since > MAX_RUN_MS;
    })
    .map(({ id }) => id);
}

/**
 * Whether a process lives on this machine. Signal 0 checks without
 * signalling, on Windows too; a process another user owns refuses it with
 * `EPERM` but lives.
 *
 * @param {number} pid The process id.
 * @returns {boolean} Whether it lives.
 */
export function processAlive(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return /** @type {NodeJS.ErrnoException} */ (error).code === 'EPERM';
  }
}
