// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Summarises a Playwright JSON report of the quarantined (`@quarantine`)
 * e2e tests: one row per test and project, counting how many of its runs
 * (one per `--repeat-each` repetition) passed, flaked (failed, then passed on
 * retry) or failed outright, with a verdict and the first error seen. A
 * quarantined test that only fails is no longer a flake: it is broken, and
 * the verdict says so.
 *
 * The table goes to the GitHub step summary when `GITHUB_STEP_SUMMARY` is
 * set, and to stdout always. The script reports; it never decides the job's
 * outcome, except that a missing or unreadable report is itself a failure,
 * since it means the suite did not finish.
 *
 * Usage: node scripts/e2e-quarantine-summary.mjs <report.json>
 */
import { appendFileSync, existsSync, readFileSync } from 'node:fs';
import { stripVTControlCharacters } from 'node:util';

/** Longest error excerpt kept per test, in characters. */
const ERROR_EXCERPT_LENGTH = 300;

/**
 * Every spec under a list of `describe` suites, depth first, with its
 * describe-title path.
 *
 * @param {Array<object> | undefined} suites JSON-reporter describe suites.
 * @param {string[]} titlePath Describe titles above `suites`.
 * @returns {Array<{ spec: object, titlePath: string[] }>}
 */
function collectSpecs(suites, titlePath) {
  const found = [];
  for (const suite of suites ?? []) {
    const path = [...titlePath, suite.title];
    for (const spec of suite.specs ?? []) {
      found.push({ spec, titlePath: path });
    }
    found.push(...collectSpecs(suite.suites, path));
  }
  return found;
}

/**
 * Every spec in a report. The top-level suites are files, whose title (the
 * file name) is not part of a test's name.
 *
 * @param {Array<object> | undefined} fileSuites The report's `suites`.
 * @returns {Array<{ spec: object, titlePath: string[] }>}
 */
function collectReportSpecs(fileSuites) {
  const found = [];
  for (const file of fileSuites ?? []) {
    for (const spec of file.specs ?? []) {
      found.push({ spec, titlePath: [] });
    }
    found.push(...collectSpecs(file.suites, []));
  }
  return found;
}

/**
 * The first error message among a test run's attempts, flattened to one
 * line, without ANSI colour codes and cut to a readable length.
 *
 * @param {object} run A JSON-reporter test entry (one project, one repetition).
 * @returns {string | undefined}
 */
function firstError(run) {
  for (const result of run.results ?? []) {
    const message = result.error?.message ?? result.errors?.[0]?.message;
    if (message) {
      const flat = stripVTControlCharacters(message).replace(/\s+/g, ' ').trim();
      return flat.length > ERROR_EXCERPT_LENGTH ? `${flat.slice(0, ERROR_EXCERPT_LENGTH)}…` : flat;
    }
  }
  return undefined;
}

/** Escapes a value for a cell of a Markdown table. */
function cell(text) {
  return String(text).replaceAll('|', '\\|');
}

// --- Read the report ---------------------------------------------------------

const reportPath = process.argv[2];
let report;
if (reportPath === undefined || !existsSync(reportPath)) {
  report = undefined;
} else {
  try {
    report = JSON.parse(readFileSync(reportPath, 'utf8'));
  } catch {
    report = undefined;
  }
}

/** Writes the summary to stdout and, on GitHub Actions, the step summary. */
function emit(markdown) {
  console.log(markdown);
  const summaryFile = process.env['GITHUB_STEP_SUMMARY'];
  if (summaryFile) {
    appendFileSync(summaryFile, `${markdown}\n`);
  }
}

if (report === undefined) {
  emit(
    `## Quarantined e2e tests\n\nNo readable Playwright report at \`${reportPath ?? '(none given)'}\`: the run did not finish.`,
  );
  process.exit(1);
}

// --- Aggregate per test and project ------------------------------------------

/** @type {Map<string, { name: string, location: string, project: string, passed: number, flaky: number, failed: number, skipped: number, error?: string }>} */
const rows = new Map();
for (const { spec, titlePath } of collectReportSpecs(report.suites)) {
  for (const run of spec.tests ?? []) {
    const key = `${spec.file}:${spec.line}:${spec.column} ${run.projectName}`;
    let row = rows.get(key);
    if (row === undefined) {
      row = {
        name: [...titlePath, spec.title].join(' › '),
        location: `${spec.file}:${spec.line}`,
        project: run.projectName,
        passed: 0,
        flaky: 0,
        failed: 0,
        skipped: 0,
      };
      rows.set(key, row);
    }
    // Playwright's per-run outcome: expected (passed first time), flaky
    // (failed, then passed on retry), unexpected (failed every attempt),
    // skipped.
    switch (run.status) {
      case 'expected':
        row.passed += 1;
        break;
      case 'flaky':
        row.flaky += 1;
        row.error ??= firstError(run);
        break;
      case 'unexpected':
        row.failed += 1;
        row.error ??= firstError(run);
        break;
      default:
        row.skipped += 1;
    }
  }
}

// --- Render ------------------------------------------------------------------

/** The verdict of one row: broken when no run passed, even on retry. */
function verdict(row) {
  if (row.failed > 0 && row.passed === 0 && row.flaky === 0) {
    return 'failing every run';
  }
  if (row.failed > 0 || row.flaky > 0) {
    return 'flaky';
  }
  return row.passed > 0 ? 'passed every run' : 'skipped';
}

const sorted = [...rows.values()].sort(
  (a, b) => a.location.localeCompare(b.location) || a.project.localeCompare(b.project),
);
const lines = ['## Quarantined e2e tests', ''];
if (sorted.length === 0) {
  lines.push('No test is quarantined.');
} else {
  lines.push(
    `${sorted.length} row(s), one per quarantined test and browser project. Each count is runs; "Flaky" means a run failed and then passed on retry.`,
    '',
    '| Test | Location | Project | Passed | Flaky | Failed | Verdict | First error |',
    '|---|---|---|---|---|---|---|---|',
  );
  for (const row of sorted) {
    lines.push(
      `| ${cell(row.name)} | ${cell(row.location)} | ${cell(row.project)} | ${row.passed} | ${row.flaky} | ${row.failed} | ${verdict(row)} | ${cell(row.error ?? '')} |`,
    );
  }
}
emit(lines.join('\n'));
