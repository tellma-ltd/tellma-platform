// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { form, FormField, required, requiredError, validate } from '@angular/forms/signals';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TM_MODAL_DATA, TmModalRef } from '@tellma/core-ui/modal';
import { TmEntityMultiPickerHarness } from '@tellma/core-ui-testing';

import { TmEntityMultiPicker } from './tm-entity-multi-picker';
import type {
  TmEntityMultiPick,
  TmEntityPick,
  TmEntityPicked,
  TmEntityPickerPageData,
  TmEntitySearchFn,
} from './tm-entity-picker-types';

/** The test directory. */
interface Agent {
  readonly id: number;
  readonly name: string;
}
const DIRECTORY: readonly Agent[] = [
  { id: 1, name: 'Adam Brown' },
  { id: 2, name: 'Alice Green' },
  { id: 3, name: 'Alan Grey' },
  { id: 4, name: 'Bob Stone' },
  { id: 5, name: 'Carol White' },
];

/** Case-insensitive substring filter over the directory. */
function filterAgents(query: string): readonly Agent[] {
  const q = query.trim().toLowerCase();
  return q === '' ? DIRECTORY : DIRECTORY.filter((a) => a.name.toLowerCase().includes(q));
}

/** The name of a directory entry. */
function nameOf(id: number): string | null {
  return DIRECTORY.find((agent) => agent.id === id)?.name ?? null;
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

/** The last TM_MODAL_DATA payload a fake page received. */
let lastPageData: TmEntityPickerPageData<number> | null = null;

/** A fake whole-selection page with buttons for each outcome under test. */
@Component({
  template: `
    <button data-testid="page-add-remove" (click)="addRemove()">add and remove</button>
    <button data-testid="page-empty" (click)="empty()">empty</button>
    <button data-testid="page-duplicates" (click)="duplicates()">duplicates</button>
    <button data-testid="page-same" (click)="same()">same</button>
    <button data-testid="page-close" (click)="ref.close()">close</button>
  `,
})
class FakeMultiPage {
  readonly ref = inject(TmModalRef) as TmModalRef<TmEntityMultiPick<number, Agent> | null>;
  readonly data = inject(TM_MODAL_DATA) as TmEntityPickerPageData<number>;
  constructor() {
    lastPageData = this.data;
  }
  /** Keeps 1, drops 2, adds 5 and 3 (in that order). */
  addRemove(): void {
    this.ref.close({
      ids: [1, 5, 3],
      picks: [
        { id: 5, label: 'Carol (page)', item: DIRECTORY[4] },
        { id: 3, label: 'Alan (page)' },
      ],
    });
  }
  empty(): void {
    this.ref.close({ ids: [], picks: [] });
  }
  duplicates(): void {
    this.ref.close({ ids: [4, 4, 1, 4], picks: [] });
  }
  same(): void {
    this.ref.close({ ids: this.data.ids ?? [], picks: [] });
  }
}

/** A fake create page that creates "Dana Reed" with id 9. */
@Component({
  template: `<button data-testid="create-save" (click)="save()">save</button>`,
})
class FakeCreatePage {
  readonly ref = inject(TmModalRef) as TmModalRef<TmEntityPick<number, Agent> | null>;
  save(): void {
    this.ref.close({ id: 9, label: 'Dana Reed' });
  }
}

// ---- Form-path host ----
@Component({
  imports: [TmEntityMultiPicker, TmFormField, FormField],
  template: `
    <tm-form-field label="Agents" hint="Pick several" [size]="size()">
      <tm-entity-multi-picker
        [formField]="f.agentIds"
        [search]="search"
        [itemId]="itemId"
        [itemLabel]="itemLabel"
        [displayWith]="displayWith()"
        [advancedSearch]="advancedPage()"
        [create]="createPage()"
        [maxItems]="maxItems()"
        placeholder="Search agents"
        (picked)="picks.push($event)"
        (removed)="removals.push($event)"
      />
    </tm-form-field>
    <input data-testid="outside" aria-label="Outside" />
  `,
})
class Host {
  readonly model = signal<{ agentIds: readonly number[] }>({ agentIds: [] });
  readonly f = form(this.model);
  readonly search: TmEntitySearchFn<Agent> = (query) => filterAgents(query);
  readonly itemId = (item: Agent): number => item.id;
  readonly itemLabel = (item: Agent): string => item.name;
  readonly displayWith = signal<((id: number) => string | null) | undefined>(undefined);
  readonly advancedPage = signal<typeof FakeMultiPage | undefined>(undefined);
  readonly createPage = signal<typeof FakeCreatePage | undefined>(undefined);
  readonly maxItems = signal<number | undefined>(undefined);
  readonly size = signal<'sm' | 'md' | 'lg' | undefined>(undefined);
  readonly picks: TmEntityPicked<Agent, number>[] = [];
  readonly removals: number[] = [];
}

/** A standalone variant for the box, the disabled and read-only states. */
@Component({
  imports: [TmEntityMultiPicker],
  template: `
    <tm-entity-multi-picker
      aria-label="Agents"
      [(value)]="ids"
      [search]="search"
      [itemId]="itemId"
      [itemLabel]="itemLabel"
      [displayWith]="displayWith"
      [disabled]="disabled()"
      [readonly]="readonly()"
    />
  `,
})
class StandaloneHost {
  readonly ids = signal<readonly number[]>([2, 4]);
  readonly calls: string[] = [];
  readonly search: TmEntitySearchFn<Agent> = (query) => {
    this.calls.push(query);
    return filterAgents(query);
  };
  readonly itemId = (item: Agent): number => item.id;
  readonly itemLabel = (item: Agent): string => item.name;
  readonly displayWith = nameOf;
  readonly disabled = signal(false);
  readonly readonly = signal(false);
}

/** A required-field variant. */
@Component({
  imports: [TmEntityMultiPicker, TmFormField, FormField],
  template: `
    <tm-form-field label="Agents">
      <tm-entity-multi-picker
        [formField]="f.agentIds"
        [search]="search"
        [itemId]="itemId"
        [itemLabel]="itemLabel"
      />
    </tm-form-field>
    <input data-testid="outside" aria-label="Outside" />
  `,
})
class RequiredHost {
  readonly model = signal<{ agentIds: readonly number[] }>({ agentIds: [] });
  // Signal Forms' `required` counts an empty array as a value: it marks the
  // field required (aria-required on the input), and the length check
  // supplies the error.
  readonly f = form(this.model, (p) => {
    required(p.agentIds);
    validate(p.agentIds, ({ value }) => (value().length === 0 ? requiredError() : undefined));
  });
  readonly search: TmEntitySearchFn<Agent> = (query) => filterAgents(query);
  readonly itemId = (item: Agent): number => item.id;
  readonly itemLabel = (item: Agent): string => item.name;
}

async function setup<H>(hostType: new () => H): Promise<{
  fixture: ComponentFixture<H>;
  host: H;
  input: HTMLInputElement;
}> {
  TestBed.configureTestingModule({
    providers: [provideTellmaUi({ availableLangs: ['en', 'ar'] })],
  });
  const fixture = TestBed.createComponent(hostType);
  await settle(fixture);
  const input = fixture.nativeElement.querySelector('.tm-entity-picker__input') as HTMLInputElement;
  return { fixture, host: fixture.componentInstance, input };
}

/** Types `text` into the input (replacing the content) and settles. */
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

/** Dispatches a key on `target` and settles; returns whether it was consumed. */
async function press(
  fixture: ComponentFixture<unknown>,
  target: Element,
  key: string,
  init?: KeyboardEventInit,
): Promise<boolean> {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
  target.dispatchEvent(event);
  await settle(fixture);
  await settle(fixture);
  return event.defaultPrevented;
}

/** Opens the browse list with a pointer press on the input. */
async function openList(
  fixture: ComponentFixture<unknown>,
  input: HTMLInputElement,
): Promise<void> {
  input.focus();
  input.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  await settle(fixture);
}

/** Clicks the result row labelled `label`. */
async function clickRow(fixture: ComponentFixture<unknown>, label: string): Promise<void> {
  const row = Array.from(
    document.querySelectorAll<HTMLElement>(
      '.tm-entity-picker__option:not(.tm-entity-picker__action)',
    ),
  ).find((r) => r.querySelector('.tm-entity-picker__option-label')?.textContent?.trim() === label);
  if (row === undefined) {
    throw new Error(`no row labelled ${label}`);
  }
  row.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  await settle(fixture);
}

/** The chip labels, in order. */
function chipLabels(fixture: ComponentFixture<unknown>): string[] {
  return Array.from(
    (fixture.nativeElement as HTMLElement).querySelectorAll('tm-chip .tm-chip__text'),
  ).map((text) => text.textContent?.trim() ?? '');
}

/** The remove buttons of the chips, in order. */
function removeButtons(fixture: ComponentFixture<unknown>): HTMLButtonElement[] {
  return Array.from(
    (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.tm-chip__remove'),
  );
}

/** The multi-picker's live-region text. */
function liveText(fixture: ComponentFixture<unknown>): string {
  const region = (fixture.nativeElement as HTMLElement).querySelector(
    '.tm-entity-picker__live[aria-live]',
  );
  return region?.textContent?.trim() ?? '';
}

/** The hidden summary element the input is described by, if referenced. */
function summaryOf(input: HTMLInputElement): string | null {
  const ids = (input.getAttribute('aria-describedby') ?? '').split(/\s+/).filter(Boolean);
  const summary = ids
    .map((id) => document.getElementById(id))
    .find((element) => element?.id.startsWith('tm-entity-multi-picker-summary-'));
  return summary?.textContent?.trim() ?? null;
}

/** Whether the panel is open. */
function panelOpen(): boolean {
  return document.querySelector('.tm-entity-picker__panel') !== null;
}

/** The label of the dropdown's highlighted row, if any. */
function activeLabel(): string | null {
  const row = document.querySelector('.tm-entity-picker__option[data-active="true"]');
  return row?.querySelector('.tm-entity-picker__option-label')?.textContent?.trim() ?? null;
}

describe('tm-entity-multi-picker', () => {
  afterEach(() => {
    lastPageData = null;
  });

  describe('anatomy & form-field integration', () => {
    it('labels the input, renders chips in value order, and binds the form value', async () => {
      const { fixture, host, input } = await setup(Host);
      const label = fixture.nativeElement.querySelector('label') as HTMLLabelElement;
      expect(label.htmlFor).toBe(input.id);
      expect(input.getAttribute('role')).toBe('combobox');
      expect(input.placeholder).toBe('Search agents');
      host.displayWith.set(nameOf);
      host.model.set({ agentIds: [4, 2] });
      await settle(fixture);
      expect(chipLabels(fixture)).toEqual(['Bob Stone', 'Alice Green']);
      // The placeholder only speaks to an empty field.
      expect(input.placeholder).toBe('');
      // The chips are a list of list items, each with a named remove button.
      const list = fixture.nativeElement.querySelector('tm-chip-set') as HTMLElement;
      expect(list.getAttribute('role')).toBe('list');
      expect(removeButtons(fixture).map((b) => b.getAttribute('aria-label'))).toEqual([
        'Remove Bob Stone',
        'Remove Alice Green',
      ]);
    });

    it('describes the input with the field hint and, while chips exist, a hidden summary', async () => {
      const { fixture, host, input } = await setup(Host);
      host.displayWith.set(nameOf);
      await settle(fixture);
      expect(summaryOf(input)).toBeNull();
      const hint = fixture.nativeElement.querySelector('.tm-form-field__hint') as HTMLElement;
      expect(input.getAttribute('aria-describedby')?.split(' ')).toContain(hint.id);
      host.model.set({ agentIds: [1, 4] });
      await settle(fixture);
      expect(summaryOf(input)).toBe('2 selected: Adam Brown, Bob Stone');
      const summary = fixture.nativeElement.querySelector(
        '[hidden][id^="tm-entity-multi-picker-summary-"]',
      );
      expect(summary).not.toBeNull(); // hidden from view and from browse mode
    });

    it('shows a count badge with the total, out of the accessibility tree, only while chips exist', async () => {
      const { fixture, host } = await setup(Host);
      host.displayWith.set(nameOf);
      await settle(fixture);
      expect(fixture.nativeElement.querySelector('.tm-entity-multi-picker__count')).toBeNull();
      host.model.set({ agentIds: [1, 2, 3] });
      await settle(fixture);
      const badge = fixture.nativeElement.querySelector(
        '.tm-entity-multi-picker__count',
      ) as HTMLElement;
      expect(badge.getAttribute('aria-hidden')).toBe('true');
      expect(
        (
          badge.querySelector('.tm-entity-multi-picker__count-value') as HTMLElement
        ).textContent?.trim(),
      ).toBe('3');
      // The probe holds the widest count the badge can show.
      expect(
        (
          badge.querySelector('.tm-entity-multi-picker__count-probe') as HTMLElement
        ).textContent?.trim(),
      ).toBe('99+');
    });

    it('sizes the chips from the enclosing field', async () => {
      const { fixture, host } = await setup(Host);
      host.displayWith.set(nameOf);
      host.model.set({ agentIds: [1] });
      host.size.set('sm');
      await settle(fixture);
      expect(fixture.nativeElement.querySelector('tm-chip').getAttribute('data-size')).toBe('sm');
      host.size.set('lg');
      await settle(fixture);
      expect(fixture.nativeElement.querySelector('tm-chip').getAttribute('data-size')).toBe('lg');
    });

    it('a required empty selection surfaces through the field once touched', async () => {
      const { fixture, input } = await setup(RequiredHost);
      const outside = fixture.nativeElement.querySelector('[data-testid="outside"]') as HTMLElement;
      expect(fixture.componentInstance.f.agentIds().invalid()).toBe(true);
      // The input itself is never natively required: chips can satisfy it.
      expect(input.required).toBe(false);
      expect(input.getAttribute('aria-required')).toBe('true');
      input.focus();
      outside.focus();
      await settle(fixture);
      expect(fixture.componentInstance.f.agentIds().touched()).toBe(true);
      const error = fixture.nativeElement.querySelector('.tm-form-field__error') as HTMLElement;
      expect(error.textContent).toContain('This field is required');
    });

    it('typed text that resolves to nothing raises the input error through the field', async () => {
      const { fixture, host, input } = await setup(Host);
      const outside = fixture.nativeElement.querySelector('[data-testid="outside"]') as HTMLElement;
      await type(fixture, input, 'Zebra');
      outside.focus();
      await settle(fixture);
      expect(host.f.agentIds().value()).toEqual([]);
      const error = fixture.nativeElement.querySelector('.tm-form-field__error') as HTMLElement;
      expect(error.textContent).toContain('No match for ‘Zebra’');
      expect(input.getAttribute('aria-invalid')).toBe('true');
    });
  });

  describe('picks and removals', () => {
    it('a list pick appends a chip, emits picked, announces it, and keeps the panel open', async () => {
      const { fixture, host, input } = await setup(Host);
      await type(fixture, input, 'Al');
      await clickRow(fixture, 'Alan Grey');
      expect(host.f.agentIds().value()).toEqual([3]);
      expect(chipLabels(fixture)).toEqual(['Alan Grey']); // the pick's own label
      expect(host.picks).toEqual([
        { id: 3, label: 'Alan Grey', item: DIRECTORY[2], source: 'list' },
      ]);
      expect(liveText(fixture)).toBe('Alan Grey added');
      expect(input.value).toBe('');
      expect(panelOpen()).toBe(true);
      // The next pick lands after it: value order is pick order.
      await clickRow(fixture, 'Adam Brown');
      expect(host.f.agentIds().value()).toEqual([3, 1]);
      expect(host.f.agentIds().dirty()).toBe(true);
    });

    it('a pointer pick on a chosen row removes its chip', async () => {
      const { fixture, host, input } = await setup(Host);
      await openList(fixture, input);
      await clickRow(fixture, 'Bob Stone');
      await clickRow(fixture, 'Carol White');
      await clickRow(fixture, 'Bob Stone');
      expect(host.f.agentIds().value()).toEqual([5]);
      expect(host.removals).toEqual([4]);
      expect(liveText(fixture)).toBe('Bob Stone removed');
      expect(panelOpen()).toBe(true);
    });

    it('Enter on a highlighted chosen row removes its chip', async () => {
      const { fixture, host, input } = await setup(Host);
      host.model.set({ agentIds: [4] });
      await settle(fixture);
      await type(fixture, input, 'Bob');
      await press(fixture, input, 'Enter');
      expect(host.f.agentIds().value()).toEqual([]);
      expect(host.removals).toEqual([4]);
      expect(panelOpen()).toBe(true);
    });

    it('Tab after a pointer toggle-off on the browse list does not put the entity back', async () => {
      const { fixture, host, input } = await setup(Host);
      host.model.set({ agentIds: [4] });
      await settle(fixture);
      await openList(fixture, input);
      await clickRow(fixture, 'Bob Stone');
      expect(host.f.agentIds().value()).toEqual([]);
      await press(fixture, input, 'Tab');
      expect(host.f.agentIds().value()).toEqual([]);
      expect(host.picks).toHaveLength(0);
      expect(host.removals).toEqual([4]);
    });

    it('Tab after an Enter toggle-off on the browse list does not put the entity back', async () => {
      const { fixture, host, input } = await setup(Host);
      host.model.set({ agentIds: [4] });
      await settle(fixture);
      await openList(fixture, input);
      // Arrow down the browse rows to Bob's.
      for (let step = 0; step < 6 && activeLabel() !== 'Bob Stone'; step++) {
        await press(fixture, input, 'ArrowDown');
      }
      expect(activeLabel()).toBe('Bob Stone');
      await press(fixture, input, 'Enter');
      expect(host.f.agentIds().value()).toEqual([]);
      expect(panelOpen()).toBe(true);
      await press(fixture, input, 'Tab');
      expect(host.f.agentIds().value()).toEqual([]);
      expect(host.picks).toHaveLength(0);
      expect(host.removals).toEqual([4]);
    });

    it('auto-resolution and Tab that land on a chosen id keep its chip and clear the text', async () => {
      const { fixture, host, input } = await setup(Host);
      const outside = fixture.nativeElement.querySelector('[data-testid="outside"]') as HTMLElement;
      host.model.set({ agentIds: [4, 5] });
      await settle(fixture);
      await type(fixture, input, 'Bob');
      outside.focus();
      await settle(fixture);
      expect(input.value).toBe('');
      await type(fixture, input, 'Carol');
      await press(fixture, input, 'Tab');
      expect(input.value).toBe('');
      expect(host.f.agentIds().value()).toEqual([4, 5]);
      expect(host.picks).toHaveLength(0);
      expect(host.removals).toHaveLength(0);
    });

    it('auto-resolution on departure adds an unchosen entity', async () => {
      const { fixture, host, input } = await setup(Host);
      const outside = fixture.nativeElement.querySelector('[data-testid="outside"]') as HTMLElement;
      await type(fixture, input, 'Carol');
      outside.focus();
      await settle(fixture);
      expect(host.f.agentIds().value()).toEqual([5]);
      expect(host.picks.map((p) => p.source)).toEqual(['auto']);
      expect(panelOpen()).toBe(false);
    });

    it('a value written with duplicates shows each entity once, and one removal clears it', async () => {
      const { fixture, host } = await setup(Host);
      host.displayWith.set(nameOf);
      host.model.set({ agentIds: [3, 3] });
      await settle(fixture);
      expect(removeButtons(fixture).map((b) => b.getAttribute('aria-label'))).toEqual([
        'Remove Alan Grey',
      ]);
      const count = fixture.nativeElement.querySelector(
        '.tm-entity-multi-picker__count-value',
      ) as HTMLElement;
      expect(count.textContent?.trim()).toBe('1');
      removeButtons(fixture)[0].click();
      await settle(fixture);
      expect(host.f.agentIds().value()).toEqual([]);
      expect(host.removals).toEqual([3]);
      expect(removeButtons(fixture)).toHaveLength(0);
    });

    it('a chip remove button removes it and emits removed', async () => {
      const { fixture, host } = await setup(Host);
      host.displayWith.set(nameOf);
      host.model.set({ agentIds: [1, 2, 3] });
      await settle(fixture);
      removeButtons(fixture)[1].click();
      await settle(fixture);
      expect(host.f.agentIds().value()).toEqual([1, 3]);
      expect(host.removals).toEqual([2]);
      expect(liveText(fixture)).toBe('Alice Green removed');
    });

    it('create adds the created entity and closes the panel', async () => {
      const { fixture, host, input } = await setup(Host);
      host.createPage.set(FakeCreatePage);
      await settle(fixture);
      await type(fixture, input, 'Dana');
      Array.from(document.querySelectorAll<HTMLElement>('.tm-entity-picker__action'))
        .find((row) => row.textContent?.includes('Create…'))
        ?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await settle(fixture);
      (document.querySelector('[data-testid="create-save"]') as HTMLButtonElement).click();
      await settle(fixture);
      await settle(fixture);
      expect(host.f.agentIds().value()).toEqual([9]);
      expect(chipLabels(fixture)).toEqual(['Dana Reed']);
      expect(host.picks.map((p) => p.source)).toEqual(['create']);
      expect(panelOpen()).toBe(false);
      expect(document.activeElement).toBe(input);
    });

    it('label parts reach the inner rows, each part in its own isolate', async () => {
      TestBed.configureTestingModule({
        providers: [provideTellmaUi({ availableLangs: ['en', 'ar'] })],
      });
      @Component({
        imports: [TmEntityMultiPicker],
        template: `
          <tm-entity-multi-picker
            aria-label="Centers"
            [search]="search"
            [itemId]="id"
            [itemLabel]="label"
            [itemLabelParts]="parts"
          />
        `,
      })
      class CentersHost {
        readonly search = (): readonly { id: number; code: string; name: string }[] => [
          { id: 1, code: 'INV-0011', name: 'نقد' },
        ];
        readonly id = (c: { id: number }): number => c.id;
        readonly label = (c: { code: string; name: string }): string => `${c.code} - ${c.name}`;
        readonly parts = (c: { code: string; name: string }): readonly string[] => [c.code, c.name];
      }
      const fixture = TestBed.createComponent(CentersHost);
      await settle(fixture);
      const input = fixture.nativeElement.querySelector(
        '.tm-entity-picker__input',
      ) as HTMLInputElement;
      await openList(fixture, input);
      const runs = Array.from(
        (document.querySelector('.tm-entity-picker__option-label') as HTMLElement).querySelectorAll(
          'bdi',
        ),
      ).map((bdi) => bdi.textContent);
      expect(runs).toEqual(['INV-0011', 'نقد']);
    });
  });

  describe('chip labels', () => {
    it('resolve through displayWith, then the pick label, then String(id) with a warning', async () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
      try {
        const { fixture, host, input } = await setup(Host);
        // The pick label.
        await type(fixture, input, 'Bob');
        await press(fixture, input, 'Enter');
        expect(chipLabels(fixture)).toEqual(['Bob Stone']);
        // displayWith wins where it has an answer, reactively.
        host.displayWith.set((id) => (id === 4 ? 'B. Stone' : null));
        await settle(fixture);
        expect(chipLabels(fixture)).toEqual(['B. Stone']);
        // An id nobody can name.
        host.model.set({ agentIds: [4, 77] });
        await settle(fixture);
        expect(chipLabels(fixture)).toEqual(['B. Stone', '77']);
        expect(warn).toHaveBeenCalledTimes(1);
        expect(warn).toHaveBeenCalledWith(expect.stringContaining('"77"'));
      } finally {
        warn.mockRestore();
      }
    });

    it('an empty displayWith answer is not a label', async () => {
      const { fixture, host, input } = await setup(Host);
      host.displayWith.set(() => '');
      await settle(fixture);
      await type(fixture, input, 'Carol');
      await press(fixture, input, 'Enter');
      expect(chipLabels(fixture)).toEqual(['Carol White']);
    });
  });

  describe('maxItems', () => {
    it('refuses a pick at the cap with a polite message and a notice in the panel', async () => {
      const { fixture, host, input } = await setup(Host);
      host.maxItems.set(2);
      await settle(fixture);
      await openList(fixture, input);
      await clickRow(fixture, 'Adam Brown');
      expect(document.querySelector('.tm-entity-picker__notice')).toBeNull();
      await clickRow(fixture, 'Bob Stone');
      const notice = document.querySelector('.tm-entity-picker__notice') as HTMLElement;
      expect(notice.textContent?.trim()).toBe('You can choose up to 2');
      await clickRow(fixture, 'Carol White');
      expect(host.f.agentIds().value()).toEqual([1, 4]);
      expect(host.picks.map((p) => p.id)).toEqual([1, 4]);
      expect(liveText(fixture)).toBe('You can choose up to 2');
      // The refused row is not ticked.
      const carol = Array.from(document.querySelectorAll('.tm-entity-picker__option')).find((r) =>
        r.textContent?.includes('Carol White'),
      );
      expect(carol?.getAttribute('aria-selected')).toBe('false');
      // Removing still works at the cap, and makes room.
      await clickRow(fixture, 'Bob Stone');
      expect(host.f.agentIds().value()).toEqual([1]);
      expect(document.querySelector('.tm-entity-picker__notice')).toBeNull();
      await clickRow(fixture, 'Carol White');
      expect(host.f.agentIds().value()).toEqual([1, 5]);
    });

    it('a refused message repeated is announced again', async () => {
      const { fixture, host, input } = await setup(Host);
      host.maxItems.set(1);
      host.model.set({ agentIds: [1] });
      await settle(fixture);
      await openList(fixture, input);
      await clickRow(fixture, 'Bob Stone');
      expect(liveText(fixture)).toBe('You can choose up to 1');
      // Every text the region takes from here on.
      const region = (fixture.nativeElement as HTMLElement).querySelector(
        '.tm-entity-picker__live[aria-live]',
      ) as HTMLElement;
      const seen: string[] = [];
      const observer = new MutationObserver(() => seen.push(region.textContent?.trim() ?? ''));
      observer.observe(region, { childList: true, characterData: true, subtree: true });
      // No extra wait: the restore is a pending task, so stability covers it.
      await clickRow(fixture, 'Bob Stone');
      observer.disconnect();
      // Cleared, then read again: a changed region is what makes it speak.
      expect(seen[0]).toBe('');
      expect(seen.at(-1)).toBe('You can choose up to 1');
      expect(liveText(fixture)).toBe('You can choose up to 1');
    });

    it('a newer message is not overwritten by a repeat waiting to be read again', async () => {
      const { fixture, host, input } = await setup(Host);
      host.displayWith.set(nameOf);
      host.maxItems.set(1);
      host.model.set({ agentIds: [1] });
      await settle(fixture);
      await openList(fixture, input);
      await clickRow(fixture, 'Bob Stone');
      expect(liveText(fixture)).toBe('You can choose up to 1');
      // The repeated refusal clears the region and waits to restore it; the
      // removal in the same task announces something newer meanwhile.
      const bob = Array.from(
        document.querySelectorAll<HTMLElement>('.tm-entity-picker__option'),
      ).find(
        (r) =>
          r.querySelector('.tm-entity-picker__option-label')?.textContent?.trim() === 'Bob Stone',
      );
      if (bob === undefined) {
        throw new Error('no row labelled Bob Stone');
      }
      bob.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      removeButtons(fixture)[0].click();
      await settle(fixture);
      await settle(fixture);
      expect(host.f.agentIds().value()).toEqual([]);
      expect(liveText(fixture)).toBe('Adam Brown removed');
    });
  });

  describe('advanced search', () => {
    it('opens with multiple, the chosen ids and maxItems', async () => {
      const { fixture, host, input } = await setup(Host);
      host.advancedPage.set(FakeMultiPage);
      host.maxItems.set(5);
      host.model.set({ agentIds: [2, 1] });
      await settle(fixture);
      await type(fixture, input, 'Bo');
      (fixture.nativeElement.querySelector('.tm-entity-picker__magnifier') as HTMLElement).click();
      await settle(fixture);
      expect(lastPageData).toEqual({ query: 'Bo', multiple: true, ids: [2, 1], maxItems: 5 });
      (document.querySelector('[data-testid="page-close"]') as HTMLElement).click();
      await settle(fixture);
      await settle(fixture);
      expect(host.f.agentIds().value()).toEqual([2, 1]);
    });

    it('a set that adds and removes replaces the value and emits the difference', async () => {
      const { fixture, host } = await setup(Host);
      host.advancedPage.set(FakeMultiPage);
      host.model.set({ agentIds: [1, 2] });
      host.displayWith.set((id) => (id === 1 || id === 2 ? nameOf(id) : null));
      await settle(fixture);
      (fixture.nativeElement.querySelector('.tm-entity-picker__magnifier') as HTMLElement).click();
      await settle(fixture);
      (document.querySelector('[data-testid="page-add-remove"]') as HTMLElement).click();
      await settle(fixture);
      await settle(fixture);
      expect(host.f.agentIds().value()).toEqual([1, 5, 3]);
      // The page's picks seed the labels displayWith has no answer for.
      expect(chipLabels(fixture)).toEqual(['Adam Brown', 'Carol (page)', 'Alan (page)']);
      expect(host.picks).toEqual([
        { id: 5, label: 'Carol (page)', item: DIRECTORY[4], source: 'advanced' },
        { id: 3, label: 'Alan (page)', item: undefined, source: 'advanced' },
      ]);
      expect(host.removals).toEqual([2]);
      // Both happened: the announcement is the selection as it now stands.
      expect(liveText(fixture)).toBe('3 selected: Adam Brown, Carol (page), Alan (page)');
    });

    it('an empty set clears the value and emits every removal', async () => {
      const { fixture, host } = await setup(Host);
      host.advancedPage.set(FakeMultiPage);
      host.displayWith.set(nameOf);
      host.model.set({ agentIds: [3, 4] });
      await settle(fixture);
      (fixture.nativeElement.querySelector('.tm-entity-picker__magnifier') as HTMLElement).click();
      await settle(fixture);
      (document.querySelector('[data-testid="page-empty"]') as HTMLElement).click();
      await settle(fixture);
      await settle(fixture);
      expect(host.f.agentIds().value()).toEqual([]);
      expect(host.removals).toEqual([3, 4]);
      expect(host.picks).toHaveLength(0);
      expect(chipLabels(fixture)).toEqual([]);
      expect(liveText(fixture)).toBe('Alan Grey, Bob Stone removed');
    });

    it('duplicates in the returned ids collapse, and an unchanged set emits nothing', async () => {
      const { fixture, host } = await setup(Host);
      host.advancedPage.set(FakeMultiPage);
      host.displayWith.set(nameOf);
      host.model.set({ agentIds: [1] });
      await settle(fixture);
      const magnifier = fixture.nativeElement.querySelector(
        '.tm-entity-picker__magnifier',
      ) as HTMLElement;
      magnifier.click();
      await settle(fixture);
      (document.querySelector('[data-testid="page-duplicates"]') as HTMLElement).click();
      await settle(fixture);
      await settle(fixture);
      expect(host.f.agentIds().value()).toEqual([4, 1]);
      expect(host.picks.map((p) => p.id)).toEqual([4]);
      expect(liveText(fixture)).toBe('Bob Stone added');

      magnifier.click();
      await settle(fixture);
      (document.querySelector('[data-testid="page-same"]') as HTMLElement).click();
      await settle(fixture);
      await settle(fixture);
      expect(host.f.agentIds().value()).toEqual([4, 1]);
      expect(host.picks).toHaveLength(1);
      expect(host.removals).toHaveLength(0);
    });

    it('warns in development when the page returns more than maxItems', async () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
      try {
        const { fixture, host } = await setup(Host);
        host.advancedPage.set(FakeMultiPage);
        host.displayWith.set(nameOf);
        host.maxItems.set(2);
        await settle(fixture);
        (
          fixture.nativeElement.querySelector('.tm-entity-picker__magnifier') as HTMLElement
        ).click();
        await settle(fixture);
        (document.querySelector('[data-testid="page-add-remove"]') as HTMLElement).click();
        await settle(fixture);
        await settle(fixture);
        expect(warn).toHaveBeenCalledWith(expect.stringContaining('more than maxItems (2)'));
      } finally {
        warn.mockRestore();
      }
    });
  });

  describe('keyboard', () => {
    it('Backspace in the empty input moves to the last chip; a second Backspace removes it', async () => {
      const { fixture, host, input } = await setup(Host);
      host.displayWith.set(nameOf);
      host.model.set({ agentIds: [1, 2] });
      await settle(fixture);
      input.focus();
      expect(await press(fixture, input, 'Backspace')).toBe(true);
      const buttons = removeButtons(fixture);
      expect(document.activeElement).toBe(buttons[1]);
      await press(fixture, buttons[1], 'Backspace');
      expect(host.f.agentIds().value()).toEqual([1]);
      // Focus moves to the chip before it (there is none after).
      expect(document.activeElement).toBe(removeButtons(fixture)[0]);
    });

    it('Backspace in an input holding text edits the text', async () => {
      const { fixture, host, input } = await setup(Host);
      host.displayWith.set(nameOf);
      host.model.set({ agentIds: [1] });
      await settle(fixture);
      await type(fixture, input, 'Bo');
      expect(await press(fixture, input, 'Backspace')).toBe(false);
      expect(document.activeElement).toBe(input);
    });

    it('a held Backspace never removes chips on its repeats', async () => {
      const { fixture, host, input } = await setup(Host);
      host.displayWith.set(nameOf);
      host.model.set({ agentIds: [1, 2, 3] });
      await settle(fixture);
      input.focus();
      await press(fixture, input, 'Backspace');
      for (let i = 0; i < 3; i++) {
        await press(fixture, document.activeElement as Element, 'Backspace', { repeat: true });
      }
      expect(host.f.agentIds().value()).toEqual([1, 2, 3]);
    });

    it('ArrowLeft at the caret start moves to the last chip; ArrowRight past it returns', async () => {
      const { fixture, host, input } = await setup(Host);
      host.displayWith.set(nameOf);
      host.model.set({ agentIds: [1, 2] });
      await settle(fixture);
      await type(fixture, input, 'Bo');
      input.setSelectionRange(1, 1);
      expect(await press(fixture, input, 'ArrowLeft')).toBe(false); // the caret moves
      expect(document.activeElement).toBe(input);
      // At the start, with the dropdown open (aria stops the arrow's
      // propagation there), it moves to the last chip.
      input.setSelectionRange(0, 0);
      await press(fixture, input, 'ArrowLeft');
      const last = removeButtons(fixture)[1];
      expect(document.activeElement).toBe(last);
      // Leaving the input resolved its text, as any departure does: the
      // unique match came on after the chip focus moved to.
      expect(host.f.agentIds().value()).toEqual([1, 2, 4]);
      expect(input.value).toBe('');
      await press(fixture, last, 'ArrowLeft');
      expect(document.activeElement).toBe(removeButtons(fixture)[0]);
      await press(fixture, document.activeElement as Element, 'ArrowLeft'); // past the first
      expect(document.activeElement).toBe(removeButtons(fixture)[0]);
      await press(fixture, document.activeElement as Element, 'ArrowRight');
      await press(fixture, document.activeElement as Element, 'ArrowRight');
      await press(fixture, document.activeElement as Element, 'ArrowRight');
      expect(document.activeElement).toBe(input);
    });

    it('in RTL the chips sit behind ArrowRight', async () => {
      const { fixture, host, input } = await setup(Host);
      host.displayWith.set(nameOf);
      host.model.set({ agentIds: [1, 2] });
      (fixture.nativeElement as HTMLElement).setAttribute('dir', 'rtl');
      await settle(fixture);
      input.focus();
      await press(fixture, input, 'ArrowLeft');
      expect(document.activeElement).toBe(input);
      await press(fixture, input, 'ArrowRight');
      expect(document.activeElement).toBe(removeButtons(fixture)[1]);
      await press(fixture, document.activeElement as Element, 'ArrowLeft');
      expect(document.activeElement).toBe(input);
    });

    it('removing the last chip by keyboard returns focus to the input', async () => {
      const { fixture, host, input } = await setup(Host);
      host.displayWith.set(nameOf);
      host.model.set({ agentIds: [1] });
      await settle(fixture);
      input.focus();
      await press(fixture, input, 'Backspace');
      await press(fixture, document.activeElement as Element, 'Delete');
      await settle(fixture);
      expect(host.f.agentIds().value()).toEqual([]);
      expect(document.activeElement).toBe(input);
      // The emptied chip list leaves the accessibility tree.
      const list = fixture.nativeElement.querySelector('tm-chip-set') as HTMLElement;
      expect(getComputedStyle(list).display).toBe('none');
    });

    it('the chips are one tab stop before the input', async () => {
      const { fixture, host, input } = await setup(Host);
      host.displayWith.set(nameOf);
      host.model.set({ agentIds: [1, 2, 3] });
      await settle(fixture);
      const tabbable = Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('button, input'),
      ).filter((element) => element.tabIndex >= 0 && element.dataset['testid'] !== 'outside');
      expect(tabbable).toEqual([removeButtons(fixture)[0], input]);
    });
  });

  describe('touch', () => {
    it('leaving the input (for a chip or outside) and leaving the chips for outside report touch; moving among the chips does not', async () => {
      const { fixture, host, input } = await setup(Host);
      const outside = fixture.nativeElement.querySelector('[data-testid="outside"]') as HTMLElement;
      host.displayWith.set(nameOf);
      host.model.set({ agentIds: [1] });
      await settle(fixture);
      removeButtons(fixture)[0].focus();
      input.focus();
      removeButtons(fixture)[0].focus();
      await settle(fixture);
      // The input's own departure (to the chip) counts as a visit.
      expect(host.f.agentIds().touched()).toBe(true);
      host.f.agentIds().reset();
      await settle(fixture);
      removeButtons(fixture)[0].focus();
      await settle(fixture);
      expect(host.f.agentIds().touched()).toBe(false);
      outside.focus();
      await settle(fixture);
      expect(host.f.agentIds().touched()).toBe(true);
    });
  });

  describe('standalone, disabled and read-only', () => {
    it('draws the field box itself and anchors the panel to it', async () => {
      const { fixture, input } = await setup(StandaloneHost);
      const hostElement = fixture.nativeElement.querySelector(
        'tm-entity-multi-picker',
      ) as HTMLElement;
      expect(hostElement.hasAttribute('data-tm-field-box')).toBe(true);
      expect(hostElement.classList.contains('tm-entity-multi-picker--standalone')).toBe(true);
      expect(input.classList.contains('tm-input--in-field')).toBe(true);
      expect(input.getAttribute('aria-label')).toBe('Agents');
      expect(hostElement.getAttribute('aria-label')).toBeNull();
      await openList(fixture, input);
      // The panel matches the box's width, not the input's.
      const panel = document.querySelector('.tm-entity-picker__panel') as HTMLElement;
      const pane = panel.closest('.cdk-overlay-pane') as HTMLElement;
      expect(Math.round(pane.getBoundingClientRect().width)).toBe(
        Math.round(hostElement.getBoundingClientRect().width),
      );
    });

    it('disabled: chips stay without remove buttons, and nothing searches', async () => {
      const { fixture, host, input } = await setup(StandaloneHost);
      host.disabled.set(true);
      await settle(fixture);
      expect(chipLabels(fixture)).toEqual(['Alice Green', 'Bob Stone']);
      expect(removeButtons(fixture)).toHaveLength(0);
      expect(input.disabled).toBe(true);
      await openList(fixture, input);
      expect(host.calls).toHaveLength(0);
    });

    it('read-only: chips stay without remove buttons and Backspace removes nothing', async () => {
      const { fixture, host, input } = await setup(StandaloneHost);
      host.readonly.set(true);
      await settle(fixture);
      expect(removeButtons(fixture)).toHaveLength(0);
      input.focus();
      await press(fixture, input, 'Backspace');
      expect(host.ids()).toEqual([2, 4]);
      expect(document.activeElement).toBe(input);
    });
  });

  describe('harness', () => {
    it('reads chips and the badge, searches, picks and removes', async () => {
      const { fixture, host } = await setup(Host);
      host.displayWith.set(nameOf);
      await settle(fixture);
      const loader = TestbedHarnessEnvironment.loader(fixture);
      const picker = await loader.getHarness(TmEntityMultiPickerHarness);
      expect(await picker.getChipLabels()).toEqual([]);
      expect(await picker.getCountBadge()).toBeNull();

      await picker.search('Al');
      await settle(fixture);
      expect(await (await picker.getPicker()).getOptionLabels()).toEqual([
        'Alice Green',
        'Alan Grey',
      ]);
      await picker.pick('Alice Green');
      await settle(fixture);
      await picker.pick('Bob Stone'); // the list stayed open on the browse rows
      await settle(fixture);
      expect(await picker.getChipLabels()).toEqual(['Alice Green', 'Bob Stone']);
      expect(await picker.getCountBadge()).toBe('2');

      await picker.removeChip('Alice Green');
      await settle(fixture);
      expect(await picker.getChipLabels()).toEqual(['Bob Stone']);
      expect(host.f.agentIds().value()).toEqual([4]);
      await expect(picker.removeChip('Nobody')).rejects.toThrow(/no chip labelled/);
    });
  });
});
