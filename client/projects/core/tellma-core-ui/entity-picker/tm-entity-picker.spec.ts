// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { form, FormField, required } from '@angular/forms/signals';
import { TranslocoService } from '@jsverse/transloco';

import { provideTellmaUi, TM_CELL_EDITOR_HOST } from '@tellma/core-ui';
import type { TmCellEditor, TmCellEditorHost } from '@tellma/core-ui/contracts';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TM_MODAL_DATA, TmModalRef } from '@tellma/core-ui/modal';
import { TmEntityPickerHarness } from '@tellma/core-ui-testing';

import { TmEntityPicker } from './tm-entity-picker';
import type {
  TmEntityPick,
  TmEntityPicked,
  TmEntityPickerPageData,
  TmEntitySearchFn,
  TmEntitySearchResult,
} from './tm-entity-picker-types';

/** The test directory: a deliberate duplicate label makes 'Adam Brown' ambiguous. */
interface Agent {
  readonly id: number;
  readonly name: string;
}
const DIRECTORY: readonly Agent[] = [
  { id: 1, name: 'Adam Brown' },
  { id: 2, name: 'Adam Brown' },
  { id: 3, name: 'Alice Green' },
  { id: 4, name: 'Alan Grey' },
  { id: 5, name: 'Bob Stone' },
  { id: 6, name: 'Carol White' },
];

/** Case-insensitive substring filter over the directory. */
function filterAgents(query: string): readonly Agent[] {
  const q = query.trim().toLowerCase();
  return q === '' ? DIRECTORY : DIRECTORY.filter((a) => a.name.toLowerCase().includes(q));
}

/** A manually-resolved promise. */
interface Deferred<T> {
  readonly promise: Promise<T>;
  readonly resolve: (value: T) => void;
  readonly reject: (reason?: unknown) => void;
}
function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

/** A recorded search call. */
interface SearchCall {
  readonly query: string;
  readonly signal: AbortSignal;
}

/** A synchronous directory search that records its calls. */
function syncSearch(): { fn: TmEntitySearchFn<Agent>; calls: SearchCall[] } {
  const calls: SearchCall[] = [];
  return {
    calls,
    fn: (query, signal) => {
      calls.push({ query, signal });
      return filterAgents(query);
    },
  };
}

/** An async search whose responses are resolved manually per call. */
function manualSearch(): {
  fn: TmEntitySearchFn<Agent>;
  calls: SearchCall[];
  responders: Deferred<TmEntitySearchResult<Agent>>[];
  /** Resolves call `index` (default: last) with the directory filter of its query. */
  answer: (index?: number) => void;
} {
  const calls: SearchCall[] = [];
  const responders: Deferred<TmEntitySearchResult<Agent>>[] = [];
  return {
    calls,
    responders,
    answer(index?: number) {
      const i = index ?? calls.length - 1;
      responders[i].resolve(filterAgents(calls[i].query));
    },
    fn: (query, signal) => {
      calls.push({ query, signal });
      const d = deferred<TmEntitySearchResult<Agent>>();
      responders.push(d);
      return d.promise;
    },
  };
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/** The zoneless settle idiom: render, flush the macrotask re-measure, render. */
async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  await fixture.whenStable();
  await sleep(0);
  await fixture.whenStable();
}

// ---- Form-path host ----
@Component({
  imports: [TmEntityPicker, TmFormField, FormField],
  template: `
    <tm-form-field label="Agent" data-testid="ff">
      <tm-entity-picker
        [formField]="f.agentId"
        [search]="search()"
        [itemId]="itemId"
        [itemLabel]="itemLabel()"
        [displayWith]="displayWith()"
        [advancedSearch]="advancedPage()"
        [create]="createPage()"
        [edit]="editPage()"
        [searchDebounce]="debounce()"
        (picked)="picks.push($event)"
      />
    </tm-form-field>
    <input data-testid="outside" aria-label="Outside" />
  `,
})
class Host {
  readonly model = signal<{ agentId: number | null }>({ agentId: null });
  readonly f = form(this.model);
  readonly search = signal<TmEntitySearchFn<Agent>>(() => []);
  readonly itemId = (item: Agent): number => item.id;
  readonly itemLabel = signal<(item: Agent) => string>((item) => item.name);
  readonly displayWith = signal<((id: number) => string | null) | undefined>(undefined);
  readonly advancedPage = signal<ReturnType<typeof pageOf> | undefined>(undefined);
  readonly createPage = signal<ReturnType<typeof pageOf> | undefined>(undefined);
  readonly editPage = signal<ReturnType<typeof pageOf> | undefined>(undefined);
  readonly debounce = signal(50);
  readonly picks: TmEntityPicked<Agent, number>[] = [];
}

/**
 * A standalone variant for the disabled/readonly matrix — Signal Forms owns
 * those states on a `[formField]`-bound control, so they are driven directly
 * here.
 */
@Component({
  imports: [TmEntityPicker],
  template: `
    <tm-entity-picker
      aria-label="Agent"
      [search]="search"
      [itemId]="id"
      [itemLabel]="label"
      [advancedSearch]="advancedPage"
      [disabled]="disabled()"
      [readonly]="readonly()"
    />
  `,
})
class StateHost {
  readonly calls: string[] = [];
  readonly search: TmEntitySearchFn<Agent> = (q) => {
    this.calls.push(q);
    return filterAgents(q);
  };
  readonly id = (item: Agent): number => item.id;
  readonly label = (item: Agent): string => item.name;
  readonly advancedPage = FakePage as never;
  readonly disabled = signal(false);
  readonly readonly = signal(false);
}

/** A required-field variant for the required-on-null case. */
@Component({
  imports: [TmEntityPicker, TmFormField, FormField],
  template: `
    <tm-form-field label="Agent">
      <tm-entity-picker [formField]="f.agentId" [search]="search" [itemId]="id" [itemLabel]="label" />
    </tm-form-field>
  `,
})
class RequiredHost {
  readonly model = signal<{ agentId: number | null }>({ agentId: null });
  readonly f = form(this.model, (p) => {
    required(p.agentId);
  });
  readonly search: TmEntitySearchFn<Agent> = (q) => filterAgents(q);
  readonly id = (item: Agent): number => item.id;
  readonly label = (item: Agent): string => item.name;
}

/** Identity helper so the host signals can hold page configs of any shape. */
function pageOf(component: unknown): { component: never } | never {
  return component as never;
}

/** The last TM_MODAL_DATA payload a fake page received. */
let lastPageData: TmEntityPickerPageData<number> | null = null;

/** A fake modal page with buttons for every close path. */
@Component({
  template: `
    <button data-testid="page-pick" (click)="pick()">pick</button>
    <button data-testid="page-rename" (click)="rename()">rename</button>
    <button data-testid="page-null" (click)="clear()">null</button>
    <button data-testid="page-close" (click)="ref.close()">close</button>
  `,
})
class FakePage {
  readonly ref = inject(TmModalRef) as TmModalRef<TmEntityPick<number, Agent> | null>;
  readonly data = inject(TM_MODAL_DATA) as TmEntityPickerPageData<number>;
  constructor() {
    lastPageData = this.data;
  }
  pick(): void {
    this.ref.close({ id: 3, label: 'Alice Green', item: DIRECTORY[2] });
  }
  rename(): void {
    this.ref.close({ id: 3, label: 'Alice G. (renamed)' });
  }
  clear(): void {
    this.ref.close(null);
  }
}

async function setup(): Promise<{
  fixture: ComponentFixture<Host>;
  host: Host;
  input: HTMLInputElement;
  outside: HTMLInputElement;
}> {
  TestBed.configureTestingModule({
    providers: [provideTellmaUi({ availableLangs: ['en', 'ar'] })],
  });
  const fixture = TestBed.createComponent(Host);
  await settle(fixture);
  const input = fixture.nativeElement.querySelector(
    '.tm-entity-picker__input',
  ) as HTMLInputElement;
  const outside = fixture.nativeElement.querySelector(
    '[data-testid="outside"]',
  ) as HTMLInputElement;
  return { fixture, host: fixture.componentInstance, input, outside };
}

async function setupState(): Promise<{
  fixture: ComponentFixture<StateHost>;
  host: StateHost;
  input: HTMLInputElement;
}> {
  TestBed.configureTestingModule({
    providers: [provideTellmaUi({ availableLangs: ['en', 'ar'] })],
  });
  const fixture = TestBed.createComponent(StateHost);
  await settle(fixture);
  const input = fixture.nativeElement.querySelector(
    '.tm-entity-picker__input',
  ) as HTMLInputElement;
  return { fixture, host: fixture.componentInstance, input };
}

/** Types `text` into the focused input (replacing the content) and settles. */
async function type(
  fixture: ComponentFixture<unknown>,
  input: HTMLInputElement,
  text: string,
): Promise<void> {
  input.focus();
  input.value = text;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await settle(fixture);
}

/** Presses a key on the input and settles (twice — aria relays after render). */
async function press(
  fixture: ComponentFixture<unknown>,
  input: HTMLInputElement,
  key: string,
  init?: KeyboardEventInit,
): Promise<{ defaultPrevented: boolean }> {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
  input.dispatchEvent(event);
  await settle(fixture);
  await settle(fixture);
  return { defaultPrevented: event.defaultPrevented };
}

/** The portaled panel, if open. */
function panel(): HTMLElement | null {
  return document.querySelector('.tm-entity-picker__panel');
}

/** The rendered entity option rows (footer rows excluded). */
function optionRows(): HTMLElement[] {
  return Array.from(
    document.querySelectorAll('.tm-entity-picker__option:not(.tm-entity-picker__action)'),
  );
}

/** The rendered footer rows. */
function actionRows(): HTMLElement[] {
  return Array.from(document.querySelectorAll('.tm-entity-picker__action'));
}

/** The active (highlighted) option row, if any. */
function activeRow(): HTMLElement | null {
  return document.querySelector('[ngoption][data-active="true"], [data-active="true"]');
}

/** The field's error element text (empty string when no error shows). */
function errorText(fixture: ComponentFixture<unknown>): string {
  const el = fixture.nativeElement.querySelector('.tm-form-field__error') as HTMLElement | null;
  return el?.textContent?.trim() ?? '';
}

/** The picker's live-region text. */
function liveText(fixture: ComponentFixture<unknown>): string {
  const el = fixture.nativeElement.querySelector('.tm-entity-picker__live') as HTMLElement;
  return el.textContent?.trim() ?? '';
}

describe('tm-entity-picker', () => {
  afterEach(() => {
    lastPageData = null;
  });

  describe('anatomy & form-field integration', () => {
    it('renders an editable combobox input with list autocomplete and label-for wiring', async () => {
      const { fixture, host, input } = await setup();
      expect(input.getAttribute('role')).toBe('combobox');
      expect(input.getAttribute('aria-expanded')).toBe('false');
      expect(input.getAttribute('autocomplete')).toBe('off');
      expect(input.getAttribute('spellcheck')).toBe('false');
      expect(input.getAttribute('dir')).toBe('auto');
      const label = fixture.nativeElement.querySelector('label') as HTMLLabelElement;
      expect(label.htmlFor).toBe(input.id);
      // aria derives list autocomplete from the registered listbox popup —
      // the popup template lives in the overlay, so the attribute
      // materializes once the dropdown opens.
      // aria derives the attribute from the LIVE popup, so it would read
      // "none" on the closed control it describes; the picker states it.
      expect(input.getAttribute('aria-autocomplete')).toBe('list');
      host.search.set(syncSearch().fn);
      await settle(fixture);
      await type(fixture, input, 'Al');
      expect(input.getAttribute('aria-autocomplete')).toBe('list');
      expect(input.getAttribute('aria-controls')).toBeTruthy();
    });

    it('disabled: no dropdown, no search, a disabled magnifier — and it announces disabled', async () => {
      const { fixture, host, input } = await setupState();
      host.disabled.set(true);
      await settle(fixture);
      expect(input.disabled).toBe(true);
      expect(input.getAttribute('aria-disabled')).toBe('true');
      const magnifier = fixture.nativeElement.querySelector(
        '.tm-entity-picker__magnifier',
      ) as HTMLButtonElement;
      expect(magnifier.disabled).toBe(true); // present, so the box never changes size
      input.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await press(fixture, input, 'ArrowDown', { altKey: true });
      expect(panel()).toBeNull();
      expect(host.calls).toHaveLength(0);
    });

    it('readonly: focusable and non-typable, no dropdown — and it never announces disabled', async () => {
      const { fixture, host, input } = await setupState();
      host.readonly.set(true);
      await settle(fixture);
      // A read-only control still conveys its value: it is reachable, and it
      // is read-only — NOT disabled, which would tell assistive tech the
      // value is unavailable.
      expect(input.disabled).toBe(false);
      expect(input.readOnly).toBe(true);
      expect(input.tabIndex).toBe(0);
      expect(input.getAttribute('aria-disabled')).toBe('false');
      input.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await press(fixture, input, 'ArrowDown', { altKey: true });
      expect(panel()).toBeNull();
      expect(host.calls).toHaveLength(0);
    });

    it('readonly holds from the FIRST render, not only after a flip', async () => {
      // The native read-only attribute is ours; aria host-binds the same
      // attribute (to null, since its own `disabled` input is false here)
      // and its host bindings run AFTER every template binding. If that
      // removal ever won, a read-only picker would be freely typable.
      TestBed.configureTestingModule({
        providers: [provideTellmaUi({ availableLangs: ['en', 'ar'] })],
      });
      const fixture = TestBed.createComponent(StateHost);
      fixture.componentInstance.readonly.set(true); // before the first render
      await settle(fixture);
      const input = fixture.nativeElement.querySelector(
        '.tm-entity-picker__input',
      ) as HTMLInputElement;
      expect(input.readOnly).toBe(true);
      expect(input.getAttribute('readonly')).not.toBeNull();
    });

    it('going disabled with the dropdown open closes it', async () => {
      const { fixture, host, input } = await setupState();
      await type(fixture, input, 'Al');
      expect(panel()).not.toBeNull();
      host.disabled.set(true);
      await settle(fixture);
      expect(panel()).toBeNull();
    });

    it('shows the magnifier only when advancedSearch is configured, and never as a tab stop', async () => {
      const { fixture, host } = await setup();
      expect(fixture.nativeElement.querySelector('.tm-entity-picker__magnifier')).toBeNull();
      host.advancedPage.set(pageOf(FakePage));
      await settle(fixture);
      const magnifier = fixture.nativeElement.querySelector(
        '.tm-entity-picker__magnifier',
      ) as HTMLButtonElement;
      expect(magnifier).not.toBeNull();
      expect(magnifier.tabIndex).toBe(-1);
      expect(magnifier.getAttribute('aria-label')).toBe('Advanced search');
    });

    it('required-on-null surfaces through the field once touched', async () => {
      TestBed.configureTestingModule({
        providers: [provideTellmaUi({ availableLangs: ['en', 'ar'] })],
      });
      const fixture = TestBed.createComponent(RequiredHost);
      await settle(fixture);
      const input = fixture.nativeElement.querySelector(
        '.tm-entity-picker__input',
      ) as HTMLInputElement;
      expect(fixture.componentInstance.f.agentId().invalid()).toBe(true);
      input.focus();
      input.blur();
      await settle(fixture);
      expect(fixture.componentInstance.f.agentId().touched()).toBe(true);
      const error = fixture.nativeElement.querySelector('.tm-form-field__error') as HTMLElement;
      expect(error.textContent).toContain('This field is required');
    });
  });

  describe('search lifecycle', () => {
    it('typing opens the dropdown and renders sync results in the same settle with no spinner', async () => {
      const { fixture, host, input } = await setup();
      const search = syncSearch();
      host.search.set(search.fn);
      await settle(fixture);
      await type(fixture, input, 'Al');
      expect(input.getAttribute('aria-expanded')).toBe('true');
      expect(panel()).not.toBeNull();
      expect(optionRows().map((r) => r.textContent?.trim())).toEqual(['Alice Green', 'Alan Grey']);
      expect(document.querySelector('.tm-entity-picker__status')).toBeNull();
      expect(search.calls.map((c) => c.query)).toEqual(['Al']);
    });

    it('an async search shows the immediate spinner, then results, and announces the count', async () => {
      const { fixture, host, input } = await setup();
      const search = manualSearch();
      host.search.set(search.fn);
      await settle(fixture);
      await type(fixture, input, 'Al');
      expect(document.querySelector('.tm-entity-picker__status tm-spinner')).not.toBeNull();
      expect(optionRows()).toHaveLength(0);
      search.answer();
      await settle(fixture);
      expect(optionRows()).toHaveLength(2);
      expect(document.querySelector('.tm-entity-picker__status')).toBeNull();
      expect(liveText(fixture)).toBe('2 results');
    });

    it('every async text change clears stale rows immediately and discards superseded responses', async () => {
      const { fixture, host, input } = await setup();
      const search = manualSearch();
      host.search.set(search.fn);
      host.debounce.set(0);
      await settle(fixture);
      await type(fixture, input, 'Al');
      // Supersede while the first request is still in flight.
      await type(fixture, input, 'Bob');
      expect(optionRows()).toHaveLength(0);
      expect(document.querySelector('.tm-entity-picker__status tm-spinner')).not.toBeNull();
      expect(search.calls[0].signal.aborted).toBe(true);
      // A consumer ignoring the signal answers anyway — discarded on arrival.
      search.responders[0].resolve(filterAgents('Al'));
      await settle(fixture);
      expect(optionRows()).toHaveLength(0);
      search.answer(1);
      await settle(fixture);
      expect(optionRows().map((r) => r.textContent?.trim())).toEqual(['Bob Stone']);
    });

    it('coalesces a simultaneous burst into two requests (leading + trailing)', async () => {
      const { fixture, host, input } = await setup();
      const search = manualSearch();
      host.search.set(search.fn);
      await settle(fixture);
      input.focus();
      for (const text of ['A', 'Ad', 'Ada', 'Adam']) {
        input.value = text;
        input.dispatchEvent(new Event('input', { bubbles: true }));
      }
      await sleep(120); // past the 50ms window
      await settle(fixture);
      expect(search.calls.map((c) => c.query)).toEqual(['A', 'Adam']);
    });

    it('a held key costs ONE trailing request however long it repeats', async () => {
      const { fixture, host, input } = await setup(); // the default 50ms window
      const search = manualSearch();
      host.search.set(search.fn);
      await settle(fixture);
      input.focus();
      // Autorepeat: each keystroke lands well inside the window, but the
      // burst runs across SEVERAL windows' worth of time. A fixed window
      // fires once per window here; a sliding one fires once, at the end.
      for (let i = 1; i <= 16; i++) {
        input.value = 'a'.repeat(i);
        input.dispatchEvent(new Event('input', { bubbles: true }));
        await sleep(15);
      }
      await sleep(140);
      await settle(fixture);
      expect(search.calls.length).toBeLessThanOrEqual(3);
      expect(search.calls[0].query).toBe('a');
      expect(search.calls.at(-1)?.query).toBe('a'.repeat(16));
    });

    it('a known-synchronous source skips the window: every keystroke searches instantly', async () => {
      const { fixture, host, input } = await setup();
      const search = syncSearch();
      host.search.set(search.fn);
      await settle(fixture);
      input.focus();
      for (const text of ['A', 'Ad', 'Ada']) {
        input.value = text;
        input.dispatchEvent(new Event('input', { bubbles: true }));
      }
      await settle(fixture);
      expect(search.calls.map((c) => c.query)).toEqual(['A', 'Ad', 'Ada']);
    });

    it('a fresh empty result set shows the reserved-height "No results" status', async () => {
      // The tokens stylesheet is not part of the unit environment — pin the
      // component token so the reserved-height calc resolves.
      document.documentElement.style.setProperty('--entity-picker-option-height', '36px');
      try {
        const { fixture, host, input } = await setup();
        host.search.set(syncSearch().fn);
        await settle(fixture);
        await type(fixture, input, 'zzz');
        const status = document.querySelector('.tm-entity-picker__status') as HTMLElement;
        expect(status.textContent?.trim()).toBe('No results');
        const height = parseFloat(getComputedStyle(status).blockSize);
        expect(height).toBe(72); // 2 × the 36px option-height token
        expect(liveText(fixture)).toBe('No results');
      } finally {
        document.documentElement.style.removeProperty('--entity-picker-option-height');
      }
    });

    it('a rejected search shows the failure status, keeps footer rows, and the next change retries', async () => {
      const { fixture, host, input } = await setup();
      host.createPage.set(pageOf(FakePage));
      const search = manualSearch();
      host.search.set(search.fn);
      host.debounce.set(0); // retry timing is under test, not coalescing
      await settle(fixture);
      await type(fixture, input, 'Al');
      search.responders[0].reject(new Error('boom'));
      await settle(fixture);
      const status = document.querySelector('.tm-entity-picker__status') as HTMLElement;
      expect(status.textContent?.trim()).toBe('Search failed');
      expect(liveText(fixture)).toBe('Search failed'); // announced on completion
      expect(actionRows().length).toBeGreaterThan(0);
      await type(fixture, input, 'Ali');
      expect(search.calls).toHaveLength(2);
    });

    it('announcements fire on fetch completion only — never per keystroke', async () => {
      const { fixture, host, input } = await setup();
      const search = manualSearch();
      host.search.set(search.fn);
      await settle(fixture);
      await type(fixture, input, 'Al');
      expect(liveText(fixture)).toBe(''); // nothing announced while loading
      search.answer();
      await settle(fixture);
      expect(liveText(fixture)).toBe('2 results');
    });

    it('warns in dev mode when a search returns more than 200 results', async () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
      try {
        const { fixture, host, input } = await setup();
        host.search.set(() =>
          Array.from({ length: 201 }, (_, i) => ({ id: i + 1000, name: `Agent ${i}` })),
        );
        await settle(fixture);
        await type(fixture, input, 'A');
        expect(warn).toHaveBeenCalledWith(expect.stringContaining('201 results'));
      } finally {
        warn.mockRestore();
      }
    });

    it('the hasMore object form renders the truncation hint and the open-ended announcement', async () => {
      const { fixture, host, input } = await setup();
      host.search.set((q) => ({ items: [...filterAgents(q)], hasMore: true }));
      await settle(fixture);
      await type(fixture, input, 'Al');
      const hint = document.querySelector('.tm-entity-picker__hint') as HTMLElement;
      // The hint names how many results are on screen and stays out of the
      // accessibility tree (the count announcement carries it for AT).
      expect(hint.textContent?.trim()).toBe('Showing top 2 matches. Keep typing to refine.');
      expect(hint.getAttribute('aria-hidden')).toBe('true');
      expect(liveText(fixture)).toBe('2+ results — more available');
    });

    it('pointer click on the input opens a pristine browse list via search("")', async () => {
      const { fixture, host, input } = await setup();
      const search = syncSearch();
      host.search.set(search.fn);
      await settle(fixture);
      input.focus();
      input.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await settle(fixture);
      expect(panel()).not.toBeNull();
      expect(search.calls.map((c) => c.query)).toEqual(['']);
      expect(optionRows()).toHaveLength(DIRECTORY.length);
    });
  });

  describe('keyboard model', () => {
    it('a reopened dropdown never re-applies the previous open highlight request', async () => {
      const { fixture, host, input } = await setup();
      const search = manualSearch();
      host.search.set(search.fn);
      host.createPage.set(pageOf(FakePage));
      host.debounce.set(0);
      await settle(fixture);
      await type(fixture, input, 'Al');
      search.answer();
      await settle(fixture);
      expect(activeRow()?.textContent?.trim()).toBe('Alice Green');
      await press(fixture, input, 'Enter'); // pick — the popup closes
      expect(panel()).toBeNull();
      // Reopen as a pristine browse. While the fresh search is in flight,
      // only footer rows exist — the LAST open's typed-query highlight
      // request must not re-apply against the new listbox.
      input.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await settle(fixture);
      await settle(fixture);
      expect(panel()).not.toBeNull();
      expect(activeRow()).toBeNull();
      search.answer();
      await settle(fixture);
      expect(activeRow()).toBeNull(); // pristine browse highlights nothing
    });

    it('a typed query auto-highlights the first result; Enter commits it', async () => {
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      await settle(fixture);
      await type(fixture, input, 'Alice');
      await settle(fixture);
      expect(activeRow()?.textContent?.trim()).toBe('Alice Green');
      expect(input.getAttribute('aria-activedescendant')).toBeTruthy();
      await press(fixture, input, 'Enter');
      expect(host.model().agentId).toBe(3);
      expect(input.value).toBe('Alice Green');
      expect(panel()).toBeNull();
      expect(host.picks.at(-1)?.source).toBe('list');
    });

    it('a pristine browse list highlights nothing and open-then-Tab changes nothing', async () => {
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      host.displayWith.set((id) => DIRECTORY.find((a) => a.id === id)?.name ?? null);
      host.model.set({ agentId: 5 });
      await settle(fixture);
      expect(input.value).toBe('Bob Stone');
      input.focus();
      input.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await settle(fixture);
      await settle(fixture);
      expect(optionRows()).toHaveLength(DIRECTORY.length);
      expect(activeRow()).toBeNull();
      expect(input.getAttribute('aria-activedescendant')).toBeFalsy();
      // The committed row still shows selected (check glyph mirror).
      const selected = document.querySelector('[aria-selected="true"]') as HTMLElement;
      expect(selected.textContent?.trim()).toBe('Bob Stone');
      await press(fixture, input, 'Tab');
      expect(host.model().agentId).toBe(5);
      expect(panel()).toBeNull();
    });

    it('arrows move the highlight off a typed query auto-highlight (no re-clamp)', async () => {
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      host.createPage.set(pageOf(FakePage));
      await settle(fixture);
      await type(fixture, input, 'Al');
      await settle(fixture);
      expect(activeRow()?.textContent?.trim()).toBe('Alice Green');
      // The auto-highlight is a one-shot per result set — arrows own the
      // highlight afterwards (a re-clamping highlight effect would snap
      // this back to the first result).
      await press(fixture, input, 'ArrowDown');
      expect(activeRow()?.textContent?.trim()).toBe('Alan Grey');
      await press(fixture, input, 'ArrowDown');
      expect(activeRow()?.classList.contains('tm-entity-picker__action')).toBe(true);
    });

    it('ArrowUp with nothing highlighted wraps to the LAST option, not the one before it', async () => {
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      host.advancedPage.set(pageOf(FakePage));
      host.createPage.set(pageOf(FakePage));
      await settle(fixture);
      input.focus();
      input.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await settle(fixture);
      await settle(fixture);
      expect(activeRow()).toBeNull(); // a pristine browse highlights nothing
      await press(fixture, input, 'ArrowUp');
      // Create… is last; aria's own prev() would have skipped it and landed
      // on Advanced search….
      expect(activeRow()?.textContent?.trim()).toBe('Create…');
    });

    it('the command rows stand down while the spinner is up, and return when it settles', async () => {
      const { fixture, host, input } = await setup();
      const search = manualSearch();
      host.search.set(search.fn);
      host.advancedPage.set(pageOf(FakePage));
      host.createPage.set(pageOf(FakePage));
      await settle(fixture);
      await type(fixture, input, 'Al');
      expect(document.querySelector('.tm-entity-picker__status tm-spinner')).not.toBeNull();
      expect(actionRows()).toHaveLength(0);
      expect(document.querySelector('.tm-entity-picker__separator')).toBeNull();
      search.answer();
      await settle(fixture);
      expect(actionRows().map((r) => r.textContent?.trim())).toEqual([
        'Advanced search…',
        'Create…',
      ]);
    });

    it('arrows highlight explicitly in a browse list and reach the footer rows', async () => {
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      host.createPage.set(pageOf(FakePage));
      await settle(fixture);
      input.focus();
      input.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await settle(fixture);
      await press(fixture, input, 'ArrowDown');
      expect(activeRow()?.textContent?.trim()).toBe('Adam Brown');
      // ArrowUp from the first row wraps to the LAST option — the Create… footer row.
      await press(fixture, input, 'ArrowUp');
      expect(activeRow()?.classList.contains('tm-entity-picker__action')).toBe(true);
    });

    it('Tab commits the highlighted entity row without being consumed', async () => {
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      await settle(fixture);
      await type(fixture, input, 'Alice');
      await settle(fixture);
      const { defaultPrevented } = await press(fixture, input, 'Tab');
      expect(defaultPrevented).toBe(false);
      expect(host.model().agentId).toBe(3);
      expect(input.value).toBe('Alice Green');
      expect(panel()).toBeNull();
    });

    it('Space never activates an option — it stays a printable character', async () => {
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      await settle(fixture);
      await type(fixture, input, 'Alice');
      await settle(fixture);
      expect(activeRow()).not.toBeNull();
      const { defaultPrevented } = await press(fixture, input, ' ');
      expect(defaultPrevented).toBe(false); // aria did not consume it
      expect(host.model().agentId).toBeNull();
      expect(panel()).not.toBeNull();
    });

    it('Esc closes the popup with the text intact; Enter is consumed while open, not while closed', async () => {
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      await settle(fixture);
      await type(fixture, input, 'Alice');
      // Enter on the highlighted row is aria's to consume, so it never
      // reaches a surrounding form's submit.
      const openEnter = await press(fixture, input, 'Enter');
      expect(openEnter.defaultPrevented).toBe(true);
      expect(host.model().agentId).toBe(3);

      await type(fixture, input, 'Alice');
      const escape = await press(fixture, input, 'Escape');
      expect(escape.defaultPrevented).toBe(true);
      expect(panel()).toBeNull();
      expect(input.value).toBe('Alice');
      // Closed: Enter is left to the platform (native submit applies).
      const closedEnter = await press(fixture, input, 'Enter');
      expect(closedEnter.defaultPrevented).toBe(false);
    });

    it('a Tab commit is not undone by the blur it triggers', async () => {
      // Tab's focus move is the keydown's own default action: it runs in the
      // same task, BEFORE aria has mirrored the committed label into the
      // native input. A departure that read the DOM there would find the
      // query that is now stale, re-resolve it — 'Adam' names two suppliers
      // — and write null over the row the user just took.
      const { fixture, host, input, outside } = await setup();
      host.search.set(syncSearch().fn);
      host.displayWith.set((id) => DIRECTORY.find((a) => a.id === id)?.name ?? null);
      await settle(fixture);
      await type(fixture, input, 'Adam');
      expect(optionRows()).toHaveLength(2);
      await press(fixture, input, 'Tab');
      // The real focus move Tab's default action would have performed.
      outside.focus();
      await settle(fixture);
      expect(host.model().agentId).toBe(1);
      expect(input.value).toBe('Adam Brown');
      expect(errorText(fixture)).toBe('');
      expect(host.picks).toHaveLength(1); // ONE gesture, ONE picked
    });

    it('a focus visit that types nothing never resolves — even if the label changed meanwhile', async () => {
      // The display context can move while the field is focused (an entity
      // cache warming, a rename); the reformat deliberately stands down
      // there, so the older rendering stays on screen. Searching for it on
      // the way out would find nothing and null a value the user never
      // touched.
      const { fixture, host, input, outside } = await setup();
      const search = syncSearch();
      host.search.set(search.fn);
      host.displayWith.set(() => null); // cold: displayFor falls back to String(id)
      await settle(fixture);
      host.model.set({ agentId: 3 });
      await settle(fixture);
      expect(input.value).toBe('3');

      input.focus();
      await settle(fixture);
      host.displayWith.set((id) => DIRECTORY.find((a) => a.id === id)?.name ?? null);
      await settle(fixture);
      expect(input.value).toBe('3'); // the focused field is not rewritten under the user

      outside.focus();
      await settle(fixture);
      expect(host.model().agentId).toBe(3); // survived
      expect(errorText(fixture)).toBe('');
      expect(search.calls).toHaveLength(0); // and cost no request
      expect(input.value).toBe('Alice Green'); // the deferred reformat lands on the way out
    });

    it('Alt+ArrowDown opens the browse dropdown; plain ArrowUp opens it standalone', async () => {
      const { fixture, host, input } = await setup();
      const search = syncSearch();
      host.search.set(search.fn);
      await settle(fixture);
      input.focus();
      await press(fixture, input, 'ArrowDown', { altKey: true });
      expect(panel()).not.toBeNull();
      expect(search.calls.map((c) => c.query)).toEqual(['']);
      await press(fixture, input, 'Escape');
      expect(panel()).toBeNull();
      await press(fixture, input, 'ArrowUp');
      expect(panel()).not.toBeNull();
    });

    it('Home/End while open move the caret, not the highlight (the aria relay is suppressed)', async () => {
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      await settle(fixture);
      await type(fixture, input, 'Alice');
      await settle(fixture);
      const before = activeRow()?.textContent?.trim();
      expect(before).toBe('Alice Green');
      const home = await press(fixture, input, 'Home');
      // Not consumed by the relay: the native caret motion runs.
      expect(home.defaultPrevented).toBe(false);
      expect(activeRow()?.textContent?.trim()).toBe(before);
    });
  });

  describe('resolution', () => {
    it('blur with a unique match auto-picks even when results arrive after the blur', async () => {
      const { fixture, host, input, outside } = await setup();
      const search = manualSearch();
      host.search.set(search.fn);
      await settle(fixture);
      await type(fixture, input, 'Alice Gr');
      outside.focus();
      await settle(fixture);
      expect(panel()).toBeNull(); // popup closes immediately
      expect(host.model().agentId).toBeNull();
      // The in-flight search is the resolution input — NOT aborted by the close.
      expect(search.calls[0].signal.aborted).toBe(false);
      search.answer();
      await settle(fixture);
      expect(host.model().agentId).toBe(3);
      expect(input.value).toBe('Alice Green');
      expect(errorText(fixture)).toBe(''); // no error flash
      expect(host.picks.at(-1)?.source).toBe('auto');
      expect(liveText(fixture)).toBe('Alice Green selected');
    });

    it('blur with an ambiguous match keeps the text, writes null, and shows the message', async () => {
      const { fixture, host, input, outside } = await setup();
      host.search.set(syncSearch().fn);
      host.model.set({ agentId: 5 });
      host.displayWith.set((id) => DIRECTORY.find((a) => a.id === id)?.name ?? null);
      await settle(fixture);
      await type(fixture, input, 'Adam Brown');
      outside.focus();
      await settle(fixture);
      expect(host.model().agentId).toBeNull();
      expect(input.value).toBe('Adam Brown');
      expect(errorText(fixture)).toBe('‘Adam Brown’ matches more than one item');
    });

    it('blur with no match keeps the text, writes null, and shows the no-match message', async () => {
      const { fixture, host, input, outside } = await setup();
      host.search.set(syncSearch().fn);
      await settle(fixture);
      await type(fixture, input, 'Zebra');
      outside.focus();
      await settle(fixture);
      expect(host.model().agentId).toBeNull();
      expect(input.value).toBe('Zebra');
      expect(errorText(fixture)).toBe('No match for ‘Zebra’');
    });

    it('a query in progress never shows a message, even once the field is touched', async () => {
      const { fixture, host, input, outside } = await setup();
      host.search.set(syncSearch().fn);
      await settle(fixture);
      // A real departure on unresolvable text reports — that is a mistake.
      await type(fixture, input, 'Zebra');
      outside.focus();
      await settle(fixture);
      expect(errorText(fixture)).toBe('No match for ‘Zebra’');

      // Resuming the edit clears it: half-typed text is a query in
      // progress, not a mistake, and the field is now touched forever.
      await type(fixture, input, 'Alic');
      expect(errorText(fixture)).toBe('');
      expect(input.getAttribute('aria-invalid')).toBeNull();
      // The form still cannot be saved on it, displayed or not.
      expect(host.f.agentId().invalid()).toBe(true);

      // Completing the pick clears the invalidity too.
      await type(fixture, input, 'Alice');
      await settle(fixture);
      await press(fixture, input, 'Enter');
      expect(host.model().agentId).toBe(3);
      expect(host.f.agentId().invalid()).toBe(false);
      expect(errorText(fixture)).toBe('');
    });

    it('a failed resolution shows the search-failed message and keeps the text', async () => {
      const { fixture, host, input, outside } = await setup();
      const search = manualSearch();
      host.search.set(search.fn);
      await settle(fixture);
      await type(fixture, input, 'Alice');
      outside.focus();
      await settle(fixture);
      search.responders[0].reject(new Error('offline'));
      await settle(fixture);
      expect(host.model().agentId).toBeNull();
      expect(input.value).toBe('Alice');
      expect(errorText(fixture)).toBe('Search failed');
    });

    it('the field is invalid and pending during resolution, so a racing submit is blocked', async () => {
      const { fixture, host, input, outside } = await setup();
      const search = manualSearch();
      host.search.set(search.fn);
      await settle(fixture);
      await type(fixture, input, 'Alice Gr');
      // Unresolved text: invalid from the first divergent keystroke.
      expect(host.f.agentId().invalid()).toBe(true);
      outside.focus();
      await settle(fixture);
      expect(host.f.agentId().invalid()).toBe(true);
      // While resolving, the field spinner shows and the error stays held.
      expect(fixture.nativeElement.querySelector('.tm-form-field__spinner')).not.toBeNull();
      expect(errorText(fixture)).toBe('');
      search.answer();
      await settle(fixture);
      expect(host.f.agentId().invalid()).toBe(false);
      expect(host.model().agentId).toBe(3);
    });

    it('Enter while results are loading resolves with focus retained', async () => {
      const { fixture, host, input } = await setup();
      const search = manualSearch();
      host.search.set(search.fn);
      await settle(fixture);
      await type(fixture, input, 'Alice Gr');
      await press(fixture, input, 'Enter');
      expect(document.activeElement).toBe(input);
      search.answer();
      await settle(fixture);
      expect(host.model().agentId).toBe(3);
      expect(input.value).toBe('Alice Green');
      expect(panel()).toBeNull();
    });

    it('Enter on a fresh empty typed set fails fast; the popup stays open for recovery', async () => {
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      host.createPage.set(pageOf(FakePage));
      await settle(fixture);
      await type(fixture, input, 'Zebra');
      await settle(fixture);
      // A fresh empty set highlights nothing — footer rows are never the
      // auto-highlight target.
      expect(activeRow()).toBeNull();
      await press(fixture, input, 'Enter');
      expect(host.model().agentId).toBeNull();
      expect(panel()).not.toBeNull();
      expect(actionRows().length).toBeGreaterThan(0);
      expect(errorText(fixture)).toBe('No match for ‘Zebra’');
    });

    it('a pristine blur is a no-op with no request; an empty blur commits null', async () => {
      const { fixture, host, input, outside } = await setup();
      const search = syncSearch();
      host.search.set(search.fn);
      host.displayWith.set((id) => DIRECTORY.find((a) => a.id === id)?.name ?? null);
      host.model.set({ agentId: 5 });
      await settle(fixture);
      input.focus();
      outside.focus();
      await settle(fixture);
      expect(search.calls).toHaveLength(0);
      expect(host.model().agentId).toBe(5);
      // Clearing the text then leaving commits null.
      await type(fixture, input, '');
      outside.focus();
      await settle(fixture);
      expect(host.model().agentId).toBeNull();
      expect(input.value).toBe('');
      expect(errorText(fixture)).toBe('');
    });

    it('refocusing and editing supersedes a pending resolution', async () => {
      const { fixture, host, input, outside } = await setup();
      const search = manualSearch();
      host.search.set(search.fn);
      host.debounce.set(0);
      await settle(fixture);
      await type(fixture, input, 'Alice Gr');
      outside.focus();
      await settle(fixture);
      await type(fixture, input, 'Bob');
      // The old response lands anyway — discarded; no auto-pick of Alice.
      search.answer(0);
      await settle(fixture);
      expect(host.model().agentId).toBeNull();
      expect(input.value).toBe('Bob');
      search.answer(1);
      await settle(fixture);
      expect(optionRows().map((r) => r.textContent?.trim())).toEqual(['Bob Stone']);
    });

    it('a display-context tick during a pending resolution never clobbers the unresolved text', async () => {
      const { fixture, host, input, outside } = await setup();
      const search = manualSearch();
      host.search.set(search.fn);
      const cacheVersion = signal(0);
      host.displayWith.set((id) => {
        cacheVersion(); // an entity cache the display closure reads
        return DIRECTORY.find((a) => a.id === id)?.name ?? null;
      });
      host.model.set({ agentId: 5 });
      await settle(fixture);
      expect(input.value).toBe('Bob Stone');
      await type(fixture, input, 'Alice Gr');
      outside.focus(); // the resolution is now pending over the OLD id
      await settle(fixture);
      cacheVersion.set(1); // a cache warm / locale tick mid-window
      await settle(fixture);
      // The user's unresolved text and the submit block both survive.
      expect(input.value).toBe('Alice Gr');
      expect(host.f.agentId().invalid()).toBe(true);
      search.answer();
      await settle(fixture);
      expect(host.model().agentId).toBe(3);
      expect(input.value).toBe('Alice Green');
    });

    it('Enter-while-loading is not superseded by its own trailing coalesced query', async () => {
      const { fixture, host, input } = await setup(); // default 50ms window
      const search = manualSearch();
      host.search.set(search.fn);
      await settle(fixture);
      input.focus();
      input.value = 'Alice';
      input.dispatchEvent(new Event('input', { bubbles: true }));
      input.value = 'Alice Gr';
      input.dispatchEvent(new Event('input', { bubbles: true })); // coalesces
      await fixture.whenStable();
      await press(fixture, input, 'Enter'); // resolution owns 'Alice Gr' now
      await sleep(80); // past the (cancelled) trailing window
      expect(search.calls.map((c) => c.query)).toEqual(['Alice', 'Alice Gr']);
      search.answer(1);
      await settle(fixture);
      expect(host.model().agentId).toBe(3);
      expect(input.value).toBe('Alice Green');
      // The pending state cleared — no stuck field spinner.
      expect(fixture.nativeElement.querySelector('.tm-form-field__spinner')).toBeNull();
    });

    it('a failed Enter resolution leaves the popup on the fetched candidates, never the spinner', async () => {
      const { fixture, host, input } = await setup();
      const search = manualSearch();
      host.search.set(search.fn);
      host.debounce.set(0);
      await settle(fixture);
      await type(fixture, input, 'Adam Brown');
      await press(fixture, input, 'Enter'); // resolves against the in-flight request
      search.answer();
      await settle(fixture);
      expect(errorText(fixture)).toBe('‘Adam Brown’ matches more than one item');
      expect(panel()).not.toBeNull();
      // The two candidates render as the recovery surface; no eternal spinner.
      expect(optionRows()).toHaveLength(2);
      expect(document.querySelector('.tm-entity-picker__status tm-spinner')).toBeNull();
    });

    it('Enter with cleared text commits the empty value', async () => {
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      host.displayWith.set((id) => DIRECTORY.find((a) => a.id === id)?.name ?? null);
      host.model.set({ agentId: 5 });
      await settle(fixture);
      await type(fixture, input, '');
      await press(fixture, input, 'Enter');
      expect(host.model().agentId).toBeNull();
      expect(input.value).toBe('');
      expect(panel()).toBeNull();
      expect(errorText(fixture)).toBe('');
    });

    it('reopening the dropdown supersedes a pending resolution instead of failing it', async () => {
      const { fixture, host, input, outside } = await setup();
      // An abort-HONORING source: reopening aborts the request the pending
      // resolution awaits — that abort must read as supersession, never as
      // a search failure that nulls the committed value.
      const responders: Deferred<TmEntitySearchResult<Agent>>[] = [];
      host.search.set((query, signal) => {
        const d = deferred<TmEntitySearchResult<Agent>>();
        responders.push(d);
        signal.addEventListener('abort', () => d.reject(new Error('aborted')));
        return d.promise;
      });
      host.displayWith.set((id) => DIRECTORY.find((a) => a.id === id)?.name ?? null);
      host.model.set({ agentId: 5 });
      await settle(fixture);
      await type(fixture, input, 'Alice Gr'); // request 0 in flight
      outside.focus(); // blur → the resolution awaits request 0
      await settle(fixture);
      input.focus();
      input.dispatchEvent(new MouseEvent('click', { bubbles: true })); // reopen
      await settle(fixture);
      await settle(fixture);
      // The committed value survives, and the aborted request never reads
      // as a failure. (The generic unresolved-text error may legitimately
      // show — the field is touched with a typed query.)
      expect(host.model().agentId).toBe(5);
      expect(errorText(fixture)).not.toContain('Search failed');
      expect(errorText(fixture)).not.toContain('No match');
      // The reopened search is LIVE while the superseded resolution's
      // continuation lands. The stranded-spinner repair must recognize that
      // the loading state on screen — same query, same text — belongs to
      // the new request and leave it alone.
      expect(panel()?.querySelector('.tm-entity-picker__status tm-spinner')).not.toBeNull();
      // The fresh search proceeds normally.
      responders[1].resolve(filterAgents('Alice Gr'));
      await settle(fixture);
      expect(optionRows()).toHaveLength(1);
    });

    it('Enter while loading, then leaving, still resolves — the second commit reuses the first request', async () => {
      // The first gesture's resolution ADOPTS the in-flight search as its
      // input. The second must not abort what it is itself about to await.
      const { fixture, host, input, outside } = await setup();
      // An abort-HONORING source: the whole point is that an abort here is
      // not a discard, it kills the answer both gestures are waiting for.
      const responders: Deferred<TmEntitySearchResult<Agent>>[] = [];
      host.search.set((query, signal) => {
        const d = deferred<TmEntitySearchResult<Agent>>();
        responders.push(d);
        signal.addEventListener('abort', () => d.reject(new Error('aborted')));
        return d.promise;
      });
      host.displayWith.set((id) => DIRECTORY.find((a) => a.id === id)?.name ?? null);
      await settle(fixture);
      await type(fixture, input, 'Alice Gr');
      await press(fixture, input, 'Enter'); // resolve #1: adopts request 0
      outside.focus(); // resolve #2: reuses request 0
      await settle(fixture);
      responders[0].resolve(filterAgents('Alice Gr'));
      await settle(fixture);
      expect(host.model().agentId).toBe(3);
      expect(errorText(fixture)).toBe('');
      expect(responders).toHaveLength(1); // and it cost ONE round trip
    });

    it('closing the popup before the answer lands leaves the NEXT open clean', async () => {
      // Enter-while-loading keeps the popup open, so the outcome is painted
      // into it — but the user can still close it first, and a status
      // written into a closed popup survives into the next open, which then
      // shows a stale failure and never searches (the initial search only
      // runs from the idle state).
      const { fixture, host, input } = await setup();
      const search = manualSearch();
      host.search.set(search.fn);
      await settle(fixture);
      await type(fixture, input, 'Alice Gr');
      await press(fixture, input, 'Enter');
      await press(fixture, input, 'Escape');
      expect(panel()).toBeNull();
      search.responders[0].reject(new Error('boom'));
      await settle(fixture);
      await settle(fixture);

      input.focus();
      input.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await settle(fixture);
      expect(panel()).not.toBeNull();
      expect(panel()?.textContent).not.toContain('Search failed');
      expect(search.calls).toHaveLength(2); // the reopen ran its own browse
    });

    it('a superseder that issues no search of its own still clears the stranded spinner', async () => {
      // The other half of the repair: an external value write supersedes the
      // resolution without starting anything, so nothing else would ever
      // resolve the spinner it leaves behind.
      const { fixture, host, input } = await setup();
      const search = manualSearch();
      host.search.set(search.fn);
      host.displayWith.set((id) => DIRECTORY.find((a) => a.id === id)?.name ?? null);
      await settle(fixture);
      await type(fixture, input, 'Alice Gr');
      await press(fixture, input, 'Enter'); // Enter-while-loading: resolve, popup stays open
      expect(panel()?.querySelector('.tm-entity-picker__status tm-spinner')).not.toBeNull();
      host.model.set({ agentId: 4 }); // external write supersedes it
      await settle(fixture);
      search.responders[0].resolve(filterAgents('Alice Gr'));
      await settle(fixture);
      expect(host.model().agentId).toBe(4);
      expect(panel()?.querySelector('.tm-entity-picker__status tm-spinner') ?? null).toBeNull();
    });

    it('focus landing inside the panel is handed straight back to the input', async () => {
      // The activedescendant model keeps DOM focus on the input. A press
      // that misses every row (the listbox padding, the scroll gutter) or a
      // programmatic move would otherwise park focus on the listbox, which
      // aria counts as "the widget still has focus" — leaving the popup
      // open above an input that no longer receives keystrokes.
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      await settle(fixture);
      await type(fixture, input, 'Al');
      const listbox = panel()?.querySelector('.tm-entity-picker__listbox') as HTMLElement;
      expect(listbox.tabIndex).toBe(-1);
      listbox.focus();
      await settle(fixture);
      expect(document.activeElement).toBe(input);
      expect(panel()).not.toBeNull();
      // …and the visit's typing is still remembered: leaving now resolves.
      await type(fixture, input, 'Alice Gr');
      input.blur();
      await settle(fixture);
      expect(host.model().agentId).toBe(3);
    });

    it('destroy supersedes a pending resolution and aborts its request', async () => {
      const { fixture, host, input, outside } = await setup();
      const search = manualSearch();
      host.search.set(search.fn);
      await settle(fixture);
      await type(fixture, input, 'Alice Gr');
      outside.focus();
      await settle(fixture);
      fixture.destroy();
      expect(search.calls[0].signal.aborted).toBe(true);
      // A late answer from a consumer ignoring the signal must be inert:
      // the resolution's continuation runs on a torn-down component, and it
      // must neither throw nor write the model it no longer owns.
      search.responders[0].resolve(filterAgents('Alice Gr'));
      await sleep(10);
      expect(host.model().agentId).toBeNull();
      expect(host.picks).toHaveLength(0);
    });

    it('a pick that was later cleared cannot be resurrected by re-typing its label', async () => {
      // The canonical-text pin says "this exact string IS that value". A
      // fail-fast Enter then writes null through the parse, and the parse's
      // own self-write marker stops the echo guard from retiring the pin —
      // so nothing but the reformat effect can, and it must, on every path.
      // Otherwise re-typing the old label rebinds it to the old id with no
      // search, no ambiguity check, and no `picked`.
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      host.displayWith.set((id) => DIRECTORY.find((a) => a.id === id)?.name ?? null);
      await settle(fixture);
      await type(fixture, input, 'Adam Brown');
      await press(fixture, input, 'Enter'); // 'Adam Brown' names two: highlight #1 commits
      expect(host.model().agentId).toBe(1);

      await type(fixture, input, 'Zed');
      await press(fixture, input, 'Enter'); // fail fast: no match, model → null, focus kept
      expect(host.model().agentId).toBeNull();

      await type(fixture, input, 'Adam Brown'); // the old pin's exact text
      expect(host.model().agentId).toBeNull(); // still null — nothing was picked
      expect(host.picks).toHaveLength(1);
    });

    it('every supersession ABORTS the resolution request, not just discards its answer', async () => {
      // The consumer is promised a signal that fires when the request is
      // superseded — a discard the consumer never hears about still costs
      // them the round trip they were told they could cancel.
      const paths: readonly {
        name: string;
        act: (ctx: Awaited<ReturnType<typeof setup>>) => Promise<void>;
      }[] = [
        {
          name: 'resuming the edit',
          act: async ({ fixture, input }) => {
            await type(fixture, input, 'Alice Green');
          },
        },
        {
          name: 'an external value write',
          act: async ({ fixture, host }) => {
            host.model.set({ agentId: 4 });
            await settle(fixture);
          },
        },
        {
          name: 'launching a modal page',
          act: async ({ fixture, host }) => {
            host.advancedPage.set(pageOf(FakePage));
            await settle(fixture);
            (
              fixture.nativeElement.querySelector(
                '.tm-entity-picker__magnifier',
              ) as HTMLButtonElement
            ).click();
            await settle(fixture);
          },
        },
      ];
      for (const path of paths) {
        TestBed.resetTestingModule(); // each path gets its own fixture
        const ctx = await setup();
        const search = manualSearch();
        ctx.host.search.set(search.fn);
        await settle(ctx.fixture);
        await type(ctx.fixture, ctx.input, 'Alice Gr');
        ctx.outside.focus(); // blur → a resolution now owns request 0
        await settle(ctx.fixture);
        await path.act(ctx);
        expect(search.calls[0].signal.aborted, path.name).toBe(true);
        ctx.fixture.destroy();
      }
    });

    it('an external value write supersedes a pending resolution', async () => {
      const { fixture, host, input, outside } = await setup();
      const search = manualSearch();
      host.search.set(search.fn);
      host.displayWith.set((id) => DIRECTORY.find((a) => a.id === id)?.name ?? null);
      await settle(fixture);
      await type(fixture, input, 'Alice Gr');
      outside.focus();
      await settle(fixture);
      host.model.set({ agentId: 6 });
      await settle(fixture);
      search.answer();
      await settle(fixture);
      expect(host.model().agentId).toBe(6);
      expect(input.value).toBe('Carol White');
      expect(errorText(fixture)).toBe('');
    });
  });

  describe('committed-value display', () => {
    it('displayWith wins, the pick memo covers the gap, String(id) is the last resort', async () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      await settle(fixture);
      // No displayWith: an external write has no memo — String(id), with
      // the dev warning naming the missing input.
      host.model.set({ agentId: 4 });
      await settle(fixture);
      expect(input.value).toBe('4');
      expect(warn).toHaveBeenCalledWith(expect.stringContaining('displayWith'));
      warn.mockRestore();
      // A pick memoizes its label.
      await type(fixture, input, 'Alice');
      await settle(fixture);
      await press(fixture, input, 'Enter');
      expect(input.value).toBe('Alice Green');
      // displayWith beats the memo — the reformat applies while unfocused
      // (while focused, the user's text always wins).
      (document.activeElement as HTMLElement).blur();
      await settle(fixture);
      host.displayWith.set(() => 'Overridden');
      await settle(fixture);
      expect(input.value).toBe('Overridden');
    });

    it('a live locale switch re-renders a KEPT resolution error in the new language', async () => {
      // A minimal Arabic table registered directly (importing the real
      // @tellma/locale-ar package across projects breaks the unit-test
      // builder's program construction); the full pack's live switch is
      // covered by the e2e battery, and its key parity by its own suite.
      const { fixture, host, input, outside } = await setup();
      const transloco = TestBed.inject(TranslocoService);
      transloco.setTranslation(
        { tmUi: { entityPicker: { errors: { noMatch: 'لا يوجد تطابق مع «{text}»' } } } },
        'ar',
        { merge: true },
      );
      host.search.set(syncSearch().fn);
      await settle(fixture);
      await type(fixture, input, 'Zebra');
      outside.focus();
      await settle(fixture);
      expect(errorText(fixture)).toBe('No match for ‘Zebra’');

      transloco.setActiveLang('ar');
      await settle(fixture);
      // The text and the null model are KEPT; the message re-renders in Arabic.
      expect(input.value).toBe('Zebra');
      expect(host.model().agentId).toBeNull();
      expect(errorText(fixture)).toContain('لا يوجد تطابق');
      expect(errorText(fixture)).toContain('Zebra');
    });

    it('a live displayWith switch re-renders the committed text with the model untouched', async () => {
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      const lang = signal<'en' | 'ar'>('en');
      host.displayWith.set((id) => (lang() === 'en' ? `Agent ${id}` : `وكيل ${id}`));
      host.model.set({ agentId: 5 });
      await settle(fixture);
      expect(input.value).toBe('Agent 5');
      lang.set('ar');
      await settle(fixture);
      expect(input.value).toBe('وكيل 5');
      expect(host.model().agentId).toBe(5);
    });
  });

  describe('footer rows & modals', () => {
    it('renders footer rows by configuration, Edit… only while a value is committed', async () => {
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      host.advancedPage.set(pageOf(FakePage));
      host.createPage.set(pageOf(FakePage));
      host.editPage.set(pageOf(FakePage));
      await settle(fixture);
      input.focus();
      input.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await settle(fixture);
      expect(actionRows().map((r) => r.textContent?.trim())).toEqual([
        'Advanced search…',
        'Create…',
      ]);
      host.model.set({ agentId: 5 });
      await settle(fixture);
      expect(actionRows().map((r) => r.textContent?.trim())).toEqual([
        'Advanced search…',
        'Create…',
        'Edit…',
      ]);
      // Footer rows are real options inside the listbox.
      expect(actionRows().every((r) => r.getAttribute('role') === 'option')).toBe(true);
    });

    it('the object page form overrides the default size and title', async () => {
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      host.advancedPage.set(
        pageOf({ component: FakePage, size: 'sm', title: 'Find an agent' }),
      );
      await settle(fixture);
      await type(fixture, input, 'Ali');
      (
        fixture.nativeElement.querySelector('.tm-entity-picker__magnifier') as HTMLButtonElement
      ).click();
      await settle(fixture);
      // 'sm' instead of advanced search's 'lg' default, and the page's own
      // title instead of the localized one.
      expect(document.querySelector('.tm-modal-panel--lg')).toBeNull();
      const pane = document.querySelector('.tm-modal-panel--sm') as HTMLElement;
      expect(pane).not.toBeNull();
      expect(pane.textContent).toContain('Find an agent');
      expect(pane.textContent).not.toContain('Advanced search');
      (document.querySelector('[data-testid="page-close"]') as HTMLButtonElement).click();
      await settle(fixture);
    });

    it('a magnifier press from an UNFOCUSED picker returns focus to the input on dismissal', async () => {
      // The modal captures the focused element as its restore target, and
      // the magnifier suppresses its own focus transfer — so without an
      // explicit focus the target would be document.body and a dismissal
      // would leave the user outside the control.
      const { fixture, host, outside } = await setup();
      host.search.set(syncSearch().fn);
      host.advancedPage.set(pageOf(FakePage));
      await settle(fixture);
      outside.focus();
      (
        fixture.nativeElement.querySelector('.tm-entity-picker__magnifier') as HTMLButtonElement
      ).click();
      await settle(fixture);
      (document.querySelector('[data-testid="page-close"]') as HTMLButtonElement).click();
      await settle(fixture);
      await settle(fixture);
      expect(document.activeElement).toBe(
        fixture.nativeElement.querySelector('.tm-entity-picker__input'),
      );
    });

    it('an advanced-search pick applies, memoizes, focuses the input, and closes the loop', async () => {
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      host.advancedPage.set(pageOf(FakePage));
      await settle(fixture);
      await type(fixture, input, 'Ali');
      const magnifier = fixture.nativeElement.querySelector(
        '.tm-entity-picker__magnifier',
      ) as HTMLButtonElement;
      magnifier.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await settle(fixture);
      expect(panel()).toBeNull(); // the dropdown closed for the modal
      expect(lastPageData?.query).toBe('Ali');
      expect(lastPageData?.id).toBeUndefined();
      // Advanced search defaults to the large bucket with the localized title.
      const pane = document.querySelector('.tm-modal-panel--lg') as HTMLElement;
      expect(pane).not.toBeNull();
      expect(pane.textContent).toContain('Advanced search');
      (document.querySelector('[data-testid="page-pick"]') as HTMLButtonElement).click();
      await settle(fixture);
      await settle(fixture);
      expect(host.model().agentId).toBe(3);
      expect(input.value).toBe('Alice Green');
      expect(host.picks.at(-1)?.source).toBe('advanced');
      expect(host.picks.at(-1)?.item).toBe(DIRECTORY[2]);
      expect(document.activeElement).toBe(input);
    });

    it('the edit page receives the committed id, re-applies a renamed label, and close(null) clears', async () => {
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      host.editPage.set(pageOf(FakePage));
      host.model.set({ agentId: 3 });
      host.displayWith.set(undefined);
      await settle(fixture);
      input.focus();
      input.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await settle(fixture);
      const editRow = actionRows().find((r) => r.textContent?.includes('Edit…'));
      editRow?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await settle(fixture);
      expect(lastPageData?.id).toBe(3);
      // Edit defaults to the medium bucket with the localized title.
      const pane = document.querySelector('.tm-modal-panel--md') as HTMLElement;
      expect(pane).not.toBeNull();
      expect(pane.textContent).toContain('Edit');
      (document.querySelector('[data-testid="page-rename"]') as HTMLButtonElement).click();
      await settle(fixture);
      await settle(fixture);
      expect(host.model().agentId).toBe(3);
      expect(input.value).toBe('Alice G. (renamed)');
      // Round 2: the edit page deletes the entity.
      input.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await settle(fixture);
      actionRows()
        .find((r) => r.textContent?.includes('Edit…'))
        ?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await settle(fixture);
      (document.querySelector('[data-testid="page-null"]') as HTMLButtonElement).click();
      await settle(fixture);
      await settle(fixture);
      expect(host.model().agentId).toBeNull();
      expect(input.value).toBe('');
    });

    it('the magnifier press keeps focus in the input (its pointerdown is consumed)', async () => {
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      host.advancedPage.set(pageOf(FakePage));
      await settle(fixture);
      input.focus();
      const magnifier = fixture.nativeElement.querySelector(
        '.tm-entity-picker__magnifier',
      ) as HTMLButtonElement;
      const press = new PointerEvent('pointerdown', { bubbles: true, cancelable: true });
      magnifier.dispatchEvent(press);
      expect(press.defaultPrevented).toBe(true); // focus never leaves the input
      expect(document.activeElement).toBe(input);
    });

    it('Esc and backdrop dismissals are no-ops; destroy closes an open modal', async () => {
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      host.advancedPage.set(pageOf(FakePage));
      host.displayWith.set((id) => DIRECTORY.find((a) => a.id === id)?.name ?? null);
      host.model.set({ agentId: 5 });
      await settle(fixture);
      const magnifier = fixture.nativeElement.querySelector(
        '.tm-entity-picker__magnifier',
      ) as HTMLButtonElement;

      // Esc dismissal: everything survives untouched.
      magnifier.click();
      await settle(fixture);
      // CDK's overlay keyboard dispatcher listens on <body>.
      document.body.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
      );
      await settle(fixture);
      await settle(fixture);
      expect(document.querySelector('[data-testid="page-pick"]')).toBeNull();
      expect(host.model().agentId).toBe(5);
      expect(input.value).toBe('Bob Stone');

      // Backdrop dismissal: same.
      magnifier.click();
      await settle(fixture);
      (document.querySelector('.cdk-overlay-backdrop') as HTMLElement).click();
      await settle(fixture);
      await settle(fixture);
      expect(document.querySelector('[data-testid="page-pick"]')).toBeNull();
      expect(host.model().agentId).toBe(5);

      // Destroy while the modal is open: the picker closes it on teardown.
      magnifier.click();
      await settle(fixture);
      expect(document.querySelector('[data-testid="page-pick"]')).not.toBeNull();
      fixture.destroy();
      await new Promise((resolve) => setTimeout(resolve, 0));
      expect(document.querySelector('[data-testid="page-pick"]')).toBeNull();
    });

    it('every dismissal path is a strict no-op: text, value, and error state survive', async () => {
      const { fixture, host, input } = await setup();
      host.search.set(syncSearch().fn);
      host.createPage.set(pageOf(FakePage));
      await settle(fixture);
      await type(fixture, input, 'Zebra');
      await press(fixture, input, 'Enter'); // fail fast → error state
      expect(errorText(fixture)).toBe('No match for ‘Zebra’');
      // Arrow to the Create… row and open it.
      await press(fixture, input, 'ArrowDown');
      await press(fixture, input, 'Enter');
      await settle(fixture);
      expect(lastPageData?.query).toBe('Zebra');
      (document.querySelector('[data-testid="page-close"]') as HTMLButtonElement).click();
      await settle(fixture);
      await settle(fixture);
      expect(host.model().agentId).toBeNull();
      expect(input.value).toBe('Zebra');
      expect(errorText(fixture)).toBe('No match for ‘Zebra’');
    });
  });

  describe('harness', () => {
    it('drives the picker: type, read options and status, select by label', async () => {
      const { fixture, host } = await setup();
      host.search.set(syncSearch().fn);
      await settle(fixture);
      const loader = TestbedHarnessEnvironment.loader(fixture);
      const picker = await loader.getHarness(TmEntityPickerHarness);
      expect(await picker.isOpen()).toBe(false);
      expect(await picker.hasMagnifier()).toBe(false);

      await picker.typeQuery('Al');
      await settle(fixture);
      expect(await picker.isOpen()).toBe(true);
      expect(await picker.getOptionLabels()).toEqual(['Alice Green', 'Alan Grey']);
      expect(await picker.getActiveOptionLabel()).toBe('Alice Green');
      expect(await picker.isSpinnerShown()).toBe(false);

      await picker.selectOptionByLabel('Alan Grey');
      await settle(fixture);
      expect(host.model().agentId).toBe(4);
      expect(await picker.getQueryText()).toBe('Alan Grey');
      expect(await picker.isOpen()).toBe(false);

      await picker.typeQuery('zzz');
      await settle(fixture);
      expect(await picker.getStatusText()).toBe('No results');
      await picker.close();
      expect(await picker.isOpen()).toBe(false);
    });
  });

  describe('as a grid cell editor', () => {
    /** A recording cell host, provided at the TestBed level. */
    let cellHost: { editor: TmCellEditor<unknown> | null; register(e: TmCellEditor<unknown>): void };

    @Component({
      imports: [TmEntityPicker],
      template: `
        <div data-tm-editor (keydown)="keys.push($event.key)">
          <tm-entity-picker
            [search]="search()"
            [itemId]="id"
            [itemLabel]="label"
            [searchDebounce]="debounce()"
            aria-label="Agent"
          />
        </div>
      `,
    })
    class CellHost {
      readonly search = signal<TmEntitySearchFn<Agent>>((q) => filterAgents(q));
      readonly id = (item: Agent): number => item.id;
      readonly label = (item: Agent): string => item.name;
      readonly debounce = signal(50);
      readonly keys: string[] = [];
    }

    async function setupCell(): Promise<{
      fixture: ComponentFixture<CellHost>;
      host: CellHost;
      input: HTMLInputElement;
      editor: TmCellEditor<number | null>;
      picker: TmEntityPicker<Agent, number>;
    }> {
      cellHost = {
        editor: null,
        register(e) {
          this.editor = e;
        },
      } satisfies TmCellEditorHost & { editor: TmCellEditor<unknown> | null };
      TestBed.configureTestingModule({
        providers: [
          provideTellmaUi({ availableLangs: ['en', 'ar'] }),
          { provide: TM_CELL_EDITOR_HOST, useValue: cellHost },
        ],
      });
      const fixture = TestBed.createComponent(CellHost);
      await settle(fixture);
      const input = fixture.nativeElement.querySelector(
        '.tm-entity-picker__input',
      ) as HTMLInputElement;
      return {
        fixture,
        host: fixture.componentInstance,
        input,
        editor: cellHost.editor as unknown as TmCellEditor<number | null>,
        picker: cellHost.editor as unknown as TmEntityPicker<Agent, number>,
      };
    }

    it('registers itself with the cell host on construction', async () => {
      const { editor } = await setupCell();
      expect(editor).not.toBeNull();
      expect(typeof editor.commit).toBe('function');
    });

    it('seed() replaces the content, searches immediately, and opens the dropdown', async () => {
      const { fixture, input, editor } = await setupCell();
      editor.focus();
      editor.seed?.('Al');
      await settle(fixture);
      expect(input.value).toBe('Al');
      expect(panel()).not.toBeNull();
      expect(optionRows()).toHaveLength(2);
    });

    it('the quiet install shows text with the dropdown closed and no search', async () => {
      const { fixture, input, picker, host } = await setupCell();
      const calls: string[] = [];
      host.search.set((q) => {
        calls.push(q);
        return filterAgents(q);
      });
      await settle(fixture);
      picker.ɵsetCellText('Bob Stone');
      await settle(fixture);
      expect(input.value).toBe('Bob Stone');
      expect(panel()).toBeNull();
      expect(calls).toHaveLength(0);
    });

    it('text() is null while the value channel is authoritative, the raw string otherwise', async () => {
      const { fixture, editor, picker } = await setupCell();
      editor.value.set(5);
      picker.ɵsetCellText('5'); // no displayWith, no memo — String(id)
      await settle(fixture);
      expect(editor.text()).toBeNull(); // pristine display of the set value
      editor.focus();
      editor.seed?.('Ali');
      await settle(fixture);
      expect(editor.text()).toBe('Ali'); // user-edited, unresolved
    });

    it('commit() auto-picks a fresh unique on-screen result synchronously', async () => {
      const { fixture, editor, picker } = await setupCell();
      editor.focus();
      editor.seed?.('Alice Gr');
      await settle(fixture);
      expect(optionRows()).toHaveLength(1);
      editor.commit();
      expect(editor.value()).toBe(3);
      expect(editor.text()).toBeNull(); // the pick made the value authoritative
      await settle(fixture);
      expect(panel()).toBeNull();
      // No cell activation for an auto pick (the grid pulled the commit).
      void picker;
    });

    it('commit() leaves ambiguous text for the grid resolver', async () => {
      const { fixture, editor } = await setupCell();
      editor.focus();
      editor.seed?.('Adam Brown');
      await settle(fixture);
      expect(optionRows()).toHaveLength(2);
      editor.commit();
      expect(editor.value()).toBeNull();
      expect(editor.text()).toBe('Adam Brown');
    });

    it('cancel() restores the value present at open', async () => {
      const { fixture, editor, input } = await setupCell();
      editor.value.set(5);
      await settle(fixture);
      editor.focus();
      editor.seed?.('Alice');
      await settle(fixture);
      editor.cancel();
      await settle(fixture);
      expect(editor.value()).toBe(5);
      expect(input.value).toBe('5');
      expect(panel()).toBeNull();
    });

    it('adopting an answer already in hand issues no request', async () => {
      // The coalescing window is flushed on adoption so a query the picker
      // was about to search is not thrown away. But an answer the picker
      // ALREADY holds has to be checked first — otherwise a text that is
      // both settled and re-queued (type 'Ali', let it settle, then 'Alic'
      // and back to 'Ali' inside the window) starts a request that nothing
      // will ever read.
      const { fixture, host, picker, input } = await setupCell();
      const search = manualSearch();
      host.search.set(search.fn);
      host.debounce.set(5_000); // the window must still be open at adoption
      await settle(fixture);
      input.focus(); // as the grid does — an unfocused picker's popup closes itself
      picker.seed('Ali'); // leading edge: one request, and the window opens
      await settle(fixture);
      expect(search.calls).toHaveLength(1);
      search.responders[0].resolve(filterAgents('Ali'));
      await settle(fixture); // …now settled for 'Ali'

      // Re-queue the SAME text inside the still-open window.
      input.focus();
      input.value = 'Alic';
      input.dispatchEvent(new Event('input', { bubbles: true }));
      input.value = 'Ali';
      input.dispatchEvent(new Event('input', { bubbles: true }));

      const adopted = picker.ɵadoptSearch('Ali');
      expect(adopted).not.toBeNull();
      expect(search.calls).toHaveLength(1); // nothing fresh was issued
      await expect(adopted!.settled).resolves.toEqual({
        items: filterAgents('Ali'),
        hasMore: false,
      });
    });

    it("cancel() restores the value after the GRID's edit-mode open sequence", async () => {
      // The grid opens an entity editor by writing the value and installing
      // the display text in ONE task. The text install parses back to the
      // value just written, and a self-write marker armed for that
      // already-current value would never be consumed — it would swallow
      // the value write's own echo instead, leaving the revert baseline on
      // its initializer and Esc reverting to null.
      const { fixture, editor, picker, input } = await setupCell();
      editor.value.set(5);
      picker.ɵsetCellText('5'); // same task, exactly as the grid does it
      await settle(fixture);
      editor.seed?.('Alice');
      await settle(fixture);
      editor.cancel();
      await settle(fixture);
      expect(editor.value()).toBe(5);
      expect(input.value).toBe('5');
    });

    it('plain vertical arrows are re-dispatched above the host for the grid, unconsumed', async () => {
      const { fixture, host, input } = await setupCell();
      input.focus();
      const event = new KeyboardEvent('keydown', {
        key: 'ArrowDown',
        bubbles: true,
        cancelable: true,
      });
      input.dispatchEvent(event);
      await settle(fixture);
      expect(host.keys).toContain('ArrowDown'); // the clone reached the wrapper
      expect(event.defaultPrevented).toBe(false);
      expect(panel()).toBeNull(); // aria never saw the original — no auto-open
    });

    it('cell blur never triggers the form-path resolution', async () => {
      const { fixture, editor } = await setupCell();
      editor.focus();
      editor.seed?.('Alice Gr');
      await settle(fixture);
      (document.activeElement as HTMLElement).blur();
      await settle(fixture);
      await sleep(20);
      expect(editor.value()).toBeNull(); // no auto-pick happened
      expect(editor.text()).toBe('Alice Gr');
    });

    it('isDropdownOpen()/openDropdown() drive the dropdown gate', async () => {
      const { fixture, picker } = await setupCell();
      expect(picker.isDropdownOpen()).toBe(false);
      picker.openDropdown();
      await settle(fixture);
      expect(picker.isDropdownOpen()).toBe(true);
      expect(panel()).not.toBeNull();
    });
  });
});
