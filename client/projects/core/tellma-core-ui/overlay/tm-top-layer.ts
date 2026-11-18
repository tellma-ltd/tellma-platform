// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The page-wide layers share the browser's top layer, where the popover
// shown last sits on top. Three services show one there: a modal's host
// (TmModal, through the CDK), the toast host and the busy overlay's host.
// Each re-shows its host to get back on top, so two of them reacting to
// the same change race, and the order they end in is whichever ran last.
// This module is the one place that order is decided: the modals are below,
// the hosts kept above them are re-shown in rank order (the toasts, then a
// blocking busy overlay), and a service that must yield to a modal hears
// when one opens or closes, in the same task, before the modal's content
// takes focus.

/** A host kept above the modals, and when it is. */
interface KeptLayer {
  /** The `popover="manual"` host. */
  readonly element: HTMLElement;
  /** Its place among the kept hosts: a higher rank ends above a lower one. */
  readonly rank: number;
  /** Whether it is kept above now; when false it is left where it is. */
  readonly above: () => boolean;
}

/** The hosts kept above the modals, lowest rank first. */
const kept: KeptLayer[] = [];

/** The open modals' top-layer hosts, oldest first. */
const modals: HTMLElement[] = [];

/** Who hears a modal open or close. */
const watchers = new Set<() => void>();

/**
 * Keeps `element`, a shown `popover="manual"` host, above every modal and
 * every host of a lower rank whenever `above()` is true: the toasts take
 * rank 1 and the busy overlay rank 2. Returns the idempotent release.
 *
 * Library wiring with no compatibility promise.
 */
export function ɵtmKeepAboveModals(element: HTMLElement, rank: number, above: () => boolean): () => void {
  const layer: KeptLayer = { element, rank, above };
  const index = kept.findIndex((candidate) => candidate.rank > rank);
  kept.splice(index === -1 ? kept.length : index, 0, layer);
  return () => {
    const at = kept.indexOf(layer);
    if (at !== -1) {
      kept.splice(at, 1);
    }
  };
}

/**
 * Re-shows every kept host whose `above()` holds, lowest rank first, so the
 * highest ends on top; focus stays where it was. Called after anything
 * reaches the top layer.
 *
 * Library wiring with no compatibility promise.
 */
export function ɵtmRaiseTopLayer(): void {
  const active = document.activeElement;
  for (const layer of kept) {
    if (layer.above() && layer.element.matches(':popover-open')) {
      layer.element.hidePopover();
      layer.element.showPopover();
    }
  }
  // Hiding a popover that holds focus moves focus to the body: put it back.
  if (active instanceof HTMLElement && document.activeElement !== active && active.isConnected) {
    active.focus({ preventScroll: true });
  }
}

/**
 * Records a modal whose top-layer `host` was just shown, tells the
 * watchers at once, and returns the idempotent call that records it closed.
 *
 * Library wiring with no compatibility promise.
 */
export function ɵtmModalOpened(host: HTMLElement): () => void {
  modals.push(host);
  notify();
  let closed = false;
  return () => {
    if (closed) {
      return;
    }
    closed = true;
    const at = modals.indexOf(host);
    if (at !== -1) {
      modals.splice(at, 1);
    }
    notify();
  };
}

/**
 * The open modals' top-layer hosts, oldest first.
 *
 * Library wiring with no compatibility promise.
 */
export function ɵtmOpenModals(): readonly HTMLElement[] {
  return [...modals];
}

/**
 * Calls `watcher` in the same task whenever a modal opens or closes, and
 * returns the idempotent release.
 *
 * Library wiring with no compatibility promise.
 */
export function ɵtmWatchModals(watcher: () => void): () => void {
  const entry = (): void => watcher();
  watchers.add(entry);
  return () => {
    watchers.delete(entry);
  };
}

/** Tells every watcher that the open modals changed. */
function notify(): void {
  for (const watcher of [...watchers]) {
    watcher();
  }
}
