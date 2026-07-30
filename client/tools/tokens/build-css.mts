// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * tokens:build-css — emits the static stylesheets (§4: build-time emission,
 * zero runtime style generation) into the package's css/ assets folder.
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';

const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const outDir = join(clientDir, 'projects', 'core', 'tellma-core-ui-tokens', 'css');
const outFile = join(outDir, 'tellma-default.css');

mkdirSync(outDir, { recursive: true });
const css = tmEmitCss(tmTokensDefault);
// Write only on a real change: an identical rewrite still moves the file's
// mtime, and this stylesheet is a build input whose mtime decides whether the
// static e2e server may reuse an existing bundle (scripts/serve.mjs).
if (!existsSync(outFile) || readFileSync(outFile, 'utf8') !== css) {
  writeFileSync(outFile, css);
}
console.log(`tokens:build-css OK -> ${outFile}`);
