// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, ElementRef, inject, input, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { applyEach, form, required } from '@angular/forms/signals';

import type { TmCellEditor } from '@tellma/core-ui/contracts';
import { provideTellmaUi, TM_CELL_EDITOR_HOST } from '@tellma/core-ui';
import { tmPushEscapeDismissal } from '@tellma/core-ui/overlay';
import { TmGridHarness } from '@tellma/core-ui-testing';

import { TmGrid } from './tm-grid';
import { TmGridColumn } from './tm-grid-column';
import { TmGridEditorDef } from './tm-grid-templates';

// Section: the fixture — a grant grid with a two-field Window column and a
// multi-line Filter column, both popup columns, and a plain User column.

interface GrantWindow {
  readonly startsAt: string | null;
  readonly endsAt: string | null;
}

interface Grant {
  readonly id: number;
  readonly user: string;
  readonly role: string;
  readonly window: GrantWindow;
  readonly filter: string | null;
}

const PERMANENT: GrantWindow = { startsAt: null, endsAt: null };

function makeGrants(): Grant[] {
  return [
    { id: 1, user: 'Ann', role: 'Clerk', window: PERMANENT, filter: 'A = 1' },
    { id: 2, user: 'Bob', role: 'Administrator', window: PERMANENT, filter: 'B = 2' },
    { id: 3, user: 'Cy', role: 'Clerk', window: PERMANENT, filter: null },
  ];
}

/** A two-field sub-form editing one window value; its text channel stands down. */
@Component({
  selector: 'tm-test-window-editor',
  template: `
    <input
      class="starts"
      aria-label="Starts"
      [readOnly]="readonly()"
      [value]="value().startsAt ?? ''"
      (input)="write('startsAt', $event)"
    />
    <input
      class="ends"
      aria-label="Ends"
      [readOnly]="readonly()"
      [value]="value().endsAt ?? ''"
      (input)="write('endsAt', $event)"
    />
  `,
})
class TestWindowEditor implements TmCellEditor<GrantWindow> {
  readonly readonly = input(false);
  private readonly host = inject(ElementRef).nativeElement as HTMLElement;
  readonly value = signal<GrantWindow>(PERMANENT);
  readonly text = signal<string | null>(null);
  constructor() {
    inject(TM_CELL_EDITOR_HOST, { optional: true })?.register(this as TmCellEditor<unknown>);
  }
  commit(): void {}
  cancel(): void {}
  focus(): void {
    this.host.querySelector<HTMLInputElement>('.starts')?.focus();
  }
  seed(text: string): void {
    this.value.update((window) => ({ ...window, startsAt: text }));
    this.host.querySelector<HTMLInputElement>('.starts')!.value = text;
  }
  protected write(field: keyof GrantWindow, event: Event): void {
    const text = (event.target as HTMLInputElement).value;
    this.value.update((window) => ({ ...window, [field]: text === '' ? null : text }));
  }
}

/** A multi-line editor: a textarea, plus a hint. */
@Component({
  selector: 'tm-test-filter-editor',
  template: `
    <textarea
      class="filter"
      aria-label="Filter"
      [readOnly]="readonly()"
      [value]="value() ?? ''"
      (input)="onInput($event)"
    ></textarea>
    <p>Leave empty for all records.</p>
  `,
})
class TestFilterEditor implements TmCellEditor<string | null> {
  readonly readonly = input(false);
  private readonly host = inject(ElementRef).nativeElement as HTMLElement;
  readonly value = signal<string | null>(null);
  readonly text = computed(() => this.value() ?? '');
  constructor() {
    inject(TM_CELL_EDITOR_HOST, { optional: true })?.register(this as TmCellEditor<unknown>);
  }
  commit(): void {}
  cancel(): void {}
  focus(): void {
    this.host.querySelector<HTMLTextAreaElement>('.filter')?.focus();
  }
  seed(text: string): void {
    this.value.set(text);
    this.host.querySelector<HTMLTextAreaElement>('.filter')!.value = text;
  }
  protected onInput(event: Event): void {
    this.value.set((event.target as HTMLTextAreaElement).value);
  }
}

@Component({
  imports: [TmGrid, TmGridColumn, TmGridEditorDef, TestWindowEditor, TestFilterEditor],
  template: `
    <tm-grid
      gridId="popup-editor-spec"
      [field]="f"
      [rowId]="rowId"
      [readonly]="readonly()"
      style="block-size: 300px"
    >
      <tm-grid-column key="user" header="User" [width]="120" />
      <tm-grid-column
        key="window"
        header="Window"
        [width]="140"
        [format]="windowText"
        [editorPopup]="{ minWidth: 360 }"
      >
        <tm-test-window-editor *tmGridEditor="let readonly = readonly" [readonly]="readonly" />
      </tm-grid-column>
      <tm-grid-column
        key="filter"
        header="Filter"
        [width]="140"
        [readonly]="takesNoFilter"
        [editorPopup]="{}"
      >
        <tm-test-filter-editor *tmGridEditor="let readonly = readonly" [readonly]="readonly" />
      </tm-grid-column>
    </tm-grid>
    <input id="outside-input" />
  `,
})
class PopupHost {
  readonly model = signal<Grant[]>(makeGrants());
  readonly withRequiredFilter = signal(false);
  readonly f = form(this.model, (grants) => {
    applyEach(grants, (grant) => {
      required(grant.filter, { when: () => this.withRequiredFilter() });
    });
  });
  readonly readonly = signal(false);
  readonly rowId = (grant: Grant): number => grant.id;
  readonly windowText = (window: GrantWindow | null): string =>
    window === null || (window.startsAt === null && window.endsAt === null)
      ? 'Permanent'
      : `${window.startsAt ?? 'Now'} to ${window.endsAt ?? 'Open'}`;
  readonly takesNoFilter = (grant: Grant): boolean => grant.role === 'Administrator';
}

// Section: helpers.

async function stable(fixture: ComponentFixture<unknown>): Promise<void> {
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

function keydown(target: Element, key: string, init: KeyboardEventInit = {}): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
  target.dispatchEvent(event);
  return event;
}

function cellAt(scroller: HTMLElement, row: number, col: number): HTMLElement | null {
  return scroller.querySelector<HTMLElement>(
    `[data-tm-cell][data-row="${row}"][data-col="${col}"]`,
  );
}

function pointerPress(target: Element): void {
  const init: PointerEventInit = { bubbles: true, cancelable: true, button: 0, pointerType: 'mouse' };
  target.dispatchEvent(new PointerEvent('pointerdown', init));
  target.dispatchEvent(new PointerEvent('pointerup', init));
}

function pane(scroller: HTMLElement): HTMLElement | null {
  return scroller.querySelector<HTMLElement>('[data-tm-popup]');
}

function typeInto(field: HTMLInputElement | HTMLTextAreaElement, text: string): void {
  field.value = text;
  field.dispatchEvent(new Event('input', { bubbles: true }));
}

async function setup(): Promise<{
  fixture: ComponentFixture<PopupHost>;
  host: PopupHost;
  scroller: HTMLElement;
}> {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(PopupHost);
  await stable(fixture);
  const scroller = (fixture.nativeElement as HTMLElement).querySelector(
    '.tm-grid__scroller',
  ) as HTMLElement;
  return { fixture, host: fixture.componentInstance, scroller };
}

/** Activates a cell by pressing it, as a user would. */
async function activate(
  fixture: ComponentFixture<unknown>,
  scroller: HTMLElement,
  row: number,
  col: number,
): Promise<void> {
  pointerPress(cellAt(scroller, row, col)!);
  await stable(fixture);
}

// Section: the tests.

describe('tm-grid (popup editors)', () => {
  it('a typed seed opens the pane in edit mode, inside its gridcell, the arrows staying in the editor', async () => {
    const { fixture, scroller } = await setup();
    await activate(fixture, scroller, 0, 2);

    const opening = keydown(cellAt(scroller, 0, 2)!, 'C');
    expect(opening.defaultPrevented).toBe(true);
    const body = pane(scroller)!;
    expect(body).not.toBeNull();
    // Hosted by the gridcell: focus inside the pane is focus inside the grid.
    expect(cellAt(scroller, 0, 2)!.contains(body)).toBe(true);
    const textarea = body.querySelector('textarea')!;
    expect(textarea.value).toBe('C');
    expect(document.activeElement).toBe(textarea);

    // Always edit mode: arrows and F2 neither commit nor toggle.
    for (const key of ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'PageDown', 'F2']) {
      expect(keydown(textarea, key).defaultPrevented, key).toBe(false);
      await stable(fixture);
      expect(pane(scroller), key).not.toBeNull();
    }
  });

  it('Tab moves among the pane controls natively and commits from the last one', async () => {
    const { fixture, host, scroller } = await setup();
    await activate(fixture, scroller, 0, 1);
    keydown(cellAt(scroller, 0, 1)!, 'F2');
    const body = pane(scroller)!;
    const starts = body.querySelector<HTMLInputElement>('.starts')!;
    const ends = body.querySelector<HTMLInputElement>('.ends')!;
    expect(document.activeElement).toBe(starts);
    typeInto(starts, '2026-01-01');

    // From the first control, Tab is the browser's: the pane stays open.
    expect(keydown(starts, 'Tab').defaultPrevented).toBe(false);
    await stable(fixture);
    expect(pane(scroller)).not.toBeNull();

    ends.focus();
    typeInto(ends, '2026-02-01');
    expect(keydown(ends, 'Tab').defaultPrevented).toBe(true);
    await stable(fixture);
    expect(pane(scroller)).toBeNull();
    expect(host.model()[0].window).toEqual({ startsAt: '2026-01-01', endsAt: '2026-02-01' });
    // Committed and moved to the next editable cell, with no editor opened there.
    expect(document.activeElement).toBe(cellAt(scroller, 0, 2));
  });

  it('Shift+Tab from the first control commits and moves back', async () => {
    const { fixture, host, scroller } = await setup();
    await activate(fixture, scroller, 0, 1);
    keydown(cellAt(scroller, 0, 1)!, 'F2');
    const starts = pane(scroller)!.querySelector<HTMLInputElement>('.starts')!;
    typeInto(starts, '2026-03-01');
    expect(keydown(starts, 'Tab', { shiftKey: true }).defaultPrevented).toBe(true);
    await stable(fixture);
    expect(host.model()[0].window.startsAt).toBe('2026-03-01');
    expect(document.activeElement).toBe(cellAt(scroller, 0, 0));
  });

  it('Enter inserts a line in a multi-line editor; Mod+Enter commits and keeps the cell', async () => {
    const { fixture, host, scroller } = await setup();
    await activate(fixture, scroller, 0, 2);
    keydown(cellAt(scroller, 0, 2)!, 'F2');
    const textarea = pane(scroller)!.querySelector('textarea')!;
    typeInto(textarea, 'A = 1\nAND B = 2');

    expect(keydown(textarea, 'Enter').defaultPrevented).toBe(false);
    await stable(fixture);
    expect(pane(scroller)).not.toBeNull();

    expect(keydown(textarea, 'Enter', { ctrlKey: true }).defaultPrevented).toBe(true);
    await stable(fixture);
    expect(pane(scroller)).toBeNull();
    expect(host.model()[0].filter).toBe('A = 1\nAND B = 2');
    expect(document.activeElement).toBe(cellAt(scroller, 0, 2));
  });

  it('Enter in a single-line control commits and moves down', async () => {
    const { fixture, host, scroller } = await setup();
    await activate(fixture, scroller, 0, 1);
    keydown(cellAt(scroller, 0, 1)!, 'F2');
    const ends = pane(scroller)!.querySelector<HTMLInputElement>('.ends')!;
    ends.focus();
    typeInto(ends, '2026-04-01');
    expect(keydown(ends, 'Enter').defaultPrevented).toBe(true);
    await stable(fixture);
    expect(host.model()[0].window.endsAt).toBe('2026-04-01');
    expect(document.activeElement).toBe(cellAt(scroller, 1, 1));
  });

  it('Escape works in stages: the editor layer first, then the pane cancels', async () => {
    const { fixture, host, scroller } = await setup();
    await activate(fixture, scroller, 0, 2);
    keydown(cellAt(scroller, 0, 2)!, 'F2');
    const textarea = pane(scroller)!.querySelector('textarea')!;
    typeInto(textarea, 'changed');

    // An editor's own layer sits on the shared Escape stack.
    let layerClosed = false;
    const release = tmPushEscapeDismissal(() => {
      layerClosed = true;
      release();
    });
    keydown(textarea, 'Escape');
    await stable(fixture);
    expect(layerClosed).toBe(true);
    expect(pane(scroller)).not.toBeNull();

    keydown(textarea, 'Escape');
    await stable(fixture);
    expect(pane(scroller)).toBeNull();
    expect(host.model()[0].filter).toBe('A = 1');
    expect(document.activeElement).toBe(cellAt(scroller, 0, 2));
  });

  it('a commit is one undo step', async () => {
    const { fixture, host, scroller } = await setup();
    await activate(fixture, scroller, 0, 1);
    keydown(cellAt(scroller, 0, 1)!, 'F2');
    const body = pane(scroller)!;
    typeInto(body.querySelector<HTMLInputElement>('.starts')!, '2026-05-01');
    typeInto(body.querySelector<HTMLInputElement>('.ends')!, '2026-06-01');
    keydown(body.querySelector('.starts')!, 'Enter', { ctrlKey: true });
    await stable(fixture);
    expect(host.model()[0].window).toEqual({ startsAt: '2026-05-01', endsAt: '2026-06-01' });

    keydown(cellAt(scroller, 0, 1)!, 'z', { ctrlKey: true });
    await stable(fixture);
    expect(host.model()[0].window).toEqual(PERMANENT);
  });

  it('a press on another cell commits the open pane', async () => {
    const { fixture, host, scroller } = await setup();
    await activate(fixture, scroller, 0, 2);
    keydown(cellAt(scroller, 0, 2)!, 'F2');
    typeInto(pane(scroller)!.querySelector('textarea')!, 'pressed');
    pointerPress(cellAt(scroller, 2, 0)!);
    await stable(fixture);
    expect(pane(scroller)).toBeNull();
    expect(host.model()[0].filter).toBe('pressed');
  });

  it('labels a pane with more than one control by its header, as a group', async () => {
    const { fixture, scroller } = await setup();
    const harness = await TestbedHarnessEnvironment.harnessForFixture(fixture, TmGridHarness);

    await activate(fixture, scroller, 0, 1);
    keydown(cellAt(scroller, 0, 1)!, 'F2');
    await stable(fixture);
    const windowPane = (await harness.getEditorPopup())!;
    expect(await windowPane.isGroup()).toBe(true);
    expect(await windowPane.getLabel()).toBe('Window');
    expect(await windowPane.isReadonly()).toBe(false);
    keydown(pane(scroller)!.querySelector('.starts')!, 'Escape');
    await stable(fixture);

    await activate(fixture, scroller, 0, 2);
    keydown(cellAt(scroller, 0, 2)!, 'F2');
    await stable(fixture);
    const filterPane = (await harness.getEditorPopup())!;
    expect(await filterPane.isGroup()).toBe(false);
    expect(await filterPane.getLabel()).toBeNull();
  });

  it('sizes the pane to at least the column least width and caps its height', async () => {
    // The placement over the cell in both directions, and the rows keeping
    // their boxes, are asserted end to end on the real layout.
    const { fixture, scroller } = await setup();
    await activate(fixture, scroller, 0, 1);
    keydown(cellAt(scroller, 0, 1)!, 'F2');
    await stable(fixture);
    const body = pane(scroller)!;
    expect(body.getBoundingClientRect().width).toBeGreaterThanOrEqual(360);
    expect(body.style.maxBlockSize).toContain('360px');
  });

  it('keeps the cell error popover closed while the pane is open', async () => {
    // The unit runner loads no token sheet, so the bubble's own in-view
    // measurement cannot be relied on for a cell past the first column; the
    // cell's error state and the bubble's absence while the pane is open are.
    const { fixture, host, scroller } = await setup();
    host.withRequiredFilter.set(true);
    await stable(fixture);
    scroller.focus();
    keydown(scroller, 'ArrowDown');
    keydown(scroller, 'ArrowRight');
    keydown(scroller, 'ArrowRight');
    await stable(fixture);
    // Clearing the required filter leaves it invalid and dirty.
    keydown(cellAt(scroller, 0, 2)!, 'Delete');
    await stable(fixture);
    expect(host.model()[0].filter).toBeNull();
    expect(cellAt(scroller, 0, 2)!.classList.contains('tm-grid__cell--error')).toBe(true);

    keydown(cellAt(scroller, 0, 2)!, 'F2');
    await stable(fixture);
    expect(pane(scroller)).not.toBeNull();
    expect(document.querySelector('.tm-grid__error-msg')).toBeNull();
    expect(cellAt(scroller, 0, 2)!.getAttribute('aria-describedby')).toBeNull();

    keydown(pane(scroller)!.querySelector('textarea')!, 'Escape');
    await stable(fixture);
    expect(pane(scroller)).toBeNull();
    expect(cellAt(scroller, 0, 2)!.classList.contains('tm-grid__cell--error')).toBe(true);
  });

  it('a column without editorPopup keeps the in-cell editor', async () => {
    const { fixture, scroller } = await setup();
    await activate(fixture, scroller, 0, 0);
    keydown(cellAt(scroller, 0, 0)!, 'F2');
    expect(pane(scroller)).toBeNull();
    expect(cellAt(scroller, 0, 0)!.querySelector('.tm-grid__editor input')).not.toBeNull();
  });

  describe('read-only open', () => {
    it('F2 on a readonly row opens the pane read-only; Escape closes it and nothing commits', async () => {
      const { fixture, host, scroller } = await setup();
      const harness = await TestbedHarnessEnvironment.harnessForFixture(fixture, TmGridHarness);
      await activate(fixture, scroller, 1, 2);

      expect(keydown(cellAt(scroller, 1, 2)!, 'F2').defaultPrevented).toBe(true);
      await stable(fixture);
      const popup = (await harness.getEditorPopup())!;
      expect(await popup.isReadonly()).toBe(true);
      const textarea = pane(scroller)!.querySelector('textarea')!;
      expect(textarea.readOnly).toBe(true);
      expect(document.activeElement).toBe(textarea);

      expect(keydown(textarea, 'Escape').defaultPrevented).toBe(true);
      await stable(fixture);
      expect(pane(scroller)).toBeNull();
      expect(host.model()[1].filter).toBe('B = 2');
      expect(document.activeElement).toBe(cellAt(scroller, 1, 2));
    });

    it('typing on a readonly popup cell opens nothing', async () => {
      const { fixture, scroller } = await setup();
      await activate(fixture, scroller, 1, 2);
      keydown(cellAt(scroller, 1, 2)!, 'C');
      await stable(fixture);
      expect(pane(scroller)).toBeNull();
    });

    it('in a readonly grid, F2 opens the view and Tab out of it closes it, leaving the key to the browser', async () => {
      const { fixture, host, scroller } = await setup();
      host.readonly.set(true);
      await stable(fixture);
      await activate(fixture, scroller, 0, 1);

      keydown(cellAt(scroller, 0, 1)!, 'F2');
      await stable(fixture);
      const body = pane(scroller)!;
      expect(body.hasAttribute('data-tm-readonly')).toBe(true);
      const ends = body.querySelector<HTMLInputElement>('.ends')!;
      ends.focus();
      // A readonly grid is one tab stop: the browser's Tab moves on from the cell.
      expect(keydown(ends, 'Tab').defaultPrevented).toBe(false);
      await stable(fixture);
      expect(pane(scroller)).toBeNull();
      expect(document.activeElement).toBe(cellAt(scroller, 0, 1));
    });

    it('Enter closes the view and keeps its readonly meaning: the active cell moves down', async () => {
      const { fixture, host, scroller } = await setup();
      host.readonly.set(true);
      await stable(fixture);
      await activate(fixture, scroller, 0, 1);
      keydown(cellAt(scroller, 0, 1)!, 'F2');
      await stable(fixture);
      keydown(pane(scroller)!.querySelector('.starts')!, 'Enter');
      await stable(fixture);
      expect(pane(scroller)).toBeNull();
      expect(document.activeElement).toBe(cellAt(scroller, 1, 1));
    });

    it('a press elsewhere, or focus leaving the grid, closes the view', async () => {
      const { fixture, host, scroller } = await setup();
      const before = host.model();
      host.readonly.set(true);
      await stable(fixture);
      await activate(fixture, scroller, 0, 2);
      keydown(cellAt(scroller, 0, 2)!, 'F2');
      await stable(fixture);
      pointerPress(cellAt(scroller, 2, 0)!);
      await stable(fixture);
      expect(pane(scroller)).toBeNull();

      keydown(cellAt(scroller, 2, 0)!, 'ArrowRight');
      keydown(cellAt(scroller, 2, 1)!, 'F2');
      await stable(fixture);
      expect(pane(scroller)).not.toBeNull();
      (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('#outside-input')!.focus();
      await stable(fixture);
      expect(pane(scroller)).toBeNull();
      // Nothing wrote: the model is the very array it started as.
      expect(host.model()).toBe(before);
    });
  });
});
