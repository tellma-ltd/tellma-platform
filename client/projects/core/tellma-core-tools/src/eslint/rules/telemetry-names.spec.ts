// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { join } from 'node:path';

import tseslint from 'typescript-eslint';
import { describe } from 'vitest';

import { createRuleTester } from '../../../fixtures/rule-tester.js';
import { tmTelemetryNames } from './telemetry-names.js';

// Each case below runs as a test of its own (see createRuleTester).

// Without type information the rule goes by the receiver's name.
const untyped = createRuleTester({
  languageOptions: { parser: tseslint.parser as never, ecmaVersion: 2022, sourceType: 'module' },
});

// With type information it goes by the receiver's type, against the stand-in
// services beside the fixture project's tsconfig.
const fixture = join(import.meta.dirname, '..', 'fixtures', 'telemetry');
const filename = join(fixture, 'file.ts');
const header = "import { AcmeCalendar, TmFakeTelemetry, TmPageActions, TmTelemetry } from './services';\n";
const typed = createRuleTester({
  languageOptions: {
    parser: tseslint.parser as never,
    parserOptions: {
      projectService: { allowDefaultProject: ['*.ts'], defaultProject: 'tsconfig.json' },
      tsconfigRootDir: fixture,
    },
  },
});

/** A typed valid case: the header, then `body`. */
const valid = (body: string) => ({ name: body, code: header + body, filename });

/** A typed invalid case reporting `messageId` once. */
const invalid = (body: string, messageId: string) => ({ name: body, code: header + body, filename, errors: [{ messageId }] });

describe('tm/telemetry-names', { timeout: 120_000 }, () => {
  describe('checks literal names by the receiver’s name, without type information', () => {
    untyped.run('tm/telemetry-names', tmTelemetryNames as never, {
      valid: [
        { code: "telemetry.event('tm.api.slow', { route: 'centers/query' });" },
        { code: "this.#telemetry.timing('acme.credit-exposure.run', 120);" },
        { code: "inject(TmTelemetry).event('gl.center.archived.bulk');" },
        { code: "telemetry.timing(cold ? 'tm.launch.cold' : 'tm.launch.warm', 5);" },
        { code: 'telemetry.event(name);' },
        { code: "calendar.event('Board meeting');" },
        { code: "actions.invoke('gl.center.activate', {}, 'user');" },
        { code: "const action = { id: 'acme.credit-review.suggest-limits', labelKey: 'Acme_Suggest', invoke: () => undefined };" },
        { code: "const banner = { id: 'Acme banner', labelKey: 'Acme_Banner' };" },
        { code: "router.invoke('Not an id');" },
      ],
      invalid: [
        { code: "telemetry.event('acme.report');", errors: [{ messageId: 'badName', data: { name: 'acme.report' } }] },
        { code: "this.#telemetry.event('Acme.report.run');", errors: [{ messageId: 'badName' }] },
        { code: "this.telemetry.event('acme.re_port.run');", errors: [{ messageId: 'badName' }] },
        { code: "telemetry.event('acme.report.a.b.c.d.e');", errors: [{ messageId: 'badName' }] },
        { code: `telemetry.event('acme.report.${'x'.repeat(60)}');`, errors: [{ messageId: 'badName' }] },
        { code: 'telemetry.event(`acme report run`);', errors: [{ messageId: 'badName' }] },
        { code: "inject(TmTelemetry).timing('tm.widget.loaded', 5);", errors: [{ messageId: 'unknownArea', data: { area: 'widget' } }] },
        { code: "telemetry.timing(cold ? 'tm.launch.cold' : 'tm.lunch.warm', 5);", errors: [{ messageId: 'unknownArea', data: { area: 'lunch' } }] },
        { code: "pageActions.invoke('Activate', {}, 'user');", errors: [{ messageId: 'badActionId', data: { id: 'Activate' } }] },
        { code: "inject(TmPageActions).invoke('gl', {}, 'user');", errors: [{ messageId: 'badActionId' }] },
        {
          code: "const action = { id: 'acme.credit_review.suggest', labelKey: 'Acme_Suggest', invoke: () => undefined };",
          errors: [{ messageId: 'badActionId' }],
        },
        {
          code: "const action = { id: 'a.b.c.d.e.f', labelKey: 'Acme_Suggest', invoke() {} };",
          errors: [{ messageId: 'badActionId' }],
        },
      ],
    });
  });

  describe('checks literal names by the receiver’s type, with type information', () => {
    typed.run('tm/telemetry-names', tmTelemetryNames as never, {
      valid: [
        valid("declare const t: TmTelemetry; t.event('tm.session.expired');"),
        valid("declare const t: TmFakeTelemetry; t.timing('acme.report.render', 5);"),
        valid("declare const telemetry: AcmeCalendar; telemetry.event('Board meeting');"),
        valid("declare const a: TmPageActions; void a.invoke('search.refresh', {}, 'user');"),
        valid("declare const actions: { invoke(id: string): void }; actions.invoke('Not an id');"),
      ],
      invalid: [
        invalid("declare const t: TmTelemetry; t.event('acme.report');", 'badName'),
        invalid("declare const fake: TmFakeTelemetry; fake.event('tm.nope.run');", 'unknownArea'),
        invalid("declare const t: TmTelemetry | null; t?.timing('Acme.Report.Run', 1);", 'badName'),
        invalid("declare const a: TmPageActions; void a.invoke('Search', {}, 'user');", 'badActionId'),
      ],
    });
  });
});
