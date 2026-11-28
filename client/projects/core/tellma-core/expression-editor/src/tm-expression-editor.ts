// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterRenderEffect,
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  model,
  numberAttribute,
  output,
  signal,
  untracked,
  viewChild,
  type Signal,
} from '@angular/core';
import type { ValidationError } from '@angular/forms/signals';
import { TmLocalization, TmTelemetry } from '@tellma/core';
import { tmQxLex, type TmQxTokenKind } from '@tellma/core/entity';
import { TM_CELL_EDITOR_HOST, TM_ERROR_DISPLAY, TM_UI_TRANSLATE, tmResolveFieldErrors } from '@tellma/core-ui';
import type { TmCellEditor, TmFieldError, TmFormFieldControl } from '@tellma/core-ui/contracts';
import { TM_FORM_FIELD_CONTROL } from '@tellma/core-ui/form-field';
import { tmKeysMatch } from '@tellma/core-ui/keys';
import { TmPopover, TmPopoverContent, TmPopoverTrigger } from '@tellma/core-ui/popover';

import { injectTmQueryexDraft, type TmQueryexDraft, type TmQueryexStatus, type TmQxClause, type TmQxDiagnostic, type TmQxParameter } from './tm-queryex-draft';
import { ɵTM_QUERYEX_STRINGS_EN } from './tm-queryex-strings';
import { tmQxParameters, ɵtmQxFold, ɵtmQxPlanInsert } from './tm-qx-text';

// The expression editor (spec 0029 §10.3–§10.5): a native textarea whose
// text is transparent, over a `pre` mirror with the same metrics that draws
// the tokens, the problems' wavy underlines and the tint of inserted text.
// Every edit is the textarea's own, so undo, IME and drops stay native; the
// editor only re-spaces a drop and inserts at the caret with one native
// edit. The box is a fixed number of lines tall, the status line and the
// chip row below it never change height, so nothing moves while typing.

/** How long inserted text stays tinted, in milliseconds. */
const TINT_MS = 1200;

/** Numbers each editor's element ids. */
let nextId = 0;

/** One run of the mirror: text and the classes that draw it. */
interface TmMirrorSegment {
  /** The run's text. */
  readonly text: string;
  /** Its classes. */
  readonly classes: string;
}

/** A span of the text, in UTF-16 code units. */
interface TmSpan {
  /** The first unit. */
  readonly start: number;
  /** One past the last unit. */
  readonly end: number;
}

/** What the status line shows. */
interface TmStatusLine {
  /** What it says. */
  readonly text: string;
  /** Whether it offers Retry. */
  readonly retry: boolean;
  /** The label of the button that lists every problem, or `null` when there is none. */
  readonly list: string | null;
}

/**
 * The mirror's class of a token.
 *
 * @param kind - The token's kind.
 * @param call - Whether the name is a function's.
 * @returns The class.
 */
function tokenClass(kind: TmQxTokenKind, call: boolean): string {
  return `tm-expression-editor__${kind === 'identifier' && call ? 'function' : kind}`;
}

/**
 * The runs the mirror draws: split wherever a token, a problem's span or
 * the inserted tint starts or ends, then one trailing space, which keeps
 * a final empty line and carries a problem at the end of the text.
 *
 * @param text - The text.
 * @param problems - The problems.
 * @param tint - The inserted span, or `null`.
 * @returns The runs.
 */
function mirrorSegments(text: string, problems: readonly TmQxDiagnostic[], tint: TmSpan | null): readonly TmMirrorSegment[] {
  const tokens = tmQxLex(text).tokens;
  // A problem with no width underlines the character it sits before.
  const underlined: TmSpan[] = problems.map((problem) => ({ start: problem.start, end: problem.start + Math.max(problem.length, 1) }));
  const cuts = new Set<number>([0, text.length]);
  for (const token of tokens) {
    cuts.add(token.start).add(token.start + token.length);
  }
  for (const span of [...underlined, ...(tint === null ? [] : [tint])]) {
    cuts.add(Math.min(span.start, text.length)).add(Math.min(span.end, text.length));
  }
  const points = [...cuts].sort((a, b) => a - b);
  const segments: TmMirrorSegment[] = [];
  let next = 0;
  for (let index = 0; index < points.length - 1; index++) {
    const start = points[index]!;
    const end = points[index + 1]!;
    // The token at the run, walked forward as the runs are.
    while (next < tokens.length && tokens[next]!.start + tokens[next]!.length <= start) {
      next++;
    }
    const token = tokens[next] !== undefined && tokens[next]!.start <= start ? tokens[next] : undefined;
    const classes = [
      token === undefined ? '' : tokenClass(token.kind, token.call === true),
      underlined.some((span) => span.start <= start && start < span.end) ? 'tm-expression-editor__problem' : '',
      tint !== null && tint.start <= start && start < tint.end ? 'tm-expression-editor__inserted' : '',
    ];
    segments.push({ text: text.slice(start, end), classes: classes.filter(Boolean).join(' ') });
  }
  const atEnd = underlined.some((span) => span.start >= text.length);
  segments.push({ text: ' ', classes: atEnd ? 'tm-expression-editor__problem' : '' });
  return segments;
}

/**
 * Edits one Queryex clause. Tokens are highlighted on every key, the
 * server's problems show a moment later, and a field dragged in or inserted
 * at the caret keeps every token apart. Editors of one query share a
 * `TmQueryexDraft`, so inference sees every clause; without one, the
 * editor checks its clause alone against `root`.
 *
 * The box is `rows` lines tall and scrolls inside; a status line below it
 * reads "No problems", "Checking…" or the first problem, with a list of
 * every problem; with `showParameters`, a row of the clause's parameters
 * and their types follows. Code is always left to right.
 *
 * Signal Forms native (`FormValueControl<string>`), and a grid's popup
 * cell editor: there Enter inserts a line and Mod+Enter, left to the grid,
 * commits.
 *
 * @example
 * ```html
 * <tm-expression-editor clause="filter" [root]="entity" [(value)]="filterText" (submit)="apply()" />
 * ```
 *
 * @tmA11yNotes A native textarea, labelled by `ariaLabel` or the enclosing
 *   field and described by the status line; `aria-invalid` while any
 *   problem exists. F8 and Shift+F8 select the next and previous problem;
 *   Tab leaves the editor. A polite region announces each check's outcome
 *   while the editor has focus, never a keystroke.
 */
@Component({
  selector: 'tm-expression-editor',
  imports: [TmPopover, TmPopoverContent, TmPopoverTrigger],
  providers: [{ provide: TM_FORM_FIELD_CONTROL, useExisting: TmExpressionEditor }],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './tm-expression-editor.css',
  host: {
    class: 'tm-expression-editor',
    '[class.tm-expression-editor--disabled]': 'disabled()',
    '[class.tm-expression-editor--readonly]': 'readonly()',
    '[class.tm-expression-editor--invalid]': 'showsInvalid()',
    '[style.--tm-expression-editor-rows]': 'rows()',
  },
  template: `
    <!-- The code box, left to right in any page: the mirror draws, the textarea edits. -->
    <div class="tm-expression-editor__box" dir="ltr">
      <pre #mirror class="tm-expression-editor__mirror" aria-hidden="true">@for (segment of segments(); track $index) {<span [class]="segment.classes">{{ segment.text }}</span>}</pre>
      <textarea
        #textarea
        class="tm-expression-editor__input"
        dir="ltr"
        spellcheck="false"
        autocomplete="off"
        autocapitalize="off"
        autocorrect="off"
        [id]="controlId()"
        [rows]="rows()"
        [disabled]="disabled()"
        [readOnly]="readonly()"
        [required]="required()"
        [placeholder]="placeholder() ?? ''"
        [attr.aria-label]="ariaLabel() ?? null"
        [attr.aria-describedby]="describedByAttr()"
        [attr.aria-invalid]="ariaInvalid() ? 'true' : null"
        (input)="onInput($event)"
        (keydown)="onKeydown($event)"
        (scroll)="syncScroll()"
        (focus)="focused = true"
        (blur)="touch.emit()"
      ></textarea>
    </div>

    <!-- The status line: always one line, so the problems never move what follows. -->
    @let line = statusLine();
    <div class="tm-expression-editor__status" [class.tm-expression-editor__status--problem]="diagnostics().length > 0" [id]="statusId">
      <span class="tm-expression-editor__status-text">{{ line.text }}</span>
      @if (line.retry) {
        <button type="button" class="tm-expression-editor__link tm-expression-editor__retry" (click)="retry()">{{ retryLabel() }}</button>
      }
      @if (line.list; as list) {
        <button type="button" class="tm-expression-editor__link tm-expression-editor__more" [tmPopoverTriggerFor]="problemsPopover">{{ list }}</button>
      }
    </div>

    <!-- Every problem, each selecting its span. -->
    <tm-popover #problemsPopover size="md" [aria-label]="problemsLabel()">
      <ng-template tmPopoverContent>
        <ul class="tm-expression-editor__problems">
          @for (problem of diagnostics(); track $index) {
            <li>
              <button type="button" class="tm-expression-editor__link tm-expression-editor__problem-item" (click)="pick(problem)">
                {{ problemText(problem) }}
              </button>
            </li>
          }
        </ul>
      </ng-template>
    </tm-popover>

    <!-- The clause's parameters and their types. -->
    @if (showParameters()) {
      <div class="tm-expression-editor__chips" role="list" [attr.aria-label]="parametersLabel()">
        @for (chip of chips(); track chip.name) {
          <span role="listitem" class="tm-expression-editor__chip" [class.tm-expression-editor__chip--warning]="chip.warning">
            <bdi dir="ltr">&#64;{{ chip.name }}</bdi> · {{ chip.type }}
          </span>
        }
      </div>
    }

    <!-- Each check's outcome, announced while the editor has focus. -->
    <span class="tm-expression-editor__announcer" aria-live="polite">{{ announcement() }}</span>
  `,
})
export class TmExpressionEditor implements TmFormFieldControl, TmCellEditor<string> {
  readonly #l10n = inject(TmLocalization);
  readonly #telemetry = inject(TmTelemetry);
  readonly #host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  readonly #translate = inject(TM_UI_TRANSLATE);
  readonly #errorDisplay = inject(TM_ERROR_DISPLAY);
  /** The enclosing grid cell's registration sink; absent outside a grid. */
  readonly #cellHost = inject(TM_CELL_EDITOR_HOST, { optional: true });

  // ---- FormValueControl<string> and the state inputs a bound field sets ----
  /** The clause's text. */
  readonly value = model('');
  /** Whether the editor is disabled; a bound field is authoritative. */
  readonly disabled = input(false, { transform: booleanAttribute });
  /** Whether the editor is read-only; a bound field is authoritative. */
  readonly readonly = input(false, { transform: booleanAttribute });
  /** Whether a value is required; a bound field is authoritative. */
  readonly required = input(false, { transform: booleanAttribute });
  /** Whether the value fails validation; a bound field is authoritative. */
  readonly invalid = input(false, { transform: booleanAttribute });
  /** Whether the user has left the editor; a bound field is authoritative. */
  readonly touched = input(false, { transform: booleanAttribute });
  /** Whether the user has changed the value; a bound field is authoritative. */
  readonly dirty = input(false, { transform: booleanAttribute });
  /** Whether an asynchronous validation runs; a bound field is authoritative. */
  readonly pending = input(false, { transform: booleanAttribute });
  /** The field's raw validation errors, localized into `localizedErrors`. */
  readonly errors = input<readonly ValidationError.WithOptionalFieldTree[]>([]);
  /** Reports the user leaving the editor. */
  readonly touch = output<void>();

  // ---- The editor's own API ----
  /** The clause the editor writes. */
  readonly clause = input.required<TmQxClause>();
  /** The query's shared draft; without one, the editor checks its clause alone against `root`. */
  readonly draft = input<TmQueryexDraft | undefined>(undefined);
  /** The entity a draft of the editor's own checks against. */
  readonly root = input<string | undefined>(undefined);
  /** The box's height in lines; 3 when omitted. */
  readonly rows = input(3, { transform: numberAttribute });
  /** Whether the row of the clause's parameters shows; it does not when omitted. */
  readonly showParameters = input(false, { transform: booleanAttribute });
  /** The editor's accessible name, when no field labels it. */
  readonly ariaLabel = input<string | undefined>(undefined);
  /** The text shown while the editor is empty. */
  readonly placeholder = input<string | undefined>(undefined);
  /** Author-supplied describedby ids; the status line's and the field's follow them. */
  readonly ariaDescribedby = input<string | null>(null, { alias: 'aria-describedby' });
  // The name the platform's specs give this output; no native `submit`
  // event reaches this element, which is never a form.
  /* eslint-disable @angular-eslint/no-output-native -- named by the spec */
  /** Emits on Mod+Enter, outside a grid cell. */
  readonly submit = output<void>();
  /* eslint-enable @angular-eslint/no-output-native */

  /** The textarea's id, which a field's label targets. */
  readonly controlId = signal(`tm-expression-editor-${nextId++}`).asReadonly();
  /** The status line's id. */
  protected readonly statusId = `${this.controlId()}-status`;

  /** The textarea. */
  private readonly textareaRef = viewChild.required<ElementRef<HTMLTextAreaElement>>('textarea');
  /** The mirror. */
  private readonly mirrorRef = viewChild.required<ElementRef<HTMLPreElement>>('mirror');
  /** The problems list. */
  private readonly problemsPopoverRef = viewChild.required<TmPopover>('problemsPopover');

  /** The draft the editor checks with when it is given none. */
  readonly #own = injectTmQueryexDraft({ root: computed(() => this.root() ?? '') });
  /** The draft in use. */
  readonly #draft = computed(() => this.draft() ?? this.#own);

  /** The span tinted after an insertion. */
  readonly #tint = signal<TmSpan | null>(null);
  /** Clears the tint once its moment is over. */
  #tintTimer: ReturnType<typeof setTimeout> | undefined;

  /** Whether the editor has had focus, so an insertion goes to its caret rather than the end. */
  protected focused = false;

  /** Whether the editor itself is editing the textarea, so the input it causes is not a user's. */
  #editing = false;

  // ---- TmFormFieldControl ----
  /** The editor draws its own box. */
  readonly ownsChrome = true;
  /** The field's hint and error ids. */
  readonly #fieldDescribedBy = signal<readonly string[]>([]);
  /** Whether the enclosing field shows an error. */
  readonly #fieldError = signal(false);
  /** Every describedby id: the author's, the status line's, then the field's. */
  readonly describedByIds: Signal<readonly string[]> = computed(() => [
    ...(this.ariaDescribedby()?.split(/\s+/).filter(Boolean) ?? []),
    this.statusId,
    ...this.#fieldDescribedBy(),
  ]);
  /** The localized messages of the field's errors. */
  readonly localizedErrors: () => readonly TmFieldError[] = tmResolveFieldErrors(this.errors, this.#translate);
  /** The aria-describedby attribute. */
  protected readonly describedByAttr = computed(() => this.describedByIds().join(' '));

  /** Whether the field's validation shows as an error. */
  protected readonly showsInvalid = computed(
    () =>
      this.#fieldError() ||
      this.#errorDisplay({ invalid: this.invalid(), touched: this.touched(), dirty: this.dirty(), pending: this.pending() }),
  );

  // ---- TmCellEditor<string> ----
  /** The committed-text view of the content: the text itself. */
  readonly text: Signal<string | null> = computed(() => this.value());
  /** The value `cancel()` returns to. */
  #committed = '';
  /** The value the editor itself just wrote, so its echo does not move the baseline. */
  #selfWrite: { readonly value: string } | null = null;

  // ---- What the template draws ----
  /** The clause's problems. */
  protected readonly diagnostics = computed(() => this.#draft().diagnostics(this.clause())());
  /** Whether a problem or a field error marks the editor invalid. */
  protected readonly ariaInvalid = computed(() => this.diagnostics().length > 0 || this.showsInvalid());
  /** The mirror's runs. */
  protected readonly segments = computed(() => mirrorSegments(this.value(), this.diagnostics(), this.#tint()));

  /** A string of the editor's. */
  readonly #text = (key: string, params?: Readonly<Record<string, unknown>>): string => this.#l10n.ɵtext(`tm.queryex.${key}`, params)();

  /** The Retry button's label. */
  protected readonly retryLabel = computed(() => this.#text('retry'));
  /** The problems list's name. */
  protected readonly problemsLabel = computed(() => this.#text('problems', { count: this.diagnostics().length }));
  /** The chip row's name. */
  protected readonly parametersLabel = computed(() => this.#text('parameters'));

  /** What the status line shows. */
  protected readonly statusLine = computed<TmStatusLine>(() => {
    const problems = this.diagnostics();
    const status = this.#draft().status();
    const unchecked = status === 'offline' || status === 'failed';
    if (unchecked) {
      // The lexer's problems still show, listed beside the reason.
      return { text: this.#text(status), retry: true, list: problems.length > 0 ? this.#text('problems', { count: problems.length }) : null };
    }
    const first = problems[0];
    if (first !== undefined) {
      return { text: this.problemText(first), retry: false, list: problems.length > 1 ? this.#text('more', { count: problems.length - 1 }) : null };
    }
    return { text: this.#text(status === 'checking' ? 'checking' : 'noProblems'), retry: false, list: null };
  });

  /** The clause's parameters as chips. */
  protected readonly chips = computed(() => {
    const named = new Set(tmQxParameters(this.value()).map((parameter) => ɵtmQxFold(parameter.name)));
    return this.#draft()
      .parameters()
      .filter((parameter) => named.has(ɵtmQxFold(parameter.name)))
      .map((parameter) => ({ name: parameter.name, type: this.#typeLabel(parameter), warning: parameter.conflicting || parameter.type === null }));
  });

  /** The last check's outcome, while the editor has focus. */
  protected readonly announcement = signal('');

  constructor() {
    this.#l10n.ɵregisterStrings('queryex', ɵTM_QUERYEX_STRINGS_EN);
    // A popup cell editor hosts the editor as its cell's editor.
    this.#cellHost?.register(this);

    // --- External writes move the revert baseline; the editor's own do not ---
    effect(() => {
      const value = this.value();
      const self = this.#selfWrite;
      this.#selfWrite = null;
      if (self === null || self.value !== value) {
        this.#committed = value;
      }
    });

    // --- External writes reach the textarea, leaving the caret alone on the user's own ---
    afterRenderEffect({
      write: () => {
        const value = this.value();
        const textarea = this.textareaRef().nativeElement;
        if (textarea.value !== value) {
          textarea.value = value;
        }
        untracked(() => this.syncScroll());
      },
    });

    // --- The draft sees the clause's text ---
    effect(() => {
      const draft = this.#draft();
      const clause = this.clause();
      const value = this.value();
      untracked(() => draft.text(clause).set(value));
    });

    // --- Each check's outcome is announced while the editor has focus ---
    let previous: TmQueryexStatus | null = null;
    effect(() => {
      const status = this.#draft().status();
      const problems = this.diagnostics().length;
      const changed = previous !== null && status !== previous;
      previous = status;
      if (!changed || status === 'checking' || status === 'idle') {
        return;
      }
      untracked(() => {
        if (this.#host.contains(document.activeElement)) {
          this.announcement.set(status === 'checked' ? (problems > 0 ? this.#text('problems', { count: problems }) : this.#text('noProblems')) : this.#text(status));
        }
      });
    });

    // --- A clause leaves the shared draft with its editor ---
    inject(DestroyRef).onDestroy(() => {
      clearTimeout(this.#tintTimer);
      const text = untracked(() => this.#draft().text(this.clause()));
      if (untracked(text) === untracked(this.value)) {
        text.set('');
      }
    });
  }

  // ---- TmFormFieldControl ----

  /**
   * Receives the field's hint and error ids.
   *
   * @param ids - The ids.
   */
  setDescribedByIds(ids: readonly string[]): void {
    this.#fieldDescribedBy.set(ids);
  }

  /**
   * Receives whether the field shows an error.
   *
   * @param showsError - Whether it does.
   */
  setFieldError(showsError: boolean): void {
    this.#fieldError.set(showsError);
  }

  /** Focuses the textarea when the field's chrome is clicked. */
  onContainerClick(): void {
    this.focus();
  }

  // ---- The editor's API ----

  /** Focuses the textarea, the caret at the end of the text. */
  focus(): void {
    const textarea = this.textareaRef().nativeElement;
    textarea.focus();
    textarea.setSelectionRange(textarea.value.length, textarea.value.length);
  }

  /**
   * Inserts text at the last caret, or at the end when the editor was never
   * focused, without splitting a token, as one native undo step. The
   * inserted text is left selected and tinted, and the editor has focus.
   *
   * @param text - The text, such as a path.
   */
  insertAtCaret(text: string): void {
    if (this.disabled() || this.readonly()) {
      return;
    }
    const textarea = this.textareaRef().nativeElement;
    const caret = this.focused ? textarea.selectionEnd : textarea.value.length;
    const plan = ɵtmQxPlanInsert(textarea.value, caret, text);
    this.#replace(plan.at, plan.at, plan.added);
    textarea.setSelectionRange(plan.start, plan.end);
    this.#tintSpan({ start: plan.start, end: plan.end });
    this.#telemetry.event('tm.queryex.insert', { via: 'keyboard' });
  }

  /** Checks the draft again at once. */
  protected retry(): void {
    this.#draft().retry();
  }

  // ---- TmCellEditor<string> ----

  /** Accepts the current text: the value already follows every keystroke. */
  commit(): void {
    this.#committed = this.value();
  }

  /** Returns to the text present when editing began. */
  cancel(): void {
    this.#selfWrite = { value: this.#committed };
    this.value.set(this.#committed);
  }

  /**
   * Replaces the text with a typed seed, the caret at its end.
   *
   * @param text - The seed.
   */
  seed(text: string): void {
    this.#selfWrite = { value: text };
    this.value.set(text);
    const textarea = this.textareaRef().nativeElement;
    textarea.value = text;
    textarea.setSelectionRange(text.length, text.length);
  }

  // ---- Events ----

  /**
   * Mirrors the textarea into the value, and re-spaces a drop.
   *
   * @param event - The input event.
   */
  protected onInput(event: Event): void {
    const textarea = this.textareaRef().nativeElement;
    const before = this.value();
    this.#write(textarea.value);
    if (this.#editing) {
      return;
    }
    this.#tint.set(null);
    if ((event as InputEvent).inputType === 'insertFromDrop') {
      this.#respaceDrop(before, textarea.value);
    }
    this.syncScroll();
  }

  /**
   * F8 and Shift+F8 select the next and previous problem; Mod+Enter
   * submits outside a grid cell and is left to the grid inside one.
   *
   * @param event - The keydown.
   */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'F8' && !event.altKey && !event.ctrlKey && !event.metaKey) {
      event.preventDefault();
      this.#stepProblem(event.shiftKey);
    } else if (tmKeysMatch(event, 'Mod+Enter') && this.#cellHost === null) {
      event.preventDefault();
      this.submit.emit();
    }
  }

  /** Keeps the mirror scrolled with the textarea. */
  protected syncScroll(): void {
    const textarea = this.textareaRef().nativeElement;
    const mirror = this.mirrorRef().nativeElement;
    mirror.scrollTop = textarea.scrollTop;
    mirror.scrollLeft = textarea.scrollLeft;
  }

  /**
   * Selects a problem's span from the problems list.
   *
   * @param problem - The problem.
   */
  protected pick(problem: TmQxDiagnostic): void {
    this.problemsPopoverRef().close();
    this.#select(problem);
  }

  /**
   * A problem as one line: its line number and message.
   *
   * @param problem - The problem.
   * @returns The text.
   */
  protected problemText(problem: TmQxDiagnostic): string {
    const line = this.value().slice(0, problem.start).split('\n').length;
    return this.#text('problemAt', { line, message: problem.message() });
  }

  // ---- Internals ----

  /**
   * Writes the textarea's text into the value and the draft at once.
   *
   * @param value - The text.
   */
  #write(value: string): void {
    this.#selfWrite = { value };
    this.value.set(value);
    this.#draft().text(this.clause()).set(value);
  }

  /**
   * Replaces a range of the textarea with text as one native edit, so undo
   * takes it back in one step; where the browser has no such edit, the
   * text is replaced directly.
   *
   * @param start - The range's start.
   * @param end - The range's end.
   * @param text - The new text.
   */
  #replace(start: number, end: number, text: string): void {
    const textarea = this.textareaRef().nativeElement;
    textarea.focus();
    textarea.setSelectionRange(start, end);
    this.#editing = true;
    try {
      // insertText is the one edit a script makes that joins the native undo stack.
      const native = text !== '' && document.execCommand('insertText', false, text);
      if (!native) {
        textarea.setRangeText(text, start, end, 'end');
        this.#write(textarea.value);
      }
    } finally {
      this.#editing = false;
    }
    this.syncScroll();
  }

  /**
   * Re-spaces a native drop as `tmQxInsert` would have placed it, with one
   * more native edit: undo first takes back the spaces, then the drop.
   *
   * @param before - The text before the drop.
   * @param after - The text after it.
   */
  #respaceDrop(before: string, after: string): void {
    // The dropped run: the selection the browser leaves on it, when the
    // text without it is the text before the drop.
    const { selectionStart, selectionEnd } = this.textareaRef().nativeElement;
    let head = selectionStart;
    let tail = after.length - selectionEnd;
    if (after.slice(0, head) + after.slice(selectionEnd) !== before) {
      // Otherwise what the text gained between the common head and tail,
      // which is ambiguous when the drop repeats the text after it.
      head = 0;
      while (head < before.length && head < after.length && before[head] === after[head]) {
        head++;
      }
      tail = 0;
      while (tail < before.length - head && tail < after.length - head && before[before.length - 1 - tail] === after[after.length - 1 - tail]) {
        tail++;
      }
    }
    const dropped = after.slice(head, after.length - tail);
    if (dropped === '') {
      return;
    }
    const base = after.slice(0, head) + after.slice(after.length - tail);
    const plan = ɵtmQxPlanInsert(base, head, dropped);
    if (plan.text !== after) {
      // One edit over the part of the text that differs.
      let start = 0;
      while (start < after.length && start < plan.text.length && after[start] === plan.text[start]) {
        start++;
      }
      let end = 0;
      while (end < after.length - start && end < plan.text.length - start && after[after.length - 1 - end] === plan.text[plan.text.length - 1 - end]) {
        end++;
      }
      this.#replace(start, after.length - end, plan.text.slice(start, plan.text.length - end));
    }
    this.textareaRef().nativeElement.setSelectionRange(plan.start, plan.end);
    this.#tintSpan({ start: plan.start, end: plan.end });
    this.#telemetry.event('tm.queryex.insert', { via: 'drop' });
  }

  /**
   * Tints a span for a moment.
   *
   * @param span - The span.
   */
  #tintSpan(span: TmSpan): void {
    clearTimeout(this.#tintTimer);
    this.#tint.set(span);
    this.#tintTimer = setTimeout(() => this.#tint.set(null), TINT_MS);
  }

  /**
   * Selects the problem after the caret, or before it, wrapping around.
   *
   * @param backward - Whether to go to the previous one.
   */
  #stepProblem(backward: boolean): void {
    const problems = this.diagnostics();
    if (problems.length === 0) {
      return;
    }
    const caret = this.textareaRef().nativeElement.selectionStart;
    const target = backward
      ? ([...problems].reverse().find((problem) => problem.start < caret) ?? problems.at(-1)!)
      : (problems.find((problem) => problem.start > caret) ?? problems[0]!);
    this.#select(target);
  }

  /**
   * Focuses the textarea with a problem's span selected.
   *
   * @param problem - The problem.
   */
  #select(problem: TmQxDiagnostic): void {
    const textarea = this.textareaRef().nativeElement;
    textarea.focus();
    textarea.setSelectionRange(problem.start, problem.start + problem.length);
    this.syncScroll();
  }

  /**
   * A parameter's type as its chip names it.
   *
   * @param parameter - The parameter.
   * @returns The type's name, or why there is none.
   */
  #typeLabel(parameter: TmQxParameter): string {
    if (parameter.conflicting) {
      return this.#text('conflicting');
    }
    if (parameter.type === null) {
      return this.#text('untyped');
    }
    return parameter.type in ɵTM_QUERYEX_STRINGS_EN.type ? this.#text(`type.${parameter.type}`) : parameter.type;
  }
}
