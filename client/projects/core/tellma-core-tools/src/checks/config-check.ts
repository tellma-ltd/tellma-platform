// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

import type { TmProcessRunner } from '../common/process.js';
import { TmBuildFailure, type TmBuildProblem } from '../common/problems.js';
import type { TmEmittedMetadata } from '../emit/emitted-metadata.js';
import type { TmAppProject } from '../project/app-project.js';

// The build's config check (spec 0024 §3.6): the application's merged
// configuration against the emitted metadata, in a Node process of its own
// (config-check-runner). Its errors fail the build; its `label-missing`
// warnings print and pass. The string packs go along, so a pack key in the
// platform's reserved namespaces is reported as `string-key-collision`.

/** The runner's compiled file; the specs run from src/ and the command from dist/, both two levels down. */
const RUNNER = fileURLToPath(new URL('../../dist/checks/config-check-runner.js', import.meta.url));

/** A problem as tmCheckAppConfig reports it. */
interface ConfigProblem {
  readonly code: string;
  readonly severity: 'error' | 'warning';
  readonly key: string;
  readonly feature: string;
  readonly message: string;
}

/** What the runner answers. */
type RunnerAnswer = { readonly problems: readonly ConfigProblem[] } | { readonly failure: string };

/**
 * Runs the config check over the application's `src/app.config.ts`.
 *
 * @param project - The client project.
 * @param metadata - The emitted metadata.
 * @param runner - Starts the check's Node process.
 * @returns The problems, errors and warnings both.
 * @throws TmBuildFailure when the configuration cannot be loaded or the check cannot run.
 */
export async function tmRunConfigCheck(
  project: TmAppProject,
  metadata: TmEmittedMetadata,
  runner: TmProcessRunner,
): Promise<TmBuildProblem[]> {
  if (!existsSync(project.appConfig)) {
    throw new TmBuildFailure(`${project.appConfig} does not exist; the config check reads the application's configuration from it.`);
  }
  const assets = join(metadata.dir, 'assets');
  const work = mkdtempSync(join(tmpdir(), 'tellma-config-check-'));
  try {
    // --- The request: the application's files and the emitted chunks ---
    const request = join(work, 'request.json');
    const result = join(work, 'result.json');
    writeFileSync(
      request,
      JSON.stringify({
        tsconfig: project.tsconfig,
        appConfig: project.appConfig,
        core: join(assets, metadata.core.name),
        entities: join(assets, metadata.entities.name),
        strings: Object.fromEntries([...metadata.strings].map(([code, file]) => [code, join(assets, file.name)])),
        result,
      }),
    );
    const code = await runner.run(process.execPath, [RUNNER, request], { cwd: project.dir });
    if (code !== 0 || !existsSync(result)) {
      throw new TmBuildFailure(`The config check exited with code ${code} without an answer.`);
    }

    // --- The answer: problems, or why the configuration could not be read ---
    const answer = JSON.parse(readFileSync(result, 'utf8')) as RunnerAnswer;
    if ('failure' in answer) {
      throw new TmBuildFailure(`The config check could not run: ${answer.failure}`);
    }
    return answer.problems.map(({ code: problemCode, severity, key, feature, message }) => ({
      code: problemCode,
      severity,
      key,
      feature,
      message,
    }));
  } finally {
    rmSync(work, { recursive: true, force: true });
  }
}
