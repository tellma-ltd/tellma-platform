// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, type Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmCheckbox } from '@tellma/core-ui/checkbox';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TmInput } from '@tellma/core-ui/input';
import { TmOption, TmSelect } from '@tellma/core-ui/select';
import { TmSpinner } from '@tellma/core-ui/spinner';

import * as checkboxExamples from './checkbox/tm-checkbox.examples';
import * as inputExamples from './input/tm-input.examples';
import * as selectExamples from './select/tm-select.examples';
import * as spinnerExamples from './spinner/tm-spinner.examples';

/**
 * The `*.examples.ts` templates ship as canonical usage in components.json /
 * llms.txt (spec 0002 §11) but are plain strings the Angular template
 * compiler never sees — without this spec, renaming a selector or an input
 * leaves the published examples silently broken. Each example is compiled
 * and rendered here against the live library API.
 *
 * (Spec-only file: the relative reach into the secondary-entry-point folders
 * is fine because ng-packagr never compiles specs, and the examples objects
 * are dependency-free data.)
 */

/**
 * One reusable host: the vitest builder AOT-compiles specs, so a decorator
 * template must be static (NG1010) — each example is swapped in at runtime
 * via `TestBed.overrideComponent`, which JIT-compiles it like an app would.
 * The placeholder template uses every import (NG8113 flags unused ones).
 */
@Component({
  imports: [TmCheckbox, TmFormField, TmInput, TmOption, TmSelect, TmSpinner],
  template: `
    <tm-form-field label="placeholder"><input tmInput /></tm-form-field>
    <tm-checkbox>placeholder</tm-checkbox>
    <tm-select><tm-option [value]="0">placeholder</tm-option></tm-select>
    <tm-spinner />
  `,
})
class ExampleHost {}

/**
 * Element selectors are covered by `errorOnUnknownElements` (an unmatched
 * `<tm-select>` throws NG0304), but an attribute DIRECTIVE that quietly
 * stopped matching is invisible to it — `<input tmInput>` is legal HTML with
 * or without the directive. So: any template that mentions an attribute
 * selector must actually instantiate its directive. (Element components are
 * deliberately absent here: tm-option instances live behind tm-select's
 * ngTemplateOutlet, outside the fixture's debug tree.)
 */
const MARKERS: { type: Type<unknown>; pattern: RegExp }[] = [
  { type: TmInput, pattern: /\btmInput\b/ },
];

const SUITES = [
  { source: 'input/tm-input.examples.ts', examples: inputExamples },
  { source: 'checkbox/tm-checkbox.examples.ts', examples: checkboxExamples },
  { source: 'select/tm-select.examples.ts', examples: selectExamples },
  { source: 'spinner/tm-spinner.examples.ts', examples: spinnerExamples },
];

describe('co-located docs examples compile against the live API (§11)', () => {
  for (const { source, examples } of SUITES) {
    describe(source, () => {
      for (const [title, { template }] of Object.entries(examples)) {
        it(`'${title}' compiles, renders, and instantiates what it names`, async () => {
          TestBed.configureTestingModule({
            providers: [provideTellmaUi()],
            errorOnUnknownElements: true,
            errorOnUnknownProperties: true,
          });
          TestBed.overrideComponent(ExampleHost, { set: { template } });

          const fixture = TestBed.createComponent(ExampleHost);
          fixture.detectChanges();
          await fixture.whenStable();

          expect(fixture.nativeElement.children.length, 'example rendered nothing').toBeGreaterThan(
            0,
          );
          for (const { type, pattern } of MARKERS.filter((m) => m.pattern.test(template))) {
            expect(
              fixture.debugElement.queryAll(By.directive(type)).length,
              `template mentions ${pattern} but ${type.name} never instantiated — renamed selector?`,
            ).toBeGreaterThan(0);
          }
        });
      }
    });
  }
});
