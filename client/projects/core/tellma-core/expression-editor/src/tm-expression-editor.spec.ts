// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal, viewChild } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { form } from '@angular/forms/signals';
import { TmExpressionEditorHarness, TmFakeDiscover, TmFakeTelemetry } from '@tellma/core-testing';
import { TmGrid, TmGridColumn, TmGridEditorDef } from '@tellma/core-ui/grid';
import { tmIsApplePlatform } from '@tellma/core-ui/kbd';
import { TM_STRINGS_AR } from '@tellma/locale-ar/platform-strings';
import { TM_QUERYEX_STRINGS_AR } from '@tellma/locale-ar/queryex-strings';

import { tmEntityRig } from '../../src/entity/tm-entity.testing.util';
import { tmSettle, tmSettleUntil } from '../../src/routes/tm-app.testing.util';
import { TmExpressionEditor } from './tm-expression-editor';
import { injectTmQueryexDraft, type TmQueryexDraft } from './tm-queryex-draft';
import type { TmQueryexDiscoverResult, TmQueryexWireDiagnostic } from './tm-queryex-wire';

// The expression editor over the fake discover route, through its harness:
// the status line and the problems list, no stale answer, F8, an insertion
// as one undo step, a drop re-spaced, Mod+Enter, the parameter chips, an
// offline check and its retry, the editor as a grid's popup cell editor,
// a shared draft, a geometry that never moves with the problems or the
// parameters, and the editor's own strings in Arabic.

/** An answer with no problem, path or parameter. */
const EMPTY: TmQueryexDiscoverResult = { diagnostics: [], parameters: [], paths: [], usesAggregation: false };

/** The geometry tokens the box, the status line and the chips are sized by, as the default theme sets them. */
const GEOMETRY =
  '--expr-line-height: 20px; --expr-padding-y: 6px; --expr-padding-x: 8px; --border-width: 1px; ' +
  '--expr-status-height: 20px; --expr-chips-height: 24px; --chip-height-sm: 20px; inline-size: 480px; display: block';

/** One editor of a customers filter. */
@Component({
  imports: [TmExpressionEditor],
  template: `
    <div [style]="geometry">
      <tm-expression-editor
        clause="filter"
        root="acme.Customer"
        ariaLabel="Filter"
        [rows]="3"
        [showParameters]="showParameters()"
        [(value)]="value"
        (submit)="submits = submits + 1"
      />
    </div>
  `,
})
class EditorHost {
  /** The geometry tokens. */
  readonly geometry = GEOMETRY;
  /** The filter's text. */
  readonly value = signal('');
  /** Whether the chips show. */
  readonly showParameters = signal(false);
  /** How many times the editor submitted. */
  submits = 0;
  /** The editor. */
  readonly editor = viewChild.required(TmExpressionEditor);
}

/** Two editors of one query, sharing a draft. */
@Component({
  imports: [TmExpressionEditor],
  template: `
    <tm-expression-editor clause="select" ariaLabel="Select" [draft]="draft" [(value)]="select" />
    @if (showFilter()) {
      <tm-expression-editor clause="filter" ariaLabel="Filter" [draft]="draft" [(value)]="filter" />
    }
  `,
})
class SharedHost {
  /** The query's draft. */
  readonly draft: TmQueryexDraft = injectTmQueryexDraft({ root: signal('acme.Customer') });
  /** The select's text. */
  readonly select = signal('');
  /** The filter's text. */
  readonly filter = signal('');
  /** Whether the filter's editor shows. */
  readonly showFilter = signal(true);
}

/** One grant row. */
interface Grant {
  /** The row's id. */
  readonly id: number;
  /** The grant's name. */
  readonly name: string;
  /** The grant's filter. */
  readonly filter: string;
}

/** A grid of grants whose filter cells edit in a popup expression editor. */
@Component({
  imports: [TmGrid, TmGridColumn, TmGridEditorDef, TmExpressionEditor],
  template: `
    <tm-grid gridId="expression-editor-spec" [field]="f" [rowId]="rowId" style="block-size: 300px">
      <tm-grid-column key="name" header="Name" [width]="120" />
      <tm-grid-column key="filter" header="Filter" [width]="200" [editorPopup]="{ minWidth: 360 }">
        <tm-expression-editor *tmGridEditor="let readonly = readonly" clause="filter" root="acme.Customer" ariaLabel="Filter" [readonly]="readonly" />
      </tm-grid-column>
    </tm-grid>
  `,
})
class GridHost {
  /** The rows. */
  readonly model = signal<Grant[]>([{ id: 1, name: 'Sales', filter: 'IsActive = true' }]);
  /** The rows' form. */
  readonly f = form(this.model);
  /** A row's id. */
  readonly rowId = (grant: Grant): number => grant.id;
}

/**
 * Starts the app for a user who reads everything, with the message of one
 * problem code, and scripts the discover route to answer each filter
 * from a table.
 *
 * @param problems - Each filter text's problems; none for any other text.
 * @returns The fake discover route.
 */
async function rig(problems: Readonly<Record<string, readonly TmQueryexWireDiagnostic[]>> = {}): Promise<TmFakeDiscover> {
  await tmEntityRig({ access: { isSystem: true }, labels: { en: { Queryex_QX2001: 'No field is named {name}' } } });
  const discover = TestBed.inject(TmFakeDiscover);
  discover.respond((request) => ({ ...EMPTY, diagnostics: problems[request.filters?.[0] ?? ''] ?? [] }));
  return discover;
}

/**
 * The editor host with its harness.
 *
 * @returns The fixture and the harness.
 */
async function editor(): Promise<{ fixture: ComponentFixture<EditorHost>; harness: TmExpressionEditorHarness }> {
  const fixture = TestBed.createComponent(EditorHost);
  await fixture.whenStable();
  return { fixture, harness: await TestbedHarnessEnvironment.loader(fixture).getHarness(TmExpressionEditorHarness) };
}

/**
 * Waits until the status line reads a text, as a check that crosses the
 * draft's wait lands.
 *
 * @param fixture - The fixture.
 * @param text - The text.
 */
async function statusReads(fixture: ComponentFixture<unknown>, text: string): Promise<void> {
  const status = (): string => (fixture.nativeElement as HTMLElement).querySelector('.tm-expression-editor__status-text')?.textContent.trim() ?? '';
  await tmSettleUntil(() => status() === text, `the status line reads "${text}"`);
}

/**
 * A problem in the filter.
 *
 * @param start - Its start.
 * @param name - The name it reports.
 * @returns The problem.
 */
function unknown(start: number, name: string): TmQueryexWireDiagnostic {
  return { clause: 'filters[0]', code: 'QX2001', start, length: name.length, arguments: { name } };
}

/**
 * Dispatches a keydown.
 *
 * @param target - The element.
 * @param key - The key.
 * @param init - The modifiers.
 * @returns The event, to read whether it was taken.
 */
function keydown(target: Element, key: string, init: KeyboardEventInit = {}): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
  target.dispatchEvent(event);
  return event;
}

/** The platform's Mod key. */
const MOD: KeyboardEventInit = tmIsApplePlatform() ? { metaKey: true } : { ctrlKey: true };

/** The textarea of a fixture's first editor. */
function textareaOf(fixture: ComponentFixture<unknown>): HTMLTextAreaElement {
  return (fixture.nativeElement as HTMLElement).querySelector('textarea')!;
}

describe('TmExpressionEditor', () => {
  afterEach(() => TestBed.resetTestingModule());

  describe('the status line', () => {
    it('reads No problems once checked, and the first problem with a count of the rest', async () => {
      await rig({ 'Nope = 1 and Bad = 2': [unknown(0, 'Nope'), unknown(13, 'Bad')] });
      const { fixture, harness } = await editor();
      expect(await harness.isInvalid()).toBe(false);

      await harness.setValue('Code = 1');
      expect(await harness.getStatusText()).toBe('Checking…');
      await statusReads(fixture, 'No problems');
      expect(fixture.componentInstance.value()).toBe('Code = 1');

      await harness.setValue('Nope = 1 and Bad = 2');
      await statusReads(fixture, 'Line 1: No field is named Nope');
      expect(await harness.getMoreText()).toBe('+1 more');
      expect(await harness.isInvalid()).toBe(true);
      expect(await harness.getProblems()).toEqual(['Line 1: No field is named Nope', 'Line 1: No field is named Bad']);

      await harness.pickProblem(1);
      expect(await harness.getSelection()).toEqual([13, 16]);
      expect(await harness.isFocused()).toBe(true);
    });

    it('never shows an answer to a text that has since changed', async () => {
      const discover = await rig({ 'Nope = 1': [unknown(0, 'Nope')] });
      const { fixture, harness } = await editor();
      discover.hold();
      await harness.setValue('Nope = 1');
      await tmSettleUntil(() => discover.requests().length === 1, 'the first check is sent');

      await harness.setValue('Code = 1');
      await tmSettleUntil(() => discover.requests().length === 2, 'the second check is sent');
      discover.release();
      await statusReads(fixture, 'No problems');
      expect(await harness.isInvalid()).toBe(false);
    });

    it('numbers a problem by its line, and shows the lexer problems while offline, with Retry', async () => {
      const discover = await rig();
      const { fixture, harness } = await editor();
      discover.fail('offline');
      await harness.setValue("Code = 1\nand Name = 'x");
      await statusReads(fixture, "Can't check while offline");
      expect(await harness.canRetry()).toBe(true);
      expect(await harness.getMoreText()).toBe('1 problem');
      expect(await harness.getProblems()).toEqual(['Line 2: QX1001']);

      discover.fail(null);
      await harness.retry();
      await statusReads(fixture, 'Line 2: QX1001');
      expect(await harness.canRetry()).toBe(false);
    });

    it('says when a check failed, and checks again on Retry', async () => {
      const discover = await rig();
      const { fixture, harness } = await editor();
      discover.fail('server');
      await harness.setValue('Code = 1');
      await statusReads(fixture, "Couldn't check the expression");

      discover.fail(null);
      await harness.retry();
      await statusReads(fixture, 'No problems');
    });

    it('announces a check politely while the editor has focus, never a keystroke', async () => {
      await rig({ 'Nope = 1': [unknown(0, 'Nope')] });
      const { fixture, harness } = await editor();
      await harness.setValue('Nope = 1');
      expect(await harness.getAnnouncement()).toBe('');
      await statusReads(fixture, 'Line 1: No field is named Nope');
      expect(await harness.getAnnouncement()).toBe('1 problem');
    });
  });

  describe('the keyboard', () => {
    it('selects the next problem on F8 and the previous on Shift+F8, wrapping around', async () => {
      await rig({ 'Nope = 1 and Bad = 2': [unknown(0, 'Nope'), unknown(13, 'Bad')] });
      const { fixture, harness } = await editor();
      await harness.setValue('Nope = 1 and Bad = 2');
      await statusReads(fixture, 'Line 1: No field is named Nope');
      textareaOf(fixture).setSelectionRange(5, 5);

      await harness.pressF8();
      expect(await harness.getSelection()).toEqual([13, 16]);
      await harness.pressF8();
      expect(await harness.getSelection()).toEqual([0, 4]);
      await harness.pressF8(true);
      expect(await harness.getSelection()).toEqual([13, 16]);
    });

    it('leaves Enter to the textarea, and submits on Mod+Enter', async () => {
      await rig();
      const { fixture } = await editor();
      const textarea = textareaOf(fixture);
      expect(keydown(textarea, 'Enter').defaultPrevented).toBe(false);
      expect(fixture.componentInstance.submits).toBe(0);
      expect(keydown(textarea, 'Enter', MOD).defaultPrevented).toBe(true);
      expect(fixture.componentInstance.submits).toBe(1);
    });
  });

  describe('insertion', () => {
    it('inserts at the caret without splitting a token, as one undo step, selected and tinted', async () => {
      await rig();
      const { fixture, harness } = await editor();
      await harness.setValue('Amount>');
      textareaOf(fixture).setSelectionRange(3, 3);

      fixture.componentInstance.editor().insertAtCaret('x');
      await fixture.whenStable();
      expect(await harness.getValue()).toBe('Amount x>');
      expect(fixture.componentInstance.value()).toBe('Amount x>');
      expect(await harness.getSelection()).toEqual([7, 8]);
      expect((fixture.nativeElement as HTMLElement).querySelector('.tm-expression-editor__inserted')?.textContent).toBe('x');
      expect(TestBed.inject(TmFakeTelemetry).recent().filter((item) => item.name === 'tm.queryex.insert').map((item) => item.props)).toEqual([{ via: 'keyboard' }]);

      // The one way a script drives the textarea's native undo.
      document.execCommand('undo');
      await fixture.whenStable();
      expect(await harness.getValue()).toBe('Amount>');
      expect(fixture.componentInstance.value()).toBe('Amount>');
    });

    it('inserts at the end of the text when the editor never had focus', async () => {
      await rig();
      const { fixture, harness } = await editor();
      fixture.componentInstance.value.set('Code = 1 and');
      await fixture.whenStable();

      fixture.componentInstance.editor().insertAtCaret('IsActive');
      await fixture.whenStable();
      expect(await harness.getValue()).toBe('Code = 1 and IsActive');
    });

    it('re-spaces a native drop as an insertion would have placed it', async () => {
      await rig();
      const { fixture, harness } = await editor();
      await harness.setValue('Amount>');

      // The browser drops "x" inside "Amount", as a drag of the text payload does.
      const textarea = textareaOf(fixture);
      textarea.value = 'Amoxunt>';
      textarea.dispatchEvent(new InputEvent('input', { inputType: 'insertFromDrop', data: 'x', bubbles: true }));
      await fixture.whenStable();
      expect(await harness.getValue()).toBe('Amount x>');
      expect(fixture.componentInstance.value()).toBe('Amount x>');
      expect(await harness.getSelection()).toEqual([7, 8]);
      expect(TestBed.inject(TmFakeTelemetry).recent().filter((item) => item.name === 'tm.queryex.insert').map((item) => item.props)).toEqual([{ via: 'drop' }]);
    });
  });

  describe('the parameters', () => {
    it("shows the clause's parameters with their types, and warns of an untyped or conflicting one", async () => {
      const discover = await rig();
      discover.respond({
        ...EMPTY,
        parameters: [
          { name: 'From', sites: [], type: 'DateTimeOffset', conflicts: [] },
          { name: 'Who', sites: [], conflicts: ['String', 'Numeric'] },
        ],
      });
      const { fixture, harness } = await editor();
      fixture.componentInstance.showParameters.set(true);
      await harness.setValue('CreatedAt >= @From and Code = @Who and Rank = @Rank');
      await statusReads(fixture, 'No problems');
      expect(await harness.getChips()).toEqual([
        { text: '@From · Moment', warning: false },
        { text: '@Who · Conflicting types', warning: true },
        { text: '@Rank · No type', warning: true },
      ]);
    });
  });

  describe('ARIA', () => {
    it('names the textarea, describes it by the status line, and keeps code left to right', async () => {
      await rig();
      const { fixture, harness } = await editor();
      expect(await harness.getAriaLabel()).toBe('Filter');
      const [described] = await harness.getDescribedByIds();
      const element = fixture.nativeElement as HTMLElement;
      expect(element.querySelector(`#${described}`)?.classList.contains('tm-expression-editor__status')).toBe(true);
      expect(textareaOf(fixture).dir).toBe('ltr');
      expect(element.querySelector('.tm-expression-editor__mirror')?.getAttribute('aria-hidden')).toBe('true');
      expect(element.querySelector('.tm-expression-editor__announcer')?.getAttribute('aria-live')).toBe('polite');
    });
  });

  describe('Arabic', () => {
    it('reads the status line and the chips in Arabic, the code still left to right', async () => {
      await tmEntityRig({ access: { isSystem: true }, language: 'ar', tmStrings: { ar: { ...TM_STRINGS_AR, ...TM_QUERYEX_STRINGS_AR } } });
      TestBed.inject(TmFakeDiscover).respond({ ...EMPTY, parameters: [{ name: 'From', sites: [], type: 'Date', conflicts: [] }] });
      const { fixture, harness } = await editor();
      fixture.componentInstance.showParameters.set(true);
      await harness.setValue('CreatedAt >= @From');
      await statusReads(fixture, 'لا توجد مشكلات');
      expect(await harness.getChips()).toEqual([{ text: '@From · تاريخ', warning: false }]);
      expect((fixture.nativeElement as HTMLElement).querySelector('.tm-expression-editor__box')?.getAttribute('dir')).toBe('ltr');
    });
  });

  describe('a shared draft', () => {
    it("checks both editors' clauses together, and drops a clause with its editor", async () => {
      const discover = await rig();
      const fixture = TestBed.createComponent(SharedHost);
      fixture.componentInstance.select.set('Code');
      fixture.componentInstance.filter.set('IsActive = true');
      await tmSettleUntil(() => fixture.componentInstance.draft.status() === 'checked', 'the draft is checked');
      expect(discover.requests().at(-1)).toEqual({ root: 'acme.Customer', select: 'Code', filters: ['IsActive = true'] });

      fixture.componentInstance.showFilter.set(false);
      await tmSettleUntil(() => discover.requests().length === 2 && fixture.componentInstance.draft.status() === 'checked', 'the select alone is checked');
      expect(discover.requests().at(-1)).toEqual({ root: 'acme.Customer', select: 'Code' });
    });
  });

  describe('a popup cell editor', () => {
    it('inserts a line on Enter and commits on Mod+Enter', async () => {
      await rig();
      const fixture = TestBed.createComponent(GridHost);
      await fixture.whenStable();
      const scroller = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.tm-grid__scroller')!;
      const cell = (): HTMLElement => scroller.querySelector<HTMLElement>('[data-tm-cell][data-row="0"][data-col="1"]')!;
      const pane = (): HTMLElement | null => scroller.querySelector<HTMLElement>('[data-tm-popup]');
      const press: PointerEventInit = { bubbles: true, cancelable: true, button: 0, pointerType: 'mouse' };
      cell().dispatchEvent(new PointerEvent('pointerdown', press));
      cell().dispatchEvent(new PointerEvent('pointerup', press));
      await tmSettle();
      keydown(cell(), 'F2');
      await tmSettleUntil(() => pane()?.querySelector('textarea') != null, 'the popup editor opens');

      const textarea = pane()!.querySelector('textarea')!;
      expect(textarea.value).toBe('IsActive = true');
      textarea.value = 'IsActive = true\nand Code = 1';
      textarea.dispatchEvent(new Event('input', { bubbles: true }));
      expect(keydown(textarea, 'Enter').defaultPrevented).toBe(false);
      await tmSettle();
      expect(pane()).not.toBeNull();

      expect(keydown(textarea, 'Enter', MOD).defaultPrevented).toBe(true);
      await tmSettleUntil(() => pane() === null, 'the popup editor closes');
      expect(fixture.componentInstance.model()[0]!.filter).toBe('IsActive = true\nand Code = 1');
    });
  });

  describe('geometry', () => {
    /**
     * The editor's block size.
     *
     * @param fixture - The fixture.
     * @returns The size, in pixels.
     */
    const height = (fixture: ComponentFixture<unknown>): number =>
      (fixture.nativeElement as HTMLElement).querySelector('tm-expression-editor')!.getBoundingClientRect().height;

    it('keeps one box for no problem and for five', async () => {
      const text = 'A = 1 and B = 2 and C = 3 and D = 4 and E = 5';
      await rig({ [text]: ['A', 'B', 'C', 'D', 'E'].map((name, index) => unknown(index * 10, name)) });
      const { fixture, harness } = await editor();
      await harness.setValue('Code = 1');
      await statusReads(fixture, 'No problems');
      const box = (fixture.nativeElement as HTMLElement).querySelector('.tm-expression-editor__box')!.getBoundingClientRect().height;
      // Three rows of 20 px, the padding and the border.
      expect(box).toBe(3 * 20 + 2 * 6 + 2 * 1);
      const clean = height(fixture);

      await harness.setValue(text);
      await statusReads(fixture, 'Line 1: No field is named A');
      expect(height(fixture)).toBe(clean);
    });

    it('keeps one box for no parameter and for four', async () => {
      await rig();
      const { fixture, harness } = await editor();
      fixture.componentInstance.showParameters.set(true);
      await harness.setValue('Code = 1');
      await statusReads(fixture, 'No problems');
      const clean = height(fixture);

      await harness.setValue('Code = @Alpha and Name = @Bravo and Rank = @Charlie and Id = @Delta');
      await statusReads(fixture, 'No problems');
      expect(await harness.getChips()).toHaveLength(4);
      expect(height(fixture)).toBe(clean);
    });
  });
});
