// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

import { tmFindAbove } from '../common/files.js';
import { TmBuildFailure } from '../common/problems.js';
import type { TmAppProject } from '../project/app-project.js';
import { tmLogStep, type TmCommandContext } from './context.js';

// Inside the platform's own repository, a distribution's client resolves
// @tellma/core to its sources, where the generated assets a published
// package ships (the token stylesheets and the boot script template) are
// gitignored. `serve` and `build` write them first there; outside the
// repository this step does nothing.

/** The platform repository's script that writes the generated assets its packages ship. */
export const TM_PLATFORM_ASSETS_SCRIPT = 'assets:generate';

/**
 * Inside the platform's repository, writes the generated assets a published
 * package holds: the nearest pnpm workspace whose package.json has the
 * script runs it.
 *
 * @param context - The process seam and streams.
 * @param project - The client project.
 * @throws TmBuildFailure when the script fails.
 */
export async function tmWritePlatformAssets(context: TmCommandContext, project: TmAppProject): Promise<void> {
  const root = tmFindAbove(project.dir, 'pnpm-workspace.yaml');
  if (root === null || !existsSync(join(root, 'package.json'))) {
    return;
  }
  const scripts = (JSON.parse(readFileSync(join(root, 'package.json'), 'utf8')) as { readonly scripts?: Record<string, string> }).scripts;
  if (scripts?.[TM_PLATFORM_ASSETS_SCRIPT] === undefined) {
    return;
  }
  tmLogStep(context, "Writing the platform's generated assets");
  const code = await context.runner.run('pnpm', ['run', TM_PLATFORM_ASSETS_SCRIPT], { cwd: root });
  if (code !== 0) {
    throw new TmBuildFailure(`pnpm run ${TM_PLATFORM_ASSETS_SCRIPT} exited with code ${code}.`);
  }
}
