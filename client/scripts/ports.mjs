/**
 * Worktree-isolated, port-free tooling (spec 0002 §1.3).
 *
 * getPort(name) resolves the port for a named local service:
 *   1. If the worktree root has a .dev-ports.local file (written by
 *      `dotnet tellma setup-worktree`; KEY=VALUE lines) and it contains the
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

const worktreeRoot = join(dirname(fileURLToPath(import.meta.url)), '..', '..');

/** @param {string} name KEY in .dev-ports.local, e.g. 'CLIENT_SANDBOX' */
export async function getPort(name) {
  const portsFile = join(worktreeRoot, '.dev-ports.local');
  if (existsSync(portsFile)) {
    const lines = readFileSync(portsFile, 'utf8').split(/\r?\n/);
    for (const line of lines) {
      const match = line.match(/^\s*([A-Z0-9_]+)\s*=\s*(\d+)\s*$/);
      if (match && match[1] === name) {
        return Number(match[2]);
      }
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
