// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * tokens:build-css — emits the static stylesheets (§4: build-time emission,
 * zero runtime style generation) into the package's css/ assets folder.
 */
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';

const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const outDir = join(clientDir, 'projects', 'core', 'tellma-core-ui-tokens', 'css');

mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, 'tellma-default.css'), tmEmitCss(tmTokensDefault));
console.log(`tokens:build-css OK -> ${join(outDir, 'tellma-default.css')}`);
