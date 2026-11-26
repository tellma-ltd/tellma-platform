// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { NgTemplateOutlet } from '@angular/common';
import {
  afterNextRender,
  Component,
  computed,
  contentChild,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  model,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import GripVertical from 'lucide/dist/esm/icons/grip-vertical.mjs';

import { TM_UI_TRANSLATE, type TmControlSize, ɵtmControlSize } from '@tellma/core-ui';
import {
  TmDragDrop,
  TmDragSource,
  TmDropTarget,
  type TmDragPayload,
  type TmDrop,
} from '@tellma/core-ui/drag-drop';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmMenu, type TmMenuItem } from '@tellma/core-ui/menu';
import { tmPushEscapeDismissal } from '@tellma/core-ui/overlay';

import { TmReorderEmpty, TmReorderItem, type TmReorderMove } from './tm-reorder-item';

/** Numbers each list, for its element ids and its own drag kind. */
let nextListId = 0;

/** A keyboard grab in progress: the item, where it started, and the order shown meanwhile. */
interface Grab<T> {
  /** The grabbed item's key. */
  readonly key: string;
  /** The grabbed item. */
  readonly item: T;
  /** Its position when grabbed. */
  readonly from: number;
  /** The items the grab started from; a change to `items` from outside ends the grab. */
  readonly base: readonly T[];
  /** The order shown while the item moves, written to `items` on drop. */
  readonly order: readonly T[];
}

/** Moves the item at `from` to `to` in a copy of `list`. */
function moved<T>(list: readonly T[], from: number, to: number): readonly T[] {
  const next = [...list];
  const [item] = next.splice(from, 1);
  next.splice(to, 0, item);
  return next;
}

/**
 * An ordered list the user rearranges by pointer or keyboard, which can
 * also take drops from other drag sources.
 *
 * Each row starts with a grab handle. By pointer, the whole row drags and
 * a 2 px accent line marks where it will land, so rows never move
 * mid-drag. By keyboard, Space or Enter on a handle picks the item up; Up
 * and Down move it one place, Home and End to the ends; Space, Enter or
 * Tab drops it, and Escape puts it back. Alt+Up and Alt+Down move an item
 * one place without picking it up. A click on a handle (a pointer, or a
 * screen reader's activation) opens a menu of Move up, Move down, Move to
 * top and Move to bottom. Focus stays on the item's handle as it moves,
 * and each step is announced.
 *
 * A move writes a new `items` array and emits `moved`. A drop of an
 * accepted external kind emits `received` with the insertion index; the
 * consumer inserts the item.
 *
 * @tmGroup collection
 * @tmA11yNotes A named list; each row's handle is a button named
 *   "Reorder {label}" and described by the keyboard instructions, one tab
 *   stop per handle. Picking up, each move, dropping and cancelling are
 *   announced. The handle's keys, and the move menu its click opens, are
 *   the non-drag paths for every reorder (WCAG 2.1.1 and 2.5.7).
 */
@Component({
  selector: 'tm-reorder-list',
  imports: [NgTemplateOutlet, TmDragSource, TmDropTarget, TmIcon, TmMenu],
  template: `
    <!-- The frame is the drop target: own rows and accepted external kinds.
         The insertion line is drawn over it, so a drag never moves a row. -->
    <div
      #frame
      class="tm-reorder-list__frame"
      tmDropTarget
      [accepts]="acceptedKinds()"
      (dropped)="onDropped($event)"
      (dragover)="onDragOver($event)"
    >
      <ul #list role="list" class="tm-reorder-list__list" [attr.aria-label]="ariaLabel()">
        @for (item of shown(); track keyOf(item); let index = $index) {
          @let key = keyOf(item);
          <li
            class="tm-reorder-list__row"
            [class.tm-reorder-list__row--grabbed]="grab()?.key === key"
            [class.tm-reorder-list__row--dragging]="draggingKey() === key"
            [attr.data-key]="key"
            [tmDragSource]="payloadOf(item)"
          >
            <!-- The handle: a native button with the grab model's keys,
                 whose click opens the move menu. -->
            <button
              type="button"
              class="tm-reorder-list__handle"
              aria-haspopup="menu"
              [attr.aria-expanded]="menu.isOpen() && menuKey() === key"
              [attr.aria-label]="handleLabel(item)"
              [attr.aria-describedby]="instructionsId"
              (click)="onHandleClick($event, key)"
              (keydown)="onHandleKeydown($event, item, key)"
              (keyup)="onHandleKeyup($event)"
              (focusout)="onHandleFocusOut($event, key)"
            >
              <tm-icon [svg]="gripGlyph" />
            </button>
            <div class="tm-reorder-list__content">
              @if (itemTemplate(); as custom) {
                <ng-container
                  [ngTemplateOutlet]="custom.template"
                  [ngTemplateOutletContext]="{ $implicit: item, index }"
                />
              } @else {
                {{ labelOf(item) }}
              }
            </div>
          </li>
        }
      </ul>

      <!-- An empty list shows the consumer's empty state, and stays the drop target. -->
      @if (shown().length === 0) {
        @if (emptyTemplate(); as empty) {
          <div class="tm-reorder-list__empty">
            <ng-container [ngTemplateOutlet]="empty.template" />
          </div>
        }
      }

      <!-- The insertion line, while an accepted drag hovers a real move. -->
      @if (indicatorOffset(); as offset) {
        <div
          class="tm-reorder-list__indicator"
          aria-hidden="true"
          [style.inset-block-start.px]="offset.px"
        ></div>
      }
    </div>

    <!-- The handles' shared description. -->
    <span [id]="instructionsId" hidden>{{ instructions() }}</span>

    <!-- The move menu, shared by the handles: opened at the clicked one. -->
    <tm-menu #menu [items]="moveItems()" [aria-label]="moveMenuLabel()" />
  `,
  styleUrl: './tm-reorder-list.css',
  host: {
    class: 'tm-reorder-list',
    '[attr.data-size]': 'resolvedSize()',
  },
})
export class TmReorderList<T> {
  /** The library's string resolver. */
  private readonly translate = inject(TM_UI_TRANSLATE);

  /** Announces each keyboard step. */
  private readonly announcer = inject(LiveAnnouncer);

  /** The drag in flight. */
  private readonly dragDrop = inject(TmDragDrop);

  /** For refocusing a handle after the render that moved it. */
  private readonly injector = inject(Injector);

  /** The items, in order. A move writes a new array. */
  readonly items = model<readonly T[]>([]);

  /** An item's key, unique in the list. */
  readonly itemKey = input.required<(item: T) => string>();

  /** An item's name: the default row content, and the {label} of the handle's name and the announcements. */
  readonly itemLabel = input.required<(item: T) => string>();

  /** The external payload kinds the list accepts as drops. Default none. */
  readonly accepts = input<readonly string[]>([]);

  /** The list's accessible name. */
  readonly ariaLabel = input.required<string>();

  /** The rows' size step. Unset, the workspace default (`lg` on a phone). */
  readonly size = input<TmControlSize | undefined>(undefined);

  /** An item moved within the list, by pointer or keyboard. */
  readonly moved = output<TmReorderMove<T>>();

  /** An accepted external payload was dropped at `index`; the consumer inserts it. */
  readonly received = output<{ readonly payload: TmDragPayload; readonly index: number }>();

  /** A row template, `ng-template[tmReorderItem]`; without one, rows show `itemLabel`. */
  protected readonly itemTemplate = contentChild<TmReorderItem<T>>(TmReorderItem);

  /** The empty state, `ng-template[tmReorderEmpty]`. */
  protected readonly emptyTemplate = contentChild(TmReorderEmpty);

  /** The frame, against which the insertion line is placed. */
  private readonly frame = viewChild.required<ElementRef<HTMLElement>>('frame');

  /** The list element, whose children are the rows. */
  private readonly list = viewChild.required<ElementRef<HTMLElement>>('list');

  /** The drop target, which knows whether an accepted drag hovers. */
  private readonly dropTarget = viewChild.required(TmDropTarget);

  /** The size step the rows render at. */
  protected readonly resolvedSize = ɵtmControlSize(this.size);

  /** The handle's grip. */
  protected readonly gripGlyph = GripVertical;

  /** This list's number. */
  private readonly listId = nextListId++;

  /** The id of the handles' description. */
  protected readonly instructionsId = `tm-reorder-list-${this.listId}-instructions`;

  /** The drag kind of this list's own rows, which no other list accepts unless told to. */
  private readonly ownKind = `tm.reorder-list.${this.listId}`;

  /** The kinds the drop target accepts: the list's own rows, then `accepts`. */
  protected readonly acceptedKinds = computed(() => [this.ownKind, ...this.accepts()]);

  /** The keyboard instructions, in the active language. */
  protected readonly instructions = computed(() => this.translate('reorderList.instructions')());

  /** The keyboard grab in progress, or `null`. */
  protected readonly grab = signal<Grab<T> | null>(null);

  /** The order shown: a grab's preview, else `items`. */
  protected readonly shown = computed(() => this.grab()?.order ?? this.items());

  /**
   * The key of the row a pointer drag carries, or `null`: read from the
   * drag in flight, so a row re-created mid-drag, or a drag whose source
   * was destroyed, never leaves a row lifted.
   */
  protected readonly draggingKey = computed(() => {
    const payload = this.dragDrop.current();
    return payload !== null && payload.kind === this.ownKind ? this.keyOf(payload.data as T) : null;
  });

  /** The move menu. */
  private readonly moveMenu = viewChild.required(TmMenu);

  /** The key of the item the move menu acts on, or `null` before any click. */
  protected readonly menuKey = signal<string | null>(null);

  /** The move menu's name, "Move {label}". */
  protected readonly moveMenuLabel = computed(() => {
    const key = this.menuKey();
    const item = key === null ? undefined : this.items().find((entry) => this.keyOf(entry) === key);
    return item === undefined
      ? null
      : this.translate('reorderList.moveMenu', { label: this.labelOf(item) })();
  });

  /** The move menu's four moves for its item, each disabled where it would move nothing. */
  protected readonly moveItems = computed<readonly TmMenuItem[]>(() => {
    const key = this.menuKey();
    const items = this.items();
    const from = key === null ? -1 : items.findIndex((item) => this.keyOf(item) === key);
    const last = items.length - 1;
    const move = (id: string, labelKey: string, to: number, disabled: boolean): TmMenuItem => ({
      id,
      labelKey,
      disabled: from === -1 || disabled,
      action: () => {
        if (key !== null) {
          this.moveTo(key, to);
        }
      },
    });
    return [
      move('up', 'reorderList.moveUp', from - 1, from <= 0),
      move('down', 'reorderList.moveDown', from + 1, from >= last),
      move('top', 'reorderList.moveToTop', 0, from <= 0),
      move('bottom', 'reorderList.moveToBottom', last, from >= last),
    ];
  });

  /** Where a pointer drag would land, 0 to the item count. */
  private readonly insertIndex = signal(0);

  /** Where the insertion line would be drawn, in px from the frame's top. */
  private readonly boundary = signal(0);

  /** The insertion line's position, while an accepted drag hovers a drop that would change something. */
  protected readonly indicatorOffset = computed(() => {
    if (!this.dropTarget().over()) {
      return null;
    }
    const index = this.insertIndex();
    const own = this.ownIndex(this.dragDrop.current());
    // Dropping a row just before or just after itself moves nothing.
    if (own !== -1 && (index === own || index === own + 1)) {
      return null;
    }
    return { px: this.boundary() };
  });

  /** Releases the grab's Escape registration, or `null` while nothing is grabbed. */
  private releaseEscape: (() => void) | null = null;

  /** Set while a keyboard move re-renders the rows, so the handle's blur is not a drop. */
  private moving = false;

  /**
   * The key whose handle takes focus back after the pending re-render, or
   * `null` once focus has gone elsewhere on purpose meanwhile.
   */
  private refocusKey: string | null = null;

  constructor() {
    // A change to `items` from outside ends a grab quietly: the order it
    // previews no longer describes the list.
    effect(() => {
      const grab = this.grab();
      if (grab !== null && grab.base !== this.items()) {
        untracked(() => this.endGrab());
      }
    });
    inject(DestroyRef).onDestroy(() => this.releaseEscape?.());
  }

  // ---- Template helpers ----

  /** An item's key. */
  protected keyOf(item: T): string {
    return this.itemKey()(item);
  }

  /** An item's label. */
  protected labelOf(item: T): string {
    return this.itemLabel()(item);
  }

  /** A handle's name, "Reorder {label}". */
  protected handleLabel(item: T): string {
    return this.translate('reorderList.handle', { label: this.labelOf(item) })();
  }

  /** What a pointer drag of a row carries: the item, under the list's own kind. */
  protected payloadOf(item: T): TmDragPayload {
    const label = this.labelOf(item);
    return { kind: this.ownKind, data: item, text: label, label };
  }

  // ---- Pointer ----

  /** Follows a hovering drag: the index it would land at, and the line's offset. */
  protected onDragOver(event: DragEvent): void {
    const rows = Array.from(this.list().nativeElement.children) as HTMLElement[];
    const frameTop = this.frame().nativeElement.getBoundingClientRect().top;
    let index = rows.length;
    for (let i = 0; i < rows.length; i += 1) {
      const rect = rows[i].getBoundingClientRect();
      if (event.clientY < rect.top + rect.height / 2) {
        index = i;
        break;
      }
    }
    // The line sits midway between the rows it falls between, or against
    // the first or last row's outer edge.
    let boundary = 0;
    if (rows.length > 0) {
      const before = index > 0 ? rows[index - 1].getBoundingClientRect().bottom : null;
      const after = index < rows.length ? rows[index].getBoundingClientRect().top : null;
      boundary =
        (before !== null && after !== null ? (before + after) / 2 : (before ?? after ?? 0)) -
        frameTop;
    }
    if (index !== this.insertIndex()) {
      this.insertIndex.set(index);
    }
    if (boundary !== this.boundary()) {
      this.boundary.set(boundary);
    }
  }

  /** A drop: an own row moves; an external payload is handed to the consumer. */
  protected onDropped(drop: TmDrop): void {
    const index = this.insertIndex();
    const from = this.ownIndex(drop.payload);
    if (from === -1) {
      this.received.emit({ payload: drop.payload, index });
      return;
    }
    const to = index > from ? index - 1 : index;
    if (to !== from) {
      this.commitMove(from, to);
    }
  }

  /**
   * A click on a handle opens the move menu for its item. The handle's own
   * keys never click it (their defaults are prevented), so this is a
   * pointer's press or a screen reader's activation.
   */
  protected onHandleClick(event: MouseEvent, key: string): void {
    const handle = event.currentTarget as HTMLElement;
    // A grab in progress ends where it is shown, as any other way out of it.
    this.drop();
    if (this.items().length < 2) {
      return;
    }
    this.menuKey.set(key);
    // The menu reads its items when it opens: after the render that gives
    // it this item's moves.
    afterNextRender(() => this.moveMenu().open(handle, { restoreFocus: handle }), {
      injector: this.injector,
    });
  }

  // ---- Keyboard ----

  /** The grab model's keys on a handle. */
  protected onHandleKeydown(event: KeyboardEvent, item: T, key: string): void {
    const grab = this.grab();
    const plain = !event.altKey && !event.ctrlKey && !event.metaKey && !event.shiftKey;
    if (grab === null || grab.key !== key) {
      if (plain && (event.key === ' ' || event.key === 'Enter')) {
        event.preventDefault();
        this.startGrab(item, key);
      } else if (
        event.altKey &&
        !event.ctrlKey &&
        !event.metaKey &&
        (event.key === 'ArrowUp' || event.key === 'ArrowDown')
      ) {
        event.preventDefault();
        this.stepWithoutGrab(key, event.key === 'ArrowUp' ? -1 : 1);
      }
      return;
    }
    if (event.key === 'Tab') {
      // Tab drops and lets focus move on.
      this.drop();
      return;
    }
    if (!plain) {
      return;
    }
    const at = grab.order.indexOf(grab.item);
    const last = grab.order.length - 1;
    const target =
      event.key === 'ArrowUp'
        ? at - 1
        : event.key === 'ArrowDown'
          ? at + 1
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? last
              : null;
    if (target !== null) {
      event.preventDefault();
      this.preview(Math.min(last, Math.max(0, target)));
    } else if (event.key === ' ' || event.key === 'Enter') {
      event.preventDefault();
      this.drop();
    }
  }

  /** A Space released on a handle would click it: nothing should. */
  protected onHandleKeyup(event: KeyboardEvent): void {
    if (event.key === ' ') {
      event.preventDefault();
    }
  }

  /**
   * Focus leaving a grabbed handle drops the item, wherever focus goes
   * (its own row included), except for the blur of the list's own
   * re-render while the item moves.
   */
  protected onHandleFocusOut(event: FocusEvent, key: string): void {
    // A re-render's blur has no next element; focus sent to an element
    // (a Tab, a click) went there on purpose, and cancels a pending refocus.
    const sent = event.relatedTarget instanceof Element;
    if (sent && this.refocusKey === key) {
      this.refocusKey = null;
    }
    const grab = this.grab();
    if (grab !== null && grab.key === key && (sent || !this.moving)) {
      this.drop();
    }
  }

  // ---- Grab model ----

  /** Picks an item up. */
  private startGrab(item: T, key: string): void {
    const items = this.items();
    const from = items.indexOf(item);
    if (from === -1) {
      return;
    }
    this.grab.set({ key, item, from, base: items, order: items });
    this.releaseEscape ??= tmPushEscapeDismissal(() => this.cancel());
    this.announce('reorderList.grabbed', item, from, items.length);
  }

  /** Moves the grabbed item to `to` in the shown order. */
  private preview(to: number): void {
    const grab = this.grab();
    if (grab === null) {
      return;
    }
    const at = grab.order.indexOf(grab.item);
    if (to !== at) {
      this.moving = true;
      this.grab.set({ ...grab, order: moved(grab.order, at, to) });
      this.refocus(grab.key);
    }
    this.announce('reorderList.position', grab.item, to, grab.order.length);
  }

  /** Drops the grabbed item where it is shown. */
  private drop(): void {
    const grab = this.grab();
    if (grab === null) {
      return;
    }
    const to = grab.order.indexOf(grab.item);
    this.endGrab();
    if (to !== grab.from) {
      this.items.set(grab.order);
      this.moved.emit({ item: grab.item, from: grab.from, to });
    }
    this.announce('reorderList.dropped', grab.item, to, grab.order.length);
  }

  /** Escape: puts the grabbed item back. */
  private cancel(): void {
    const grab = this.grab();
    if (grab === null) {
      return;
    }
    const moving = grab.order !== grab.base;
    this.endGrab();
    if (moving) {
      this.moving = true;
      this.refocus(grab.key);
    }
    this.announce('reorderList.cancelled', grab.item, grab.from, grab.base.length);
  }

  /** Clears the grab and its Escape registration. */
  private endGrab(): void {
    this.grab.set(null);
    this.releaseEscape?.();
    this.releaseEscape = null;
  }

  /** Alt+Up or Alt+Down: moves an item one place at once. */
  private stepWithoutGrab(key: string, step: -1 | 1): void {
    const from = this.items().findIndex((item) => this.keyOf(item) === key);
    if (from !== -1) {
      this.moveTo(key, from + step);
    }
  }

  /**
   * Moves an item straight to `to`, without a grab: Alt+Arrow and the move
   * menu. Focus stays on its handle, and the move is announced.
   */
  private moveTo(key: string, to: number): void {
    const items = this.items();
    const from = items.findIndex((item) => this.keyOf(item) === key);
    if (from === -1 || to === from || to < 0 || to >= items.length) {
      return;
    }
    this.moving = true;
    this.commitMove(from, to);
    this.refocus(key);
    this.announce('reorderList.dropped', items[from], to, items.length);
  }

  /** Writes a move to `items` and reports it. */
  private commitMove(from: number, to: number): void {
    const items = this.items();
    this.items.set(moved(items, from, to));
    this.moved.emit({ item: items[from], from, to });
  }

  /**
   * After the render that moved a row, puts focus back on its handle: only
   * when the move lost it, never taking it from where a key (such as a Tab
   * pressed before the render) sent it since, even when the render then
   * moves that element too.
   */
  private refocus(key: string): void {
    this.refocusKey = key;
    afterNextRender(
      () => {
        this.moving = false;
        if (this.refocusKey !== key) {
          return;
        }
        this.refocusKey = null;
        const row = Array.from(this.list().nativeElement.children).find(
          (element) => element.getAttribute('data-key') === key,
        );
        if (row === undefined) {
          return;
        }
        // Moving a focused node drops focus to the body; focus anywhere
        // else outside the row went there on purpose and stays.
        const active = row.ownerDocument.activeElement;
        if (active === null || active === row.ownerDocument.body || row.contains(active)) {
          row.querySelector<HTMLElement>('.tm-reorder-list__handle')?.focus();
        }
      },
      { injector: this.injector },
    );
  }

  /** Announces a step: the label, a 1-based position, and the count. */
  private announce(key: string, item: T, index: number, count: number): void {
    const message = this.translate(key, {
      label: this.labelOf(item),
      index: index + 1,
      count,
    })();
    void this.announcer.announce(message, 'assertive');
  }

  /** The position of an own row a payload carries, or -1 for anything else. */
  private ownIndex(payload: TmDragPayload | null): number {
    if (payload === null || payload.kind !== this.ownKind) {
      return -1;
    }
    return this.items().indexOf(payload.data as T);
  }
}
