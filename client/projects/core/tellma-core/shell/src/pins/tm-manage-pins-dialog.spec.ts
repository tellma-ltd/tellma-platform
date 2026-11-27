// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { type HarnessLoader, HarnessPredicate } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TestBed } from '@angular/core/testing';
import { type TmLaunchDocs, TmPreferences, tmPinsKey, type TmPinsDoc } from '@tellma/core';
import { TmPinnedBarHarness } from '@tellma/core-testing';
import { TmButtonHarness, TmMenuHarness, type TmModalHarness, TmReorderListHarness, TmSwitchHarness } from '@tellma/core-ui-testing';
import { page } from 'vitest/browser';

import { type TmAppRig, tmAppRig, tmNavigate, tmSettle, tmSettleUntil, TM_T17_DOCS } from '../../../src/routes/tm-app.testing.util';
import { TM_PIN_LIMITS } from './tm-pin-rules';
import { TmPins } from './tm-pins';

// The Manage pins dialog in a whole test app, opened from the pinned bar:
// its rows with their icons, labels and locks, folders with their own lists,
// reordering, a pin dragged between the top list and a folder's, Rename with
// its refusals, Move to, Remove with where focus goes, New folder and its
// cap, the bar's switch, which applies at once, and Save (overwriting a
// change made meanwhile), Cancel and Escape, Save held while the tenant is
// frozen.

/** A pins document of items. */
function doc(...items: readonly unknown[]): TmPinsDoc {
  return { v: 1, items: items as TmPinsDoc['items'] };
}

/** A pin of the credit review page with a query. */
function pin(n: number, title: string): { readonly id: string; readonly url: string; readonly title: string } {
  return { id: `a${String(n).padStart(7, '0')}`, url: `credit-review?q=${n}`, title };
}

/** The stored pins: two pins, a folder of two, and a pin the user cannot open. */
const STORED = doc(
  pin(1, 'Receivables'),
  { id: 'f0000001', folder: 'Daily', items: [pin(2, 'Payables'), pin(3, 'Cash')] },
  { id: 'b0000001', url: 'approvals', title: 'Approvals' },
  { id: 'b0000002', url: 'centers', title: 'Cost centres' },
);

/** The distribution's strings. */
const STRINGS = { en: { Acme_CreditReview: 'Credit review', Acme_Approvals: 'Approvals' } };

/** Tenant 17's documents with stored pins, granting the centers, in a state. */
function docs(pins: TmPinsDoc, state: TmLaunchDocs['tenant']['state'] = 'Active'): TmLaunchDocs {
  const { me } = TM_T17_DOCS;
  const access = { ...me.access, securables: [{ resource: 'gl.Center', action: 'Read', hasFilter: false }] };
  return { ...TM_T17_DOCS, tenant: { ...TM_T17_DOCS.tenant, state }, me: { ...me, access, preferences: { 'nav.pinned': JSON.stringify(pins) } } };
}

/** The dialog's element. */
function dialog(): HTMLElement {
  return document.querySelector<HTMLElement>('tm-manage-pins-dialog')!;
}

/** The ids of the dialog's rows, in order, a folder's pins after it. */
function rowIds(): string[] {
  return [...dialog().querySelectorAll<HTMLElement>('[data-tm-row]')].map((row) => row.dataset['tmRow']!);
}

/** A row's label, then its lock's line when it has one. */
function rowText(id: string): string {
  const parts = dialog().querySelectorAll(`[data-tm-row="${id}"] > .tm-manage-pins__label, [data-tm-row="${id}"] > .tm-manage-pins__revoked`);
  return [...parts].map((part) => (part.textContent ?? '').replace(/\s+/g, ' ').trim()).join(' ');
}

/** A row's button by its accessible name's start. */
function rowButton(id: string, name: string): HTMLButtonElement {
  return [...dialog().querySelectorAll<HTMLButtonElement>(`[data-tm-row="${id}"] button`)].find((button) => button.getAttribute('aria-label')?.startsWith(name))!;
}

/** A footer button by its text. */
function footerButton(text: string): HTMLButtonElement {
  return [...document.querySelectorAll<HTMLButtonElement>('[tmModalFooter] button')].find((button) => button.textContent?.trim() === text)!;
}

/** Types into the rename field and presses Enter. */
async function renameTo(text: string): Promise<void> {
  const field = dialog().querySelector<HTMLInputElement>('input')!;
  field.value = text;
  field.dispatchEvent(new Event('input'));
  field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
  await tmSettle();
}

/** A row's draggable element in its list. */
function listRow(id: string): HTMLElement {
  return dialog().querySelector<HTMLElement>(`[data-tm-row="${id}"]`)!.closest<HTMLElement>('.tm-reorder-list__row')!;
}

/** Drags a row onto a list's frame and drops it below another row of that list. */
function dragRow(from: string, frame: HTMLElement, below: string): void {
  const data = new DataTransfer();
  const source = listRow(from);
  const clientY = listRow(below).getBoundingClientRect().bottom - 1;
  const init = { bubbles: true, cancelable: true, dataTransfer: data, clientY };
  source.dispatchEvent(new DragEvent('dragstart', init));
  frame.dispatchEvent(new DragEvent('dragenter', init));
  frame.dispatchEvent(new DragEvent('dragover', init));
  frame.dispatchEvent(new DragEvent('drop', init));
  source.dispatchEvent(new DragEvent('dragend', init));
}

/** The stored pins' ids, a folder's as `id[pin ids]`. */
function storedIds(): string[] {
  return TestBed.inject(TmPreferences)
    .get(tmPinsKey)()
    .items.map((item) => ('folder' in item ? `${item.id}[${item.items.map((inner) => inner.id).join(' ')}]` : item.id));
}

describe('the Manage pins dialog', () => {
  let rig: TmAppRig;
  let loader: HarnessLoader;
  let modal: TmModalHarness;
  const announced: string[] = [];
  const viewport = [window.innerWidth, window.innerHeight] as const;

  afterEach(async () => {
    TestBed.resetTestingModule();
    announced.length = 0;
    await page.viewport(...viewport);
  });

  /** Starts the app and opens the dialog from the bar. */
  async function open(pins: TmPinsDoc = STORED, state: TmLaunchDocs['tenant']['state'] = 'Active'): Promise<void> {
    await page.viewport(1280, 900);
    rig = await tmAppRig({ docs: docs(pins, state), strings: STRINGS });
    vi.spyOn(TestBed.inject(LiveAnnouncer), 'announce').mockImplementation((message) => {
      announced.push(String(message));
      return Promise.resolve();
    });
    const router = await tmNavigate('/17/credit-review');
    await tmSettleUntil(() => document.querySelector('tm-pinned-bar .tm-pinned-bar__control') !== null, 'the pinned bar renders');
    loader = TestbedHarnessEnvironment.documentRootLoader(router.fixture);
    modal = await (await loader.getHarness(TmPinnedBarHarness)).openManagePins();
    await tmSettle();
  }

  /** The events of a name the app reported, by their properties. */
  const events = (name: string): unknown[] =>
    rig.telemetry
      .recent()
      .filter((item) => item.name === name)
      .map((item) => item.props);

  it('lists the pins and folders, a folder holding its own list, a pin the user cannot open locked', async () => {
    await open();
    expect(await modal.getTitle()).toBe('Manage pins');
    expect(rowIds()).toEqual(['a0000001', 'f0000001', 'a0000002', 'a0000003', 'b0000001', 'b0000002']);
    expect(rowText('a0000001')).toBe('Receivables');
    expect(rowText('b0000001')).toBe('Approvals No access');
    // A plain URL's pin takes its entry's label and icon.
    expect(rowText('b0000002')).toBe('Centers');
    expect(dialog().querySelector('[data-tm-row="b0000002"] tm-icon svg')).not.toBeNull();
    const lists = await loader.getAllHarnesses(TmReorderListHarness);
    expect(await Promise.all(lists.map(async (list) => list.getLabel()))).toEqual(['Manage pins', 'Daily']);
    // The folder's disclosure hides its pins.
    rowButton('f0000001', 'Pages in Daily').click();
    await tmSettle();
    expect(rowIds()).toEqual(['a0000001', 'f0000001', 'b0000001', 'b0000002']);
  });

  it('reorders by keyboard, which Save keeps and Cancel drops', async () => {
    await open(doc(pin(1, 'Receivables'), pin(2, 'Payables'), pin(3, 'Cash')));
    const [list] = await loader.getAllHarnesses(TmReorderListHarness);
    await list.press(0, 'Space');
    await list.pressFocused('ArrowDown');
    await list.pressFocused('Space');
    await tmSettle();
    expect(rowIds()).toEqual(['a0000002', 'a0000001', 'a0000003']);
    expect(storedIds()).toEqual(['a0000001', 'a0000002', 'a0000003']);
    footerButton('Cancel').click();
    await tmSettle();
    expect(storedIds()).toEqual(['a0000001', 'a0000002', 'a0000003']);
    modal = await (await loader.getHarness(TmPinnedBarHarness)).openManagePins();
    await tmSettle();
    const [again] = await loader.getAllHarnesses(TmReorderListHarness);
    await again.press(2, 'Space');
    await again.pressFocused('Home');
    await again.pressFocused('Space');
    footerButton('Save').click();
    await tmSettle();
    expect(document.querySelector('tm-manage-pins-dialog')).toBeNull();
    expect(storedIds()).toEqual(['a0000003', 'a0000001', 'a0000002']);
    expect(events('tm.shell.pins-changed')).toEqual([{ op: 'replace', via: 'dialog', count: '1-5' }]);
  });

  it("drags a pin from the top list into a folder's list, and back out", async () => {
    await open();
    const frames = (): HTMLElement[] => [...dialog().querySelectorAll<HTMLElement>('.tm-reorder-list__frame')];
    dragRow('a0000001', frames()[1], 'a0000003');
    await tmSettle();
    expect(rowIds()).toEqual(['f0000001', 'a0000002', 'a0000003', 'a0000001', 'b0000001', 'b0000002']);
    dragRow('a0000002', frames()[0], 'b0000002');
    await tmSettle();
    expect(rowIds()).toEqual(['f0000001', 'a0000003', 'a0000001', 'b0000001', 'b0000002', 'a0000002']);
    footerButton('Save').click();
    await tmSettle();
    expect(storedIds()).toEqual(['f0000001[a0000003 a0000001]', 'b0000001', 'b0000002', 'a0000002']);
  });

  it('discards the copy on Escape', async () => {
    await open();
    rowButton('a0000001', 'Remove Receivables').click();
    await tmSettle();
    expect(rowIds()).not.toContain('a0000001');
    dialog().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', bubbles: true, cancelable: true }));
    await tmSettle();
    expect(document.querySelector('tm-manage-pins-dialog')).toBeNull();
    expect(storedIds()).toEqual(['a0000001', 'f0000001[a0000002 a0000003]', 'b0000001', 'b0000002']);
    expect(events('tm.shell.pins-changed')).toEqual([]);
  });

  it('saves its copy over a change made meanwhile', async () => {
    await open();
    rowButton('a0000001', 'Remove Receivables').click();
    await tmSettle();
    // Another tab pins a page while the dialog is open.
    TestBed.inject(TmPreferences).set(tmPinsKey, doc(...STORED.items, pin(9, 'Later')));
    await tmSettle();
    footerButton('Save').click();
    await tmSettle();
    expect(storedIds()).toEqual(['f0000001[a0000002 a0000003]', 'b0000001', 'b0000002']);
  });

  it('renames a pin, a blank name giving its derived label back, and a folder, refusing a duplicate', async () => {
    await open(doc(pin(1, 'Receivables'), { id: 'f0000001', folder: 'Daily', items: [] }, { id: 'f0000002', folder: 'Weekly', items: [] }));
    rowButton('a0000001', 'Rename Receivables').click();
    await tmSettle();
    const field = dialog().querySelector<HTMLInputElement>('input')!;
    expect([field.value, field.selectionStart, field.selectionEnd, document.activeElement === field]).toEqual(['Receivables', 0, 11, true]);
    await renameTo('Money in');
    expect(rowText('a0000001')).toBe('Money in');
    expect(document.activeElement).toBe(rowButton('a0000001', 'Rename'));
    rowButton('f0000001', 'Rename Daily').click();
    await tmSettle();
    await renameTo(' weekly ');
    expect(dialog().textContent).toContain('A folder with this name exists');
    await renameTo('Every day');
    expect(rowText('f0000001')).toBe('Every day');
    footerButton('Save').click();
    await tmSettle();
    const saved = TestBed.inject(TmPreferences).get(tmPinsKey)();
    expect(saved.items[0]).toMatchObject({ id: 'a0000001', label: 'Money in' });
    expect(saved.items[1]).toMatchObject({ id: 'f0000001', folder: 'Every day' });
  });

  it('cancels a rename with Escape, keeping the dialog open', async () => {
    await open();
    rowButton('a0000001', 'Rename Receivables').click();
    await tmSettle();
    const field = dialog().querySelector<HTMLInputElement>('input')!;
    field.value = 'Something else';
    field.dispatchEvent(new Event('input'));
    field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', bubbles: true, cancelable: true }));
    await tmSettle();
    expect(document.querySelector('tm-manage-pins-dialog')).not.toBeNull();
    expect(rowText('a0000001')).toBe('Receivables');
  });

  it('files a pin with Move to, announcing where it went', async () => {
    await open();
    rowButton('a0000001', 'Move Receivables to').click();
    await tmSettle();
    const menu = await loader.getHarness(new HarnessPredicate(TmMenuHarness, {}));
    expect(await menu.getItemLabels()).toEqual(['No folder', 'Daily']);
    await menu.clickItem('Daily');
    await tmSettle();
    expect(rowIds()).toEqual(['f0000001', 'a0000002', 'a0000003', 'a0000001', 'b0000001', 'b0000002']);
    expect(announced).toContain('Moved Receivables to Daily');
    rowButton('a0000002', 'Move Payables to').click();
    await tmSettle();
    await (await loader.getHarness(new HarnessPredicate(TmMenuHarness, {}))).clickItem('No folder');
    await tmSettle();
    expect(rowIds().at(-1)).toBe('a0000002');
    expect(announced).toContain('Moved Payables out of its folder');
  });

  it('removes a row, focus going to the next row, else the previous one, else New folder', async () => {
    await open(doc(pin(1, 'Receivables'), pin(2, 'Payables')));
    rowButton('a0000001', 'Remove Receivables').click();
    await tmSettle();
    expect(rowIds()).toEqual(['a0000002']);
    const handles = (): HTMLElement[] => [...dialog().querySelectorAll<HTMLElement>('.tm-reorder-list__handle')];
    expect(document.activeElement).toBe(handles()[0]);
    rowButton('a0000002', 'Remove Payables').click();
    await tmSettle();
    expect(rowIds()).toEqual([]);
    expect(dialog().textContent).toContain('Nothing pinned yet.');
    expect(document.activeElement).toBe(footerButton('New folder'));
  });

  it('adds a folder with a name not yet taken and renames it at once, up to 10 folders', async () => {
    await open(doc({ id: 'f0000001', folder: 'New folder', items: [] }));
    footerButton('New folder').click();
    await tmSettle();
    const field = dialog().querySelector<HTMLInputElement>('input')!;
    expect(field.value).toBe('New folder 2');
    expect(document.activeElement).toBe(field);
    TestBed.resetTestingModule();
    const ten = doc(...Array.from({ length: TM_PIN_LIMITS.folders }, (_, n) => ({ id: `f${String(n).padStart(7, '0')}`, folder: `Folder ${n}`, items: [] })));
    await open(ten);
    const button = await loader.getHarness(new HarnessPredicate(TmButtonHarness, { selector: '.tm-manage-pins__new' }));
    expect(await button.getDisabledReason()).toBe('You have 10 folders, the most a user can have.');
  });

  it('shows or hides the bar at once from its switch', async () => {
    await open();
    const toggle = await loader.getHarness(TmSwitchHarness);
    expect(await toggle.isChecked()).toBe(true);
    await toggle.toggle();
    await tmSettle();
    expect(TestBed.inject(TmPins).visible()).toBe(false);
    expect(document.querySelector('tm-pinned-bar')).toBeNull();
    expect(events('tm.shell.pinned-bar-toggled')).toEqual([{ visible: false }]);
  });

  it('holds Save while the tenant is frozen', async () => {
    await open(STORED, 'Frozen');
    const buttons = await loader.getAllHarnesses(new HarnessPredicate(TmButtonHarness, { selector: '[tmModalFooter] button' }));
    const texts = await Promise.all(buttons.map(async (button) => button.getText()));
    expect(await buttons[texts.indexOf('Save')].getDisabledReason()).toBe('Acme is under maintenance.');
    footerButton('Save').click();
    await tmSettle();
    expect(document.querySelector('tm-manage-pins-dialog')).not.toBeNull();
  });
});
