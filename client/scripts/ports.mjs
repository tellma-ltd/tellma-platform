// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Worktree-isolated, port-free tooling (spec 0002 §1.3).
 *
 * getPort(name) resolves the port for a named local service:
 *   1. If the worktree root has a .dev-ports.local file and it contains the
 *      key, that port is used — stable per worktree.
 *   2. Otherwise the OS assigns a free port (listen on 0).
 *
 * Nothing in the repo ever hardcodes a literal port, so any number of
 * worktrees run their tooling in parallel without collisions.
 */
import { existsSync, readFileSync } from 'node:fs';
import { createServer } from 'node:net';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseEnv } from 'node:util';

const worktreeRoot = join(dirname(fileURLToPath(import.meta.url)), '..', '..');

/** @param {string} name KEY in .dev-ports.local, e.g. 'CLIENT_SHOWCASE' */
export async function getPort(name) {
  const portsFile = join(worktreeRoot, '.dev-ports.local');
  if (existsSync(portsFile)) {
    const value = parseEnv(readFileSync(portsFile, 'utf8'))[name];
    if (value !== undefined && /^\d+$/.test(value)) {
      return Number(value);
    }
  }
  return osAssignedPort();
}

function osAssignedPort() {
  return new Promise((resolve, reject) => {
    const server = createServer();
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address();
      server.close(() => resolve(port));
    });
  });
}
