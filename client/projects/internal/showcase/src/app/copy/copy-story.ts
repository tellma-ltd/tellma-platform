// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import { TmCopyButton, TmReference } from '@tellma/core-ui/copy';

/**
 * tm-copy-button icon-only and with its label, at each size and with a
 * value built on the click; tm-reference with a trace id, in a narrow box
 * that cuts it short, and with its own label.
 */
@Component({
  imports: [TmCopyButton, TmReference],
  template: `
    <h2>Copy</h2>

    <!-- Icon-only beside a value, at each size. -->
    <section class="row">
      <span>INV-0042</span>
      <tm-copy-button
        text="INV-0042"
        label="Copy invoice number"
        data-testid="copy-icon"
        (copied)="copies.set(copies() + 1)"
      />
      <tm-copy-button text="INV-0042" size="md" />
      <tm-copy-button text="INV-0042" size="lg" />
      <span data-testid="copies">{{ copies() }}</span>
    </section>

    <!-- With the label: the box keeps the width of "Copied". -->
    <section class="row">
      <tm-copy-button
        text="https://example.com/invoices/42"
        label="Copy link"
        showLabel
        data-testid="copy-label"
      />
      <tm-copy-button [text]="build" label="Copy report" showLabel data-testid="copy-built" />
    </section>

    <!-- References: full width, cut short, and labelled. -->
    <section class="stack">
      <tm-reference value="4bf92f3577b34da6a3ce929d0e0e4736" data-testid="reference" />
      <div class="narrow">
        <tm-reference value="4bf92f3577b34da6a3ce929d0e0e4736" data-testid="reference-narrow" />
      </div>
      <tm-reference label="Version" value="2026.11.3+a41c9e2" />
    </section>
  `,
  styles: `
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 12px;
      margin-block-end: 24px;
    }
    .stack {
      display: grid;
      justify-items: start;
      gap: 12px;
    }
    .narrow {
      inline-size: 220px;
    }
  `,
})
export class CopyStory {
  /** How many copies the first button reported. */
  protected readonly copies = signal(0);

  /** A value built on the click, a moment later. */
  protected readonly build = (): Promise<string> =>
    new Promise((resolve) => setTimeout(() => resolve('Report 2026-11 (built on demand)'), 50));
}
