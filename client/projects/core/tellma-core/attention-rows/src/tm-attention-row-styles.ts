// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, ViewEncapsulation } from '@angular/core';

// The row anatomy every attention list shares (spec 0028 §6.2, §7.3): the
// bell's notification and job rows, and the tray's and the home page's
// assignment rows. One unencapsulated stylesheet, attached while any list
// that renders this element is on the page, so each list's own styles hold
// only what is its alone. Every selector is scoped by a `tm-attention-row`
// class.

/**
 * The attention rows' shared stylesheet: an empty, hidden element whose
 * presence attaches the `tm-attention-row` classes. Internal: the attention
 * lists render it beside their rows.
 *
 * Classes: `tm-attention-rows` (the `ul`), `tm-attention-row` (a row's
 * box: two lines tall, a touch target below `sm`, tinted on hover, and
 * ringed on focus when it is a link),
 * `tm-attention-row__icon`, `tm-attention-row__main` (the two lines),
 * `tm-attention-row__title`, `tm-attention-row__line` and
 * `tm-attention-row__end` (the time and the marks).
 *
 * @example
 * ```html
 * <tm-attention-row-styles />
 * <ul class="tm-attention-rows">
 *   <li class="tm-attention-row">…</li>
 * </ul>
 * ```
 */
@Component({
  selector: 'tm-attention-row-styles',
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  host: { hidden: '' },
  template: '',
  styles: `
    .tm-attention-rows {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    /* A row: icon, the two lines, the end and any controls, on one baseline. */
    .tm-attention-row {
      display: flex;
      align-items: flex-start;
      gap: var(--space-2);
      min-block-size: 3.25rem;
      padding: var(--space-2);
      box-sizing: border-box;
      border-radius: var(--radius-md);
      color: var(--text-body);
      text-decoration: none;
    }

    /* A live row tints on hover: a list's row, or a row that is its own link; never a skeleton. */
    .tm-attention-row:is([data-tm-row], [href]):hover {
      background: var(--surface-hover);
    }

    /* The row's links draw the focus ring: the row itself, its two lines, or its title. */
    a.tm-attention-row:focus-visible,
    .tm-attention-row__main:focus-visible,
    .tm-attention-row__title:focus-visible {
      outline: var(--focus-ring-width) solid transparent;
      box-shadow: var(--focus-ring);
    }

    .tm-attention-row__icon {
      flex: none;
      margin-block-start: 2px;
      --icon-size: var(--icon-size-md);
    }

    .tm-attention-row__main {
      display: flex;
      flex: 1;
      flex-direction: column;
      gap: var(--space-1);
      min-inline-size: 0;
      border-radius: var(--radius-sm);
      color: inherit;
      text-decoration: none;
    }

    .tm-attention-row__title,
    .tm-attention-row__line {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .tm-attention-row__title {
      border-radius: var(--radius-sm);
      color: var(--text-strong);
      text-decoration: none;
    }

    .tm-attention-row__line,
    .tm-attention-row__end {
      color: var(--text-secondary);
      font-size: var(--text-xs);
    }

    .tm-attention-row__line {
      min-block-size: 1lh;
    }

    .tm-attention-row__end {
      display: flex;
      flex: none;
      flex-direction: column;
      align-items: flex-end;
      gap: var(--space-1);
    }

    /* Below sm, rows are a touch target tall, as menu rows are. */
    @media (width < 576px) {
      .tm-attention-row {
        min-block-size: 44px;
      }
    }
  `,
})
export class ɵTmAttentionRowStyles {}
