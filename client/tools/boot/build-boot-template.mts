// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Writes `@tellma/core`'s boot script template (`boot/tm-boot.template.js`),
 * a package asset generated from the boot script's TypeScript before the
 * package builds.
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

import { bootTemplateFile, buildBootTemplate } from './boot-template.mjs';

const text = await buildBootTemplate();
mkdirSync(dirname(bootTemplateFile), { recursive: true });
// Write only on a real change, so an unchanged template keeps its mtime and
// does not look like a new build input.
if (!existsSync(bootTemplateFile) || readFileSync(bootTemplateFile, 'utf8') !== text) {
  writeFileSync(bootTemplateFile, text);
}
console.log(`boot template OK -> ${bootTemplateFile}`);
