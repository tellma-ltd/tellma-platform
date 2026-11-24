// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Type-checks @tellma/core's sources, and @tellma/core-testing's, against the
 * entity declarations a distribution emits for core's own entities:
 *
 *   pnpm run typecheck:emitted
 *
 * The plain typecheck compiles core with `TmEntityTypes` and `TmEntityShapes`
 * empty, so `TmEntityType<'core.Notification'>` is an open JSON object there
 * and any member name indexes it. A distribution's build compiles the same
 * sources beside its emitted `tellma-entities.d.ts`, which closes those rows
 * to their wire interfaces; code that only holds while they are open passes
 * core's gates and fails there. This check compiles each project once more
 * with the declarations the platform alone emits, so such code fails here.
 *
 * The declarations are the server's own output, kept as text in
 * `fixtures/tellma-entities.d.ts.txt`: the server's suite writes the file
 * from a composition of the platform alone and fails when the file no longer
 * matches, so it follows every change to core's model. As a declaration file
 * on disk it would augment @tellma/core in every program a tool builds over
 * the workspace, so this check serves it to the compiler from memory.
 */
import { readFileSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import ts from 'typescript';

/** The client workspace's root. */
const clientRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');

/** The folder of this check's fixture. */
const fixturesDir = join(clientRoot, 'tools', 'typecheck', 'fixtures');

/** The projects checked, each by its typecheck view: every source file, specs included. */
const PROJECTS = [
  'projects/core/tellma-core/tsconfig.json',
  'projects/core/tellma-core-testing/tsconfig.json',
];

/**
 * The declarations' path in each program, beside the fixture, under the name
 * a distribution's build gives them. No file exists there: the host serves it.
 */
const declarationsPath = join(fixturesDir, 'tellma-entities.d.ts').replaceAll('\\', '/');

/** The declarations' text. */
const declarations = readFileSync(join(fixturesDir, 'tellma-entities.d.ts.txt'), 'utf8');

// --- A fixture that registers no core entity would leave every row open and check nothing ---

if (!/interface TmEntityTypes \{[^}]*'core\.\w+':/.test(declarations)) {
  console.error(
    'typecheck:emitted: the fixture registers no core entity in TmEntityTypes, so it closes no row of core.',
  );
  process.exit(1);
}

/** How diagnostics print: paths relative to the client workspace, lines as the platform ends them. */
const formatHost: ts.FormatDiagnosticsHost = {
  getCanonicalFileName: (name) => name,
  getCurrentDirectory: () => clientRoot,
  getNewLine: () => ts.sys.newLine,
};

/** Whether a path the compiler asks for is the in-memory declarations. */
function isDeclarations(name: string): boolean {
  const normalized = name.replaceAll('\\', '/');
  return ts.sys.useCaseSensitiveFileNames
    ? normalized === declarationsPath
    : normalized.toLowerCase() === declarationsPath.toLowerCase();
}

/** Checks one project with the declarations added; returns its error diagnostics. */
function check(project: string): readonly ts.Diagnostic[] {
  // --- The project's own options and files, exactly as its plain typecheck reads them ---
  const parsed = ts.getParsedCommandLineOfConfigFile(
    join(clientRoot, project),
    {},
    {
      ...ts.sys,
      onUnRecoverableConfigFileDiagnostic: (diagnostic) => {
        throw new Error(ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n'));
      },
    },
  );
  if (parsed === undefined) {
    throw new Error(`${project} did not parse.`);
  }
  if (parsed.errors.length > 0) {
    return parsed.errors;
  }

  // --- A host that serves the declarations from memory and every other file from disk ---
  const host = ts.createCompilerHost(parsed.options, true);
  const readFile = host.readFile.bind(host);
  const fileExists = host.fileExists.bind(host);
  const getSourceFile = host.getSourceFile.bind(host);
  host.readFile = (name) => (isDeclarations(name) ? declarations : readFile(name));
  host.fileExists = (name) => isDeclarations(name) || fileExists(name);
  host.getSourceFile = (name, language, onError, shouldCreate) =>
    isDeclarations(name)
      ? ts.createSourceFile(name, declarations, language, true)
      : getSourceFile(name, language, onError, shouldCreate);

  const program = ts.createProgram({
    rootNames: [...parsed.fileNames, declarationsPath],
    options: parsed.options,
    host,
    projectReferences: parsed.projectReferences,
  });
  return ts
    .getPreEmitDiagnostics(program)
    .filter((diagnostic) => diagnostic.category === ts.DiagnosticCategory.Error);
}

/** A diagnostic's identity across programs: its file, position and code. */
function keyOf(diagnostic: ts.Diagnostic): string {
  return `${diagnostic.file?.fileName ?? ''}:${diagnostic.start ?? -1}:${diagnostic.code}`;
}

// --- Every project, each reported in full before the exit code is set ---

// @tellma/core-testing's program holds core's sources too, so an error in
// core shows in both programs; each is printed once, where it first shows.
const reported = new Set<string>();
let failed = false;
for (const project of PROJECTS) {
  const started = performance.now();
  const errors = check(project);
  const seconds = ((performance.now() - started) / 1000).toFixed(1);
  if (errors.length === 0) {
    console.log(
      `typecheck:emitted: ${project} compiles against the emitted declarations (${seconds} s).`,
    );
    continue;
  }
  failed = true;
  const fresh = errors.filter((diagnostic) => !reported.has(keyOf(diagnostic)));
  fresh.forEach((diagnostic) => reported.add(keyOf(diagnostic)));
  if (fresh.length > 0) {
    console.error(ts.formatDiagnosticsWithColorAndContext(fresh, formatHost));
  }
  console.error(
    `typecheck:emitted: ${project} has ${errors.length} error(s) against the emitted declarations of ${relative(clientRoot, declarationsPath).replaceAll('\\', '/')}, ${errors.length - fresh.length} of them shown for an earlier project (${seconds} s).`,
  );
}
process.exit(failed ? 1 : 0);
