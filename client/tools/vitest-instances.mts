// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * One Vitest and one Vite per unit run.
 *
 * Angular's unit-test builder (`@angular/build:unit-test`) imports `vitest/node`
 * and `vite` from its own folder, and the workspace's browser provider
 * (`@vitest/browser-playwright`) from the workspace's folder. The provider
 * brings `@vitest/browser` and `@vitest/mocker`, which import Vitest and Vite
 * from theirs; `@vitest/browser` starts the browser server with its Vitest's
 * `createViteServer`. pnpm links one copy of a package for each set of peers
 * it resolves, and Vite takes sass, less, terser and others as optional peers,
 * so a second version of any of them in the graph can leave the builder's
 * Vitest core and the provider's browser server on two copies of Vitest and
 * Vite. Each copy keeps its own module state, and no suite tests the pair, so
 * this module lists every copy a workspace's unit run can load, and a
 * lint:test spec fails on more than one.
 *
 * The workspaces are every pnpm workspace package whose angular.json has a
 * target on the unit-test builder: the client workspace and each
 * distribution's Client project.
 */
import { existsSync, globSync, readFileSync, realpathSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join, relative } from 'node:path';

/** The builder whose targets run a workspace's unit tests. */
export const UNIT_TEST_BUILDER = '@angular/build:unit-test';

/** The package that holds the unit-test builder. */
export const BUILDER_PACKAGE = '@angular/build';

/** The browser providers the unit-test builder can load from a workspace. */
export const BROWSER_PROVIDERS: readonly string[] = Object.freeze([
  '@vitest/browser-playwright',
  '@vitest/browser-webdriverio',
  '@vitest/browser-preview',
]);

/** One way a unit run reaches a package: the packages it passes through, and the folder it ends in. */
export interface TmPackageRoute {
  /** The packages the route passes through, from the workspace, for example `@angular/build > vitest`. */
  readonly route: string;
  /** The real folder the route ends in, or null when some package on it does not resolve. */
  readonly dir: string | null;
}

/** Every copy of Vitest and Vite one workspace's unit run can load, and the sass its builder runs on. */
export interface TmUnitRunInstances {
  /** Every route to Vitest. */
  readonly vitest: readonly TmPackageRoute[];
  /** Every route to Vite. */
  readonly vite: readonly TmPackageRoute[];
  /** The sass the builder's package.json names, or null when it names none. */
  readonly sassPinned: string | null;
  /** The version of the sass the builder resolves, or null when it resolves none. */
  readonly sassResolved: string | null;
}

/** A pnpm workspace package whose unit tests run on Angular's unit-test builder. */
export interface TmUnitTestWorkspace {
  /** The folder's path from the repository root, with forward slashes. */
  readonly name: string;
  /** The folder's absolute path. */
  readonly dir: string;
}

/**
 * Reads the `packages` globs of the repository's pnpm-workspace.yaml: the
 * items of its top-level `packages:` list, one `- <glob>` per line.
 *
 * @param repoRoot - The repository root, which holds pnpm-workspace.yaml.
 * @returns The globs, in file order.
 */
export function readWorkspaceGlobs(repoRoot: string): string[] {
  const lines = readFileSync(join(repoRoot, 'pnpm-workspace.yaml'), 'utf8').split(/\r?\n/);
  const globs: string[] = [];
  let inPackages = false;
  for (const line of lines) {
    // A top-level key starts or ends the list; comments and blank lines are skipped.
    if (/^[^\s#]/.test(line)) {
      inPackages = /^packages:\s*$/.test(line);
      continue;
    }
    const item = inPackages ? /^\s+-\s+(['"]?)(.+?)\1\s*$/.exec(line) : null;
    if (item !== null) {
      globs.push(item[2]!);
    }
  }
  return globs;
}

/**
 * Whether an angular.json has a target on the unit-test builder.
 *
 * @param angularJsonPath - The angular.json file's path.
 * @returns True when some project's target uses the unit-test builder.
 */
function runsUnitTests(angularJsonPath: string): boolean {
  const angularJson = JSON.parse(readFileSync(angularJsonPath, 'utf8')) as {
    readonly projects?: Record<
      string,
      { readonly architect?: Record<string, { readonly builder?: string }> }
    >;
  };
  return Object.values(angularJson.projects ?? {}).some((project) =>
    Object.values(project.architect ?? {}).some((target) => target.builder === UNIT_TEST_BUILDER),
  );
}

/**
 * Lists the pnpm workspace packages whose angular.json has a target on the
 * unit-test builder.
 *
 * @param repoRoot - The repository root, which holds pnpm-workspace.yaml.
 * @returns The workspaces, sorted by name.
 */
export function discoverUnitTestWorkspaces(repoRoot: string): TmUnitTestWorkspace[] {
  const workspaces = new Map<string, TmUnitTestWorkspace>();
  for (const glob of readWorkspaceGlobs(repoRoot)) {
    for (const match of globSync(glob, { cwd: repoRoot })) {
      const dir = join(repoRoot, match);
      const angularJson = join(dir, 'angular.json');
      if (existsSync(angularJson) && runsUnitTests(angularJson)) {
        const name = relative(repoRoot, dir).replaceAll('\\', '/');
        workspaces.set(name, { name, dir });
      }
    }
  }
  return [...workspaces.values()].sort((a, b) => a.name.localeCompare(b.name));
}

/**
 * Finds the real folder of the package a module in a folder would import,
 * by Node's own lookup: each node_modules folder from the given folder up.
 * Package `exports` are not consulted, so a package that does not export its
 * package.json is found too.
 *
 * @param fromDir - The real folder the import is made from.
 * @param name - The package's name.
 * @returns The package's real folder, or null when it does not resolve.
 */
export function resolvePackageDir(fromDir: string, name: string): string | null {
  const lookup = createRequire(join(fromDir, 'index.js')).resolve.paths(name) ?? [];
  for (const modulesDir of lookup) {
    const dir = join(modulesDir, name);
    if (existsSync(join(dir, 'package.json'))) {
      return realpathSync(dir);
    }
  }
  return null;
}

/**
 * Finds a package's real folder from the real folder of the module that
 * imports it, or null when it does not resolve.
 */
export type TmPackageResolver = (fromDir: string, name: string) => string | null;

/**
 * Follows a route of package imports from a workspace: each package is
 * resolved from the real folder of the one before it.
 *
 * @param workspaceDir - The workspace's folder.
 * @param packages - The packages, in import order.
 * @param resolve - The lookup each import uses.
 * @returns The route and the real folder it ends in, or null in its place when some package does not resolve.
 */
function follow(
  workspaceDir: string,
  packages: readonly string[],
  resolve: TmPackageResolver,
): TmPackageRoute {
  let dir: string | null = realpathSync(workspaceDir);
  for (const name of packages) {
    dir = dir === null ? null : resolve(dir, name);
  }
  return { route: packages.join(' > '), dir };
}

/**
 * Reads a package's package.json.
 *
 * @param dir - The package's folder.
 * @returns Its version and its dependencies.
 */
function readPackage(dir: string): {
  readonly version?: string;
  readonly dependencies?: Record<string, string>;
} {
  return JSON.parse(readFileSync(join(dir, 'package.json'), 'utf8')) as {
    readonly version?: string;
    readonly dependencies?: Record<string, string>;
  };
}

/**
 * Lists every route by which one workspace's unit run loads Vitest and Vite:
 * the builder's own imports, the workspace's Vitest, and each browser
 * provider the workspace resolves, through its `@vitest/browser` and
 * `@vitest/mocker`. It also reads the sass the builder pins and the one it
 * resolves.
 *
 * @param workspaceDir - The workspace's folder.
 * @param resolve - The lookup each import uses; Node's by default.
 * @returns The routes, and the builder's sass.
 */
export function unitRunInstances(
  workspaceDir: string,
  resolve: TmPackageResolver = resolvePackageDir,
): TmUnitRunInstances {
  const builder = BUILDER_PACKAGE;
  const route = (packages: readonly string[]): TmPackageRoute =>
    follow(workspaceDir, packages, resolve);
  // The builder's own imports, which every unit run makes.
  const vitest: TmPackageRoute[] = [route([builder, 'vitest'])];
  const vite: TmPackageRoute[] = [
    route([builder, 'vite']),
    route([builder, 'vitest', 'vite']),
    route([builder, 'vitest', '@vitest/mocker', 'vite']),
  ];
  // The workspace's own Vitest, which its config files and specs import.
  const own = route(['vitest']);
  if (own.dir !== null) {
    vitest.push(own);
  }
  // Each browser provider the builder would load from the workspace, and what it brings.
  for (const provider of BROWSER_PROVIDERS) {
    if (route([provider]).dir === null) {
      continue;
    }
    vitest.push(route([provider, 'vitest']), route([provider, '@vitest/browser', 'vitest']));
    vite.push(
      route([provider, '@vitest/browser', 'vite']),
      route([provider, '@vitest/mocker', 'vite']),
    );
  }
  // The sass the builder's package.json names, and the version it resolves.
  const builderDir = route([builder]).dir;
  const sassDir = builderDir === null ? null : resolve(builderDir, 'sass');
  return {
    vitest,
    vite,
    sassPinned:
      builderDir === null ? null : (readPackage(builderDir).dependencies?.['sass'] ?? null),
    sassResolved: sassDir === null ? null : (readPackage(sassDir).version ?? null),
  };
}

/**
 * Describes one package's routes when they do not all end in one folder,
 * or when some route does not resolve.
 *
 * @param workspace - The workspace's name, as the problems name it.
 * @param name - The package's name.
 * @param routes - Every route to the package.
 * @param repoRoot - The repository root, against which the folders are shown.
 * @returns One line per problem, none when every route ends in one folder.
 */
function routeProblems(
  workspace: string,
  name: string,
  routes: readonly TmPackageRoute[],
  repoRoot: string,
): string[] {
  const problems = routes
    .filter((route) => route.dir === null)
    .map((route) => `${workspace}: ${route.route} does not resolve; run pnpm install.`);
  // Group the resolved routes by the folder they end in.
  const byDir = new Map<string, string[]>();
  for (const route of routes) {
    if (route.dir !== null) {
      byDir.set(route.dir, [...(byDir.get(route.dir) ?? []), route.route]);
    }
  }
  if (byDir.size > 1) {
    const copies = [...byDir].map(
      ([dir, via]) => `  ${relative(repoRoot, dir).replaceAll('\\', '/')} (${via.join('; ')})`,
    );
    problems.push(
      `${workspace}: one unit run loads ${byDir.size} copies of ${name}, which pnpm links for different peer versions ` +
        `(the folder names differ only in their peer hash). Keep one version of each of its peers in the graph, ` +
        `for example with an override in pnpm-workspace.yaml:\n${copies.join('\n')}`,
    );
  }
  return problems;
}

/**
 * Lists what is wrong with one workspace's unit run: more than one copy of
 * Vitest or Vite, a route that does not resolve, or a builder on a sass other
 * than the exact version its package.json names (an override left behind by
 * an upgrade of the builder).
 *
 * @param workspace - The workspace's name, as the problems name it.
 * @param instances - What the workspace's unit run loads.
 * @param repoRoot - The repository root, against which the folders are shown.
 * @returns One line per problem, none when the run loads one Vitest and one Vite.
 */
export function unitRunProblems(
  workspace: string,
  instances: TmUnitRunInstances,
  repoRoot: string,
): string[] {
  const problems = [
    ...routeProblems(workspace, 'vitest', instances.vitest, repoRoot),
    ...routeProblems(workspace, 'vite', instances.vite, repoRoot),
  ];
  // Only an exact pin can be compared; Angular pins sass exactly.
  const pinned = instances.sassPinned;
  if (pinned !== null && /^\d+\.\d+\.\d+$/.test(pinned) && instances.sassResolved !== pinned) {
    problems.push(
      `${workspace}: ${BUILDER_PACKAGE} pins sass ${pinned} but resolves ${instances.sassResolved ?? 'none'}; ` +
        `set the sass override in pnpm-workspace.yaml to ${pinned}.`,
    );
  }
  return problems;
}
