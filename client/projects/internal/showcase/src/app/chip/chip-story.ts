// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal, type WritableSignal } from '@angular/core';

import type { TmBadgeTone } from '@tellma/core-ui/badge';
import { TmButton } from '@tellma/core-ui/button';
import { TmChip, TmChipSet } from '@tellma/core-ui/chip';

/** The labels every chip set in the story starts with. */
const LABELS = ['Service', 'Sale', 'Operation', 'Purchase', 'Payroll', 'Inventory'];

/**
 * tm-chip in every tone and size, as text, link and button, removable and
 * with projected content; tm-chip-set wrapping, collapsing behind a "+N"
 * chip at two widths, and scrolling beside an input the way a multi-value
 * field uses it (exit refocuses the input; Backspace in the empty input
 * focuses the last chip).
 */
@Component({
  imports: [TmButton, TmChip, TmChipSet],
  template: `
    <h2>Chips</h2>

    <!-- Every tone, and the three size steps. -->
    <section class="row" data-testid="tones">
      @for (tone of tones; track tone) {
        <tm-chip [tone]="tone" [label]="tone" [attr.data-testid]="'chip-' + tone" />
      }
    </section>
    <section class="row" data-testid="sizes">
      <tm-chip size="sm" label="Small" data-testid="chip-sm" />
      <tm-chip size="md" label="Medium" data-testid="chip-md" />
      <tm-chip size="lg" label="Large" data-testid="chip-lg" />
      <tm-chip size="sm" label="Small" icon="check" [removable]="true" />
      <tm-chip size="lg" label="Large" icon="check" [removable]="true" />
    </section>

    <!-- Text, link, button, removable, projected and other-script labels. -->
    <section class="row" data-testid="kinds">
      <tm-chip label="Plain text" data-testid="chip-text" />
      <tm-chip label="SAL-0042" href="#sal-0042" tone="accent" data-testid="chip-link" />
      <tm-chip label="Activate me" (activate)="log('activate')" data-testid="chip-button" />
      <tm-chip
        label="Removable"
        tone="info"
        [removable]="true"
        (remove)="log('remove')"
        data-testid="chip-removable"
      />
      <tm-chip label="Posted after 1 May" (activate)="log('activate date')" data-testid="chip-projected"
        >Posted after <b>1 May</b></tm-chip
      >
      <tm-chip label="الرياض" lang="ar" [removable]="true" data-testid="chip-arabic" />
    </section>
    <p class="log" data-testid="chip-log">{{ events().join(', ') }}</p>

    <!-- Wrap: removable filters, one tab stop. -->
    <h3>Wrap</h3>
    <tm-chip-set ariaLabel="Filters" data-testid="set-wrap" (exit)="log('exit ' + $event)">
      @for (label of wrapLabels(); track label) {
        <tm-chip [label]="label" [removable]="true" (remove)="removeFrom(wrapLabels, label)" />
      }
    </tm-chip-set>
    <button tmButton type="button" data-testid="reset-wrap" (click)="wrapLabels.set(labels)">
      Reset
    </button>

    <!-- Collapse, in a box whose width the buttons switch. -->
    <h3>Collapse</h3>
    <div class="box" [style.inline-size.px]="collapseWidth()" data-testid="collapse-box">
      <tm-chip-set ariaLabel="Tags" overflow="collapse" data-testid="set-collapse">
        @for (label of labels; track label) {
          <tm-chip [label]="label" (activate)="log('activate ' + label)" />
        }
      </tm-chip-set>
    </div>
    <div class="row">
      <button tmButton type="button" data-testid="collapse-narrow" (click)="collapseWidth.set(220)">
        Narrow
      </button>
      <button tmButton type="button" data-testid="collapse-wide" (click)="collapseWidth.set(640)">
        Wide
      </button>
    </div>

    <!-- Scroll, beside an input: a multi-value field's chip row. -->
    <h3>Scroll</h3>
    <div class="field" data-testid="scroll-field">
      <tm-chip-set
        #chips
        class="field__chips"
        ariaLabel="Chosen agents"
        overflow="scroll"
        data-testid="set-scroll"
        (exit)="$event === 'end' && input.focus()"
      >
        @for (label of scrollLabels(); track label) {
          <tm-chip [label]="label" [removable]="true" (remove)="removeFrom(scrollLabels, label)" />
        }
      </tm-chip-set>
      <input
        #input
        class="field__input"
        aria-label="Add an agent"
        data-testid="scroll-input"
        (keydown.backspace)="input.value === '' && chips.focusLast()"
      />
    </div>
    <button tmButton type="button" data-testid="reset-scroll" (click)="scrollLabels.set(labels)">
      Reset
    </button>
  `,
  styles: `
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 12px;
      margin-block-end: 16px;
    }
    .log {
      min-block-size: 1.5em;
    }
    .box {
      max-inline-size: 100%;
      padding: 8px;
      border: 1px dashed var(--border-default);
      margin-block-end: 8px;
    }
    .field {
      display: flex;
      align-items: center;
      gap: 8px;
      inline-size: 280px;
      max-inline-size: 100%;
      padding-inline: 8px;
      border: 1px solid var(--border-default);
      border-radius: 6px;
      margin-block-end: 8px;
    }
    .field__chips {
      flex: 0 1 auto;
      max-inline-size: 70%;
    }
    .field__input {
      flex: 1 1 60px;
      min-inline-size: 60px;
      block-size: 34px;
      border: 0;
      background: transparent;
      color: inherit;
      font: inherit;
    }
  `,
})
export class ChipStory {
  /** Every chip tone, in the order the story shows them. */
  protected readonly tones: readonly TmBadgeTone[] = [
    'neutral',
    'accent',
    'info',
    'success',
    'warning',
    'error',
  ];

  /** The labels every set starts with. */
  protected readonly labels = LABELS;

  /** The wrapping set's chips. */
  protected readonly wrapLabels = signal(LABELS);

  /** The scrolling set's chips. */
  protected readonly scrollLabels = signal(LABELS);

  /** The collapsing set's box width, in px. */
  protected readonly collapseWidth = signal(220);

  /** What the chips emitted, in order. */
  protected readonly events = signal<readonly string[]>([]);

  /** Records an event for the log line. */
  protected log(event: string): void {
    this.events.set([...this.events(), event]);
  }

  /** Removes a chip's label from one of the sets. */
  protected removeFrom(list: WritableSignal<string[]>, label: string): void {
    list.set(list().filter((candidate) => candidate !== label));
  }
}
