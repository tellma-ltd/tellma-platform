// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { describe, it } from 'node:test';

import {
  containerLabels,
  MAX_RUN_MS,
  parseContainers,
  processAlive,
  staleContainers,
} from './containers.mjs';

// The container sweep's rules: a starting run removes only containers whose
// host is gone or which outlived any run, never a live run's.

/** A fixed time for the rules. */
const NOW = 1_800_000_000_000;

describe('the e2e container sweep', () => {
  it("keeps a live run's container and removes one whose host is gone", () => {
    const containers = parseContainers(
      [`live\t100\t${NOW - 60_000}`, `orphan\t200\t${NOW - 60_000}`].join('\n'),
    );

    const stale = staleContainers(containers, { now: NOW, alive: (pid) => pid === 100 });

    assert.deepEqual(stale, ['orphan']);
  });

  it('removes a container older than any run, even if its process id lives again', () => {
    const containers = parseContainers(`old\t100\t${NOW - MAX_RUN_MS - 1}\n`);

    assert.deepEqual(staleContainers(containers, { now: NOW, alive: () => true }), ['old']);
  });

  it('keeps a container without its owner and start labels', () => {
    const containers = parseContainers('unlabelled\t\t\r\nhalf\t100\t\n');

    assert.deepEqual(staleContainers(containers, { now: NOW, alive: () => false }), []);
  });

  it('labels a container with its suffix, its host and its start', () => {
    assert.deepEqual(containerLabels('pc-f2', 42, NOW), [
      'tellma-e2e=pc-f2',
      'tellma-e2e.pid=42',
      `tellma-e2e.started=${NOW}`,
    ]);
  });

  it('tells a live process from an ended one', async () => {
    assert.equal(processAlive(process.pid), true);
    const child = spawn(process.execPath, ['-e', '']);
    const pid = child.pid;
    await new Promise((resolve) => child.once('exit', resolve));
    assert.ok(pid !== undefined);
    assert.equal(processAlive(pid), false);
  });
});
