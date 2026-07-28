// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * API goldens per entry point (spec §10, D11/DoD 14): Microsoft API
 * Extractor runs against ng-packagr's flattened dist .d.ts and emits a
 * diff-able *.api.md snapshot per public surface, committed under
 * client/api/.
 *
 *   node scripts/api-extractor.mjs --check     # CI gate: fail on drift
 *   node scripts/api-extractor.mjs --approve   # regenerate the goldens
 *
 * Every public-API change is therefore an explicit, reviewed act
 * (`pnpm run api:approve` + commit).
 */
import { createRequire } from 'node:module';

import { discoverLibraries } from '../tools/workspace.mjs';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const clientDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(join(clientDir, 'package.json'));
const { Extractor, ExtractorConfig } = require('@microsoft/api-extractor');

/**
 * Every published entry point, discovered from the workspace (any folder
 * with an ng-package.json). The MCP Node package has none, so it stays out
 * of golden scope by construction. `publicApi` is the source entry point
 * whose `@packageDocumentation` header is restored into the flattened d.ts
 * (see `prepareDts`).
 */
const ENTRY_POINTS = discoverLibraries(
  resolve(dirname(fileURLToPath(import.meta.url)), '..'),
).flatMap((library) => library.entryPoints);

/** The fixed note injected above Angular's generated static members. */
const GENERATED_MEMBER_NOTE =
  '/** Angular compiler metadata — generated machinery, not for direct use. */';

/**
 * Restores documentation the build pipeline cannot carry, patching the
 * flattened d.ts in place (comments only — declarations are never touched;
 * idempotent, and the build regenerates the files anyway):
 *
 *  - ng-packagr's d.ts flattener (rollup-plugin-dts) drops the entry point's
 *    leading comment, so the `@packageDocumentation` block is copied
 *    verbatim from the source public-api.ts to the top of the rollup.
 *  - The Angular compiler emits its ɵcmp/ɵdir/ɵfac metadata statics with no
 *    TSDoc and no way to attach any in source, so those get a fixed note.
 */
function prepareDts(dtsFullPath, publicApiFullPath) {
  const original = readFileSync(dtsFullPath, 'utf8');
  let dts = original;

  // A file-level source comment may survive flattening, so "starts with a
  // comment" is not enough — the FIRST comment must carry the package tag.
  const firstComment = /^\/\*\*[\s\S]*?\*\//.exec(dts);
  if (!firstComment || !firstComment[0].includes('@packageDocumentation')) {
    const header = /^\/\*\*[\s\S]*?\*\//.exec(readFileSync(publicApiFullPath, 'utf8'));
    if (!header || !header[0].includes('@packageDocumentation')) {
      throw new Error(
        `${publicApiFullPath} must start with a /** … @packageDocumentation */ header ` +
          `so it can be restored into the flattened ${dtsFullPath}.`,
      );
    }
    dts = `${header[0]}\n${dts}`;
  }

  const lines = dts.split('\n');
  const out = [];
  for (const line of lines) {
    const generated = /^([ \t]*)static ɵ\w+/.exec(line);
    const previous = out.length > 0 ? out[out.length - 1].trim() : '';
    if (generated && !previous.endsWith('*/')) {
      out.push(`${generated[1]}${GENERATED_MEMBER_NOTE}`);
    }
    out.push(line);
  }
  dts = out.join('\n');

  if (dts !== original) {
    writeFileSync(dtsFullPath, dts);
  }
}

/**
 * Matches a report line that DECLARES an `ɵ`-prefixed export — the
 * private-by-convention surface shared entry points expose (the tree grid
 * builds on the grid's ɵ internals), excluded from the API goldens by
 * spec. Anchored to a declaration keyword so a public declaration that
 * merely REFERENCES a ɵ type (`class TmGrid extends ɵTmGridBase`) never
 * matches.
 */
const INTERNAL_DECLARATION = /^export\s+(?:declare\s+)?(?:abstract\s+)?(?:class|interface|type|const|enum|function|let|var|namespace)\s+ɵ/mu;

/**
 * Matches an `ɵ`-prefixed INSTANCE member inside a (public) class body — the
 * private-by-convention fields/methods a shared class exposes for its
 * siblings. These must leave the golden too (and with them their references
 * to internal types). The Angular compiler's `static ɵcmp/ɵfac/ɵprov`
 * metadata is deliberately NOT matched — `static` sits before the `ɵ`, so it
 * stays (documented by the generated-member note).
 */
const INTERNAL_MEMBER = /^[ \t]+(?:readonly\s+|get\s+|set\s+|abstract\s+|protected\s+)*ɵ\w/u;

/**
 * Drops every `ɵ`-declaring block from a generated .api.md report. The
 * report's fenced ```ts section is a sequence of blank-line-separated
 * blocks — imports first, then declarations, each led by its `// @public`
 * (or warning) marker comments — so filtering groups the fence body into
 * blank-line-separated blocks and drops the ones whose declaration line
 * exports a ɵ name; everything outside the fence passes through
 * untouched. The input is normalized to LF first (the extractor emits
 * CRLF on Windows), which also keeps the committed goldens byte-identical
 * across platforms. Applied to BOTH the approve and the check path, so
 * the goldens and the comparison always see the same shape.
 */
function stripPrivateByConvention(report) {
  const lines = report.replaceAll('\r\n', '\n').split('\n');
  const open = lines.indexOf('```ts');
  const close = lines.lastIndexOf('```');
  if (open === -1 || close <= open) {
    return lines.join('\n');
  }
  const kept = lines.slice(0, open + 1);
  let block = [];
  const flush = () => {
    if (!block.some((line) => INTERNAL_DECLARATION.test(line))) {
      // Within a kept (public) block, still drop any ɵ instance members and
      // the generated-member note that would immediately precede one.
      for (let i = 0; i < block.length; i++) {
        if (INTERNAL_MEMBER.test(block[i])) {
          if (kept.length > 0 && kept[kept.length - 1].includes(GENERATED_MEMBER_NOTE)) {
            kept.pop();
          }
          continue;
        }
        kept.push(block[i]);
      }
    }
    block = [];
  };
  for (const line of lines.slice(open + 1, close)) {
    block.push(line);
    if (line === '') {
      flush(); // blocks are blank-line-separated; the blank stays with its block
    }
  }
  flush();
  kept.push(...lines.slice(close));
  return kept.join('\n');
}

const approve = process.argv.includes('--approve');
const reportFolder = join(clientDir, 'api');
const tempFolder = join(clientDir, '.artifacts', 'api');
mkdirSync(reportFolder, { recursive: true });
mkdirSync(tempFolder, { recursive: true });

let failed = false;

for (const entryPoint of ENTRY_POINTS) {
  prepareDts(join(clientDir, entryPoint.dts), entryPoint.publicApi);

  const config = ExtractorConfig.prepare({
    configObjectFullPath: undefined,
    packageJsonFullPath: join(clientDir, 'package.json'),
    configObject: {
      projectFolder: clientDir,
      mainEntryPointFilePath: join(clientDir, entryPoint.dts),
      compiler: {
        overrideTsconfig: {
          compilerOptions: {
            target: 'es2022',
            module: 'esnext',
            moduleResolution: 'bundler',
            skipLibCheck: true,
            types: [],
            // Cross-package d.ts imports resolve against the BUILT packages.
            paths: {
              '@tellma/core-ui': ['./dist/core-ui'],
              '@tellma/core-ui/*': ['./dist/core-ui/*'],
              '@tellma/core-ui-tokens': ['./dist/core-ui-tokens'],
              '@tellma/core-ui-testing': ['./dist/core-ui-testing'],
            },
          },
        },
      },
      apiReport: {
        enabled: true,
        reportFileName: entryPoint.report,
        // The extractor always writes to the temp folder; the committed
        // golden is written/compared AFTER the ɵ filter below, so both
        // paths see the same filtered shape.
        reportFolder: tempFolder,
        reportTempFolder: tempFolder,
      },
      docModel: { enabled: false },
      dtsRollup: { enabled: false },
      tsdocMetadata: { enabled: false },
      messages: {
        extractorMessageReporting: {
          // Angular d.ts references internal aria/CDK types by design.
          'ae-forgotten-export': { logLevel: 'none' },
          'ae-unresolved-link': { logLevel: 'none' },
        },
        tsdocMessageReporting: {
          default: { logLevel: 'none' },
        },
      },
    },
  });

  const result = Extractor.invoke(config, {
    // localBuild keeps the temp report current; the golden itself is
    // managed below, after the ɵ filter.
    localBuild: true,
    messageCallback: (message) => {
      message.handled = false;
    },
  });
  if (!result.succeeded) {
    console.error(`api-extractor failed for ${entryPoint.report}`);
    failed = true;
    continue;
  }

  // The golden-shaped report: the generated surface minus the ɵ blocks
  // (private-by-convention, excluded from the goldens by spec).
  const report = stripPrivateByConvention(
    readFileSync(join(tempFolder, entryPoint.report), 'utf8'),
  );

  // Documentation gate: every public member carries TSDoc and every entry
  // point a @packageDocumentation — neither marker may reach a golden.
  // (ɵ blocks are already filtered out, so the gate never counts them.)
  const undocumented = (report.match(/\/\/ \(undocumented\)/g) ?? []).length;
  if (undocumented > 0) {
    console.error(
      `${entryPoint.report}: ${undocumented} undocumented public member(s) — every public export needs TSDoc.`,
    );
    failed = true;
  }
  if (report.includes('(No @packageDocumentation comment for this package)')) {
    console.error(`${entryPoint.report}: the entry point is missing its @packageDocumentation.`);
    failed = true;
  }

  const goldenPath = join(reportFolder, entryPoint.report);
  if (approve) {
    writeFileSync(goldenPath, report);
    console.log(`approved ${entryPoint.report}`);
  } else if (
    !existsSync(goldenPath) ||
    // EOL-normalized compare: goldens are LF, but a checkout may not be.
    readFileSync(goldenPath, 'utf8').replaceAll('\r\n', '\n') !== report
  ) {
    console.error(
      `API DRIFT: ${entryPoint.report} no longer matches the public surface. ` +
        `Review the change and run \`pnpm run api:approve\` to accept it.`,
    );
    failed = true;
  } else {
    console.log(`ok ${entryPoint.report}`);
  }
}

process.exit(failed ? 1 : 0);
