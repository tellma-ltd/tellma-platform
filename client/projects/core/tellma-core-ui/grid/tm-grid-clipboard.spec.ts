// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { form } from '@angular/forms/signals';

import {
  TM_PARSE_ERROR,
  type TmCellEditor,
  type TmLabelResolution,
  type TmPasteContext,
} from '@tellma/core-ui/contracts';
import { tmClipboardFingerprint } from '@tellma/core-ui/grid-engine';
import { provideTellmaUi, TM_CELL_EDITOR_HOST } from '@tellma/core-ui';

import { TmGrid } from './tm-grid';
import { TmGridColumn } from './tm-grid-column';
import { TmGridEditorDef } from './tm-grid-templates';

interface Line {
  readonly id: number;
  readonly name: string | null;
  readonly qty: number | null;
  readonly agentId: number | null;
}

function makeLines(): Line[] {
  return [
    { id: 1, name: 'Alpha', qty: 10, agentId: 7 },
    { id: 2, name: 'Beta', qty: 20, agentId: null },
    { id: 3, name: 'Gamma', qty: 30, agentId: null },
  ];
}

/** A promise whose settlement the spec controls. */
interface Deferred {
  resolve(results: ReadonlyMap<string, TmLabelResolution<number>>): void;
  reject(reason: unknown): void;
}

/** A minimal consumer entity editor (entity columns require one to edit). */
@Component({
  selector: 'tm-test-agent-editor',
  template: `<input class="agent-editor" (input)="onInput($event)" />`,
})
class AgentEditor implements TmCellEditor<string | null> {
  private readonly cellHost = inject(TM_CELL_EDITOR_HOST, { optional: true });
  readonly value = signal<string | null>(null);
  readonly text = computed(() => this.value());
  constructor() {
    this.cellHost?.register(this as TmCellEditor<unknown>);
  }
  commit(): void {}
  cancel(): void {}
  focus(): void {
    document.querySelector<HTMLInputElement>('.agent-editor')?.focus();
  }
  seed(text: string): void {
    this.value.set(text);
    const el = document.querySelector<HTMLInputElement>('.agent-editor');
    if (el !== null) {
      el.value = text;
    }
  }
  protected onInput(event: Event): void {
    this.value.set((event.target as HTMLInputElement).value);
  }
}

@Component({
  imports: [TmGrid, TmGridColumn, TmGridEditorDef, AgentEditor],
  template: `
    <tm-grid
      gridId="clipboard-spec-grid"
      [field]="f"
      [rowId]="rowId"
      [newRow]="allowNewRows() ? makeRow : undefined"
      [tenant]="'acme'"
      style="block-size: 300px"
    >
      <tm-grid-column key="name" header="Name" [width]="140" />
      <tm-grid-column key="qty" type="number" header="Qty" [width]="100" [readonly]="qtyReadonly" />
      <tm-grid-column
        key="agentId"
        type="entity"
        header="Agent"
        [format]="agentFormat"
        [parse]="agentParse"
        [resolvePastedLabels]="resolveAgents"
        [width]="140"
      >
        <tm-test-agent-editor *tmGridEditor />
      </tm-grid-column>
    </tm-grid>
  `,
})
class ClipboardHost {
  readonly model = signal<Line[]>(makeLines());
  readonly f = form(this.model);
  readonly allowNewRows = signal(true);
  readonly grid = viewChild.required(TmGrid);
  private nextId = 100;
  readonly rowId = (row: Line): number => row.id;
  readonly makeRow = (): Line => ({ id: this.nextId++, name: null, qty: null, agentId: null });

  /** While locked, row 2 (id 2) has a readonly qty cell (skip-in-place test). */
  readonly qtyLocked = signal(false);
  readonly qtyReadonly = (row: Line): boolean => this.qtyLocked() && row.id === 2;

  readonly agentFormat = (value: number | null): string =>
    value === null ? '' : `Agent ${value}`;
  /** Parses only the manual `#N` form; anything else falls to the resolver. */
  readonly agentParse = (text: string): number | typeof TM_PARSE_ERROR => {
    const match = /^#(\d+)$/.exec(text.trim());
    return match === null ? TM_PARSE_ERROR : Number(match[1]);
  };

  /** Every resolver call, recorded; each returns a spec-controlled promise. */
  readonly resolveCalls: Array<{ labels: string[]; ctx: TmPasteContext; deferred: Deferred }> = [];
  readonly resolveAgents = (
    labels: string[],
    ctx: TmPasteContext,
  ): Promise<ReadonlyMap<string, TmLabelResolution<number>>> =>
    new Promise((resolve, reject) => {
      this.resolveCalls.push({ labels, ctx, deferred: { resolve, reject } });
    });
}

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

function dispatchPaste(
  scroller: HTMLElement,
  flavors: { text?: string; html?: string },
): ClipboardEvent {
  const data = new DataTransfer();
  if (flavors.text !== undefined) {
    data.setData('text/plain', flavors.text);
  }
  if (flavors.html !== undefined) {
    data.setData('text/html', flavors.html);
  }
  const event = new ClipboardEvent('paste', {
    clipboardData: data,
    bubbles: true,
    cancelable: true,
  });
  scroller.dispatchEvent(event);
  return event;
}

function dispatchClipboard(scroller: HTMLElement, kind: 'copy' | 'cut'): DataTransfer {
  const data = new DataTransfer();
  scroller.dispatchEvent(
    new ClipboardEvent(kind, { clipboardData: data, bubbles: true, cancelable: true }),
  );
  return data;
}

async function setup(): Promise<{
  fixture: ComponentFixture<ClipboardHost>;
  host: ClipboardHost;
  scroller: HTMLElement;
}> {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(ClipboardHost);
  await stable(fixture);
  const scroller = (fixture.nativeElement as HTMLElement).querySelector(
    '.tm-grid__scroller',
  ) as HTMLElement;
  return { fixture, host: fixture.componentInstance, scroller };
}

/**
 * Waits (bounded) for an async-clipboard chain to land — `Blob.text()`
 * settles on its own task queue, so a single macrotask turn is not enough.
 */
async function eventually(
  fixture: ComponentFixture<unknown>,
  predicate: () => boolean,
): Promise<void> {
  for (let i = 0; i < 20 && !predicate(); i++) {
    await stable(fixture);
  }
}

/** Activates cell (0,0) via the keyboard entry path. */
async function activateOrigin(
  fixture: ComponentFixture<unknown>,
  scroller: HTMLElement,
): Promise<void> {
  scroller.focus();
  keydown(scroller, 'ArrowDown');
  await stable(fixture);
}

/** Arrow-steps the active cell from the origin to (row, col). */
async function activateCell(
  fixture: ComponentFixture<unknown>,
  scroller: HTMLElement,
  row: number,
  col: number,
): Promise<void> {
  await activateOrigin(fixture, scroller);
  for (let i = 0; i < row; i++) {
    keydown(scroller, 'ArrowDown');
  }
  for (let i = 0; i < col; i++) {
    keydown(scroller, 'ArrowRight');
  }
  await stable(fixture);
}

describe('tm-grid (clipboard paste)', () => {
  it('pastes TSV through the field as ONE undo op', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);

    const event = dispatchPaste(scroller, { text: 'X\t5\r\nY\t6\r\n' });
    await stable(fixture);
    expect(event.defaultPrevented).toBe(true);
    expect(host.model()[0].name).toBe('X');
    expect(host.model()[0].qty).toBe(5);
    expect(host.model()[1].name).toBe('Y');
    expect(host.model()[1].qty).toBe(6);

    keydown(scroller, 'z', { ctrlKey: true }); // ONE undo restores all four cells
    await stable(fixture);
    expect(host.model()[0].name).toBe('Alpha');
    expect(host.model()[0].qty).toBe(10);
    expect(host.model()[1].name).toBe('Beta');
    expect(host.model()[1].qty).toBe(20);
  });

  it('tiles the source when the selection is an exact multiple of its shape', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'ArrowRight', { shiftKey: true });
    keydown(scroller, 'ArrowDown', { shiftKey: true });
    await stable(fixture);

    dispatchPaste(scroller, { text: 'A\t8\r\n' }); // 1×2 into a 2×2 selection
    await stable(fixture);
    expect(host.model()[0].name).toBe('A');
    expect(host.model()[0].qty).toBe(8);
    expect(host.model()[1].name).toBe('A');
    expect(host.model()[1].qty).toBe(8);
  });

  it('skips readonly cells in place — values are not shifted around them', async () => {
    const { fixture, host, scroller } = await setup();
    host.qtyLocked.set(true); // row 2's qty is readonly
    await activateCell(fixture, scroller, 0, 1); // qty column

    dispatchPaste(scroller, { text: '5\r\n6\r\n' });
    await stable(fixture);
    expect(host.model()[0].qty).toBe(5);
    expect(host.model()[1].qty).toBe(20); // readonly: skipped, not shifted
    expect(host.model()[2].qty).toBe(30); // untouched — nothing slid down
  });

  it('materializes overflow rows via newRow (one undo op), drops them without it', async () => {
    const { fixture, host, scroller } = await setup();
    await activateCell(fixture, scroller, 2, 0); // last data row

    dispatchPaste(scroller, { text: 'P1\r\nP2\r\n' });
    await stable(fixture);
    expect(host.model().length).toBe(4);
    expect(host.model()[2].name).toBe('P1');
    expect(host.model()[3].name).toBe('P2');
    expect(host.model()[3].id).toBe(100); // minted by the factory

    keydown(scroller, 'z', { ctrlKey: true }); // ONE undo removes rows AND writes
    await stable(fixture);
    expect(host.model().length).toBe(3);
    expect(host.model()[2].name).toBe('Gamma');

    host.allowNewRows.set(false);
    await stable(fixture);
    dispatchPaste(scroller, { text: 'Q1\r\nQ2\r\n' });
    await stable(fixture);
    expect(host.model().length).toBe(3); // overflow dropped
    expect(host.model()[2].name).toBe('Q1');
  });

  it('takes the typed fast path from HTML metadata on tenant match, falls to parse on mismatch', async () => {
    const { fixture, host, scroller } = await setup();
    await activateCell(fixture, scroller, 0, 1); // qty

    const htmlFor = (tenant: string): string =>
      `<table data-tm-grid='{"v":1,"tenant":"${tenant}","locale":"en-US",` +
      `"cols":[{"key":"qty","type":"number"}]}'>` +
      `<tbody><tr><td data-tm-v="42" data-tm-h="${tmClipboardFingerprint('forty-two')}">` +
      `forty-two</td></tr></tbody></table>`;

    dispatchPaste(scroller, { html: htmlFor('acme') });
    await stable(fixture);
    expect(host.model()[0].qty).toBe(42); // the raw value, no parsing

    keydown(scroller, 'ArrowDown');
    await stable(fixture);
    dispatchPaste(scroller, { html: htmlFor('other') });
    await stable(fixture);
    // Cross-tenant raw values are not trusted: the display string parsed
    // (and failed) instead, so the cell is an invalid input.
    expect(host.model()[1].qty).toBeNull();
    const cell = cellAt(scroller, 1, 1) as HTMLElement;
    expect(cell.textContent).toContain('forty-two');
    expect(cell.classList.contains('tm-grid__cell--error')).toBe(true);
    expect(host.grid().errorCount()).toBe(1);
  });

  it('ignores a raw value whose display text was edited downstream (Excel round trip)', async () => {
    const { fixture, host, scroller } = await setup();
    await activateCell(fixture, scroller, 0, 1); // qty

    // A SAME-tenant payload carrying data-tm-v="6" — but the visible text was
    // changed to "99" after the copy (as Excel does: it round-trips our
    // attribute verbatim while the user edits the cell). The data-tm-h still
    // fingerprints the original "6", so it no longer matches the cell text and
    // the stale raw value is discarded — the edited "99" is parsed instead.
    const tampered =
      `<table data-tm-grid='{"v":1,"tenant":"acme","locale":"en-US",` +
      `"cols":[{"key":"qty","type":"number"}]}'>` +
      `<tbody><tr><td data-tm-v="6" data-tm-h="${tmClipboardFingerprint('6')}">99</td></tr>` +
      `</tbody></table>`;

    dispatchPaste(scroller, { html: tampered });
    await stable(fixture);
    expect(host.model()[0].qty).toBe(99); // the edited text won, not the stale 6
  });

  it('skips the marked header row of an HTML payload', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);

    dispatchPaste(scroller, {
      html:
        `<table data-tm-grid='{"v":1,"tenant":"acme","headers":true,` +
        `"cols":[{"key":"name","type":"text"}]}'>` +
        `<thead><tr><th>Name</th></tr></thead>` +
        `<tbody><tr><td>FromHtml</td></tr></tbody></table>`,
    });
    await stable(fixture);
    expect(host.model()[0].name).toBe('FromHtml'); // the header row never landed
    expect(host.model()[1].name).toBe('Beta');
  });

  it('parses a foreign HTML table by display strings, converting <br> to newlines', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);

    dispatchPaste(scroller, {
      html: '<table><tbody><tr><td> a<br>b </td><td>9</td></tr></tbody></table>',
    });
    await stable(fixture);
    expect(host.model()[0].name).toBe('a\nb');
    expect(host.model()[0].qty).toBe(9);
  });

  it('a cell that becomes readonly sheds its error (never errored while readonly)', async () => {
    const { fixture, host, scroller } = await setup();
    await activateCell(fixture, scroller, 1, 1); // row id 2, qty (editable)
    dispatchPaste(scroller, { text: 'abc' }); // unparseable → invalid input
    await stable(fixture);
    const cell = cellAt(scroller, 1, 1) as HTMLElement;
    expect(cell.classList.contains('tm-grid__cell--error')).toBe(true);
    expect(cell.textContent).toContain('abc'); // rejected text visible while editable
    expect(host.grid().errorCount()).toBe(1);

    // Lock row 2's qty → the cell turns readonly. A readonly cell is never in
    // an error state: no tint, no tally, and the stuck raw text gives way to
    // the model value (the input can no longer be corrected in place).
    host.qtyLocked.set(true);
    await stable(fixture);
    const locked = cellAt(scroller, 1, 1) as HTMLElement;
    expect(locked.classList.contains('tm-grid__cell--readonly')).toBe(true);
    expect(locked.classList.contains('tm-grid__cell--error')).toBe(false);
    expect(locked.getAttribute('aria-invalid')).toBeNull();
    expect(locked.textContent).not.toContain('abc');
    expect(host.grid().errorCount()).toBe(0);
  });

  it('collapses the source-line wrapping an editor adds to cell text', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);
    // Excel and Sheets wrap long cell text across HTML source lines (a newline
    // + indentation) - layout, not content. It must collapse to a single space
    // the way the cell renders (the exact shape that broke the agent paste).
    dispatchPaste(scroller, {
      html: '<table><tbody><tr><td>Ada\n      Lovelace</td></tr></tbody></table>',
    });
    await stable(fixture);
    expect(host.model()[0].name).toBe('Ada Lovelace');
  });

  it('an entity round trip survives an editor re-wrapping the label across source lines', async () => {
    const { fixture, host, scroller } = await setup();
    await activateCell(fixture, scroller, 0, 2); // agent cell, row id 1
    // Same-tenant payload: raw id 11 + a data-tm-h over the canonical "Alice
    // Green", but Excel re-wrapped the visible text (newline + indentation).
    // The rendered text still collapses to "Alice Green", so the fingerprint
    // matches, the raw id is used, and the resolver is never consulted.
    const html =
      `<table data-tm-grid='{"v":1,"tenant":"acme","locale":"en-US",` +
      `"cols":[{"key":"agentId","type":"entity"}]}'>` +
      `<tbody><tr><td data-tm-v="11" data-tm-h="${tmClipboardFingerprint('Alice Green')}">Alice\n      Green</td></tr></tbody></table>`;
    dispatchPaste(scroller, { html });
    await stable(fixture);
    expect(host.model()[0].agentId).toBe(11);
    expect(host.resolveCalls.length).toBe(0);
    expect(host.grid().errorCount()).toBe(0);
  });

  it('prefers the in-memory descriptor when the payload fingerprint matches (typed same-session paste)', async () => {
    const { fixture, host, scroller } = await setup();
    await activateCell(fixture, scroller, 0, 2); // agent cell of row 1 (agentId 7)

    const copied = dispatchClipboard(scroller, 'copy');
    const tsv = copied.getData('text/plain');
    expect(tsv).toBe('Agent 7\r\n'); // the display string — parse would mangle it

    keydown(scroller, 'ArrowDown');
    await stable(fixture);
    // Only the text flavor survives (a browser may strip the HTML one): the
    // fingerprint still finds the descriptor and the RAW value pastes.
    dispatchPaste(scroller, { text: tsv });
    await stable(fixture);
    expect(host.model()[1].agentId).toBe(7);
    expect(host.resolveCalls.length).toBe(0); // never parsed, never resolved
  });
});

describe('tm-grid (clipboard resolver)', () => {
  it('issues ONE deduped resolver call per column, shows pending affordances, writes values and marks failures distinctly', async () => {
    const { fixture, host, scroller } = await setup();
    await activateCell(fixture, scroller, 0, 2);

    dispatchPaste(scroller, { text: 'Adam\r\nBob\r\nAdam\r\n' });
    await stable(fixture);

    expect(host.resolveCalls.length).toBe(1);
    expect(host.resolveCalls[0].labels).toEqual(['Adam', 'Bob']); // deduped, first-seen order
    expect(host.grid().pendingCount()).toBe(3);
    // Pending cells carry the inline spinner.
    expect(cellAt(scroller, 0, 2)!.querySelector('.tm-grid__cell-spin')).not.toBeNull();
    expect(cellAt(scroller, 1, 2)!.querySelector('.tm-grid__cell-spin')).not.toBeNull();

    host.resolveCalls[0].deferred.resolve(
      new Map<string, TmLabelResolution<number>>([
        ['Adam', { value: 11 }],
        ['Bob', { error: 'notFound' }],
      ]),
    );
    await stable(fixture);
    expect(host.grid().pendingCount()).toBe(0);
    expect(host.model()[0].agentId).toBe(11);
    expect(host.model()[2].agentId).toBe(11);
    expect(host.model()[1].agentId).toBeNull();
    const failed = cellAt(scroller, 1, 2) as HTMLElement;
    expect(failed.textContent).toContain('Bob'); // the label stays visible in place
    expect(failed.classList.contains('tm-grid__cell--error')).toBe(true);

    // The notFound message names the collection and the label.
    keydown(scroller, 'ArrowDown'); // (1,2) becomes active
    await stable(fixture);
    const describedBy = failed.getAttribute('aria-describedby');
    expect(describedBy).not.toBeNull();
    expect(document.getElementById(describedBy!)?.textContent).toContain('No Agent named');
  });

  it('gives ambiguous outcomes their own message', async () => {
    const { fixture, host, scroller } = await setup();
    await activateCell(fixture, scroller, 1, 2);

    dispatchPaste(scroller, { text: 'Dup' });
    await stable(fixture);
    host.resolveCalls[0].deferred.resolve(
      new Map<string, TmLabelResolution<number>>([['Dup', { error: 'ambiguous' }]]),
    );
    await stable(fixture);

    const cell = cellAt(scroller, 1, 2) as HTMLElement;
    expect(cell.classList.contains('tm-grid__cell--error')).toBe(true);
    const describedBy = cell.getAttribute('aria-describedby');
    expect(document.getElementById(describedBy!)?.textContent).toContain(
      'matches more than one Agent',
    );
  });

  it('discards a late resolution once the cell was manually edited', async () => {
    const { fixture, host, scroller } = await setup();
    await activateCell(fixture, scroller, 0, 2);

    dispatchPaste(scroller, { text: 'Adam' });
    await stable(fixture);
    expect(host.grid().pendingCount()).toBe(1);

    // A manual edit through the consumer editor (#N parses) bumps the
    // cell's sequence token.
    keydown(scroller, '#');
    const input = scroller.querySelector<HTMLInputElement>('[data-tm-editor] .agent-editor')!;
    input.value = '#99';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    keydown(input, 'Enter');
    await stable(fixture);
    expect(host.model()[0].agentId).toBe(99);

    host.resolveCalls[0].deferred.resolve(
      new Map<string, TmLabelResolution<number>>([['Adam', { value: 11 }]]),
    );
    await stable(fixture);
    expect(host.model()[0].agentId).toBe(99); // the late result was discarded
    expect(host.grid().pendingCount()).toBe(0);
    expect(host.grid().errorCount()).toBe(0);
  });

  it('undo during pending aborts the resolution signal and restores pre-paste state', async () => {
    const { fixture, host, scroller } = await setup();
    await activateCell(fixture, scroller, 1, 2);

    dispatchPaste(scroller, { text: 'Adam' });
    await stable(fixture);
    const call = host.resolveCalls[0];
    expect(call.ctx.signal.aborted).toBe(false);

    keydown(scroller, 'z', { ctrlKey: true });
    await stable(fixture);
    expect(call.ctx.signal.aborted).toBe(true);
    expect(host.grid().pendingCount()).toBe(0);
    expect(host.model()[1].agentId).toBeNull();

    call.deferred.resolve(
      new Map<string, TmLabelResolution<number>>([['Adam', { value: 11 }]]),
    );
    await stable(fixture);
    expect(host.model()[1].agentId).toBeNull(); // nothing lands after the abort
  });

  it('re-resolves labels instead of trusting cross-tenant raw ids', async () => {
    const { fixture, host, scroller } = await setup();
    await activateCell(fixture, scroller, 1, 2);

    dispatchPaste(scroller, {
      html:
        `<table data-tm-grid='{"v":1,"tenant":"other","locale":"fr-FR",` +
        `"cols":[{"key":"agentId","type":"entity"}]}'>` +
        `<tbody><tr><td data-tm-v="55" data-tm-h="${tmClipboardFingerprint('Adam')}">Adam` +
        `</td></tr></tbody></table>`,
    });
    await stable(fixture);

    expect(host.model()[1].agentId).toBeNull(); // 55 was NOT written
    expect(host.resolveCalls.length).toBe(1);
    expect(host.resolveCalls[0].labels).toEqual(['Adam']);
    expect(host.resolveCalls[0].ctx.sourceTenant).toBe('other');
    expect(host.resolveCalls[0].ctx.sourceLocale).toBe('fr-FR');

    host.resolveCalls[0].deferred.resolve(
      new Map<string, TmLabelResolution<number>>([['Adam', { value: 11 }]]),
    );
    await stable(fixture);
    expect(host.model()[1].agentId).toBe(11); // the re-resolved value, not the foreign id
  });
});

describe('tm-grid (clipboard cut)', () => {
  it('arms the marquee, moves on same-grid paste as one undo op, and clears it', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'ArrowRight', { shiftKey: true }); // (0,0)-(0,1)
    await stable(fixture);

    const data = dispatchClipboard(scroller, 'cut');
    await stable(fixture);
    expect(data.getData('text/plain')).toBe('Alpha\t10\r\n');
    expect(host.model()[0].name).toBe('Alpha'); // a cut is deferred — nothing moved yet
    expect(cellAt(scroller, 0, 0)!.classList.contains('tm-grid__cell--cut')).toBe(true);
    expect(cellAt(scroller, 0, 1)!.classList.contains('tm-grid__cell--cut')).toBe(true);
    expect(cellAt(scroller, 1, 0)!.classList.contains('tm-grid__cell--cut')).toBe(false);

    keydown(scroller, 'ArrowDown');
    keydown(scroller, 'ArrowDown'); // anchor (2,0)
    await stable(fixture);
    dispatchPaste(scroller, { text: data.getData('text/plain') });
    await stable(fixture);
    expect(host.model()[2].name).toBe('Alpha');
    expect(host.model()[2].qty).toBe(10);
    expect(host.model()[0].name).toBeNull(); // the source cleared — a move
    expect(host.model()[0].qty).toBeNull();
    expect(cellAt(scroller, 0, 0)!.classList.contains('tm-grid__cell--cut')).toBe(false);

    keydown(scroller, 'z', { ctrlKey: true }); // ONE undo restores write AND clear
    await stable(fixture);
    expect(host.model()[0].name).toBe('Alpha');
    expect(host.model()[0].qty).toBe(10);
    expect(host.model()[2].name).toBe('Gamma');
  });

  it('Esc disarms the pending cut', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);

    const data = dispatchClipboard(scroller, 'cut');
    await stable(fixture);
    expect(cellAt(scroller, 0, 0)!.classList.contains('tm-grid__cell--cut')).toBe(true);

    keydown(scroller, 'Escape');
    await stable(fixture);
    expect(cellAt(scroller, 0, 0)!.classList.contains('tm-grid__cell--cut')).toBe(false);

    // A paste of the cut payload after disarming is a plain paste: the
    // source keeps its value.
    keydown(scroller, 'ArrowDown');
    await stable(fixture);
    dispatchPaste(scroller, { text: data.getData('text/plain') });
    await stable(fixture);
    expect(host.model()[1].name).toBe('Alpha');
    expect(host.model()[0].name).toBe('Alpha');
  });

  it('pasting DIFFERENT content is a plain paste and disarms the cut', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);

    dispatchClipboard(scroller, 'cut');
    await stable(fixture);
    expect(cellAt(scroller, 0, 0)!.classList.contains('tm-grid__cell--cut')).toBe(true);

    keydown(scroller, 'ArrowDown');
    await stable(fixture);
    dispatchPaste(scroller, { text: 'Zed' }); // not the cut payload
    await stable(fixture);
    expect(host.model()[1].name).toBe('Zed');
    expect(host.model()[0].name).toBe('Alpha'); // the source stayed
    expect(cellAt(scroller, 0, 0)!.classList.contains('tm-grid__cell--cut')).toBe(false);
  });
});

describe('tm-grid (clipboard menu)', () => {
  /** Replaces `navigator.clipboard.read` for one test; returns the restore. */
  function patchClipboardRead(read: () => Promise<ClipboardItem[]>): () => void {
    Object.defineProperty(navigator.clipboard, 'read', { configurable: true, value: read });
    return () => {
      Reflect.deleteProperty(navigator.clipboard, 'read');
    };
  }

  function textClipboardItem(text: string): ClipboardItem {
    return {
      types: ['text/plain'],
      getType: () => Promise.resolve(new Blob([text], { type: 'text/plain' })),
    } as unknown as ClipboardItem;
  }

  function menuItemByLabel(label: RegExp): HTMLElement | undefined {
    const labels = document.querySelectorAll<HTMLElement>('.tm-menu__panel .tm-menu__label');
    for (const el of labels) {
      if (label.test(el.textContent?.trim() ?? '')) {
        return el.closest<HTMLElement>('.tm-menu__item') ?? undefined;
      }
    }
    return undefined;
  }

  it('menu Paste reads the async clipboard and drives the same ladder', async () => {
    const { fixture, host, scroller } = await setup();
    const restore = patchClipboardRead(() => Promise.resolve([textClipboardItem('MenuPasted')]));
    try {
      await activateOrigin(fixture, scroller);
      keydown(scroller, 'F10', { shiftKey: true });
      await stable(fixture);

      const paste = menuItemByLabel(/^Paste$/);
      expect(paste).toBeDefined();
      expect(paste!.getAttribute('aria-disabled')).not.toBe('true');
      paste!.click();
      await eventually(fixture, () => host.model()[0].name === 'MenuPasted');
      expect(host.model()[0].name).toBe('MenuPasted');
    } finally {
      restore();
    }
  });

  it('degrades the menu Paste item to the shortcut hint after a denied read', async () => {
    const { fixture, host, scroller } = await setup();
    const restore = patchClipboardRead(() => Promise.reject(new Error('denied')));
    try {
      await activateOrigin(fixture, scroller);
      keydown(scroller, 'F10', { shiftKey: true });
      await stable(fixture);
      menuItemByLabel(/^Paste$/)!.click();
      await stable(fixture);
      await stable(fixture); // the rejected read settles across turns
      expect(host.model()[0].name).toBe('Alpha'); // nothing pasted

      keydown(scroller, 'F10', { shiftKey: true }); // reopen: the item degraded
      await stable(fixture);
      const hint = menuItemByLabel(/Press .+V to paste/);
      expect(hint).toBeDefined();
      expect(hint!.getAttribute('aria-disabled')).toBe('true');
    } finally {
      restore();
    }
  });

  it('menu Cut arms the deferred move with the async write fingerprint discipline', async () => {
    const { fixture, host, scroller } = await setup();
    // Capture what the menu cut writes; the mock resolves the write.
    const written: ClipboardItem[][] = [];
    Object.defineProperty(navigator.clipboard, 'write', {
      configurable: true,
      value: (items: ClipboardItem[]) => {
        written.push(items);
        return Promise.resolve();
      },
    });
    try {
      await activateOrigin(fixture, scroller);
      keydown(scroller, 'F10', { shiftKey: true });
      await stable(fixture);
      menuItemByLabel(/^Cut$/)!.click();
      await stable(fixture);
      expect(written.length).toBe(1);
      expect(cellAt(scroller, 0, 0)!.classList.contains('tm-grid__cell--cut')).toBe(true);

      // The armed fingerprint matches the written TSV: pasting it moves.
      const blob = await written[0][0].getType('text/plain');
      const tsv = await blob.text();
      keydown(scroller, 'ArrowDown');
      await stable(fixture);
      dispatchPaste(scroller, { text: tsv });
      await stable(fixture);
      expect(host.model()[1].name).toBe('Alpha');
      expect(host.model()[0].name).toBeNull(); // moved, not copied
    } finally {
      Reflect.deleteProperty(navigator.clipboard, 'write');
    }
  });
});
