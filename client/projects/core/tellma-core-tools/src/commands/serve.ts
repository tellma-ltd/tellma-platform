// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { parseEnv } from 'node:util';

import { TmBuildFailure } from '../common/problems.js';
import { tmRunEmit } from '../emit/emit.js';
import { tmReadEmittedMetadata, type TmEmittedRouting } from '../emit/emitted-metadata.js';
import { tmReadAppProject, tmRequirePackageDir, type TmAppProject } from '../project/app-project.js';
import { tmLogStep, type TmCommandContext } from './context.js';

// `tellma-client serve` (spec 0024 §2.6): the development loop. It runs the
// emit, exports the ASP.NET Core development certificate, and runs
// `ng serve` over HTTPS on the worktree's client port, proxying every path
// the server owns (the routing words, websockets included) to the
// distribution's web host. It sets no CSP and registers no worker: the dev
// server serves the app unbuilt. Inside the platform's own repository it
// first writes the platform's generated assets (the token stylesheets), which
// a published package already holds.

/** How `serve` runs. */
export interface TmServeOptions {
  /** The port to serve on, over the worktree's and the default. */
  readonly port?: number;
  /** Where the development certificate is exported; where ASP.NET Core's SPA templates keep it by default. */
  readonly certificateDir?: string;
}

/** The folder ASP.NET Core's SPA templates export the development certificate to. */
export function tmDefaultCertificateDir(): string {
  return process.platform === 'win32' && process.env['APPDATA'] !== undefined
    ? join(process.env['APPDATA'], 'ASP.NET', 'https')
    : join(homedir(), '.aspnet', 'https');
}

/** The client port when the worktree names none. */
export const TM_DEFAULT_CLIENT_PORT = 4200;
/** The web host's port when the worktree names none. */
export const TM_DEFAULT_WEB_PORT = 7052;

/** The file of worktree-local ports, KEY=port lines, at the worktree's root. */
const PORTS_FILE = '.dev-ports.local';

/** The nearest folder at or above `start` holding `name`, or null. */
function findAbove(start: string, name: string): string | null {
  for (let current = start; ; current = dirname(current)) {
    if (existsSync(join(current, name))) {
      return current;
    }
    if (dirname(current) === current) {
      return null;
    }
  }
}

/**
 * Reads a port from the worktree's `.dev-ports.local`.
 *
 * @param projectDir - The client project's folder; the file is at or above it.
 * @param key - The key, such as `ACME_CLIENT`.
 * @returns The port, or null when the file or key is absent.
 */
export function tmDevPort(projectDir: string, key: string): number | null {
  const root = findAbove(projectDir, PORTS_FILE);
  if (root === null) {
    return null;
  }
  const value = parseEnv(readFileSync(join(root, PORTS_FILE), 'utf8'))[key];
  return value !== undefined && /^\d+$/.test(value) ? Number(value) : null;
}

/** Escapes a string for use in a regular expression. */
function escape(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/**
 * The dev server's proxy configuration: the server's first segments, and its
 * segments under a tenant id, to the web host, websockets included. The
 * keys are regular expressions over the request URL.
 *
 * @param routing - The routing words of the Core chunk.
 * @param webPort - The web host's port on localhost.
 * @returns The configuration, ready to serialize.
 */
export function tmProxyConfig(routing: TmEmittedRouting, webPort: number): Record<string, unknown> {
  const target = { target: `https://localhost:${webPort}`, secure: false, ws: true };
  const words = (list: readonly string[]): string => list.map(escape).join('|');
  const config: Record<string, unknown> = {};
  if (routing.serverFirstSegments.length > 0) {
    config[`^/(?:${words(routing.serverFirstSegments)})(?:[/?#]|$)`] = target;
  }
  if (routing.serverTenantSegments.length > 0) {
    config[`^/[0-9]+/(?:${words(routing.serverTenantSegments)})(?:[/?#]|$)`] = target;
  }
  return config;
}

/** The ASP.NET Core development certificate, exported as PEM. */
interface DevCertificate {
  readonly cert: string;
  readonly key: string;
}

/** Exports the development certificate to a folder, once. */
async function exportDevCertificate(context: TmCommandContext, folder: string, slug: string): Promise<DevCertificate> {
  const cert = join(folder, `${slug}.pem`);
  const key = join(folder, `${slug}.key`);
  if (!existsSync(cert) || !existsSync(key)) {
    mkdirSync(folder, { recursive: true });
    const code = await context.runner.run('dotnet', ['dev-certs', 'https', '--export-path', cert, '--format', 'Pem', '--no-password'], {
      cwd: context.cwd,
    });
    if (code !== 0 || !existsSync(cert) || !existsSync(key)) {
      throw new TmBuildFailure(
        `The ASP.NET Core development certificate could not be exported (code ${code}); create and trust it with 'dotnet dev-certs https --trust'.`,
      );
    }
  }
  return { cert, key };
}

/** Inside the platform's repository, writes the generated assets a published package holds. */
async function platformAssets(context: TmCommandContext, project: TmAppProject): Promise<void> {
  const root = findAbove(project.dir, 'pnpm-workspace.yaml');
  if (root === null || !existsSync(join(root, 'package.json'))) {
    return;
  }
  const scripts = (JSON.parse(readFileSync(join(root, 'package.json'), 'utf8')) as { readonly scripts?: Record<string, string> }).scripts;
  if (scripts?.['tokens:build-css'] === undefined) {
    return;
  }
  tmLogStep(context, "Writing the platform's generated assets");
  const code = await context.runner.run('pnpm', ['run', 'tokens:build-css'], { cwd: root });
  if (code !== 0) {
    throw new TmBuildFailure(`pnpm run tokens:build-css exited with code ${code}.`);
  }
}

/**
 * Runs the development server of the client project in the working
 * directory, until it stops.
 *
 * @param context - The project folder, process seam and streams.
 * @param options - The port.
 * @returns 0, once the dev server has stopped cleanly.
 * @throws TmBuildFailure when a step before the dev server cannot run, or the dev server fails.
 */
export async function tmServe(context: TmCommandContext, options: TmServeOptions): Promise<number> {
  const project = tmReadAppProject(context.cwd);
  if (!project.hasServeTarget) {
    throw new TmBuildFailure(`angular.json: the application '${project.angularProject}' needs a serve target on @angular/build:dev-server.`);
  }
  await platformAssets(context, project);

  // --- The emit, the certificate and the ports ---
  tmLogStep(context, 'Emitting the client metadata');
  await tmRunEmit(project, {}, context.runner);
  const { core } = tmReadEmittedMetadata(project.generatedDir);
  const slug = core.value.distribution.slug;
  const certificate = await exportDevCertificate(context, options.certificateDir ?? tmDefaultCertificateDir(), slug);
  const prefix = slug.toUpperCase().replace(/[^A-Z0-9]/g, '_');
  const port = options.port ?? tmDevPort(project.dir, `${prefix}_CLIENT`) ?? TM_DEFAULT_CLIENT_PORT;
  const webPort = tmDevPort(project.dir, `${prefix}_WEB`) ?? TM_DEFAULT_WEB_PORT;

  // --- The dev server, with the proxy to the web host ---
  const work = mkdtempSync(join(tmpdir(), 'tellma-serve-'));
  try {
    const proxy = join(work, 'proxy.conf.json');
    writeFileSync(proxy, JSON.stringify(tmProxyConfig(core.value.routing, webPort), null, 2));
    tmLogStep(context, `Serving on https://localhost:${port}/, proxying the server's paths to https://localhost:${webPort}/`);
    const ng = join(tmRequirePackageDir(project, '@angular/cli'), 'bin', 'ng.js');
    const code = await context.runner.run(
      process.execPath,
      [
        ng,
        'serve',
        '--port',
        String(port),
        '--ssl',
        '--ssl-cert',
        certificate.cert,
        '--ssl-key',
        certificate.key,
        '--proxy-config',
        proxy,
      ],
      { cwd: project.dir },
    );
    if (code !== 0) {
      throw new TmBuildFailure(`ng serve exited with code ${code}.`);
    }
    return code;
  } finally {
    rmSync(work, { recursive: true, force: true });
  }
}
