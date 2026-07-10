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
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const clientDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(join(clientDir, 'package.json'));
const { Extractor, ExtractorConfig } = require('@microsoft/api-extractor');

/**
 * Every published entry point (the MCP Node package is out of golden scope).
 * `publicApi` is the source entry point whose `@packageDocumentation` header
 * is restored into the flattened d.ts (see `prepareDts`).
 */
const ENTRY_POINTS = [
  {
    report: 'core-ui.api.md',
    dts: 'dist/core-ui/types/tellma-core-ui.d.ts',
    publicApi: 'projects/core/tellma-core-ui/public-api.ts',
  },
  {
    report: 'core-ui.contracts.api.md',
    dts: 'dist/core-ui/types/tellma-core-ui-contracts.d.ts',
    publicApi: 'projects/core/tellma-core-ui/contracts/public-api.ts',
  },
  {
    report: 'core-ui.input.api.md',
    dts: 'dist/core-ui/types/tellma-core-ui-input.d.ts',
    publicApi: 'projects/core/tellma-core-ui/input/public-api.ts',
  },
  {
    report: 'core-ui.checkbox.api.md',
    dts: 'dist/core-ui/types/tellma-core-ui-checkbox.d.ts',
    publicApi: 'projects/core/tellma-core-ui/checkbox/public-api.ts',
  },
  {
    report: 'core-ui.form-field.api.md',
    dts: 'dist/core-ui/types/tellma-core-ui-form-field.d.ts',
    publicApi: 'projects/core/tellma-core-ui/form-field/public-api.ts',
  },
  {
    report: 'core-ui.select.api.md',
    dts: 'dist/core-ui/types/tellma-core-ui-select.d.ts',
    publicApi: 'projects/core/tellma-core-ui/select/public-api.ts',
  },
  {
    report: 'core-ui-tokens.api.md',
    dts: 'dist/core-ui-tokens/types/tellma-core-ui-tokens.d.ts',
    publicApi: 'projects/core/tellma-core-ui-tokens/public-api.ts',
  },
  {
    report: 'core-ui-testing.api.md',
    dts: 'dist/core-ui-testing/types/tellma-core-ui-testing.d.ts',
    publicApi: 'projects/core/tellma-core-ui-testing/public-api.ts',
  },
  {
    report: 'locale-ar.api.md',
    dts: 'dist/locale-ar/types/tellma-locale-ar.d.ts',
    publicApi: 'projects/locale/tellma-locale-ar/public-api.ts',
  },
];

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

const approve = process.argv.includes('--approve');
const reportFolder = join(clientDir, 'api');
const tempFolder = join(clientDir, '.artifacts', 'api');
mkdirSync(reportFolder, { recursive: true });
mkdirSync(tempFolder, { recursive: true });

let failed = false;

for (const entryPoint of ENTRY_POINTS) {
  prepareDts(join(clientDir, entryPoint.dts), join(clientDir, entryPoint.publicApi));

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
        reportFolder,
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
    // localBuild=true updates the committed report; false = CI compare mode
    // that fails when the temp report differs from the committed golden.
    localBuild: approve,
    messageCallback: (message) => {
      message.handled = false;
    },
  });

  // Documentation gate: every public member carries TSDoc and every entry
  // point a @packageDocumentation — neither marker may reach a golden.
  const report = readFileSync(join(reportFolder, entryPoint.report), 'utf8');
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

  if (approve) {
    console.log(`approved ${entryPoint.report}`);
  } else if (result.apiReportChanged) {
    console.error(
      `API DRIFT: ${entryPoint.report} no longer matches the public surface. ` +
        `Review the change and run \`pnpm run api:approve\` to accept it.`,
    );
    failed = true;
  } else if (!result.succeeded) {
    console.error(`api-extractor failed for ${entryPoint.report}`);
    failed = true;
  } else {
    console.log(`ok ${entryPoint.report}`);
  }
}

process.exit(failed ? 1 : 0);
