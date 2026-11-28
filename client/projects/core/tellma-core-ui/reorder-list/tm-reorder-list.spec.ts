// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmDragSource, type TmDragPayload } from '@tellma/core-ui/drag-drop';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';
import { TmReorderListHarness } from '@tellma/core-ui-testing';

import { TmReorderEmpty, TmReorderItem, type TmReorderMove } from './tm-reorder-item';
import { TmReorderList } from './tm-reorder-list';

/** A column of the test list. */
interface Column {
  readonly key: string;
  readonly label: string;
}

const COLUMNS: readonly Column[] = ['Name', 'Code', 'Date', 'Amount', 'Status'].map((label) => ({
  key: label.toLowerCase(),
  label,
}));

const FIELD: TmDragPayload = {
  kind: 'tm.path',
  data: { key: 'agent', label: 'Agent' },
  text: 'Agent',
  label: 'Agent',
};

@Component({
  imports: [TmDragSource, TmReorderEmpty, TmReorderItem, TmReorderList],
  template: `
    <span id="field" [tmDragSource]="field">Agent</span>
    <tm-reorder-list
      [(items)]="items"
      [itemKey]="keyOf"
      [itemLabel]="labelOf"
      [accepts]="['tm.path']"
      ariaLabel="Shown columns"
      (moved)="moves.push($event)"
      (received)="received.push($event)"
    >
      @if (custom()) {
        <ng-template tmReorderItem [tmReorderItemOf]="items()" let-column let-index="index">
          {{ index + 1 }}. {{ column.label }}
          <button class="row-action" type="button" aria-label="Edit"></button>
        </ng-template>
      }
      <ng-template tmReorderEmpty>Nothing shown</ng-template>
    </tm-reorder-list>
    <button id="after" type="button">After</button>
  `,
})
class Host {
  readonly field = FIELD;
  readonly items = signal<readonly Column[]>(COLUMNS);
  readonly custom = signal(false);
  readonly moves: TmReorderMove<Column>[] = [];
  readonly received: { payload: TmDragPayload; index: number }[] = [];
  readonly keyOf = (column: Column): string => column.key;
  readonly labelOf = (column: Column): string => column.label;
}

/** Two lists whose rows move between them: kinds by row, each list accepting the other's rows. */
@Component({
  imports: [TmReorderList],
  template: `
    <tm-reorder-list
      class="left"
      [(items)]="left"
      [itemKey]="keyOf"
      [itemLabel]="labelOf"
      [itemKind]="kindOf"
      [accepts]="['test.column', 'test.locked']"
      ariaLabel="Left"
      (received)="leftReceived.push($event)"
    />
    <tm-reorder-list
      class="right"
      [(items)]="right"
      [itemKey]="keyOf"
      [itemLabel]="labelOf"
      [itemKind]="kindOf"
      [accepts]="['test.column']"
      ariaLabel="Right"
      (received)="rightReceived.push($event)"
    />
  `,
})
class KindsHost {
  readonly left = signal<readonly Column[]>(COLUMNS.slice(0, 3));
  readonly right = signal<readonly Column[]>(COLUMNS.slice(3));
  readonly leftReceived: { payload: TmDragPayload; index: number }[] = [];
  readonly rightReceived: { payload: TmDragPayload; index: number }[] = [];
  readonly keyOf = (column: Column): string => column.key;
  readonly labelOf = (column: Column): string => column.label;
  /** The date column is locked to its list's kind, which the right list refuses. */
  readonly kindOf = (column: Column): string => (column.key === 'date' ? 'test.locked' : 'test.column');
}

/** The unit page loads no token stylesheet; the row geometry needs one. */
let tokenSheet: HTMLStyleElement;

beforeAll(() => {
  tokenSheet = document.createElement('style');
  tokenSheet.textContent = tmEmitCss(tmTokensDefault);
  document.head.appendChild(tokenSheet);
});

afterAll(() => tokenSheet.remove());

async function setup(configure: (host: Host) => void = () => undefined) {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  configure(fixture.componentInstance);
  fixture.autoDetectChanges();
  await fixture.whenStable();
  const list = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmReorderListHarness);
  const announce = vi.spyOn(TestBed.inject(LiveAnnouncer), 'announce');
  const element = fixture.nativeElement as HTMLElement;
  return { fixture, host: fixture.componentInstance, list, announce, element };
}

/** The announcements so far, as text. */
function said(announce: { mock: { calls: unknown[][] } }): unknown[] {
  return announce.mock.calls.map((call) => call[0]);
}

/** The labels of `items`. */
function labels(items: readonly Column[]): string[] {
  return items.map((column) => column.label);
}

/** A synthetic drag event carrying `data`, at a vertical position. */
function drag(type: string, data: DataTransfer, clientY = 0): DragEvent {
  return new DragEvent(type, { bubbles: true, cancelable: true, dataTransfer: data, clientY });
}

describe('tm-reorder-list', () => {
  it('is a named list whose handles are named for their rows and described by the keys', async () => {
    const { list } = await setup();
    expect(await list.getLabel()).toBe('Shown columns');
    expect(await list.getItemTexts()).toEqual(labels(COLUMNS));
    expect((await list.getHandleLabels())[1]).toBe('Reorder Code');
    expect(await list.getInstructions()).toBe(
      'Press Space to pick up. Use the arrow keys to move, Space to drop, and Escape to cancel.',
    );
  });

  it('picks up, moves and drops by keyboard, announcing each step and keeping focus', async () => {
    const { host, list, announce } = await setup();
    await list.press(1, 'Space');
    expect(await list.getGrabbedIndex()).toBe(1);
    await list.pressFocused('ArrowDown');
    // The move is a preview: items change only on the drop.
    expect(labels(host.items())).toEqual(labels(COLUMNS));
    expect(await list.getItemTexts()).toEqual(['Name', 'Date', 'Code', 'Amount', 'Status']);
    expect(await list.getFocusedHandleIndex()).toBe(2);
    await list.pressFocused('Space');
    expect(labels(host.items())).toEqual(['Name', 'Date', 'Code', 'Amount', 'Status']);
    expect(host.moves).toEqual([{ item: COLUMNS[1], from: 1, to: 2 }]);
    expect(await list.getGrabbedIndex()).toBe(-1);
    expect(said(announce)).toEqual([
      'Code picked up. Position 2 of 5.',
      'Position 3 of 5',
      'Code dropped at position 3 of 5.',
    ]);
    expect(announce).toHaveBeenLastCalledWith('Code dropped at position 3 of 5.', 'assertive');
  });

  it('moves to the ends with Home and End, and drops with Enter', async () => {
    const { host, list } = await setup();
    await list.press(2, 'Enter');
    await list.pressFocused('Home');
    expect(await list.getFocusedHandleIndex()).toBe(0);
    await list.pressFocused('End');
    expect(await list.getFocusedHandleIndex()).toBe(4);
    await list.pressFocused('Enter');
    expect(labels(host.items())).toEqual(['Name', 'Code', 'Amount', 'Status', 'Date']);
    expect(host.moves).toEqual([{ item: COLUMNS[2], from: 2, to: 4 }]);
  });

  it('cancels with Escape, putting the item back', async () => {
    const { host, list, announce } = await setup();
    await list.press(1, 'Space');
    await list.pressFocused('ArrowDown');
    await list.pressFocused('ArrowDown');
    await list.pressFocused('Escape');
    expect(await list.getItemTexts()).toEqual(labels(COLUMNS));
    expect(await list.getFocusedHandleIndex()).toBe(1);
    expect(host.moves).toEqual([]);
    expect(said(announce).at(-1)).toBe('Move cancelled. Code is back at position 2.');
  });

  it('drops when focus tabs away', async () => {
    const { host, list } = await setup();
    await list.press(0, 'Space');
    await list.pressFocused('ArrowDown');
    await list.pressFocused('Tab');
    expect(labels(host.items())).toEqual(['Code', 'Name', 'Date', 'Amount', 'Status']);
    expect(host.moves).toEqual([{ item: COLUMNS[0], from: 0, to: 1 }]);
  });

  it('leaves focus where a Tab sent it before the moved row rendered', async () => {
    const { fixture, list, element } = await setup();
    await list.press(0, 'Space');
    const handle = element.querySelector<HTMLElement>('.tm-reorder-list__handle')!;
    // Both keys land before the render the move asks for; Tab then takes
    // focus out of the list.
    for (const key of ['ArrowDown', 'Tab']) {
      handle.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
    }
    const after = element.querySelector<HTMLElement>('#after')!;
    after.focus();
    await fixture.whenStable();
    expect(await list.getItemTexts()).toEqual(['Code', 'Name', 'Date', 'Amount', 'Status']);
    expect(document.activeElement).toBe(after);
  });

  it('leaves focus off the moved item when a Tab before the render sent it to the next row', async () => {
    const { fixture, list, element } = await setup();
    await list.press(0, 'Space');
    const handles = () =>
      Array.from(element.querySelectorAll<HTMLElement>('.tm-reorder-list__handle'));
    const [name, code] = handles();
    for (const key of ['ArrowDown', 'Tab']) {
      name.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
    }
    // Tab's own move: to the next handle, whose row the render may then move.
    code.focus();
    await fixture.whenStable();
    expect(await list.getItemTexts()).toEqual(['Code', 'Name', 'Date', 'Amount', 'Status']);
    expect(document.activeElement).not.toBe(name);
  });

  it('moves one place at once with Alt+Up and Alt+Down', async () => {
    const { host, list, announce } = await setup();
    await list.press(3, 'ArrowUp', { alt: true });
    expect(labels(host.items())).toEqual(['Name', 'Code', 'Amount', 'Date', 'Status']);
    expect(await list.getFocusedHandleIndex()).toBe(2);
    expect(said(announce)).toEqual(['Amount dropped at position 3 of 5.']);
    await list.pressFocused('ArrowDown', { alt: true });
    expect(labels(host.items())).toEqual(labels(COLUMNS));
    expect(host.moves).toEqual([
      { item: COLUMNS[3], from: 3, to: 2 },
      { item: COLUMNS[3], from: 2, to: 3 },
    ]);
  });

  it('keeps the first row in place on Alt+Up and the last on Alt+Down', async () => {
    const { host, list } = await setup();
    await list.press(0, 'ArrowUp', { alt: true });
    await list.press(4, 'ArrowDown', { alt: true });
    expect(host.moves).toEqual([]);
  });

  it('ends a grab when focus moves into its own row, so a later Escape is not its own', async () => {
    const { host, list, element, announce } = await setup((h) => h.custom.set(true));
    await list.press(1, 'Space');
    await list.pressFocused('ArrowDown');
    element.querySelectorAll<HTMLElement>('.row-action')[2].focus();
    expect(await list.getGrabbedIndex()).toBe(-1);
    expect(labels(host.items())).toEqual(['Name', 'Date', 'Code', 'Amount', 'Status']);
    document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(said(announce).at(-1)).toBe('Code dropped at position 3 of 5.');
  });

  describe('the move menu', () => {
    it('opens on a click on a handle, with each move disabled where it moves nothing', async () => {
      const { list } = await setup();
      const menu = await list.openMoveMenu(0);
      expect(await list.isMoveMenuOpen(0)).toBe(true);
      expect(await menu.getItemLabels()).toEqual([
        'Move up',
        'Move down',
        'Move to top',
        'Move to bottom',
      ]);
      const disabled = await Promise.all((await menu.getItems()).map((item) => item.isDisabled()));
      expect(disabled).toEqual([true, false, true, false]);
    });

    it('moves the item, announces it and puts focus back on its handle', async () => {
      const { fixture, host, list, announce } = await setup();
      const menu = await list.openMoveMenu(1);
      await menu.clickItem('Move to bottom');
      await fixture.whenStable();
      expect(labels(host.items())).toEqual(['Name', 'Date', 'Amount', 'Status', 'Code']);
      expect(host.moves).toEqual([{ item: COLUMNS[1], from: 1, to: 4 }]);
      expect(said(announce)).toEqual(['Code dropped at position 5 of 5.']);
      expect(await list.getFocusedHandleIndex()).toBe(4);
      expect(await list.isMoveMenuOpen(4)).toBe(false);
    });

    it('moves one place up from the menu', async () => {
      const { fixture, host, list } = await setup();
      await (await list.openMoveMenu(2)).clickItem('Move up');
      await fixture.whenStable();
      expect(labels(host.items())).toEqual(['Name', 'Date', 'Code', 'Amount', 'Status']);
      expect(await list.getFocusedHandleIndex()).toBe(1);
    });
  });

  it('ends a grab quietly when the items change from outside', async () => {
    const { fixture, host, list } = await setup();
    await list.press(1, 'Space');
    host.items.set(COLUMNS.slice(0, 3));
    await fixture.whenStable();
    expect(await list.getGrabbedIndex()).toBe(-1);
    expect(await list.getItemTexts()).toEqual(['Name', 'Code', 'Date']);
  });

  it('gives a row template the item and its position as shown', async () => {
    const { list } = await setup((h) => h.custom.set(true));
    await list.press(0, 'Space');
    await list.pressFocused('ArrowDown');
    expect(await list.getItemTexts()).toEqual([
      '1. Code',
      '2. Name',
      '3. Date',
      '4. Amount',
      '5. Status',
    ]);
  });

  it('shows the empty state for an empty list', async () => {
    const { element } = await setup((h) => h.items.set([]));
    expect(element.querySelector('.tm-reorder-list__empty')?.textContent?.trim()).toBe(
      'Nothing shown',
    );
  });

  describe('pointer drops', () => {
    /** The vertical middle of the row at `index`, plus `offset`. */
    function rowY(element: HTMLElement, index: number, offset = 0): number {
      const rect = element.querySelectorAll('.tm-reorder-list__row')[index].getBoundingClientRect();
      return rect.top + rect.height / 2 + offset;
    }

    it('moves its own row to the insertion line', async () => {
      const { fixture, host, element } = await setup();
      const frame = element.querySelector<HTMLElement>('.tm-reorder-list__frame')!;
      const source = element.querySelectorAll<HTMLElement>('.tm-reorder-list__row')[0];
      const data = new DataTransfer();
      source.dispatchEvent(drag('dragstart', data));
      frame.dispatchEvent(drag('dragenter', data, rowY(element, 2, 1)));
      frame.dispatchEvent(drag('dragover', data, rowY(element, 2, 1)));
      await fixture.whenStable();
      expect(source.classList).toContain('tm-reorder-list__row--dragging');
      const line = element.querySelector<HTMLElement>('.tm-reorder-list__indicator');
      expect(line).not.toBeNull();
      const rows = element.querySelectorAll('.tm-reorder-list__row');
      const between =
        (rows[2].getBoundingClientRect().bottom + rows[3].getBoundingClientRect().top) / 2;
      expect(
        line!.getBoundingClientRect().top + line!.getBoundingClientRect().height / 2,
      ).toBeCloseTo(between, 1);
      frame.dispatchEvent(drag('drop', data, rowY(element, 2, 1)));
      source.dispatchEvent(drag('dragend', data));
      await fixture.whenStable();
      expect(labels(host.items())).toEqual(['Code', 'Date', 'Name', 'Amount', 'Status']);
      expect(host.moves).toEqual([{ item: COLUMNS[0], from: 0, to: 2 }]);
    });

    it('never leaves a row lifted after its source is destroyed mid-drag', async () => {
      const { fixture, host, element } = await setup();
      const row = (key: string) =>
        element.querySelector<HTMLElement>(`.tm-reorder-list__row[data-key="${key}"]`);
      row('code')!.dispatchEvent(drag('dragstart', new DataTransfer()));
      await fixture.whenStable();
      expect(row('code')!.classList).toContain('tm-reorder-list__row--dragging');
      // The row goes mid-drag, so its dragend never comes; the drag ends at
      // the next sign of none, and the row comes back unlifted.
      host.items.set(COLUMNS.filter((column) => column.key !== 'code'));
      await fixture.whenStable();
      window.dispatchEvent(new PointerEvent('pointermove'));
      host.items.set(COLUMNS);
      await fixture.whenStable();
      expect(row('code')!.classList).not.toContain('tm-reorder-list__row--dragging');
    });

    it('draws no line where its own row would not move', async () => {
      const { fixture, element } = await setup();
      const frame = element.querySelector<HTMLElement>('.tm-reorder-list__frame')!;
      const source = element.querySelectorAll<HTMLElement>('.tm-reorder-list__row')[1];
      const data = new DataTransfer();
      source.dispatchEvent(drag('dragstart', data));
      frame.dispatchEvent(drag('dragenter', data, rowY(element, 1, -1)));
      frame.dispatchEvent(drag('dragover', data, rowY(element, 1, -1)));
      await fixture.whenStable();
      expect(element.querySelector('.tm-reorder-list__indicator')).toBeNull();
    });

    it('hands an accepted external payload to the consumer with its index', async () => {
      const { fixture, host, element } = await setup();
      const frame = element.querySelector<HTMLElement>('.tm-reorder-list__frame')!;
      const data = new DataTransfer();
      element.querySelector('#field')!.dispatchEvent(drag('dragstart', data));
      frame.dispatchEvent(drag('dragenter', data, rowY(element, 0, -1)));
      frame.dispatchEvent(drag('dragover', data, rowY(element, 0, -1)));
      await fixture.whenStable();
      expect(element.querySelector('.tm-reorder-list__indicator')).not.toBeNull();
      frame.dispatchEvent(drag('drop', data, rowY(element, 0, -1)));
      expect(host.received).toEqual([{ payload: FIELD, index: 0 }]);
      expect(labels(host.items())).toEqual(labels(COLUMNS));
    });
  });
  describe('row kinds', () => {
    /** The vertical middle of a row of a list, plus `offset`. */
    function rowY(list: HTMLElement, index: number, offset = 0): number {
      const rect = list.querySelectorAll('.tm-reorder-list__row')[index].getBoundingClientRect();
      return rect.top + rect.height / 2 + offset;
    }

    async function kinds() {
      TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
      const fixture = TestBed.createComponent(KindsHost);
      fixture.autoDetectChanges();
      await fixture.whenStable();
      const element = fixture.nativeElement as HTMLElement;
      return {
        fixture,
        host: fixture.componentInstance,
        left: element.querySelector<HTMLElement>('tm-reorder-list.left')!,
        right: element.querySelector<HTMLElement>('tm-reorder-list.right')!,
      };
    }

    it("drags each row under its kind, which a list that accepts it receives at the drop's index", async () => {
      const { fixture, host, left, right } = await kinds();
      const source = left.querySelectorAll<HTMLElement>('.tm-reorder-list__row')[1];
      const frame = right.querySelector<HTMLElement>('.tm-reorder-list__frame')!;
      const data = new DataTransfer();
      source.dispatchEvent(drag('dragstart', data));
      frame.dispatchEvent(drag('dragenter', data, rowY(right, 0, -1)));
      frame.dispatchEvent(drag('dragover', data, rowY(right, 0, -1)));
      await fixture.whenStable();
      expect(right.querySelector('.tm-reorder-list__indicator')).not.toBeNull();
      frame.dispatchEvent(drag('drop', data, rowY(right, 0, -1)));
      source.dispatchEvent(drag('dragend', data));
      await fixture.whenStable();
      expect(host.rightReceived).toEqual([{ payload: { kind: 'test.column', data: COLUMNS[1], text: 'Code', label: 'Code' }, index: 0 }]);
      // The consumer moves it: neither list changed its own items.
      expect(labels(host.left())).toEqual(['Name', 'Code', 'Date']);
      expect(labels(host.right())).toEqual(['Amount', 'Status']);
    });

    it('refuses a row whose kind a list does not accept, and still moves its own rows', async () => {
      const { fixture, host, left, right } = await kinds();
      const locked = left.querySelectorAll<HTMLElement>('.tm-reorder-list__row')[2];
      const frame = right.querySelector<HTMLElement>('.tm-reorder-list__frame')!;
      const data = new DataTransfer();
      locked.dispatchEvent(drag('dragstart', data));
      frame.dispatchEvent(drag('dragenter', data, rowY(right, 0, -1)));
      frame.dispatchEvent(drag('dragover', data, rowY(right, 0, -1)));
      await fixture.whenStable();
      expect(right.querySelector('.tm-reorder-list__indicator')).toBeNull();
      frame.dispatchEvent(drag('drop', data, rowY(right, 0, -1)));
      locked.dispatchEvent(drag('dragend', data));
      expect(host.rightReceived).toEqual([]);
      // Its own list takes it back to the top.
      const own = left.querySelector<HTMLElement>('.tm-reorder-list__frame')!;
      const again = new DataTransfer();
      locked.dispatchEvent(drag('dragstart', again));
      own.dispatchEvent(drag('dragenter', again, rowY(left, 0, -1)));
      own.dispatchEvent(drag('dragover', again, rowY(left, 0, -1)));
      own.dispatchEvent(drag('drop', again, rowY(left, 0, -1)));
      locked.dispatchEvent(drag('dragend', again));
      await fixture.whenStable();
      expect(labels(host.left())).toEqual(['Date', 'Name', 'Code']);
      expect(host.leftReceived).toEqual([]);
    });
  });
});
