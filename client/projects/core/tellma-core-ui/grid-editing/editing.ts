// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The grid's editing controller: opening an editor session on a cell and
// mounting its editor, commit (with the entity label-resolution ladder)
// and cancel. A readonly grid never needs any of it, so it ships in this
// entry point, which the grid preloads with import() once it is editable
// (spec 0010 §C.11, Part C flag 6); the grid's own core keeps every path a
// readonly grid runs, the editing keys included. The core implements
// `ɵTmGridEditingHost`, the seam this controller works through, and keeps
// a draft input for what the user types while this code is on its way
// (`resume`, `resolveLabel`).

import {
  isDevMode,
  untracked,
  type ApplicationRef,
  type Injector,
  type Signal,
  type TemplateRef,
  type ViewContainerRef,
  type WritableSignal,
} from '@angular/core';

import type { TmCellEditor, TmLabelResolution, TmRowId } from '@tellma/core-ui/contracts';
import {
  ɵtmEntitySearchOnce,
  type ɵTmEntityAdoptedSearch,
  type TmEntityId,
  type TmEntityPickerPage,
  type TmEntitySearchFn,
} from '@tellma/core-ui/entity-picker';
import type {
  TmGridColumnType,
  TmGridEngine,
  TmGridResolutionRequest,
  TmRowCol,
} from '@tellma/core-ui/grid-engine';

import {
  ɵTmGridEditorSession,
  type ɵTmGridEditorMountConfig,
  type ɵTmGridEditorTemplateContext,
  type ɵTmGridMountedEditor,
} from './editor-session';

/**
 * "This session recorded no value baseline" — distinct from every value a
 * cell can hold, `undefined` and `null` included.
 */
const NO_VALUE_BASELINE: unique symbol = Symbol('tmNoValueBaseline');

/**
 * A search taken over from an entity editor at commit time. Detached from
 * the editor's lifetime, so whoever holds it owns aborting it.
 */
type AdoptedSearch = ɵTmEntityAdoptedSearch<unknown>;

/** What an adopted search settles to. */
type AdoptedResult = Awaited<AdoptedSearch['settled']>;

/**
 * What the editing controller reads from one grid column: its identity and
 * type, its editor template, and the facilities of its built-in editors.
 * Declared here by shape, so this entry point never imports the grid it
 * serves; the grid's own column model carries all of it.
 */
export interface ɵTmGridEditingColumn<T> {
  /** The column's identity. */
  readonly id: string;
  /** The column's type, which picks the built-in editor. */
  readonly type: TmGridColumnType;
  /** The header text, the editor's accessible name. */
  readonly header: Signal<string>;
  /** The consumer's `*tmGridEditor` template, when projected. */
  readonly editorDef: { readonly template: TemplateRef<ɵTmGridEditorTemplateContext> } | undefined;
  /** An enum column's options. */
  readonly enumOptions: readonly unknown[] | undefined;
  /** Maps an enum option to its display label. */
  readonly optionLabel: ((option: unknown) => string) | undefined;
  /** Maps an enum option to the value written to the model. */
  readonly optionValue: ((option: unknown) => unknown) | undefined;
  /** The column's batched label resolver; only its presence is read here. */
  readonly resolveLabels: unknown;
  /** The text an edit-mode open seeds from the cell's value, when the column has one. */
  readonly editSeedText: ((value: unknown) => string) | undefined;
  /** The column's display formatter, also the entity editor's id display. */
  readonly format: ((value: unknown, row: T) => string) | undefined;
  /** An entity column's search facility. */
  readonly entitySearch: TmEntitySearchFn<unknown> | undefined;
  /** Maps an entity search result to its id. */
  readonly entityItemId: ((item: unknown) => TmEntityId) | undefined;
  /** Maps an entity search result to its display text. */
  readonly entityItemLabel: ((item: unknown) => string) | undefined;
  /** Maps an entity search result to its description line, if bound. */
  readonly entityItemDescription: ((item: unknown) => string | null) | undefined;
  /** Maps an entity search result to its label parts, if bound. */
  readonly entityItemLabelParts: ((item: unknown) => readonly string[] | null) | undefined;
  /** An entity column's advanced-search page. */
  readonly entityAdvancedSearch: TmEntityPickerPage | undefined;
  /** An entity column's create page. */
  readonly entityCreate: TmEntityPickerPage | undefined;
  /** An entity column's edit page. */
  readonly entityEdit: TmEntityPickerPage | undefined;
}

/**
 * The grid core as the editing controller sees it: the engine, the column
 * model, the editor checks it shares with the core, focus and reveal
 * requests, and the label-resolution runner the paste path shares.
 */
export interface ɵTmGridEditingHost<T> {
  /** The grid's headless engine. */
  readonly engine: TmGridEngine<T>;
  /** The column model, in view order. */
  readonly columnsInternal: Signal<readonly ɵTmGridEditingColumn<T>[]>;
  /** Whether Escape parked focus on the scroller; an open clears it. */
  readonly escapedSignal: WritableSignal<boolean>;
  /** Renders the editing cell's outlet synchronously before a mount. */
  readonly appRef: ApplicationRef;
  /** The editing cell's editor outlet, or `null` while none renders. */
  readonly editorOutlet: ViewContainerRef | null;
  /**
   * Whether a cell's column can host an editor: every column but an
   * `entity` one without a `*tmGridEditor` template or the built-in
   * picker's three facilities.
   */
  hasEditor(cell: TmRowCol): boolean;
  /**
   * The text an edit-mode open edits: the cell's display text, a number
   * column's full-precision value, or an errored cell's raw text.
   */
  editTextOf(cell: TmRowCol): string;
  /** Scrolls the active cell into view now. */
  revealActiveCell(): void;
  /** Focuses the active cell, now when it is rendered and after render too. */
  requestFocusActive(): void;
  /** The active cell's element, or `null`. */
  activeCellElement(): HTMLElement | null;
  /**
   * Runs the columns' batched label resolvers for resolution requests,
   * with a verdict to keep where a resolver cannot name a label.
   */
  runResolutions(
    requests: readonly TmGridResolutionRequest[],
    fallback?: TmLabelResolution<unknown>,
  ): void;
}

/** The grid's draft as an editor takes it over: its text and caret. */
export interface ɵTmGridDraftHandoff {
  /** The draft's text. */
  readonly text: string;
  /** Whether the text differs from what the editor opens with, which it must then take. */
  readonly changed: boolean;
  /** The caret's or selection's start. */
  readonly selectionStart: number;
  /** The selection's end. */
  readonly selectionEnd: number;
  /** Which end of the selection the caret is at. */
  readonly selectionDirection: 'forward' | 'backward' | 'none';
}

/** How an editor opens: under an IME composition, or showing its choices. */
export interface ɵTmGridEditorOpenOptions {
  /** The open answers an IME composition's first keydown: unseeded. */
  readonly ime?: boolean;
  /** Alt+ArrowDown: the editor's dropdown opens with it. */
  readonly dropdown?: boolean;
}

/**
 * The editing controller of one grid. It owns the single live editor view
 * (through `ɵTmGridEditorSession`) and the pristine baselines a commit is
 * judged against; the engine owns the session state.
 */
export class ɵTmGridEditing<T> {
  /** The live editor view's mount and teardown. */
  private readonly session: ɵTmGridEditorSession;

  /**
   * The text a text-channel editor was OPENED with (edit mode's display
   * text), or `null` when there is no pristine baseline (seeded opens,
   * enum/template editors). A commit whose text still equals this baseline
   * writes nothing — opening and closing an editor never rewrites the
   * model, so a display-rounded cell's underlying precision survives an
   * F2 + Enter pass untouched.
   */
  private editorOpenText: string | null = null;

  /**
   * The VALUE an entity editor was opened with, or `NO_VALUE_BASELINE` when
   * the session has none. Entity editors are the only ones that own both
   * channels, and a clean entity cell hands the commit to the value channel
   * — so the text baseline above cannot see an untouched session and the
   * value baseline has to.
   */
  private editorOpenValue: unknown = NO_VALUE_BASELINE;

  /**
   * @param host The grid core this controller edits through.
   * @param injector The grid's injector, parent of each editor's cell-scoped one.
   */
  constructor(
    private readonly host: ɵTmGridEditingHost<T>,
    injector: Injector,
  ) {
    this.session = new ɵTmGridEditorSession(injector, () => host.editorOutlet);
  }

  /** The mounted editor, or `null` while nothing is live. */
  current(): ɵTmGridMountedEditor | null {
    return this.session.current();
  }

  /** Destroys the mounted editor view (grid disposal). */
  destroy(): void {
    this.session.destroy();
  }

  /**
   * Opens an editor session on a cell and mounts its editor SYNCHRONOUSLY
   * (see `mount`). Returns whether a session opened.
   */
  open(
    cell: TmRowCol,
    mode: 'edit' | 'enter',
    seedText?: string,
    opts?: ɵTmGridEditorOpenOptions,
  ): boolean {
    const engine = this.host.engine;
    if (!this.admits(cell) || !engine.edit.openEdit(cell, mode, seedText)) {
      return false;
    }
    this.host.escapedSignal.set(false);
    // The opening keystroke on a scrolled-away cell scrolls it back (§4).
    this.host.revealActiveCell();
    return this.mount(opts);
  }

  /**
   * Mounts the editor of the session the grid opened before this code
   * arrived, focused, and hands it the draft the user typed meanwhile: its
   * text, when it differs from what the editor opens with, and its caret.
   */
  resume(opts: ɵTmGridEditorOpenOptions | undefined, draft: ɵTmGridDraftHandoff): void {
    const engine = this.host.engine;
    const session = untracked(() => engine.edit.session());
    if (session === null) {
      return;
    }
    try {
      if (!this.admits(session.cell) || !this.mount(opts)) {
        engine.edit.cancel();
        return;
      }
    } catch (error) {
      // The development error for a misconfigured column: the session the
      // grid opened ahead of the code must not outlive it.
      engine.edit.cancel();
      throw error;
    }
    const mounted = this.session.current();
    if (mounted === null) {
      return;
    }
    if (draft.changed) {
      // The draft replaces what the mount seeded; the pristine baseline the
      // mount recorded still judges the commit.
      this.seed(mounted.editor, draft.text);
    }
    // The caret goes where it was in the draft, when the editor's focused
    // input holds the same text.
    const focused = document.activeElement;
    if (
      (focused instanceof HTMLInputElement || focused instanceof HTMLTextAreaElement) &&
      focused.value === draft.text
    ) {
      focused.setSelectionRange(draft.selectionStart, draft.selectionEnd, draft.selectionDirection);
    }
  }

  /**
   * Resolves an entity commit the grid made before this code arrived, left
   * pending in its cell, exactly as the built-in picker's own commit would:
   * the column's search for the text decides it where it can, and the
   * column's resolver where it cannot.
   */
  resolveLabel(request: TmGridResolutionRequest, text: string): void {
    const column = untracked(this.host.columnsInternal).find((each) => each.id === request.columnId);
    const search = column?.entitySearch;
    const itemId = column?.entityItemId;
    if (column === undefined || search === undefined || itemId === undefined) {
      this.host.runResolutions([request]);
      return;
    }
    this.settleLabel(request, text, column, ɵtmEntitySearchOnce(search, itemId, text));
  }

  /** Whether the mounted editor's dropdown panel is open. */
  isDropdownOpen(): boolean {
    return this.session.current()?.isDropdownOpen() ?? false;
  }

  /**
   * Opens the mounted editor's dropdown panel (Alt+ArrowDown); returns
   * whether the editor has one.
   */
  openDropdown(): boolean {
    const mounted = this.session.current();
    if (
      mounted === null ||
      (mounted.kind !== 'enum' && mounted.kind !== 'date' && mounted.kind !== 'entity')
    ) {
      return false;
    }
    mounted.openDropdown();
    return true;
  }

  /**
   * Commits the open session through the engine: the enum select commits
   * its VALUE; text-path editors commit their text through the column's
   * parse — unless `text()` is `null` (content not representable as text,
   * or an entity picker whose value channel is authoritative), which
   * commits the value channel directly. A pristine text editor (the text
   * still equals what it opened with) commits nothing. Unresolved text on
   * an `entity` column takes the label-resolution ladder instead of the
   * plain text commit (see `commitEditorText`).
   */
  commit(opts: { readonly refocus: boolean }): void {
    const engine = this.host.engine;
    const mounted = this.session.current();
    if (mounted === null) {
      engine.edit.cancel();
    } else if (mounted.kind === 'enum') {
      engine.edit.commitValue(untracked(() => mounted.editor.value()));
    } else {
      let adopted: AdoptedSearch | null = null;
      if (mounted.kind === 'entity') {
        // Take the editor's search over FIRST: `commit()` below closes the
        // dropdown, and a closing popup aborts what it was fetching. What
        // is adopted here is detached from the editor's lifetime, so it
        // survives the teardown that follows — and every early exit past
        // this point has to abort it, or the request leaks.
        const pending = untracked(() => mounted.editor.text());
        // Text the session opened with is not a commit to resolve, so
        // adopting for it would issue a request only to abort it below.
        adopted =
          pending === null || pending === this.editorOpenText
            ? null
            : (mounted.adoptSearch?.(pending) ?? null);
        // The picker's synchronous fast path: a fresh unique result for the
        // current unresolved text auto-picks (no request) before the
        // channels are read. PULLED here — a push through the activation
        // output would re-enter this commit mid-flight.
        mounted.editor.commit();
        if (this.isPristineEntitySession(mounted.editor)) {
          adopted?.abort();
          engine.edit.cancel();
          this.close(opts);
          return;
        }
      }
      const text = untracked(() => mounted.editor.text());
      if (text === null) {
        adopted?.abort(); // the fast path answered it
        engine.edit.commitValue(untracked(() => mounted.editor.value()));
      } else if (text === this.editorOpenText) {
        adopted?.abort();
        engine.edit.cancel();
      } else {
        this.commitEditorText(text, adopted);
      }
    }
    this.close(opts);
  }

  /** Cancels the open session: the model is never written. */
  cancel(opts: { readonly refocus: boolean }): void {
    this.session.current()?.editor.cancel();
    this.host.engine.edit.cancel();
    this.close(opts);
  }

  // ---- opening ----

  /**
   * Whether a cell's column can host an editor at all. An `entity` column
   * needs a `*tmGridEditor` template or the built-in picker's three
   * facilities; a cell that could never have opened an editor stays quiet,
   * and an editable one reports the misconfiguration in development.
   */
  private admits(cell: TmRowCol): boolean {
    if (this.host.hasEditor(cell)) {
      return true;
    }
    const engine = this.host.engine;
    const column = untracked(this.host.columnsInternal)[cell.col];
    // A cell that could never have opened an editor stays quiet; an
    // editable one reports the misconfiguration in development.
    if (column !== undefined && isDevMode() && untracked(() => engine.model.isCellEditable(cell))) {
      throw new Error(
        column.entitySearch === undefined
          ? `tm-grid: column "${column.id}" of type 'entity' has no editor — bind ` +
              `[search]/[itemId]/[itemLabel] for the built-in tm-entity-picker, or project ` +
              `a *tmGridEditor template hosting a control that implements TmCellEditor.`
          : `tm-grid: column "${column.id}" binds [search] without [itemId]/[itemLabel] — ` +
              `both are required alongside [search] for the built-in tm-entity-picker editor.`,
      );
    }
    // In production the cell stays non-editable: opening anyway would
    // render every option as "[object Object]" through the String(item)
    // fallbacks and commit that string into the foreign-key field.
    return false;
  }

  /**
   * Mounts the open session's editor SYNCHRONOUSLY and seeds it. The outlet
   * only exists once the session signal has rendered, so this forces one
   * render pass via `ApplicationRef.tick()` — DOM event handlers always run
   * outside a tick in zoneless Angular, so the call is re-entrancy-safe,
   * and the synchronous mount is what lets an IME composition land inside
   * the editor's input within the same task (`opts.ime`); every other open
   * path shares it for one behavior. The editor takes focus. Returns
   * whether an editor mounted (the session is cancelled when none did).
   */
  private mount(opts: ɵTmGridEditorOpenOptions | undefined): boolean {
    const engine = this.host.engine;
    const session = untracked(() => engine.edit.session());
    const column =
      session === null ? undefined : untracked(this.host.columnsInternal)[session.cell.col];
    if (session === null || column === undefined) {
      return false;
    }
    const cell = session.cell;
    const seedText = session.seedText;
    const view = untracked(() => engine.model.rowAt(cell.row));
    const valueAtOpen = untracked(() => engine.model.cellValue(cell));
    // An invalid-input cell opens on its raw text, which has no value
    // baseline (see the entity branch below).
    const hasInvalidInput =
      view !== null &&
      untracked(() => engine.annotations.invalidInput(view.id, column.id)) !== undefined;
    const editText = this.host.editTextOf(cell);
    const header = untracked(column.header);

    let config: ɵTmGridEditorMountConfig;
    if (column.editorDef !== undefined) {
      config = {
        kind: 'template',
        template: column.editorDef.template,
        context: { $implicit: valueAtOpen, row: view?.row },
      };
    } else if (column.type === 'entity') {
      const format = column.format;
      // The row is addressed by IDENTITY, not captured: an immutable model
      // replaces the row object on every write, so a snapshot taken here
      // would render the committed id through the row as it stood when the
      // editor opened — and would pin that object for the editor's whole
      // lifetime. `null` on the placeholder row, which has no identity yet.
      const rowId: TmRowId | null = view?.id ?? null;
      config = {
        kind: 'entity',
        label: header,
        search: column.entitySearch!,
        // Both are guaranteed by the half-config guard (which throws in dev
        // and refuses the open in prod); the `??` only satisfies the
        // picker's required inputs, and its branch is unreachable.
        itemId: column.entityItemId ?? ((item: unknown) => String(item)),
        itemLabel: column.entityItemLabel ?? ((item: unknown) => String(item)),
        // Two-line rows and label parts reach the dropdown as the column
        // binds them (spec 0010 §E.4).
        itemDescription: column.entityItemDescription,
        itemLabelParts: column.entityItemLabelParts,
        // The column's `format` doubles as the picker's committed-id display
        // resolver; an empty string defers to the picker's own fallbacks
        // (label memo, then String(id) with the dev warning). On the
        // placeholder row there is no row to hand a consumer `format`
        // (whose signature promises one) — defer to the fallbacks instead
        // of calling it with undefined.
        displayWith:
          format === undefined
            ? undefined
            : (id: unknown) => {
                const row = rowId === null ? undefined : engine.model.rowById(rowId);
                if (row === undefined) {
                  return null;
                }
                const text = format(id, row);
                return text === '' ? null : text;
              },
        advancedSearch: column.entityAdvancedSearch,
        create: column.entityCreate,
        edit: column.entityEdit,
        onActivation: () => this.onEditorActivation(),
      };
    } else if (column.type === 'enum') {
      config = {
        kind: 'enum',
        label: header,
        options: column.enumOptions ?? [],
        optionLabel: column.optionLabel,
        optionValue: column.optionValue,
        onActivation: () => this.onEditorActivation(),
      };
    } else if (column.type === 'number') {
      config = { kind: 'number', label: header };
    } else if (column.type === 'date') {
      config = { kind: 'date', label: header, onActivation: () => this.onEditorActivation() };
    } else {
      config = { kind: 'text', label: header };
    }

    this.session.stage(config);
    this.host.appRef.tick(); // render the outlet now (see the method TSDoc)
    const mounted = this.session.mountIfReady();
    if (mounted === null) {
      // Nothing registered (prod fallback of the dev-mode throw).
      engine.edit.cancel();
      return false;
    }

    const editor = mounted.editor;
    editor.focus();
    // Every open starts WITHOUT a pristine baseline; only the branches that
    // seed a known text install one. Leaving the previous session's baseline
    // in place would make a later commit of a coincidentally equal string
    // (an emptied cell after an emptied cell) look pristine and be dropped.
    this.editorOpenText = null;
    this.editorOpenValue = NO_VALUE_BASELINE;
    if (opts?.ime === true) {
      // IME opens UNSEEDED: the composition itself supplies the content.
      this.editorOpenText = '';
      if (mounted.kind === 'entity') {
        // The committed id is installed even though the text is empty: the
        // Edit… row still targets it, and an abandoned composition must
        // leave the cell exactly as it was — which on the new-row
        // placeholder means not materializing a row at all, so the value
        // baseline is recorded here as well. (The dropdown's own selection
        // mirror follows the TEXT, so it shows nothing until one is typed.)
        editor.value.set(valueAtOpen);
        mounted.setTextQuiet?.('');
        this.editorOpenValue = valueAtOpen;
      }
    } else if (mounted.kind === 'enum') {
      editor.value.set(valueAtOpen);
      if (seedText !== undefined) {
        // Type-to-edit: the select opens its panel seeding the typeahead.
        editor.seed?.(seedText);
      } else {
        mounted.openDropdown();
      }
    } else if (mounted.kind === 'entity') {
      // The committed id first: the dropdown mirrors it (check glyph) and
      // the Edit… footer row targets it.
      editor.value.set(valueAtOpen);
      if (seedText !== undefined) {
        // Type-to-edit: replaces the content and searches the seed at once.
        editor.seed?.(seedText);
      } else {
        // Edit mode shows the display text (or an errored cell's raw text)
        // with the dropdown CLOSED — `seed()` would search; the quiet
        // install does not. The pristine baseline makes F2 + Enter commit
        // nothing (no re-resolution of the committed label).
        mounted.setTextQuiet?.(editText);
        this.editorOpenText = editText;
        // An entity editor owns BOTH channels, and on a clean cell the text
        // channel stands down (it reports null so the id commits). The text
        // baseline alone can therefore never recognize an untouched
        // session; the value baseline is what makes F2 + Enter a no-op.
        //
        // An INVALID-INPUT cell gets no value baseline: it opens on raw
        // text that is not the value's display text, so "the value channel
        // is authoritative again" means the user cleared the bad text —
        // a real edit, and the only way to clear the annotation from the
        // editor. Its own pristine case (the raw text still unchanged) is
        // covered by the text baseline above.
        if (!hasInvalidInput) {
          this.editorOpenValue = valueAtOpen;
        }
      }
    } else if (seedText !== undefined) {
      this.seed(editor, seedText);
      // A type-to-edit seed REPLACES the content — committing it unchanged
      // is still a user edit, so the baseline stays null (set above), never
      // the seed.
    } else if (mounted.kind === 'text' || mounted.kind === 'number' || mounted.kind === 'date') {
      // Edit mode edits the cell's CURRENT DISPLAY TEXT (Excel edits the
      // formatted text; for an invalid-input cell that is the raw text) — but a
      // number column edits its full-precision value, not the rounded display.
      this.seed(editor, editText);
      this.editorOpenText = editText;
    } else {
      // Consumer template editor: the grid owns the value channel and
      // seeds it with the raw cell value (also carried in the context).
      editor.value.set(valueAtOpen);
    }
    // Alt+ArrowDown means "show me the choices": one press reaches them,
    // whatever the cell's editor is. An enum's panel is already open above
    // (it has nothing to type, so every open shows it); a date's calendar
    // and an entity's dropdown are not, because typing is those cells'
    // primary path and F2 must not pop them. Type-to-edit is exempt — the
    // seed IS the intent.
    if (opts?.dropdown === true && seedText === undefined && !mounted.isDropdownOpen()) {
      mounted.openDropdown();
    }
    return true;
  }

  /**
   * Installs text in an editor: through `seed()` (caret at the end), or
   * its value channel for a control that has no seed.
   */
  private seed(editor: TmCellEditor<unknown>, text: string): void {
    if (editor.seed !== undefined) {
      editor.seed(text);
    } else {
      editor.value.set(text);
    }
  }

  // ---- committing ----

  /**
   * A dropdown editor produced a value by pointing at it — an enum option
   * activated, or a day picked in a date cell's calendar. Commit and CLOSE,
   * no move (Sheets): the pick IS the edit, so it must not sit uncommitted
   * waiting for an Enter that a mouse user has no reason to press.
   */
  private onEditorActivation(): void {
    if (untracked(() => this.host.engine.edit.session()) !== null) {
      this.commit({ refocus: true });
    }
  }

  /**
   * Whether an entity session is still exactly as it opened — neither
   * channel moved. Such a commit must write NOTHING: on a real row the
   * value write would be elided anyway, but on the new-row placeholder any
   * commit at all materializes the row, so opening an editor on the `*` row
   * and closing it would append a blank row the user never typed into.
   *
   * Only "the value is unchanged" is checked here, never "the value write
   * would be a no-op": a commit also clears the cell's invalid input, and a
   * session that opened on one has no value baseline for exactly that
   * reason.
   */
  private isPristineEntitySession(editor: TmCellEditor<unknown>): boolean {
    if (this.editorOpenValue === NO_VALUE_BASELINE) {
      return false; // a seeded/IME open, or an errored cell: an edit by construction
    }
    const text = untracked(() => editor.text());
    if (text !== null) {
      return text === this.editorOpenText; // an errored cell's raw text, unchanged
    }
    return Object.is(
      untracked(() => editor.value()),
      this.editorOpenValue,
    );
  }

  /**
   * Text commits: `entity` columns route through the label-resolution
   * ladder — the same conversion chain pasted labels take (consumer parse,
   * then `resolvePastedLabels` with the single text under the grid's
   * pending affordance and sequence tokens, else a definitive invalid
   * input) — applying to consumer template editors on entity columns too.
   * Every other column commits through the column's parse as before. The
   * resolver runs in a microtask, after the editor's teardown completes —
   * exactly one authoritative resolution, under the GRID's pending state.
   */
  private commitEditorText(text: string, adopted: AdoptedSearch | null): void {
    const engine = this.host.engine;
    const session = untracked(() => engine.edit.session());
    const column =
      session === null ? undefined : untracked(this.host.columnsInternal)[session.cell.col];
    if (column?.type !== 'entity') {
      adopted?.abort();
      engine.edit.commitText(text);
      return;
    }
    const request = engine.commitEditorLabel(text, { deferToHost: adopted !== null });
    if (request === null) {
      adopted?.abort(); // an earlier rung settled it — the search is nobody's input now
      return;
    }
    if (adopted === null) {
      this.host.runResolutions([request]);
      return;
    }
    this.settleLabel(request, text, column, adopted);
  }

  /**
   * Answers a pending entity commit from a search for its text: the search
   * decides where it can (`decideFromSearch`), and the column's resolver
   * takes the rest, with the search's verdict as its fallback.
   */
  private settleLabel(
    request: TmGridResolutionRequest,
    text: string,
    column: ɵTmGridEditingColumn<T>,
    adopted: AdoptedSearch,
  ): void {
    const engine = this.host.engine;
    // Undo while pending aborts the request; the adopted search is part of
    // that request now, so it goes with it.
    request.context.signal.addEventListener('abort', adopted.abort, { once: true });
    void adopted.settled
      .then((outcome) => {
        if (request.context.signal.aborted) {
          // Undone, or the grid was disposed. An abort surfaces here as a
          // search FAILURE (the picker cannot tell the two apart), and
          // falling through on a failure would start the very round trip
          // the abort was cancelling — against a request that is already
          // gone, from a grid that may already be destroyed.
          return;
        }
        const verdict = this.decideFromSearch(outcome, text, column);
        if (verdict.decided !== undefined) {
          engine.clipboard.applyResolution(request.id, new Map([[text, verdict.decided]]));
          return;
        }
        if (outcome === 'failed' && column.resolveLabels === undefined) {
          // The search FAILED and there is no identity authority to ask
          // instead — so nothing ever examined this text. Left to
          // `runResolutions`, the unanswered label would default to the
          // definitive `notFound` ("there is no Alice"), which is a verdict
          // no one reached; a dropped round trip is retryable and must say
          // so, exactly as a REJECTED resolver does.
          engine.clipboard.applyResolution(request.id, new Map(), { failed: true });
          return;
        }
        // The search could not settle it. The SAME request carries on to
        // the column's resolver — same pending mark, same sequence token,
        // same open history entry — carrying whatever the search DID
        // conclude as the fallback, for a resolver that cannot name it
        // either.
        this.host.runResolutions([request], verdict.fallback);
      })
      .catch(() => {
        // A consumer accessor threw inside the decision. The request must
        // still be answered: an unanswered one leaves the cell pending
        // forever inside a history entry that never finalizes.
        engine.clipboard.applyResolution(request.id, new Map(), { failed: true });
      });
  }

  /**
   * What the editor's own search says about the committed text.
   *
   * The two facilities answer different questions. `search` asks "what
   * matches this query?" — ranked, capped, typically scoped to what a user
   * may pick today. `resolvePastedLabels` asks "which entity IS this
   * label?" — an identity lookup that may reach codes, aliases, or records
   * the type-ahead never offers. So the search is authoritative about
   * MULTIPLICITY and the resolver about IDENTITY OF SOMETHING THE SEARCH
   * CANNOT SEE. Three answers:
   *
   * The line between the two verdicts is whether the search's answer is an
   * IDENTITY fact or only a ranking one:
   *
   * - `decided` — the text names exactly one entity, or names several BY
   *   LABEL. Both are identity facts the resolver cannot improve on, so it
   *   is not consulted at all.
   * - `fallback` — the search matched several rows and the text is none of
   *   their labels. That is a dead end for the search and still an open
   *   identity question: a consumer type-ahead that matches codes as well
   *   as names returns several rows for a code, and that code is exactly
   *   what the resolver knows. So the resolver is asked, and this verdict
   *   is used only if it cannot name the text either — which is what keeps
   *   the better message ("'Al' matches more than one Agent" is true and
   *   useful; "no match for 'Al'" is neither).
   *
   * A TRUNCATED result set (`hasMore`) can be trusted about what it
   * contains and never about what it does not: the duplicate that would
   * make a lone exact match ambiguous may be sitting past the cap.
   */
  private decideFromSearch(
    outcome: AdoptedResult,
    text: string,
    column: ɵTmGridEditingColumn<T>,
  ): {
    readonly decided?: TmLabelResolution<unknown>;
    readonly fallback?: TmLabelResolution<unknown>;
  } {
    if (outcome === 'failed' || outcome.items.length === 0) {
      return {};
    }
    const itemId = column.entityItemId;
    const itemLabel = column.entityItemLabel;
    if (itemId === undefined || itemLabel === undefined) {
      return {}; // half-configured column: the editor never mounts, but stay honest
    }
    const items = outcome.items;
    const exact = items.filter((item: unknown) => itemLabel(item as never) === text);
    if (exact.length > 1) {
      // Two entities carry this exact label. Rows past a cap could only
      // agree, and no identity lookup can undo it.
      return { decided: { error: 'ambiguous' } };
    }
    if (!outcome.hasMore && (exact.length === 1 || items.length === 1)) {
      return { decided: { value: itemId((exact[0] ?? items[0]) as never) } };
    }
    return { fallback: { error: 'ambiguous' } };
  }

  /** Destroys the editor view and, when asked, returns focus to the active cell. */
  private close(opts: { readonly refocus: boolean }): void {
    this.session.destroy();
    if (opts.refocus) {
      // Synchronously, so focus never falls to <body> (a body-focus frame
      // would look like a grid blur to outside observers) — then again
      // after render, when the move (if any) re-targets the active cell.
      this.host.activeCellElement()?.focus({ preventScroll: true });
      this.host.requestFocusActive();
    }
  }
}
