// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  Component,
  computed,
  ElementRef,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { form } from '@angular/forms/signals';

import type { TmCellEditor } from '@tellma/core-ui/contracts';
import { TM_CELL_EDITOR_HOST } from '@tellma/core-ui';
import { TmGrid, TmGridColumn, TmGridEditorDef } from '@tellma/core-ui/grid';

/** A membership's time window: two nullable dates; both `null` is permanent. */
interface MembershipWindow {
  /** When the membership starts (`YYYY-MM-DD`), or `null` for now. */
  readonly startsAt: string | null;
  /** When the membership ends (`YYYY-MM-DD`), or `null` for never. */
  readonly endsAt: string | null;
}

/** One role membership row. */
interface Membership {
  readonly id: number;
  readonly user: string;
  readonly role: string;
  readonly window: MembershipWindow;
  readonly filter: string | null;
}

/** The permanent window. */
const PERMANENT: MembershipWindow = { startsAt: null, endsAt: null };

/** The roles the seed rows cycle through; "Administrator" takes no filter. */
const ROLES = ['Accountant', 'Clerk', 'Administrator', 'Auditor'] as const;

/** Thirty rows, so the grid scrolls and a pane near its edge can spill past it. */
function makeSeedMemberships(): Membership[] {
  return Array.from({ length: 30 }, (_, i) => ({
    id: i + 1,
    user: `User ${i + 1}`,
    role: ROLES[i % ROLES.length],
    window: i % 3 === 0 ? { startsAt: '2026-11-01', endsAt: '2026-12-31' } : PERMANENT,
    filter: i % 2 === 0 ? `CreatedById = me()` : null,
  }));
}

/**
 * A consumer sub-form editor for the Window column: two plain fields,
 * Starts and Ends, edited as ONE cell value. It implements
 * `TmCellEditor<MembershipWindow>` and registers itself through
 * TM_CELL_EDITOR_HOST; its `text` is `null`, so the grid commits the value
 * channel. Two controls make the popup pane a labelled group.
 */
@Component({
  selector: 'app-demo-window-editor',
  template: `
    <div class="window-editor">
      <label class="window-editor__field">
        <span>Starts</span>
        <input
          #starts
          data-testid="window-starts"
          placeholder="YYYY-MM-DD"
          [readOnly]="readonly()"
          [value]="value().startsAt ?? ''"
          (input)="write('startsAt', $event)"
        />
      </label>
      <label class="window-editor__field">
        <span>Ends</span>
        <input
          data-testid="window-ends"
          placeholder="YYYY-MM-DD"
          [readOnly]="readonly()"
          [value]="value().endsAt ?? ''"
          (input)="write('endsAt', $event)"
        />
      </label>
      <p class="window-editor__hint">Leave both empty for a permanent membership.</p>
    </div>
  `,
  styles: `
    .window-editor {
      display: grid;
      gap: var(--space-2);
      padding: var(--space-3);
    }
    .window-editor__field {
      display: grid;
      gap: var(--space-1);
    }
    .window-editor__field input {
      font: inherit;
      padding: var(--space-1) var(--space-2);
      border: var(--border-width) solid var(--border-default);
      border-radius: var(--radius-sm);
    }
    .window-editor__hint {
      margin: 0;
      color: var(--text-muted);
      font-size: var(--text-xs);
    }
  `,
})
export class DemoWindowEditor implements TmCellEditor<MembershipWindow> {
  /** Whether the pane only shows the window (F2 on a readonly cell). */
  readonly readonly = input(false);

  private readonly starts = viewChild.required<ElementRef<HTMLInputElement>>('starts');

  /** The window; the grid seeds it with the cell value at open. */
  readonly value = signal<MembershipWindow>(PERMANENT);
  /** Always `null`: the value channel is authoritative. */
  readonly text = signal<string | null>(null);

  constructor() {
    inject(TM_CELL_EDITOR_HOST, { optional: true })?.register(this as TmCellEditor<unknown>);
  }

  /** Nothing pending to flush: the fields write live. */
  commit(): void {}

  /** The grid never reads the editor after cancel; nothing to restore. */
  cancel(): void {}

  /** Focuses the Starts field, caret at the end. */
  focus(): void {
    const element = this.starts().nativeElement;
    element.focus();
    element.setSelectionRange(element.value.length, element.value.length);
  }

  /** Type-to-edit: the typed text starts the Starts field. */
  seed(text: string): void {
    this.value.update((window) => ({ ...window, startsAt: text }));
    const element = this.starts().nativeElement;
    element.value = text;
    element.setSelectionRange(text.length, text.length);
  }

  /** Writes one field into the window; an emptied field is `null`. */
  protected write(field: keyof MembershipWindow, event: Event): void {
    const text = (event.target as HTMLInputElement).value.trim();
    this.value.update((window) => ({ ...window, [field]: text === '' ? null : text }));
  }
}

/**
 * A consumer multi-line editor for the Filter column: a textarea, where
 * Enter inserts a line and Mod+Enter commits, plus a hint under it.
 */
@Component({
  selector: 'app-demo-filter-editor',
  template: `
    <div class="filter-editor">
      <textarea
        #area
        class="filter-editor__input"
        data-testid="filter-input"
        rows="4"
        aria-label="Filter"
        [readOnly]="readonly()"
        [value]="value() ?? ''"
        (input)="onInput($event)"
      ></textarea>
      <p class="filter-editor__hint">
        Records this permission covers. Leave empty for all records.
      </p>
    </div>
  `,
  styles: `
    .filter-editor {
      display: grid;
      gap: var(--space-2);
      padding: var(--space-3);
    }
    .filter-editor__input {
      font-family: var(--font-mono, monospace);
      font-size: inherit;
      resize: vertical;
      padding: var(--space-1) var(--space-2);
      border: var(--border-width) solid var(--border-default);
      border-radius: var(--radius-sm);
    }
    .filter-editor__hint {
      margin: 0;
      color: var(--text-muted);
      font-size: var(--text-xs);
    }
  `,
})
export class DemoFilterEditor implements TmCellEditor<string | null> {
  /** Whether the pane only shows the filter (F2 on a readonly cell). */
  readonly readonly = input(false);

  private readonly area = viewChild.required<ElementRef<HTMLTextAreaElement>>('area');

  /** The filter text; the grid seeds it with the cell value at open. */
  readonly value = signal<string | null>(null);
  /** The committed text the column's parse reads. */
  readonly text = computed(() => this.value() ?? '');

  constructor() {
    inject(TM_CELL_EDITOR_HOST, { optional: true })?.register(this as TmCellEditor<unknown>);
  }

  /** Nothing pending to flush: the textarea writes live. */
  commit(): void {}

  /** The grid never reads the editor after cancel; nothing to restore. */
  cancel(): void {}

  /** Focuses the textarea, caret at the end. */
  focus(): void {
    const element = this.area().nativeElement;
    element.focus();
    element.setSelectionRange(element.value.length, element.value.length);
  }

  /** Type-to-edit: the typed text replaces the filter. */
  seed(text: string): void {
    this.value.set(text);
    const element = this.area().nativeElement;
    element.value = text;
    element.setSelectionRange(text.length, text.length);
  }

  protected onInput(event: Event): void {
    this.value.set((event.target as HTMLTextAreaElement).value);
  }
}

/**
 * Popup cell editors: the Window and Filter columns open their editors in
 * a pane over the cell that spills past the grid while every row keeps
 * its height. Window is a two-field sub-form (a labelled group); Filter is
 * multi-line. Administrator rows take no filter, so F2 there opens the
 * pane read-only, as it does on every popup cell in view mode.
 */
@Component({
  imports: [TmGrid, TmGridColumn, TmGridEditorDef, DemoWindowEditor, DemoFilterEditor],
  template: `
    <h2>Grid (popup editors)</h2>

    <p>
      <button type="button" data-testid="toggle-readonly" (click)="readonly.set(!readonly())">
        {{ readonly() ? 'Edit' : 'View' }}
      </button>
    </p>

    <tm-grid
      class="demo-grid"
      gridId="grid-popup-editor"
      data-testid="grid-popup-editor"
      [field]="f"
      [rowId]="rowId"
      [readonly]="readonly()"
    >
      <tm-grid-column key="user" header="User" [flex]="1" [minWidth]="120" />
      <tm-grid-column key="role" header="Role" [width]="130" />
      <tm-grid-column
        key="window"
        header="Window"
        [width]="190"
        [format]="windowText"
        [editorPopup]="{ minWidth: 360 }"
      >
        <app-demo-window-editor *tmGridEditor="let readonly = readonly" [readonly]="readonly" />
      </tm-grid-column>
      <tm-grid-column
        key="filter"
        header="Filter"
        [flex]="1"
        [minWidth]="160"
        [readonly]="takesNoFilter"
        [editorPopup]="{ minWidth: 480 }"
      >
        <app-demo-filter-editor *tmGridEditor="let readonly = readonly" [readonly]="readonly" />
      </tm-grid-column>
    </tm-grid>

    <h3>Model</h3>
    <!-- Scrollable, so keyboard-reachable (axe scrollable-region-focusable). -->
    <pre class="model-dump" data-testid="model-json" tabindex="0" aria-label="Model JSON">{{
      modelJson()
    }}</pre>
  `,
  styles: `
    .demo-grid {
      display: block;
      block-size: 320px;
    }
    .model-dump {
      max-block-size: 160px;
      overflow: auto;
      font-size: 11px;
      background: var(--surface-raised, transparent);
      padding: 8px;
    }
  `,
})
export class GridPopupEditorStory {
  readonly model = signal<Membership[]>(makeSeedMemberships());
  readonly f = form(this.model);
  /** View mode: every popup cell opens read-only on F2. */
  readonly readonly = signal(false);

  readonly rowId = (membership: Membership): number => membership.id;

  /** The window as the cell shows it. */
  readonly windowText = (window: MembershipWindow | null): string =>
    window === null || (window.startsAt === null && window.endsAt === null)
      ? 'Permanent'
      : `${window.startsAt ?? 'Now'} – ${window.endsAt ?? 'Open'}`;

  /** Administrators cover every record: their filter cell is read-only. */
  readonly takesNoFilter = (membership: Membership): boolean =>
    membership.role === 'Administrator';

  /** The live model dump the e2e battery parses (one JSON line). */
  readonly modelJson = computed(() => JSON.stringify(this.model()));
}
