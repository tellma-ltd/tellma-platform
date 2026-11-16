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
 * - `--prod`: the production build pipeline (`ng build` + the font-preload
 *   injection) served as static files. The e2e suite uses this on CI: the
 *   dev bundle's latency widens the grid's async post-click focus/render
 *   race, so a synthetic key press occasionally lands before focus settles
 *   and is dropped — a broad, flaky red across the keyboard/clipboard
 *   specs. The optimized build closes that window. A bundle that is newer
 *   than every source file is reused rather than rebuilt (scripts/prod-build.mjs).
 * - `--prod --no-build`: serves the bundle already in dist/ as it is, and
 *   fails at once when there is none. The CI-mode e2e web server runs this:
 *   scripts/e2e.mjs has built the bundle before Playwright starts the server,
 *   so the server's startup timeout times the server alone and never a build.
 *
 * Usage: node scripts/serve.mjs [project] [--port <n>] [--prod [--no-build]]
 */
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { createServer } from 'node:http';
import { createRequire } from 'node:module';
import { readFile, stat } from 'node:fs/promises';
import { dirname, extname, join, normalize, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

import { getPort } from './ports.mjs';
import { ensureProdBuild, prodBuildDir } from './prod-build.mjs';

const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..');

// --- Arguments ---
const args = process.argv.slice(2);
const portFlag = args.indexOf('--port');
const prod = args.includes('--prod');
const noBuild = args.includes('--no-build');
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

// --- Mode dispatch ---
if (noBuild && !prod) {
  console.error('[serve] --no-build applies to --prod only: the dev server always compiles');
  process.exit(1);
}
if (!prod) {
  console.log(`[serve] ${project} on http://localhost:${port}/`);
  const ng = createRequire(import.meta.url).resolve('@angular/cli/bin/ng.js', {
    paths: [clientDir],
  });
  const child = spawn(process.execPath, [ng, 'serve', project, '--port', String(port)], {
    cwd: clientDir,
    stdio: 'inherit',
  });
  child.on('exit', (code) => process.exit(code ?? 0));
} else {
  serveProd();
}

/**
 * Serves the production bundle as static files on the loopback interface,
 * building it first unless `--no-build` was passed.
 */
function serveProd() {
  // The application builder emits the browser bundle under dist/<project>/browser.
  const root = prodBuildDir(project);
  const rootPrefix = root + sep;
  const indexHtml = join(root, 'index.html');

  // --- The bundle: built (or reused) here, or required to exist ---
  if (noBuild) {
    if (!existsSync(indexHtml)) {
      console.error(
        `[serve] no production build of ${project} in ${root}; build it first, or drop --no-build to let this script build it`,
      );
      process.exit(1);
    }
    console.log(`[serve] serving the existing ${project} build as it is (--no-build)`);
  } else {
    const status = ensureProdBuild(project, { logPrefix: '[serve]' });
    if (status !== 0) {
      process.exit(status);
    }
  }

  // --- The static server ---
  const server = createServer((req, res) => {
    void respond(req.url ?? '/', res);
  });
  // Loopback only, matching `ng serve`'s default — a dev/e2e host must not
  // publish an unauthenticated app to the whole network.
  server.listen(port, '127.0.0.1', () =>
    console.log(`[serve] ${project} (prod) on http://localhost:${port}/`),
  );

  /** Answers one request with a file of the bundle, or the app shell for a client route. */
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
