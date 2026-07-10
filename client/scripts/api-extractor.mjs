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
import { mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const clientDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(join(clientDir, 'package.json'));
const { Extractor, ExtractorConfig } = require('@microsoft/api-extractor');

/** Every published entry point (the MCP Node package is out of golden scope). */
const ENTRY_POINTS = [
  { report: 'core-ui.api.md', dts: 'dist/core-ui/types/tellma-core-ui.d.ts' },
  { report: 'core-ui.contracts.api.md', dts: 'dist/core-ui/types/tellma-core-ui-contracts.d.ts' },
  { report: 'core-ui.input.api.md', dts: 'dist/core-ui/types/tellma-core-ui-input.d.ts' },
  { report: 'core-ui.checkbox.api.md', dts: 'dist/core-ui/types/tellma-core-ui-checkbox.d.ts' },
  {
    report: 'core-ui.form-field.api.md',
    dts: 'dist/core-ui/types/tellma-core-ui-form-field.d.ts',
  },
  { report: 'core-ui.select.api.md', dts: 'dist/core-ui/types/tellma-core-ui-select.d.ts' },
  { report: 'core-ui-tokens.api.md', dts: 'dist/core-ui-tokens/types/tellma-core-ui-tokens.d.ts' },
  {
    report: 'core-ui-testing.api.md',
    dts: 'dist/core-ui-testing/types/tellma-core-ui-testing.d.ts',
  },
  { report: 'locale-ar.api.md', dts: 'dist/locale-ar/types/tellma-locale-ar.d.ts' },
];

const approve = process.argv.includes('--approve');
const reportFolder = join(clientDir, 'api');
const tempFolder = join(clientDir, '.artifacts', 'api');
mkdirSync(reportFolder, { recursive: true });
mkdirSync(tempFolder, { recursive: true });

let failed = false;

for (const entryPoint of ENTRY_POINTS) {
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
