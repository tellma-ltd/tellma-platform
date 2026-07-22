// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Launches the showcase (default project) on a port-free basis per spec 0002
 * §1.3: the port comes from .dev-ports.local when present, else the OS
 * assigns one.
 *
 * Two modes:
 * - default: `ng serve` (dev server) — fast rebuilds for local work.
 * - `--prod`: a production `ng build` served as static files. The e2e suite
 *   uses this on CI: the dev bundle's latency widens the grid's async
 *   post-click focus/render race, so a synthetic key press occasionally
 *   lands before focus settles and is dropped — a broad, flaky red across
 *   the keyboard/clipboard specs. The optimized build closes that window.
 *
 * Usage: node scripts/serve.mjs [project] [--port <n>] [--prod]
 */
import { spawn, spawnSync } from 'node:child_process';
import { createServer } from 'node:http';
import { createRequire } from 'node:module';
import { readFile, stat } from 'node:fs/promises';
import { dirname, extname, join, normalize, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

import { getPort } from './ports.mjs';

const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);
const ng = require.resolve('@angular/cli/bin/ng.js', { paths: [clientDir] });

const args = process.argv.slice(2);
const portFlag = args.indexOf('--port');
const prod = args.includes('--prod');
// Positionals exclude flags AND flag values ('--port 4300' must not make
// '4300' the project).
const positionals = args.filter(
  (a, i) => !a.startsWith('--') && (portFlag < 0 || i !== portFlag + 1),
);
const project = positionals[0] ?? 'showcase';
const port =
  portFlag >= 0
    ? Number(args[portFlag + 1])
    : await getPort(`CLIENT_${project.toUpperCase().replaceAll('-', '_')}`);

if (!prod) {
  console.log(`[serve] ${project} on http://localhost:${port}/`);
  const child = spawn(process.execPath, [ng, 'serve', project, '--port', String(port)], {
    cwd: clientDir,
    stdio: 'inherit',
  });
  child.on('exit', (code) => process.exit(code ?? 0));
} else {
  serveProd();
}

function serveProd() {
  console.log(`[serve] building ${project} (production)…`);
  const build = spawnSync(process.execPath, [ng, 'build', project], {
    cwd: clientDir,
    stdio: 'inherit',
  });
  if (build.status !== 0) {
    process.exit(build.status ?? 1);
  }
  // The application builder emits the browser bundle under dist/<project>/browser.
  const root = join(clientDir, 'dist', project, 'browser');
  const rootPrefix = root + sep;
  const indexHtml = join(root, 'index.html');

  const server = createServer((req, res) => {
    void respond(req.url ?? '/', res);
  });
  server.listen(port, () => console.log(`[serve] ${project} (prod) on http://localhost:${port}/`));

  async function respond(rawUrl, res) {
    try {
      const pathname = decodeURIComponent(rawUrl.split('?')[0].split('#')[0]);
      let filePath = normalize(join(root, pathname));
      // Path-traversal guard: never serve outside the build output.
      if (filePath !== root && !filePath.startsWith(rootPrefix)) {
        res.writeHead(403).end('Forbidden');
        return;
      }
      let info = await stat(filePath).catch(() => null);
      if (info?.isDirectory()) {
        filePath = join(filePath, 'index.html');
        info = await stat(filePath).catch(() => null);
      }
      // SPA fallback: a client route (/story/…) resolves to no file — serve
      // the app shell so Angular's router takes over.
      if (info === null) {
        filePath = indexHtml;
      }
      const body = await readFile(filePath);
      res.writeHead(200, {
        'content-type': MIME[extname(filePath).toLowerCase()] ?? 'application/octet-stream',
        'cache-control': 'no-store',
      });
      res.end(body);
    } catch (error) {
      res.writeHead(500).end(String(error));
    }
  }
}

/** Minimal content types for the assets an Angular build emits. */
const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.map': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.gif': 'image/gif',
  '.webp': 'image/webp',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
  '.ttf': 'font/ttf',
  '.txt': 'text/plain; charset=utf-8',
  '.wasm': 'application/wasm',
};
