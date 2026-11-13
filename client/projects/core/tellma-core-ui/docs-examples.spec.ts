// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { form } from '@angular/forms/signals';
import { Check, ChevronRight, CircleAlert, CircleX, Inbox, Plus, Search, SearchX } from 'lucide';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmAlert } from '@tellma/core-ui/alert';
import { TmAvatar } from '@tellma/core-ui/avatar';
import { TmBadge, TmCounter } from '@tellma/core-ui/badge';
import { TmBannerBar } from '@tellma/core-ui/banner';
import { TmButton } from '@tellma/core-ui/button';
import { TmCard } from '@tellma/core-ui/card';
import { TmCheckbox } from '@tellma/core-ui/checkbox';
import { TmChip, TmChipSet } from '@tellma/core-ui/chip';
import { TmCopyButton, TmReference } from '@tellma/core-ui/copy';
import { TmDatePicker } from '@tellma/core-ui/date-picker';
import { TmDescriptionList } from '@tellma/core-ui/description-list';
import { TmEntityPicker } from '@tellma/core-ui/entity-picker';
import { TmEmptyState } from '@tellma/core-ui/empty-state';
import { TmDropzone, TmFilePicker } from '@tellma/core-ui/files';
import { TmFormField } from '@tellma/core-ui/form-field';
import {
  TmGrid,
  TmGridColumn,
  TmGridDisplayDef,
  TmGridDriver,
  TmGridEmptyDef,
  TmGridLoadingDef,
  type TmGridRowActivation,
  type TmGridSort,
} from '@tellma/core-ui/grid';
import { TmIcon, provideTellmaIcons } from '@tellma/core-ui/icon';
import { TmImage, TmImagePlaceholder } from '@tellma/core-ui/image';
import { TmInput } from '@tellma/core-ui/input';
import { TmKbd } from '@tellma/core-ui/kbd';
import { TmContextMenuTrigger, TmMenu, TmMenuHeaderDef, TmMenuTrigger } from '@tellma/core-ui/menu';
import { TmModalFooter } from '@tellma/core-ui/modal';
import { TmNumber } from '@tellma/core-ui/number';
import { TmPageHeader } from '@tellma/core-ui/page-header';
import { TmPopover, TmPopoverContent, TmPopoverTrigger } from '@tellma/core-ui/popover';
import { TmProgressBar, TmProgressRing } from '@tellma/core-ui/progress';
import { TmRelativeTime } from '@tellma/core-ui/relative-time';
import { TmMultiSelect, TmOption, TmOptionGroup, TmSelect } from '@tellma/core-ui/select';
import { TmSkeleton } from '@tellma/core-ui/skeleton';
import { TmSpinner } from '@tellma/core-ui/spinner';
import { TmSplitter, TmSplitterPane } from '@tellma/core-ui/splitter';
import { TmTab, TmTabContent, TmTabGroup, TmTabLabel } from '@tellma/core-ui/tabs';
import { TmTooltip } from '@tellma/core-ui/tooltip';
import { TmTreeGrid } from '@tellma/core-ui/tree-grid';

import * as alertExamples from './alert/tm-alert.examples';
import * as avatarExamples from './avatar/tm-avatar.examples';
import * as badgeExamples from './badge/tm-badge.examples';
import * as counterExamples from './badge/tm-counter.examples';
import * as bannerBarExamples from './banner/tm-banner-bar.examples';
import * as buttonExamples from './button/tm-button.examples';
import * as cardExamples from './card/tm-card.examples';
import * as checkboxExamples from './checkbox/tm-checkbox.examples';
import * as chipExamples from './chip/tm-chip.examples';
import * as chipSetExamples from './chip/tm-chip-set.examples';
import * as copyButtonExamples from './copy/tm-copy-button.examples';
import * as referenceExamples from './copy/tm-reference.examples';
import * as datePickerExamples from './date-picker/tm-date-picker.examples';
import * as descriptionListExamples from './description-list/tm-description-list.examples';
import * as entityPickerExamples from './entity-picker/tm-entity-picker.examples';
import * as emptyStateExamples from './empty-state/tm-empty-state.examples';
import * as dropzoneExamples from './files/tm-dropzone.examples';
import * as filePickerExamples from './files/tm-file-picker.examples';
import * as gridExamples from './grid/tm-grid.examples';
import * as iconExamples from './icon/tm-icon.examples';
import * as imageExamples from './image/tm-image.examples';
import * as inputExamples from './input/tm-input.examples';
import * as kbdExamples from './kbd/tm-kbd.examples';
import * as menuExamples from './menu/tm-menu.examples';
import * as menuTriggerExamples from './menu/tm-menu-trigger.examples';
import * as modalFooterExamples from './modal/tm-modal-footer.examples';
import * as numberExamples from './number/tm-number.examples';
import * as pageHeaderExamples from './page-header/tm-page-header.examples';
import * as popoverExamples from './popover/tm-popover.examples';
import * as progressBarExamples from './progress/tm-progress-bar.examples';
import * as progressRingExamples from './progress/tm-progress-ring.examples';
import * as relativeTimeExamples from './relative-time/tm-relative-time.examples';
import * as multiSelectExamples from './select/tm-multi-select.examples';
import * as selectExamples from './select/tm-select.examples';
import * as skeletonExamples from './skeleton/tm-skeleton.examples';
import * as spinnerExamples from './spinner/tm-spinner.examples';
import * as splitterExamples from './splitter/tm-splitter.examples';
import * as tabsExamples from './tabs/tm-tabs.examples';
import * as tooltipExamples from './tooltip/tm-tooltip.examples';
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

/** A trivial modal page behind the entity-picker examples' page bindings. */
@Component({ template: `placeholder` })
class ExamplePage {}

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
    TmAvatar,
    TmBadge,
    TmBannerBar,
    TmButton,
    TmCard,
    TmCheckbox,
    TmChip,
    TmChipSet,
    TmContextMenuTrigger,
    TmCopyButton,
    TmCounter,
    TmDatePicker,
    TmDescriptionList,
    TmDropzone,
    TmEmptyState,
    TmEntityPicker,
    TmFilePicker,
    TmFormField,
    TmGrid,
    TmGridColumn,
    TmGridDisplayDef,
    TmGridDriver,
    TmGridEmptyDef,
    TmGridLoadingDef,
    TmIcon,
    TmImage,
    TmImagePlaceholder,
    TmInput,
    TmKbd,
    TmMenu,
    TmMenuHeaderDef,
    TmMenuTrigger,
    TmModalFooter,
    TmMultiSelect,
    TmNumber,
    TmOption,
    TmOptionGroup,
    TmPageHeader,
    TmPopover,
    TmPopoverContent,
    TmPopoverTrigger,
    TmProgressBar,
    TmProgressRing,
    TmReference,
    TmRelativeTime,
    TmSelect,
    TmSkeleton,
    TmSpinner,
    TmSplitter,
    TmSplitterPane,
    TmTab,
    TmTabContent,
    TmTabGroup,
    TmTabLabel,
    TmTooltip,
    TmTreeGrid,
  ],
  template: `
    <tm-form-field label="placeholder"><input tmInput /></tm-form-field>
    <tm-form-field label="placeholder"><input tmNumber /></tm-form-field>
    <tm-form-field label="placeholder"><tm-date-picker /></tm-form-field>
    <tm-entity-picker
      [search]="searchAgents"
      [itemId]="agentId"
      [itemLabel]="agentName"
      aria-label="placeholder"
    />
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
    <tm-multi-select aria-label="placeholder">
      <tm-option-group label="placeholder"
        ><tm-option [value]="0">placeholder</tm-option></tm-option-group
      >
    </tm-multi-select>
    <tm-spinner />
    <tm-badge>placeholder</tm-badge>
    <tm-chip-set><tm-chip label="placeholder" /></tm-chip-set>
    <tm-banner-bar message="placeholder" />
    <tm-counter [value]="1" />
    <tm-avatar name="placeholder" />
    <tm-progress-bar label="placeholder" />
    <tm-progress-ring />
    <tm-skeleton />
    <tm-relative-time value="2026-01-01T00:00:00Z" />
    <tm-copy-button text="placeholder" />
    <tm-reference value="placeholder" />
    <tm-empty-state heading="placeholder" />
    <tm-icon name="plus" />
    <tm-kbd keys="Mod+K" />
    <div [tmContextMenuTrigger]="placeholderMenu">placeholder</div>
    <button [tmMenuTriggerFor]="placeholderMenu">placeholder</button>
    <tm-menu #placeholderMenu [items]="[]">
      <ng-template tmMenuHeader>placeholder</ng-template>
    </tm-menu>
    <div tmModalFooter>placeholder</div>
    <tm-card heading="placeholder">placeholder</tm-card>
    <tm-page-header><h1 tmPageTitle>placeholder</h1></tm-page-header>
    <dl tmDescriptionList><div><dt>placeholder</dt><dd>placeholder</dd></div></dl>
    <tm-splitter [defaultSize]="100">
      <div tmSplitterPane="start">placeholder</div>
      <div tmSplitterPane="end">placeholder</div>
    </tm-splitter>
    <tm-image src="" alt="" [width]="24" [height]="24">
      <ng-template tmImagePlaceholder>placeholder</ng-template>
    </tm-image>
    <button tmFilePicker (filesSelected)="onFiles($event)">placeholder</button>
    <tm-dropzone (filesSelected)="onFiles($event)" />
    <button [tmPopoverTriggerFor]="placeholderPopover">placeholder</button>
    <tm-popover #placeholderPopover aria-label="placeholder">
      <ng-template tmPopoverContent>placeholder</ng-template>
    </tm-popover>
    <span tmTooltip="placeholder">placeholder</span>
    <input [tmGridDriver]="placeholderGrid" aria-label="placeholder" />
    <tm-grid
      #placeholderGrid
      gridId="placeholder-grid"
      [data]="rows"
      [rowId]="rowId"
      style="block-size: 120px"
    >
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
  protected readonly onImageChange = (): void => undefined;
  // The icon examples' direct-data binding: library code passes Lucide data.
  protected readonly checkIcon = Check;
  // The button and input examples' projected glyph.
  protected readonly searchIcon = Search;
  protected readonly onFiles = (selection: unknown): void => void selection;
  protected readonly treeHasChildren = (row: ExampleTreeRow): boolean => row.parentId === null;
  protected readonly loadTreeChildren = (): Promise<void> => Promise.resolve();
  // The chip and banner examples' event handlers.
  protected readonly onRemove = (label: string): void => void label;
  protected readonly onActivate = (key: string): void => void key;
  protected readonly onReload = (): void => undefined;
  protected readonly onPrevious = (): void => undefined;
  protected readonly onNext = (): void => undefined;
  protected readonly onDismiss = (): void => undefined;
  // The card examples' two-way disclosure and error state, and the splitter
  // example's two-way size.
  protected readonly addressOpen = signal(true);
  protected readonly addressInvalid = false;
  protected readonly listWidth = signal<number | null>(null);

  // The entity-picker examples' members: an in-memory directory search plus
  // the accessor closures and a trivial modal page.
  protected readonly agents: ReadonlyArray<{ id: number; name: string }> = [
    { id: 1, name: 'Adam Brown' },
    { id: 2, name: 'Alice Green' },
    { id: 3, name: 'Bob Stone' },
  ];
  protected readonly searchAgents = (query: string): ReadonlyArray<{ id: number; name: string }> =>
    this.agents.filter((agent) => agent.name.toLowerCase().includes(query.trim().toLowerCase()));
  protected readonly agentId = (agent: { id: number; name: string }): number => agent.id;
  protected readonly agentName = (agent: { id: number; name: string }): string => agent.name;
  protected readonly agentDisplay = (id: number): string | null =>
    this.agents.find((agent) => agent.id === id)?.name ?? null;
  protected readonly agentPage = ExamplePage;

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

  // The search-page example's members: the two-way checked set, active row
  // and sort, the row-open handler and a row-class function.
  protected readonly selected = signal<ReadonlySet<string | number>>(new Set());
  protected readonly active = signal<string | number | null>(null);
  protected readonly order = signal<TmGridSort | null>(null);
  protected readonly onRow = (activation: TmGridRowActivation<ExampleRow>): void => void activation;
  protected readonly byStatus = (row: ExampleRow): string | null =>
    row.active ? null : 'tm-grid__row--muted';
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
    name: 'TmMenuTrigger',
    pattern: /\btmMenuTriggerFor\b/,
    instantiated: (fixture) =>
      fixture.debugElement.queryAll(By.directive(TmMenuTrigger)).length > 0,
  },
  {
    // The header ng-template is unprojected content of tm-menu (the
    // tm-option caveat), so the marker reads the component's content-query
    // signal instead of the debug tree.
    name: 'TmMenuHeaderDef',
    pattern: /\btmMenuHeader\b/,
    instantiated: (fixture) =>
      fixture.debugElement
        .queryAll(By.directive(TmMenu))
        .some(
          (el) => (el.componentInstance as unknown as { header(): unknown }).header() !== undefined,
        ),
  },
  {
    name: 'TmModalFooter',
    pattern: /\btmModalFooter\b/,
    instantiated: (fixture) =>
      fixture.debugElement.queryAll(By.directive(TmModalFooter)).length > 0,
  },
  {
    name: 'TmDescriptionList',
    pattern: /\btmDescriptionList\b/,
    instantiated: (fixture) =>
      fixture.debugElement.queryAll(By.directive(TmDescriptionList)).length > 0,
  },
  {
    name: 'TmSplitterPane',
    pattern: /\btmSplitterPane\b/,
    instantiated: (fixture) =>
      fixture.debugElement.queryAll(By.directive(TmSplitterPane)).length > 0,
  },
  {
    name: 'TmFilePicker',
    pattern: /\btmFilePicker\b/,
    instantiated: (fixture) => fixture.debugElement.queryAll(By.directive(TmFilePicker)).length > 0,
  },
  {
    // The placeholder ng-template is unprojected content of tm-image (the
    // tm-option caveat), so the marker reads the component's content-query
    // signal instead of the debug tree.
    name: 'TmImagePlaceholder',
    pattern: /\btmImagePlaceholder\b/,
    instantiated: (fixture) =>
      fixture.debugElement
        .queryAll(By.directive(TmImage))
        .some((el) => (el.componentInstance as TmImage).placeholder() !== undefined),
  },
  {
    name: 'TmPopoverTrigger',
    pattern: /\btmPopoverTriggerFor\b/,
    instantiated: (fixture) =>
      fixture.debugElement.queryAll(By.directive(TmPopoverTrigger)).length > 0,
  },
  {
    // The content ng-template is unprojected content of tm-popover (the
    // tm-option caveat), so the marker reads the component's content-query
    // signal instead of the debug tree.
    name: 'TmPopoverContent',
    pattern: /\btmPopoverContent\b/,
    instantiated: (fixture) =>
      fixture.debugElement
        .queryAll(By.directive(TmPopover))
        .some((el) => (el.componentInstance as TmPopover).contentTemplate() !== undefined),
  },
  {
    // An unregistered name draws an empty box without failing, so an icon
    // example that names an icon must actually draw every glyph it shows.
    name: 'TmIcon glyph',
    pattern: /<tm-icon\b/,
    instantiated: (fixture) =>
      Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('tm-icon')).every(
        (icon) => icon.querySelector(':scope > svg') !== null,
      ),
  },
  {
    name: 'TmTooltip',
    pattern: /\btmTooltip\b/,
    instantiated: (fixture) => fixture.debugElement.queryAll(By.directive(TmTooltip)).length > 0,
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
    name: 'TmGridDriver',
    pattern: /\btmGridDriver\b/,
    instantiated: (fixture) => fixture.debugElement.queryAll(By.directive(TmGridDriver)).length > 0,
  },
  {
    name: 'rowActivation',
    pattern: /\browActivation\b/,
    instantiated: (fixture) => grids(fixture).some((grid) => grid.rowActivation() === 'row'),
  },
  {
    name: 'sortable',
    pattern: /\bsortable\b/,
    instantiated: (fixture) =>
      grids(fixture).some((grid) => grid.columns().some((column) => column.sortable())),
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
  { source: 'entity-picker/tm-entity-picker.examples.ts', examples: entityPickerExamples },
  { source: 'alert/tm-alert.examples.ts', examples: alertExamples },
  { source: 'button/tm-button.examples.ts', examples: buttonExamples },
  { source: 'image/tm-image.examples.ts', examples: imageExamples },
  { source: 'files/tm-file-picker.examples.ts', examples: filePickerExamples },
  { source: 'files/tm-dropzone.examples.ts', examples: dropzoneExamples },
  { source: 'checkbox/tm-checkbox.examples.ts', examples: checkboxExamples },
  { source: 'select/tm-select.examples.ts', examples: selectExamples },
  { source: 'select/tm-multi-select.examples.ts', examples: multiSelectExamples },
  { source: 'spinner/tm-spinner.examples.ts', examples: spinnerExamples },
  { source: 'badge/tm-badge.examples.ts', examples: badgeExamples },
  { source: 'badge/tm-counter.examples.ts', examples: counterExamples },
  { source: 'chip/tm-chip.examples.ts', examples: chipExamples },
  { source: 'chip/tm-chip-set.examples.ts', examples: chipSetExamples },
  { source: 'banner/tm-banner-bar.examples.ts', examples: bannerBarExamples },
  { source: 'card/tm-card.examples.ts', examples: cardExamples },
  { source: 'page-header/tm-page-header.examples.ts', examples: pageHeaderExamples },
  {
    source: 'description-list/tm-description-list.examples.ts',
    examples: descriptionListExamples,
  },
  { source: 'splitter/tm-splitter.examples.ts', examples: splitterExamples },
  { source: 'avatar/tm-avatar.examples.ts', examples: avatarExamples },
  { source: 'progress/tm-progress-bar.examples.ts', examples: progressBarExamples },
  { source: 'progress/tm-progress-ring.examples.ts', examples: progressRingExamples },
  { source: 'skeleton/tm-skeleton.examples.ts', examples: skeletonExamples },
  { source: 'relative-time/tm-relative-time.examples.ts', examples: relativeTimeExamples },
  { source: 'copy/tm-copy-button.examples.ts', examples: copyButtonExamples },
  { source: 'copy/tm-reference.examples.ts', examples: referenceExamples },
  { source: 'empty-state/tm-empty-state.examples.ts', examples: emptyStateExamples },
  { source: 'icon/tm-icon.examples.ts', examples: iconExamples },
  { source: 'kbd/tm-kbd.examples.ts', examples: kbdExamples },
  { source: 'tabs/tm-tabs.examples.ts', examples: tabsExamples },
  { source: 'modal/tm-modal-footer.examples.ts', examples: modalFooterExamples },
  { source: 'popover/tm-popover.examples.ts', examples: popoverExamples },
  { source: 'tooltip/tm-tooltip.examples.ts', examples: tooltipExamples },
  { source: 'menu/tm-menu.examples.ts', examples: menuExamples },
  { source: 'menu/tm-menu-trigger.examples.ts', examples: menuTriggerExamples },
  { source: 'grid/tm-grid.examples.ts', examples: gridExamples },
  { source: 'tree-grid/tm-tree-grid.examples.ts', examples: treeGridExamples },
];

describe('co-located docs examples compile against the live API (§11)', () => {
  for (const { source, examples } of SUITES) {
    describe(source, () => {
      for (const [title, { template }] of Object.entries(examples)) {
        it(`'${title}' compiles, renders, and instantiates what it names`, async () => {
          TestBed.configureTestingModule({
            // provideHttpClient backs tm-image's default TM_BLOB_FETCHER; the
            // icons are the registry names the examples draw.
            providers: [
              provideTellmaUi(),
              provideHttpClient(),
              provideTellmaIcons({
                plus: Plus,
                'circle-alert': CircleAlert,
                'circle-x': CircleX,
                inbox: Inbox,
                'search-x': SearchX,
                'chevron-right': { node: ChevronRight, mirrorInRtl: true },
              }),
            ],
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
