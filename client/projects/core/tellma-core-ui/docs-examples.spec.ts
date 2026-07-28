// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { form } from '@angular/forms/signals';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmAlert } from '@tellma/core-ui/alert';
import { TmButton } from '@tellma/core-ui/button';
import { TmCheckbox } from '@tellma/core-ui/checkbox';
import { TmDatePicker } from '@tellma/core-ui/date-picker';
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
import { TmModalFooter } from '@tellma/core-ui/modal';
import { TmNumber } from '@tellma/core-ui/number';
import { TmOption, TmSelect } from '@tellma/core-ui/select';
import { TmSpinner } from '@tellma/core-ui/spinner';
import { TmTab, TmTabContent, TmTabGroup, TmTabLabel } from '@tellma/core-ui/tabs';
import { TmTreeGrid } from '@tellma/core-ui/tree-grid';

import * as alertExamples from './alert/tm-alert.examples';
import * as buttonExamples from './button/tm-button.examples';
import * as checkboxExamples from './checkbox/tm-checkbox.examples';
import * as datePickerExamples from './date-picker/tm-date-picker.examples';
import * as gridExamples from './grid/tm-grid.examples';
import * as inputExamples from './input/tm-input.examples';
import * as menuExamples from './menu/tm-menu.examples';
import * as modalFooterExamples from './modal/tm-modal-footer.examples';
import * as numberExamples from './number/tm-number.examples';
import * as selectExamples from './select/tm-select.examples';
import * as spinnerExamples from './spinner/tm-spinner.examples';
import * as tabsExamples from './tabs/tm-tabs.examples';
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
    TmAlert,
    TmButton,
    TmCheckbox,
    TmContextMenuTrigger,
    TmDatePicker,
    TmFormField,
    TmGrid,
    TmGridColumn,
    TmGridDisplayDef,
    TmGridEmptyDef,
    TmGridLoadingDef,
    TmInput,
    TmMenu,
    TmModalFooter,
    TmNumber,
    TmOption,
    TmSelect,
    TmSpinner,
    TmTab,
    TmTabContent,
    TmTabGroup,
    TmTabLabel,
    TmTreeGrid,
  ],
  template: `
    <tm-form-field label="placeholder"><input tmInput /></tm-form-field>
    <tm-form-field label="placeholder"><input tmNumber /></tm-form-field>
    <tm-form-field label="placeholder"><tm-date-picker /></tm-form-field>
    <button tmButton>placeholder</button>
    <tm-alert kind="info">placeholder</tm-alert>
    <tm-tab-group>
      <tm-tab id="placeholder" label="placeholder">
        <ng-template tmTabLabel>placeholder</ng-template>
        <ng-template tmTabContent>placeholder</ng-template>
      </tm-tab>
    </tm-tab-group>
    <tm-checkbox>placeholder</tm-checkbox>
    <tm-select><tm-option [value]="0">placeholder</tm-option></tm-select>
    <tm-spinner />
    <div [tmContextMenuTrigger]="placeholderMenu">placeholder</div>
    <tm-menu #placeholderMenu [items]="[]" />
    <div tmModalFooter>placeholder</div>
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

  // The editable example's members: a Signal Forms field tree over the rows,
  // a new-row factory, and the view/edit toggle bound to `readonly`.
  protected readonly editing = signal(true);
  protected readonly lines = signal<ExampleRow[]>([...this.rows]);
  protected readonly lineForm = form(this.lines);
  private lineId = 100;
  protected readonly makeLine = (): ExampleRow => ({
    id: this.lineId++,
    name: '',
    qty: 0,
    price: 0,
    active: false,
    status: 'Draft',
  });
}

/** The example templates' grids — flat and tree — read off the debug tree. */
function grids(fixture: ComponentFixture<ExampleHost>): TmGrid<unknown>[] {
  return fixture.debugElement
    .queryAll(By.directive(TmGrid))
    .concat(fixture.debugElement.queryAll(By.directive(TmTreeGrid)))
    .map((element) => element.componentInstance as TmGrid<unknown>);
}

/**
 * Element selectors are covered by `errorOnUnknownElements` (an unmatched
 * `<tm-select>` throws NG0304), but an attribute DIRECTIVE that quietly
 * stopped matching is invisible to it — `<input tmInput>` is legal HTML with
 * or without the directive. So: any template that mentions an attribute
 * selector must actually instantiate its directive. The same blind spot
 * covers a renamed static-attribute INPUT: `errorOnUnknownProperties` flags
 * a stale `[selectable]` binding but not a bare `selectable` attribute
 * (legal HTML either way), so a template that names one must show its effect
 * on the live grid. (Element components are deliberately absent here:
 * tm-option instances live behind tm-select's ngTemplateOutlet, outside the
 * fixture's debug tree. And the grid's template directives sit on
 * unprojected CONTENT of tm-grid, which debug queries cannot reach — those
 * are read off the grid's content-query signals instead.)
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
    name: 'TmButton',
    pattern: /\btmButton\b/,
    instantiated: (fixture) => fixture.debugElement.queryAll(By.directive(TmButton)).length > 0,
  },
  {
    name: 'TmNumber',
    pattern: /\btmNumber\b/,
    instantiated: (fixture) => fixture.debugElement.queryAll(By.directive(TmNumber)).length > 0,
  },
  {
    // Projected ng-template directives never surface in the debug tree
    // (the tm-option caveat above), so the markers assert the RENDERED
    // effect: a matched tmTabContent template fills the active panel.
    name: 'TmTabContent',
    pattern: /\btmTabContent\b/,
    instantiated: (fixture) =>
      Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
          '.tm-tab-group__panel:not([inert])',
        ),
      ).some((panel) => (panel.textContent ?? '').trim() !== ''),
  },
  {
    // A matched tmTabLabel template renders the strip button's content —
    // unmatched, its tab would show an empty label.
    name: 'TmTabLabel',
    pattern: /\btmTabLabel\b/,
    instantiated: (fixture) =>
      Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.tm-tab-group__tab'),
      ).every((tab) => (tab.textContent ?? '').trim() !== ''),
  },
  {
    name: 'TmContextMenuTrigger',
    pattern: /\btmContextMenuTrigger\b/,
    instantiated: (fixture) =>
      fixture.debugElement.queryAll(By.directive(TmContextMenuTrigger)).length > 0,
  },
  {
    name: 'TmModalFooter',
    pattern: /\btmModalFooter\b/,
    instantiated: (fixture) =>
      fixture.debugElement.queryAll(By.directive(TmModalFooter)).length > 0,
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
  {
    name: 'selectable',
    pattern: /\bselectable\b/,
    instantiated: (fixture) => grids(fixture).some((grid) => grid.selectable()),
  },
  {
    name: 'searchable',
    pattern: /\bsearchable\b/,
    instantiated: (fixture) => grids(fixture).some((grid) => grid.searchable()),
  },
  {
    name: 'loading',
    pattern: /\bloading\b/,
    instantiated: (fixture) => grids(fixture).some((grid) => grid.loading()),
  },
  {
    name: 'hierarchy',
    pattern: /\bhierarchy\b/,
    instantiated: (fixture) =>
      grids(fixture).some((grid) => grid.columns().some((column) => column.hierarchy())),
  },
];

const SUITES = [
  { source: 'input/tm-input.examples.ts', examples: inputExamples },
  { source: 'number/tm-number.examples.ts', examples: numberExamples },
  { source: 'date-picker/tm-date-picker.examples.ts', examples: datePickerExamples },
  { source: 'alert/tm-alert.examples.ts', examples: alertExamples },
  { source: 'button/tm-button.examples.ts', examples: buttonExamples },
  { source: 'checkbox/tm-checkbox.examples.ts', examples: checkboxExamples },
  { source: 'select/tm-select.examples.ts', examples: selectExamples },
  { source: 'spinner/tm-spinner.examples.ts', examples: spinnerExamples },
  { source: 'tabs/tm-tabs.examples.ts', examples: tabsExamples },
  { source: 'modal/tm-modal-footer.examples.ts', examples: modalFooterExamples },
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
