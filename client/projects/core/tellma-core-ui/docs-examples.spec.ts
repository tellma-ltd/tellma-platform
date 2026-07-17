// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmCheckbox } from '@tellma/core-ui/checkbox';
import { TmFormField } from '@tellma/core-ui/form-field';
import {
  TmGrid,
  TmGridColumn,
  TmGridDisplayDef,
  TmGridEmptyDef,
  TmGridLoadingDef,
} from '@tellma/core-ui/grid';
import { TmInput } from '@tellma/core-ui/input';
import { TmContextMenuTrigger, TmMenu } from '@tellma/core-ui/menu';
import { TmOption, TmSelect } from '@tellma/core-ui/select';
import { TmSpinner } from '@tellma/core-ui/spinner';
import { TmTreeGrid } from '@tellma/core-ui/tree-grid';

import * as checkboxExamples from './checkbox/tm-checkbox.examples';
import * as gridExamples from './grid/tm-grid.examples';
import * as inputExamples from './input/tm-input.examples';
import * as menuExamples from './menu/tm-menu.examples';
import * as selectExamples from './select/tm-select.examples';
import * as spinnerExamples from './spinner/tm-spinner.examples';
import * as treeGridExamples from './tree-grid/tm-tree-grid.examples';

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

/** The row shape behind the grid examples' `rows`/`rowId`/`total` bindings. */
interface ExampleRow {
  readonly id: number;
  readonly name: string;
  readonly qty: number;
  readonly price: number;
  readonly active: boolean;
  readonly status: string;
}

/** The adjacency-list row shape behind the tree-grid examples' bindings. */
interface ExampleTreeRow {
  readonly id: number;
  readonly parentId: number | null;
  readonly name: string;
  readonly qty: number;
}

/**
 * One reusable host: the vitest builder AOT-compiles specs, so a decorator
 * template must be static (NG1010) — each example is swapped in at runtime
 * via `TestBed.overrideComponent`, which JIT-compiles it like an app would.
 * The placeholder template uses every import (NG8113 flags unused ones).
 *
 * The members exist for the grid examples: `rowId` is a required
 * function-valued input, which no template literal can express — the
 * examples bind host members exactly like a real consumer component.
 */
@Component({
  imports: [
    TmCheckbox,
    TmContextMenuTrigger,
    TmFormField,
    TmGrid,
    TmGridColumn,
    TmGridDisplayDef,
    TmGridEmptyDef,
    TmGridLoadingDef,
    TmInput,
    TmMenu,
    TmOption,
    TmSelect,
    TmSpinner,
    TmTreeGrid,
  ],
  template: `
    <tm-form-field label="placeholder"><input tmInput /></tm-form-field>
    <tm-checkbox>placeholder</tm-checkbox>
    <tm-select><tm-option [value]="0">placeholder</tm-option></tm-select>
    <tm-spinner />
    <div [tmContextMenuTrigger]="placeholderMenu">placeholder</div>
    <tm-menu #placeholderMenu [items]="[]" />
    <tm-grid gridId="placeholder-grid" [data]="rows" [rowId]="rowId" style="block-size: 120px">
      <tm-grid-column key="name" header="placeholder">
        <span *tmGridDisplay="let value">{{ value }}</span>
      </tm-grid-column>
      <span *tmGridEmpty>placeholder</span>
      <span *tmGridLoading>placeholder</span>
    </tm-grid>
    <tm-tree-grid
      gridId="placeholder-tree"
      [data]="treeRows"
      [rowId]="treeRowId"
      [parentId]="treeParentId"
      style="block-size: 120px"
    >
      <tm-grid-column key="name" header="placeholder" />
    </tm-tree-grid>
  `,
})
class ExampleHost {
  protected readonly rows: readonly ExampleRow[] = [
    { id: 1, name: 'Anvil', qty: 3, price: 120, active: true, status: 'Posted' },
    { id: 2, name: 'Rope', qty: 12, price: 8.5, active: false, status: 'Draft' },
  ];
  protected readonly rowId = (row: ExampleRow): number => row.id;
  protected readonly total = (row: ExampleRow): number => row.qty * row.price;
  protected readonly statuses: readonly string[] = ['Draft', 'Posted', 'Void'];

  protected readonly treeRows: readonly ExampleTreeRow[] = [
    { id: 1, parentId: null, name: 'Assets', qty: 2 },
    { id: 2, parentId: 1, name: 'Cash', qty: 5 },
    { id: 3, parentId: 1, name: 'Receivables', qty: 8 },
    { id: 4, parentId: null, name: 'Liabilities', qty: 3 },
  ];
  protected readonly treeRowId = (row: ExampleTreeRow): number => row.id;
  protected readonly treeParentId = (row: ExampleTreeRow): number | null => row.parentId;
  protected readonly treeHasChildren = (row: ExampleTreeRow): boolean => row.parentId === null;
  protected readonly loadTreeChildren = (): Promise<void> => Promise.resolve();
}

/** The example templates' grids, read off the debug tree. */
function grids(fixture: ComponentFixture<ExampleHost>): TmGrid<unknown>[] {
  return fixture.debugElement
    .queryAll(By.directive(TmGrid))
    .map((element) => element.componentInstance as TmGrid<unknown>);
}

/**
 * Element selectors are covered by `errorOnUnknownElements` (an unmatched
 * `<tm-select>` throws NG0304), but an attribute DIRECTIVE that quietly
 * stopped matching is invisible to it — `<input tmInput>` is legal HTML with
 * or without the directive. So: any template that mentions an attribute
 * selector must actually instantiate its directive. (Element components are
 * deliberately absent here: tm-option instances live behind tm-select's
 * ngTemplateOutlet, outside the fixture's debug tree. And the grid's
 * template directives sit on unprojected CONTENT of tm-grid, which debug
 * queries cannot reach — those are read off the grid's content-query
 * signals instead.)
 */
const MARKERS: {
  name: string;
  pattern: RegExp;
  instantiated: (fixture: ComponentFixture<ExampleHost>) => boolean;
}[] = [
  {
    name: 'TmInput',
    pattern: /\btmInput\b/,
    instantiated: (fixture) => fixture.debugElement.queryAll(By.directive(TmInput)).length > 0,
  },
  {
    name: 'TmContextMenuTrigger',
    pattern: /\btmContextMenuTrigger\b/,
    instantiated: (fixture) =>
      fixture.debugElement.queryAll(By.directive(TmContextMenuTrigger)).length > 0,
  },
  {
    name: 'TmGridDisplayDef',
    pattern: /\btmGridDisplay\b/,
    instantiated: (fixture) =>
      grids(fixture).some((grid) =>
        grid.columns().some((column) => column.displayDef() !== undefined),
      ),
  },
  {
    name: 'TmGridEmptyDef',
    pattern: /\btmGridEmpty\b/,
    instantiated: (fixture) => grids(fixture).some((grid) => grid.emptyDef() !== undefined),
  },
  {
    name: 'TmGridLoadingDef',
    pattern: /\btmGridLoading\b/,
    instantiated: (fixture) => grids(fixture).some((grid) => grid.loadingDef() !== undefined),
  },
];

const SUITES = [
  { source: 'input/tm-input.examples.ts', examples: inputExamples },
  { source: 'checkbox/tm-checkbox.examples.ts', examples: checkboxExamples },
  { source: 'select/tm-select.examples.ts', examples: selectExamples },
  { source: 'spinner/tm-spinner.examples.ts', examples: spinnerExamples },
  { source: 'menu/tm-menu.examples.ts', examples: menuExamples },
  { source: 'grid/tm-grid.examples.ts', examples: gridExamples },
  { source: 'tree-grid/tm-tree-grid.examples.ts', examples: treeGridExamples },
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
          for (const { name, pattern, instantiated } of MARKERS.filter((m) =>
            m.pattern.test(template),
          )) {
            expect(
              instantiated(fixture),
              `template mentions ${pattern} but ${name} never instantiated — renamed selector?`,
            ).toBe(true);
          }
        });
      }
    });
  }
});
