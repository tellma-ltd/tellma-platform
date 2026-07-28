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
 *   specs. The optimized build closes that window.
 *
 * Usage: node scripts/serve.mjs [project] [--port <n>] [--prod]
 */
import { spawn, spawnSync } from 'node:child_process';
import { createServer } from 'node:http';
import { createRequire } from 'node:module';
import { readdirSync, statSync } from 'node:fs';
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

/**
 * Everything the production build reads: the app compiles from SOURCE (the
 * workspace tsconfig maps every `@tellma/*` specifier at projects/), so dist/
 * is output, never input. Deletions count too — a folder's own mtime moves
 * when an entry disappears, hence directories are walked, not just files.
 */
const BUILD_INPUTS = ['projects', 'angular.json', 'package.json', 'pnpm-lock.yaml', 'tsconfig.json'];

/** Folders inside the source tree that are build OUTPUT, not build input. */
const NON_SOURCE_DIRS = new Set(['node_modules', 'dist', '.angular']);

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

/** The newest mtime at or under a path (0 when it does not exist). */
function newestMtime(path) {
  const info = statSync(path, { throwIfNoEntry: false });
  if (info === undefined) {
    return 0;
  }
  if (!info.isDirectory()) {
    return info.mtimeMs;
  }
  let newest = info.mtimeMs;
  for (const entry of readdirSync(path, { withFileTypes: true })) {
    if (entry.isDirectory() && NON_SOURCE_DIRS.has(entry.name)) {
      continue;
    }
    newest = Math.max(newest, newestMtime(join(path, entry.name)));
  }
  return newest;
}

/**
 * Whether an already-built bundle still matches the sources. CI runs
 * `build:showcase` and only then the e2e suite, so rebuilding here would
 * repeat a multi-minute build for bytes that already exist. Errs toward
 * rebuilding: a stale reuse would test the wrong code, a needless rebuild
 * only costs time.
 */
function isBuildFresh(indexHtml) {
  const built = statSync(indexHtml, { throwIfNoEntry: false });
  return (
    built !== undefined &&
    BUILD_INPUTS.every((input) => newestMtime(join(clientDir, input)) < built.mtimeMs)
  );
}

/** Runs a build step in the client workspace, exiting on its failure. */
function runStep(argv) {
  const step = spawnSync(process.execPath, argv, { cwd: clientDir, stdio: 'inherit' });
  if (step.status !== 0) {
    process.exit(step.status ?? 1);
  }
}

function serveProd() {
  // The application builder emits the browser bundle under dist/<project>/browser.
  const root = join(clientDir, 'dist', project, 'browser');
  const rootPrefix = root + sep;
  const indexHtml = join(root, 'index.html');

  if (isBuildFresh(indexHtml)) {
    console.log(`[serve] reusing the existing ${project} build (newer than every source file)`);
  } else {
    console.log(`[serve] building ${project} (production)…`);
    runStep([ng, 'build', project]);
  }
  // The page that SHIPS is the build plus its font preloads (`build:showcase`
  // runs both), so e2e must serve that page and not a bare `ng build` output.
  // The step is idempotent, hence unconditional: it also covers a reused
  // bundle that was produced without it.
  runStep([
    join(clientDir, 'scripts', 'inject-font-preloads.mjs'),
    join('dist', project, 'browser'),
  ]);

  const server = createServer((req, res) => {
    void respond(req.url ?? '/', res);
  });
  // Loopback only, matching `ng serve`'s default — a dev/e2e host must not
  // publish an unauthenticated app to the whole network.
  server.listen(port, '127.0.0.1', () =>
    console.log(`[serve] ${project} (prod) on http://localhost:${port}/`),
  );

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
