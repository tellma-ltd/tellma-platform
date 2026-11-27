// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { afterEveryRender, DestroyRef, inject, type Signal, signal } from '@angular/core';
import { type TmJson, type TmPageAction, TmPageContexts, type TmVisibleData } from '@tellma/core';

import type { TmPanel } from './tm-attention';

// What the bell's and the tray's panels share inside the panels' chunk: the
// data the end drawer opens them with, the place bucket of an opened row,
// which the `tm.attention.item-opened` event reports, the overlay page
// context an open panel publishes (spec 0028 §13), and the rule that keeps
// focus inside an open panel when what held it leaves (spec 0028 §6.2).

/** What the panels' host opens a panel in the end drawer with. Internal. */
export interface TmAttentionPanelData {
  /** Whether the panel sits in the drawer, whose title stands in for the panel's heading. */
  readonly drawer: boolean;
}

/** What an open panel's page context shows; the rest is the same for both panels. Internal. */
export interface TmAttentionPanelContext {
  /** The panel's heading. */
  readonly title: Signal<string>;
  /** `loading` before any copy, else `ready`. */
  readonly status: Signal<'loading' | 'ready'>;
  /** The panel's counts and flags. */
  readonly state: Signal<TmJson>;
  /** The rows the panel shows. */
  readonly visible: Signal<TmVisibleData>;
}

/** The parameters of an action on one row: its id. */
export const TM_ROW_PARAMS = { type: 'object', properties: { id: { type: 'integer' } }, required: ['id'] } as const;

/**
 * The place bucket of an opened row.
 *
 * @param index - The row's index in its list, counting from 0.
 * @returns `1`, `2-3` or `4-10`.
 */
export function tmItemPosition(index: number): string {
  return index === 0 ? '1' : index < 3 ? '2-3' : '4-10';
}

/**
 * Publishes an open panel's overlay page context, `panel:bell` or
 * `panel:tray`, until the panel closes: its `attention.open-item` action
 * first, then the panel's others. Call it in the panel's constructor.
 *
 * @param panel - The panel.
 * @param context - What the context shows.
 * @param open - Opens the row with an id, as choosing it does; `false` when the panel shows no such row.
 * @param more - The panel's other actions.
 */
export function tmPublishPanel(
  panel: TmPanel,
  context: TmAttentionPanelContext,
  open: (id: unknown) => boolean,
  more: readonly TmPageAction[] = [],
): void {
  const item: TmPageAction = {
    id: 'attention.open-item',
    labelKey: 'tm.attentionPanels.actions.openItem',
    group: 'menu',
    enabled: signal(true).asReadonly(),
    destructive: false,
    disabledReason: signal(null).asReadonly(),
    params: TM_ROW_PARAMS,
    invoke: (args) => {
      const { id } = args as { readonly id?: unknown };
      return Promise.resolve(
        open(id) ? { outcome: 'done' } : { outcome: 'failed', error: new Error(`The ${panel} panel shows no row ${String(id)} to open.`) },
      );
    },
  };
  inject(TmPageContexts).register(
    {
      kind: 'custom',
      routeTemplate: `panel:${panel}`,
      overlay: true,
      ...context,
      errors: signal([]).asReadonly(),
      actions: signal([item, ...more]).asReadonly(),
    },
    inject(DestroyRef),
  );
}

/**
 * Keeps focus inside an open panel when the element that held it leaves the
 * page, such as a verb that removes its own control, or a list that empties:
 * focus moves to the fallback, so the popover the panel sits in stays open.
 * It runs after the lists' own recovery, which moves focus to a neighbouring
 * row while the list stays. Call it in the panel's constructor.
 *
 * @param host - The panel's element.
 * @param fallback - Where focus goes: the panel's heading, else the panel.
 */
export function tmKeepPanelFocus(host: HTMLElement, fallback: () => HTMLElement): void {
  let held: Element | null = null;
  host.addEventListener('focusin', (event) => (held = event.target as Element));
  afterEveryRender({
    read: () => {
      const active = host.ownerDocument.activeElement;
      // Removing a focused element leaves focus on the body.
      if (held?.isConnected === false && (active === null || active === host.ownerDocument.body)) {
        held = null;
        fallback().focus();
      }
    },
  });
}
