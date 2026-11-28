// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable } from '@angular/core';

import type { TmGridColumnWidths, TmGridContentState } from '@tellma/core-ui/contracts';

/**
 * Angular's development-mode flag. A production build defines it `false`,
 * which folds the development-mode branch away (`isDevMode()`, a call into
 * @angular/core, cannot be folded). Read through `typeof`, so code running
 * where nothing defines it never throws.
 */
declare const ngDevMode: unknown;

/** LRU capacity for per-content slices (scroll, selection, undo, expansion). */
const CONTENT_CAPACITY = 50;
/** LRU capacity for per-definition width slices. */
const WIDTHS_CAPACITY = 200;

class Lru<V> {
  private readonly map = new Map<string, V>();
  constructor(private readonly capacity: number) {}

  get(key: string): V | undefined {
    const value = this.map.get(key);
    if (value !== undefined) {
      // Touch: re-insertion moves the key to the back of the eviction order.
      this.map.delete(key);
      this.map.set(key, value);
    }
    return value;
  }

  set(key: string, value: V): void {
    this.map.delete(key);
    this.map.set(key, value);
    if (this.map.size > this.capacity) {
      const oldest = this.map.keys().next().value;
      if (oldest !== undefined) {
        this.map.delete(oldest);
      }
    }
  }

  delete(key: string): void {
    this.map.delete(key);
  }
}

function contentSliceKey(gridId: string, contentKey: string | number | undefined): string {
  // The NUL separator (never present in an authored id) isolates the gridId
  // from the content marker; the marker keeps the three contentKey domains —
  // undefined, string, number — disjoint, so an absent key and an empty string
  // address different slices.
  const marker = contentKey === undefined ? '~' : typeof contentKey === 'number' ? '#' : '$';
  return `${gridId}\u0000${marker}${contentKey ?? ''}`;
}

/**
 * A grid instance's session with the store: reads and writes the slices for
 * its `gridId` (widths) and `gridId` + `contentKey` (scroll, selection,
 * undo stack, expansion). Obtained from `TmGridStateStore.register` and
 * released on destroy.
 */
export class TmGridStateHandle {
  constructor(
    private readonly store: TmGridStateStore,
    /** The grid definition's stable identity. */
    readonly gridId: string,
    private contentKeyValue: string | number | undefined,
    private readonly onRelease: () => void,
  ) {}

  /** The current content identity. */
  get contentKey(): string | number | undefined {
    return this.contentKeyValue;
  }

  /** The persisted column widths for this grid definition. */
  getWidths(): TmGridColumnWidths | undefined {
    return this.store.ɵwidths.get(this.gridId);
  }

  /** Persists column widths (survives navigation and content switches). */
  setWidths(widths: TmGridColumnWidths): void {
    this.store.ɵwidths.set(this.gridId, widths);
  }

  /** The persisted per-content slice for the current content, if any. */
  getContentState(): TmGridContentState | undefined {
    return this.store.ɵcontent.get(contentSliceKey(this.gridId, this.contentKeyValue));
  }

  /** Persists the per-content slice for the current content. */
  setContentState(state: TmGridContentState): void {
    this.store.ɵcontent.set(contentSliceKey(this.gridId, this.contentKeyValue), state);
  }

  /**
   * Moves the handle to a new content identity: the caller snapshots the
   * outgoing content BEFORE switching and restores the incoming content's
   * slice (if any) after.
   */
  switchContent(contentKey: string | number | undefined): void {
    this.contentKeyValue = contentKey;
  }

  /** Drops the persisted undo stack for the current content (save/cancel). */
  clearHistory(): void {
    const current = this.getContentState();
    if (current?.history !== undefined) {
      this.setContentState({ ...current, history: undefined });
    }
  }

  /** Ends this registration (component destroy). */
  release(): void {
    this.onRelease();
  }
}

/**
 * The root-provided, in-memory grid state store: keeps column widths per
 * grid definition (`gridId`) and scroll/selection/undo/expansion per
 * content (`gridId` + `contentKey`) across component destroy/recreate.
 * Both sides are LRU-bounded so long sessions can't grow without limit —
 * an evicted slice simply means defaults on the next mount. The store
 * itself never touches browser storage; widths can be exported/imported as
 * a blob by a host application that wants to persist them.
 */
@Injectable({ providedIn: 'root' })
export class TmGridStateStore {
  /** Width slices by gridId. Store-internal. */
  readonly ɵwidths = new Lru<TmGridColumnWidths>(WIDTHS_CAPACITY);
  /** Per-content slices by gridId + contentKey. Store-internal. */
  readonly ɵcontent = new Lru<TmGridContentState>(CONTENT_CAPACITY);
  /** Live registration count per gridId — a refcount, not a flag. */
  private readonly live = new Map<string, number>();

  /**
   * Registers a live grid instance. A second LIVE registration under the
   * same `gridId` is a configuration error: it throws in dev mode and warns
   * in production (the instances then share the width slice and
   * last-writer-wins on per-content slices).
   */
  register(gridId: string, contentKey: string | number | undefined): TmGridStateHandle {
    const liveCount = this.live.get(gridId) ?? 0;
    if (liveCount > 0) {
      const message =
        `tm-grid: a grid with gridId "${gridId}" is already live. ` +
        'Grid ids identify one grid definition; give each concurrent grid its own id.';
      if (typeof ngDevMode !== 'undefined' && ngDevMode) {
        throw new Error(message);
      }
      console.warn(message);
    }
    this.live.set(gridId, liveCount + 1);
    return new TmGridStateHandle(this, gridId, contentKey, () => this.releaseLive(gridId));
  }

  /** Drops one live registration of `gridId`, forgetting it at zero. */
  private releaseLive(gridId: string): void {
    const liveCount = this.live.get(gridId) ?? 0;
    if (liveCount <= 1) {
      this.live.delete(gridId);
    } else {
      this.live.set(gridId, liveCount - 1);
    }
  }

  /** Exports a grid definition's widths as an opaque blob (user settings). */
  serializeWidths(gridId: string): string | null {
    const widths = this.ɵwidths.get(gridId);
    return widths === undefined ? null : JSON.stringify(widths);
  }

  /** Imports a widths blob produced by {@link serializeWidths}. */
  restoreWidths(gridId: string, blob: string): void {
    try {
      const parsed: unknown = JSON.parse(blob);
      if (parsed !== null && typeof parsed === 'object' && !Array.isArray(parsed)) {
        const widths: Record<string, number> = {};
        for (const [key, value] of Object.entries(parsed)) {
          // A non-positive or non-finite width would poison the whole
          // grid-template track list, so drop it rather than trust the blob.
          if (typeof value === 'number' && Number.isFinite(value) && value > 0) {
            widths[key] = value;
          }
        }
        this.ɵwidths.set(gridId, widths);
      }
    } catch {
      // A malformed blob restores nothing — defaults apply.
    }
  }
}
