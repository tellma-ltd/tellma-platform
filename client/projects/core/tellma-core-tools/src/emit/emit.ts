// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync } from 'node:fs';

import type { TmProcessRunner } from '../common/process.js';
import { TmBuildFailure } from '../common/problems.js';
import type { TmAppProject } from '../project/app-project.js';

// `tellma-client emit` (spec 0024 §2.6, §3.2): the distribution's Migrator
// realises its composition with no database and writes the client metadata,
// the loaders module and the generated types into the project's generated/
// folder. The Migrator owns the files and their names; this command only
// runs it.

/** How the emit runs. */
export interface TmEmitOptions {
  /** The .NET build configuration of the Migrator, such as `Release`; the .NET default when absent. */
  readonly configuration?: string;
  /** Whether the Migrator is already built, as when MSBuild built it before the client project. */
  readonly noDotnetBuild?: boolean;
}

/**
 * Runs the Migrator's `emit-client-assets` into the project's `generated/`.
 *
 * @param project - The client project.
 * @param options - The configuration and whether to skip the .NET build.
 * @param runner - Starts the .NET CLI.
 * @throws TmBuildFailure when the project names no Migrator, or the Migrator fails.
 */
export async function tmRunEmit(project: TmAppProject, options: TmEmitOptions, runner: TmProcessRunner): Promise<void> {
  if (project.migratorDir === null) {
    throw new TmBuildFailure('package.json names no Migrator: add "tellma": { "migrator": "<path of the Migrator project\'s folder>" }.');
  }
  if (!existsSync(project.migratorDir)) {
    throw new TmBuildFailure(`The Migrator project ${project.migratorDir}, named by "tellma.migrator" in package.json, does not exist.`);
  }

  // The Migrator reads its appsettings.json from its own content root, so it
  // runs in its project folder; the output folder is absolute for that reason.
  const args = ['run', '--project', project.migratorDir, '--no-launch-profile'];
  if (options.configuration !== undefined) {
    args.push('--configuration', options.configuration);
  }
  if (options.noDotnetBuild === true) {
    args.push('--no-build');
  }
  args.push('--', 'emit-client-assets', project.generatedDir);

  const code = await runner.run('dotnet', args, { cwd: project.migratorDir });
  if (code !== 0) {
    throw new TmBuildFailure(`The Migrator's emit-client-assets exited with code ${code}.`);
  }
}
