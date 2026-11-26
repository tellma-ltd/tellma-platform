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
 * core's gates and fails there. This check compiles the projects once more
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
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { isDeepStrictEqual } from 'node:util';

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

/** A path in the fixtures' folder, as the compiler spells it. */
function fixturePath(name: string): string {
  return join(fixturesDir, name).replaceAll('\\', '/');
}

/**
 * The declarations' path in each program, beside the fixture, under the name
 * a distribution's build gives them. No file exists there: the host serves it.
 */
const declarationsPath = fixturePath('tellma-entities.d.ts');

/**
 * The probe's path, also served from memory. The probe is a source file, not
 * a declaration file, so it is checked even where the options skip
 * declaration files: if the declarations' augmentation does not reach the
 * registries (its module no longer resolves, the registry moved or was
 * renamed), the registries stay empty, their keys are `never`, and the probe
 * fails rather than the whole check passing with every row still open.
 */
const probePath = fixturePath('registries.probe.ts');

/** The files the host serves from memory, by path. */
const served = new Map<string, string>([
  [declarationsPath, readFileSync(join(fixturesDir, 'tellma-entities.d.ts.txt'), 'utf8')],
  [
    probePath,
    [
      "import type { TmEntityShapes, TmEntityTypes } from '@tellma/core';",
      '',
      '// Each assignment holds only while the emitted declarations register core.Notification.',
      "export const row: keyof TmEntityTypes = 'core.Notification';",
      "export const shape: keyof TmEntityShapes = 'core.Notification';",
      '',
    ].join('\n'),
  ],
]);

/** How diagnostics print: paths relative to the client workspace, lines as the platform ends them. */
const formatHost: ts.FormatDiagnosticsHost = {
  getCanonicalFileName: (name) => name,
  getCurrentDirectory: () => clientRoot,
  getNewLine: () => ts.sys.newLine,
};

/** A path as this check compares paths: forward slashes, and lower case where the file system ignores case. */
function canonical(name: string): string {
  const normalized = name.replaceAll('\\', '/');
  return ts.sys.useCaseSensitiveFileNames ? normalized : normalized.toLowerCase();
}

/** The in-memory files, by canonical path. */
const servedByCanonical = new Map([...served].map(([name, text]) => [canonical(name), text]));

/** Reads a project's own options and files, exactly as its plain typecheck reads them. */
function parse(project: string): ts.ParsedCommandLine {
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
  return parsed;
}

/** A project's options without the one member that always differs: the path of the file they came from. */
function comparableOptions(parsed: ts.ParsedCommandLine): ts.CompilerOptions {
  const options = { ...parsed.options };
  delete options['configFilePath'];
  return options;
}

/** Compiles some files with some options and the in-memory files added; returns the error diagnostics. */
function check(
  parsed: ts.ParsedCommandLine,
  fileNames: readonly string[],
): readonly ts.Diagnostic[] {
  // --- A host that serves the in-memory files and reads every other file from disk ---
  const host = ts.createCompilerHost(parsed.options, true);
  const readFile = host.readFile.bind(host);
  const fileExists = host.fileExists.bind(host);
  const getSourceFile = host.getSourceFile.bind(host);
  host.readFile = (name) => servedByCanonical.get(canonical(name)) ?? readFile(name);
  host.fileExists = (name) => servedByCanonical.has(canonical(name)) || fileExists(name);
  host.getSourceFile = (name, language, onError, shouldCreate) => {
    const text = servedByCanonical.get(canonical(name));
    return text === undefined
      ? getSourceFile(name, language, onError, shouldCreate)
      : ts.createSourceFile(name, text, language, true);
  };

  const program = ts.createProgram({
    rootNames: [...fileNames, ...served.keys()],
    options: parsed.options,
    host,
    projectReferences: parsed.projectReferences,
  });
  return ts
    .getPreEmitDiagnostics(program)
    .filter((diagnostic) => diagnostic.category === ts.DiagnosticCategory.Error);
}

// --- The programs: one over every project's files while their options agree, else one per project ---

// core-testing's program reaches core's sources through the @tellma/core
// path mapping, so with the same options one program checks each source once.
const parsedProjects = PROJECTS.map((project) => ({ project, parsed: parse(project) }));
const configErrors = parsedProjects.flatMap(({ parsed }) => parsed.errors);
if (configErrors.length > 0) {
  console.error(ts.formatDiagnosticsWithColorAndContext(configErrors, formatHost));
  process.exit(1);
}
const [first] = parsedProjects;
const sameOptions = parsedProjects.every(
  ({ parsed }) =>
    isDeepStrictEqual(comparableOptions(parsed), comparableOptions(first!.parsed)) &&
    isDeepStrictEqual(parsed.projectReferences, first!.parsed.projectReferences),
);
const programs = sameOptions
  ? [
      {
        name: PROJECTS.join(' + '),
        parsed: first!.parsed,
        fileNames: [
          ...new Map(
            parsedProjects.flatMap(({ parsed }) =>
              parsed.fileNames.map((name) => [canonical(name), name]),
            ),
          ).values(),
        ],
      },
    ]
  : parsedProjects.map(({ project, parsed }) => ({
      name: project,
      parsed,
      fileNames: parsed.fileNames,
    }));
if (!sameOptions) {
  console.log(
    'typecheck:emitted: the projects compile with different options, so each is checked on its own.',
  );
}

// --- Every program, each reported in full before the exit code is set ---

let failed = false;
for (const { name, parsed, fileNames } of programs) {
  const started = performance.now();
  const errors = check(parsed, fileNames);
  const seconds = ((performance.now() - started) / 1000).toFixed(1);
  if (errors.length === 0) {
    console.log(
      `typecheck:emitted: ${name} compiles against the emitted declarations (${seconds} s).`,
    );
    continue;
  }
  failed = true;
  console.error(ts.formatDiagnosticsWithColorAndContext(errors, formatHost));
  if (
    errors.some(
      (diagnostic) =>
        diagnostic.file !== undefined &&
        canonical(diagnostic.file.fileName) === canonical(probePath),
    )
  ) {
    console.error(
      "typecheck:emitted: the emitted declarations do not register core.Notification in @tellma/core's TmEntityTypes and TmEntityShapes, so they close no row of core and the rest of this check proves nothing.",
    );
  }
  console.error(
    `typecheck:emitted: ${name} has ${errors.length} error(s) against the emitted declarations (${seconds} s).`,
  );
}
process.exit(failed ? 1 : 0);
