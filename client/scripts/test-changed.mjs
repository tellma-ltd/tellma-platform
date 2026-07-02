/**
 * Changed-test selection (spec §10): on PRs, run the unit suites of every
 * package whose sources changed against the merge base PLUS the direct
 * consumers of any changed package (a contracts or tokens change re-tests
 * the components); on main/release, the full suite always runs.
 *
 *   node scripts/test-changed.mjs           # changed + consumers
 *   node scripts/test-changed.mjs --all     # full suite (main/release)
 *
 * Implemented as a static project graph over `git diff --name-only`
 * (plan D7): vitest's own --changed cannot see the Angular builder's
 * compilation or the cross-project consumer rule.
 */
import { execFileSync, spawnSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const clientDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(join(clientDir, 'package.json'));
const ng = require.resolve('@angular/cli/bin/ng.js');

/** path prefix (repo-relative) → the ng projects to re-test. */
const GRAPH = [
  // Tokens feed every component's CSS + the sandbox theme — test everything.
  {
    prefix: 'client/projects/core/tellma-core-ui-tokens/',
    projects: ['core-ui-tokens', 'core-ui', 'core-ui-testing', 'locale-ar', 'sandbox'],
  },
  // core-ui (incl. contracts) — consumers: testing, locale-ar, sandbox.
  {
    prefix: 'client/projects/core/tellma-core-ui/',
    projects: ['core-ui', 'core-ui-testing', 'locale-ar', 'sandbox'],
  },
  // Harness changes re-test the component suites that drive them.
  {
    prefix: 'client/projects/core/tellma-core-ui-testing/',
    projects: ['core-ui-testing', 'core-ui', 'locale-ar'],
  },
  { prefix: 'client/projects/locale/tellma-locale-ar/', projects: ['locale-ar'] },
  { prefix: 'client/projects/internal/sandbox/', projects: ['sandbox'] },
];

/** Workspace-tooling paths run the tools vitest suite. */
const TOOLS_PREFIXES = ['client/tools/', 'client/scripts/', 'client/projects/core/tellma-core-ui-mcp/'];

/** Workspace-config paths invalidate everything. */
const GLOBAL_PREFIXES = [
  'client/package.json',
  'client/pnpm-lock.yaml',
  'client/pnpm-workspace.yaml',
  'client/angular.json',
  'client/tsconfig.json',
  'client/eslint.config.mjs',
  'client/stylelint.config.mjs',
];

const ALL_PROJECTS = ['core-ui-tokens', 'core-ui', 'core-ui-testing', 'locale-ar', 'sandbox'];

function run(command, args) {
  const result = spawnSync(command, args, { cwd: clientDir, stdio: 'inherit' });
  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }
}

const all = process.argv.includes('--all');
let projects = new Set();
let runTools = false;

if (all) {
  projects = new Set(ALL_PROJECTS);
  runTools = true;
} else {
  const baseRef = process.env.TM_TEST_BASE ?? 'origin/main';
  const mergeBase = execFileSync('git', ['merge-base', 'HEAD', baseRef], {
    cwd: clientDir,
    encoding: 'utf8',
  }).trim();
  const changed = execFileSync('git', ['diff', '--name-only', mergeBase], {
    cwd: clientDir,
    encoding: 'utf8',
  })
    .split('\n')
    .filter(Boolean);

  for (const file of changed) {
    if (GLOBAL_PREFIXES.some((prefix) => file === prefix)) {
      projects = new Set(ALL_PROJECTS);
      runTools = true;
      break;
    }
    for (const edge of GRAPH) {
      if (file.startsWith(edge.prefix)) {
        for (const project of edge.projects) {
          projects.add(project);
        }
      }
    }
    if (TOOLS_PREFIXES.some((prefix) => file.startsWith(prefix))) {
      runTools = true;
    }
  }

  if (projects.size === 0 && !runTools) {
    console.log('test-changed: no client changes against the merge base — nothing to test.');
    process.exit(0);
  }
}

console.log(
  `test-changed: projects [${[...projects].join(', ')}]${runTools ? ' + tools' : ''}${all ? ' (full)' : ''}`,
);

for (const project of ALL_PROJECTS.filter((p) => projects.has(p))) {
  run(process.execPath, [ng, 'test', project]);
}
if (runTools) {
  run(process.execPath, [require.resolve('vitest/vitest.mjs'), 'run', '--config', 'tools/vitest.config.mts']);
}
