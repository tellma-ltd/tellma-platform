// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import { LiveAnnouncer } from '@angular/cdk/a11y';
import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
  ViewEncapsulation,
} from '@angular/core';
import { Router } from '@angular/router';
import { TmLocalization, TmTelemetry, TmUrls } from '@tellma/core';
import { TmButton } from '@tellma/core-ui/button';
import { TmDragDrop } from '@tellma/core-ui/drag-drop';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TmInput } from '@tellma/core-ui/input';
import { TmMenu, type TmMenuEntry, TmMenuHeaderDef, type TmMenuItem, type TmMenuLink } from '@tellma/core-ui/menu';
import { TmPopover, TmPopoverContent } from '@tellma/core-ui/popover';
import { TmOption, TmSelect } from '@tellma/core-ui/select';
import Lock from 'lucide/dist/esm/icons/lock.mjs';
import PinOff from 'lucide/dist/esm/icons/pin-off.mjs';

import { TM_PIN_LIMITS, tmPinFolderNameProblem } from './tm-pin-rules';
import { type TmPinFolderView, TmPins, tmPinsVia, type TmPinView } from './tm-pins';
import type { TmPinMenu, TmPinnedBarPort, TmPinOverlays } from './tm-pins-ui';

// The pinned bar's overlays (spec 0027 §8.5, §8.7 to §8.9, §8.12), the
// deferred chunk the bar's chips and pin control ask for:
//
// - one tm-menu, re-filled for the chip that opens it: More, with its
//   hidden pins as links, folders as submenus and revoked pins as locked
//   rows; a folder's pins; or a revoked pin's message and Unpin;
// - the pin popover, of size sm on the pin control: "Pinned" after a pin,
//   "Edit pin" on a pinned page, with the name, the folder (or a new one),
//   Remove and Done;
// - drag and drop along the bar: each chip drags as a `tm.pin`, a 2 px line
//   marks where it lands, and a pin held over a folder chip for 500 ms opens
//   it, so the pin can drop inside.
//
// The chunk loads at idle after the shell's first render; from then on
// every pin that opens loads its route's chunks at idle (spec 0027 §8.4).
//
// Its strings are registered here, under `tm.pinOverlays`. Every glyph is
// drawn from its own data, so it registers no icon name.

/** The scope of the overlays' strings under `tm`. */
const SCOPE = 'pinOverlays';

/** The overlays' English strings, under `tm.pinOverlays`. */
export const TM_PIN_OVERLAYS_STRINGS_EN = {
  /** The popover's title after a pin, and on a pinned page. */
  pinned: 'Pinned',
  edit: 'Edit pin',
  /** The popover's fields. */
  name: 'Name',
  folder: 'Folder',
  noFolder: 'No folder',
  newFolder: 'New folder…',
  folderName: 'Folder name',
  /** The popover's buttons. */
  remove: 'Remove',
  done: 'Done',
  /** Why a name is refused. */
  blank: 'Enter a name.',
  long: 'Use 40 characters or fewer.',
  duplicate: 'A folder with this name exists',
  /** An empty folder's menu. */
  emptyFolder: 'No pages in this folder',
  /** A revoked pin's menu: why it does not open, an unavailable one's, and its item. */
  noAccess: 'You no longer have access to this page.',
  unavailable: "This page isn't available.",
  unpin: 'Unpin',
  /** What a drop announces: into a folder, and along the bar. */
  movedTo: 'Moved {label} to {folder}',
  moved: 'Moved {label}, position {index} of {count}',
} as const;

/** The folder select's value of "New folder…", which no folder id can take. */
const NEW_FOLDER = '+';

/** How long a pin held over a folder chip waits before the folder opens, in milliseconds. */
const HOVER_OPEN_MS = 500;

/** The drag kind of the bar's chips. */
const PIN_KIND = 'tm.pin';

/** The drag in flight from the bar: the dragged item and the drag's id. */
interface TmBarDrag {
  /** The dragged pin or folder. */
  readonly item: TmPinView | TmPinFolderView;
  /** The drag's id, as the drag service knows it. */
  readonly id: string;
}

/** Where a drag over the bar would land: into a folder, or before an item's place. */
type TmBarDrop = { readonly folder: TmPinFolderView } | { readonly index: number };

/** A pin's or folder's name. */
function nameOf(item: TmPinView | TmPinFolderView): string {
  return 'folder' in item ? item.folder : untracked(item.label);
}

/**
 * The host of the pinned bar's overlays: its menu, its popover and its drag
 * and drop. The pins' controls create it once in the tenant shell.
 */
@Component({
  selector: 'tm-pin-overlays',
  imports: [TmButton, TmFormField, TmInput, TmMenu, TmMenuHeaderDef, TmOption, TmPopover, TmPopoverContent, TmSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // The popover's panel lives in the overlay container and is tm-popover's
  // markup, so its rules are unencapsulated; every selector is scoped by a
  // tm-pin-overlays class.
  encapsulation: ViewEncapsulation.None,
  // Out of the shell's flow: the menu and the popover open in the overlay container.
  host: { style: 'position: absolute' },
  template: `
    <!-- The chips' menu: More, a folder's pins, or a revoked pin's message and Unpin. -->
    <tm-menu #menu panelClass="tm-pin-overlays__menu" [items]="menuItems()" [aria-label]="menuLabel()" (closed)="opened.set(null)">
      @if (message(); as message) {
        <ng-template tmMenuHeader>{{ message }}</ng-template>
      }
    </tm-menu>
    <!-- The pin popover, on the pin control. -->
    <tm-popover #popover size="sm" align="end" panelClass="tm-pin-overlays__popover" [aria-label]="title()" (closed)="closed()">
      <ng-template tmPopoverContent>
        <form class="tm-pin-overlays__form" (submit)="apply($event)">
          <h2 class="tm-pin-overlays__title">{{ title() }}</h2>
          <tm-form-field [label]="text('name')" [error]="nameError()">
            <input tmInput [(value)]="name" (focus)="selectOnce($event)" />
          </tm-form-field>
          <tm-form-field [label]="text('folder')">
            <tm-select [(value)]="folder">
              <tm-option value="" [label]="text('noFolder')">{{ text('noFolder') }}</tm-option>
              @for (option of folders(); track option.id) {
                <tm-option [value]="option.id" [label]="option.folder">{{ option.folder }}</tm-option>
              }
              @if (folders().length < limits.folders) {
                <tm-option [value]="newFolder" [label]="text('newFolder')">{{ text('newFolder') }}</tm-option>
              }
            </tm-select>
          </tm-form-field>
          @if (folder() === newFolder) {
            <tm-form-field [label]="text('folderName')" [error]="folderError()">
              <input tmInput [(value)]="folderName" />
            </tm-form-field>
          }
          <div class="tm-pin-overlays__actions">
            <button tmButton variant="ghost" (click)="remove()">{{ text('remove') }}</button>
            <button tmButton variant="primary" type="submit">{{ text('done') }}</button>
          </div>
        </form>
      </ng-template>
    </tm-popover>
  `,
  styles: `
    .tm-pin-overlays__form {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }

    .tm-pin-overlays__title {
      margin: 0;
      font-size: var(--text-sm);
      font-weight: var(--weight-semibold);
    }

    .tm-pin-overlays__actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--space-2);
    }
  `,
})
export class TmPinOverlayHost implements TmPinOverlays {
  readonly #pins = inject(TmPins);
  readonly #l10n = inject(TmLocalization);
  readonly #router = inject(Router);
  readonly #telemetry = inject(TmTelemetry);
  readonly #announcer = inject(LiveAnnouncer);
  readonly #dragDrop = inject(TmDragDrop);
  readonly #document = inject(DOCUMENT);
  readonly #changeDetector = inject(ChangeDetectorRef);
  readonly #triggers = new Map<HTMLElement, () => void>();
  /** The chips' menu. */
  private readonly menu = viewChild.required<TmMenu>('menu');
  /** The pin popover. */
  private readonly popover = viewChild.required<TmPopover>('popover');
  // Whether the name field's text is still to be selected, as the popover opens.
  #select = false;

  /** The caps, which the folder select reads. */
  protected readonly limits = TM_PIN_LIMITS;

  /** The folder select's value of "New folder…". */
  protected readonly newFolder = NEW_FOLDER;

  /** The open menu and its anchor, or `null`. */
  protected readonly opened = signal<{ readonly menu: TmPinMenu; readonly anchor: HTMLElement } | null>(null);

  /** The pin the popover edits, its title's mode and its anchor, or `null`. */
  protected readonly editing = signal<{ readonly id: string; readonly mode: 'pinned' | 'edit'; readonly anchor: HTMLElement; readonly name: string } | null>(
    null,
  );

  /** The popover's fields: the name, the folder (`''` for none) and a new folder's name. */
  protected readonly name = signal('');
  protected readonly folder = signal('');
  protected readonly folderName = signal('');

  /** Why Done refused the name or the new folder's name, or `''`. */
  protected readonly nameError = signal('');
  protected readonly folderError = signal('');

  /** The folders, which the folder select lists. */
  protected readonly folders = computed(() => this.#pins.items().filter((item): item is TmPinFolderView => 'folder' in item));

  /** The popover's title. */
  protected readonly title = computed(() => this.text(this.editing()?.mode === 'pinned' ? 'pinned' : 'edit'));

  /** The open menu's pin when it is a revoked one's, or `null`. */
  readonly #revoked = computed(() => {
    const menu = this.opened()?.menu;
    return menu?.kind === 'revoked' ? this.#find(menu.id) : null;
  });

  /** A revoked pin's message, the menu's header. */
  protected readonly message = computed(() => {
    const state = this.#revoked()?.state();
    return state === undefined || state === 'ok' ? null : this.#message(state);
  });

  /** The open menu's name: More, the folder's name, or the pin's label. */
  protected readonly menuLabel = computed(() => {
    const menu = this.opened()?.menu;
    if (menu === undefined) {
      return null;
    }
    if (menu.kind === 'more') {
      return this.#l10n.ɵtext('tm.pins.more')();
    }
    const item = menu.kind === 'folder' ? this.#folder(menu.id) : this.#revoked();
    return item === null ? null : 'folder' in item ? item.folder : item.label();
  });

  /** The open menu's rows. */
  protected readonly menuItems = computed((): readonly TmMenuEntry[] => {
    const menu = this.opened()?.menu;
    if (menu === undefined) {
      return [];
    }
    if (menu.kind === 'more') {
      return menu
        .items()
        .map((item) => ('folder' in item ? { id: item.id, label: item.folder, items: this.#folderRows(item) } : this.#pinRow(item)));
    }
    if (menu.kind === 'folder') {
      const folder = this.#folder(menu.id);
      return folder === null ? [] : this.#folderRows(folder);
    }
    const pin = this.#revoked();
    return pin === null ? [] : [{ id: 'unpin', label: this.text('unpin'), icon: PinOff, action: () => this.#unpin(pin) }];
  });

  // The bar, while one is attached.
  #bar: TmPinnedBarPort | null = null;

  /** Creates the host, which registers its strings, preloads the pins' routes and releases its anchors at its end. */
  constructor() {
    this.#l10n.ɵregisterStrings(SCOPE, TM_PIN_OVERLAYS_STRINGS_EN);

    // --- Preloading: every pin that opens loads its route's chunks at idle, once ---
    const urls = inject(TmUrls);
    const preloaded = new Set<string>();
    effect(() => {
      for (const item of this.#pins.items()) {
        for (const pin of 'folder' in item ? item.items : [item]) {
          if (pin.state() === 'ok' && !preloaded.has(pin.href)) {
            preloaded.add(pin.href);
            untracked(() => urls.preload(pin.href));
          }
        }
      }
    });

    inject(DestroyRef).onDestroy(() => {
      this.#triggers.forEach((release) => release());
      this.closeAll();
    });
  }

  /**
   * A string of the overlays' scope.
   *
   * @param key - The key under `tm.pinOverlays`.
   * @param params - The message's parameters.
   * @returns The text.
   */
  protected text(key: string, params?: Readonly<Record<string, unknown>>): string {
    return this.#l10n.ɵtext(`tm.${SCOPE}.${key}`, params)();
  }

  /** {@inheritDoc TmPinOverlays.toggleMenu} */
  toggleMenu(anchor: HTMLElement, menu: TmPinMenu): void {
    const chips = this.menu();
    if (chips.ɵisOpenFor(anchor)) {
      chips.close();
      return;
    }
    this.#register(anchor, () => chips.ɵregisterTrigger(anchor));
    this.opened.set({ menu, anchor });
    // The menu reads its rows as it opens: render them first.
    this.#render();
    chips.open(anchor, { restoreFocus: anchor });
  }

  /** {@inheritDoc TmPinOverlays.openPopover} */
  openPopover(anchor: HTMLElement, id: string, mode: 'pinned' | 'edit'): void {
    const pin = this.#find(id);
    if (pin === null) {
      return;
    }
    const name = untracked(pin.label);
    this.#register(anchor, () => this.popover().ɵregisterTrigger(anchor));
    this.editing.set({ id, mode, anchor, name });
    this.name.set(name);
    this.folder.set(pin.folderId ?? '');
    this.folderName.set('');
    this.nameError.set('');
    this.folderError.set('');
    this.#select = true;
    this.popover().open(anchor);
  }

  /** {@inheritDoc TmPinOverlays.isOpenFor} */
  isOpenFor(anchor: HTMLElement): boolean {
    return this.menu().ɵisOpenFor(anchor) || (this.popover().isOpen() && this.editing()?.anchor === anchor);
  }

  /** {@inheritDoc TmPinOverlays.closeAll} */
  closeAll(): void {
    this.menu().close({ restoreFocus: false });
    this.popover().close();
  }

  /** {@inheritDoc TmPinOverlays.attachBar} */
  attachBar(bar: TmPinnedBarPort): () => void {
    this.#bar = bar;
    const stop = this.#dragAlong(bar);
    return () => {
      stop();
      if (this.#bar === bar) {
        this.#bar = null;
      }
    };
  }

  // --- The popover ---

  /**
   * The popover closed. Escape and Done give focus back to the pin control
   * themselves; an outside click on nothing focusable leaves it on the
   * page, so it goes back to the control too.
   */
  protected closed(): void {
    const anchor = untracked(this.editing)?.anchor;
    this.editing.set(null);
    const focused = this.#document.activeElement;
    if (anchor?.isConnected && (focused === null || focused === this.#document.body)) {
      anchor.focus();
    }
  }

  /**
   * Selects the name field's text the first time it takes focus after the popover opens.
   *
   * @param event - The focus.
   */
  protected selectOnce(event: FocusEvent): void {
    if (this.#select) {
      this.#select = false;
      (event.target as HTMLInputElement).select();
    }
  }

  /**
   * Done, or Enter in a field: applies the name and the folder, creating a
   * new folder first, then closes the popover, which gives focus back to
   * the pin control.
   *
   * @param event - The form's submit.
   */
  protected apply(event: Event): void {
    event.preventDefault();
    const editing = untracked(this.editing);
    const pin = editing === null ? null : this.#find(editing.id);
    if (editing === null || pin === null) {
      this.popover().close();
      return;
    }
    // An untouched name keeps the derived label, which follows the language.
    const name = untracked(this.name);
    const label = name === editing.name ? undefined : name;
    if (label !== undefined && [...label.trim()].length > TM_PIN_LIMITS.label) {
      this.nameError.set(this.text('long'));
      return;
    }
    const folder = untracked(this.folder);
    if (folder === NEW_FOLDER) {
      const problem = tmPinFolderNameProblem(untracked(this.#pins.items), untracked(this.folderName));
      if (problem !== null) {
        this.folderError.set(this.text(problem));
        return;
      }
    }
    tmPinsVia('popover', () => {
      const folderId = folder === NEW_FOLDER ? this.#pins.addFolder(untracked(this.folderName)) : folder === '' ? null : folder;
      this.#pins.update(pin.id, { label, folderId: folderId === pin.folderId ? undefined : folderId });
    });
    this.popover().close();
  }

  /** Remove: unpins the page, announces it and closes the popover, which gives focus back to the pin control. */
  protected remove(): void {
    const editing = untracked(this.editing);
    const pin = editing === null ? null : this.#find(editing.id);
    if (pin !== null) {
      const label = untracked(pin.label);
      tmPinsVia('popover', () => this.#pins.remove(pin.id));
      void this.#announcer.announce(this.#l10n.ɵtext('tm.pins.unpinned', { label })(), 'polite');
    }
    this.popover().close();
  }

  // --- The menu's rows ---

  /** A pin's row: a link while it opens, else a locked row that says why it does not. */
  #pinRow(pin: TmPinView): TmMenuItem | TmMenuLink {
    const label = pin.label();
    const state = pin.state();
    if (state !== 'ok') {
      return { id: pin.id, label, icon: Lock, disabledReason: this.#message(state), action: () => undefined };
    }
    return { id: pin.id, label, href: pin.href, current: pin.current(), action: () => this.#follow(pin.href) };
  }

  /** A folder's rows: its pins, or a line that says it has none. */
  #folderRows(folder: TmPinFolderView): readonly (TmMenuItem | TmMenuLink)[] {
    return folder.items.length === 0
      ? [{ id: 'empty', label: this.text('emptyFolder'), disabled: true, action: () => undefined }]
      : folder.items.map((pin) => this.#pinRow(pin));
  }

  /** A revoked pin's Unpin: focus goes on along the bar, and the removal is announced. */
  #unpin(pin: TmPinView): void {
    const label = untracked(pin.label);
    if (this.#bar !== null) {
      this.#bar.unpin(pin.id, 'menu', true);
    } else {
      tmPinsVia('menu', () => this.#pins.remove(pin.id));
    }
    void this.#announcer.announce(this.#l10n.ɵtext('tm.pins.unpinned', { label })(), 'polite');
  }

  /** Why a revoked pin does not open. */
  #message(state: 'no-access' | 'unavailable'): string {
    return this.text(state === 'no-access' ? 'noAccess' : 'unavailable');
  }

  /** Opens a pin from the menu, as a plain activation does. */
  #follow(href: string): void {
    this.#telemetry.event('tm.shell.navigated', { source: 'pin' }, { sampled: true });
    void this.#router.navigateByUrl(href);
  }

  /** A pin by id, at the top level or in a folder; `null` when it is gone. */
  #find(id: string): TmPinView | null {
    for (const item of untracked(this.#pins.items)) {
      const found = 'folder' in item ? item.items.find((pin) => pin.id === id) : item.id === id ? item : undefined;
      if (found !== undefined) {
        return found;
      }
    }
    return null;
  }

  /** A folder by id; `null` when it is gone. */
  #folder(id: string): TmPinFolderView | null {
    return this.folders().find((folder) => folder.id === id) ?? null;
  }

  /** Registers an anchor with its overlay once, so a press on it is the anchor's, not an outside press. */
  #register(anchor: HTMLElement, register: () => () => void): void {
    if (!this.#triggers.has(anchor)) {
      this.#triggers.set(anchor, register());
    }
  }

  /** Renders the host's view at once, as an open that reads its inputs needs. */
  #render(): void {
    this.#changeDetector.detectChanges();
  }

  // --- Drag and drop along the bar ---

  /**
   * Makes the bar's chips drag sources of kind `tm.pin`: a 2 px line marks
   * where the dragged chip lands, a pin held over a folder chip for 500 ms
   * opens it, and a drop moves the item and announces where it went.
   *
   * @returns Removes the listeners.
   */
  #dragAlong(bar: TmPinnedBarPort): () => void {
    const element = bar.element;
    const document = this.#document;
    const line = document.createElement('div');
    line.setAttribute('aria-hidden', 'true');
    line.style.cssText = 'position:fixed;z-index:1;inline-size:2px;block-size:20px;background:var(--accent);pointer-events:none;display:none';
    element.append(line);
    let drag: TmBarDrag | null = null;
    let target: TmBarDrop | null = null;
    let lit: HTMLElement | null = null;
    let timer: ReturnType<typeof setTimeout> | undefined;

    // The chip an event is on, More excepted.
    const chipOf = (node: EventTarget | null): HTMLElement | null => {
      const chip = node instanceof Element ? node.closest<HTMLElement>('[data-tm-pin]') : null;
      return chip !== null && chip.dataset['tmPin'] !== 'more' && element.contains(chip) ? chip : null;
    };
    const top = (): readonly (TmPinView | TmPinFolderView)[] => untracked(this.#pins.items);
    const light = (chip: HTMLElement | null): void => {
      if (lit !== chip) {
        lit?.style.removeProperty('outline');
        chip?.style.setProperty('outline', 'var(--focus-ring-width) solid var(--accent)');
        lit = chip;
        clearTimeout(timer);
        // A pin held over a folder opens it, so it can drop inside.
        if (chip !== null) {
          timer = setTimeout(() => {
            if (!this.menu().ɵisOpenFor(chip)) {
              this.toggleMenu(chip, { kind: 'folder', id: chip.dataset['tmPin']! });
            }
          }, HOVER_OPEN_MS);
        }
      }
    };
    const clear = (): void => {
      line.style.display = 'none';
      light(null);
      target = null;
    };
    // Whether a drag over the open folder menu drops into its folder.
    const menuFolder = (node: EventTarget | null): TmPinFolderView | null => {
      const menu = untracked(this.opened)?.menu;
      const inMenu = node instanceof Element && node.closest('.tm-pin-overlays__menu') !== null;
      return inMenu && menu?.kind === 'folder' && drag !== null && !('folder' in drag.item) ? this.#folder(menu.id) : null;
    };

    const listeners: [EventTarget, string, (event: DragEvent) => void][] = [
      [
        element,
        'dragstart',
        (event) => {
          const chip = chipOf(event.target);
          const item = top().find((entry) => entry.id === chip?.dataset['tmPin']);
          if (chip === null || item === undefined || event.dataTransfer === null) {
            return;
          }
          const text = 'folder' in item ? item.folder : new URL(item.href, document.baseURI).href;
          const id = this.#dragDrop.ɵbegin({ kind: PIN_KIND, data: item, text, label: nameOf(item) }, chip);
          drag = { item, id };
          event.dataTransfer.effectAllowed = 'move';
          event.dataTransfer.setData('text/plain', text);
        },
      ],
      [
        element,
        'dragover',
        (event) => {
          const chip = chipOf(event.target);
          if (drag === null || chip === null) {
            return;
          }
          event.preventDefault();
          event.dataTransfer!.dropEffect = 'move';
          const rect = chip.getBoundingClientRect();
          const id = chip.dataset['tmPin']!;
          const items = top();
          const over = items.find((item) => item.id === id);
          // A pin over the middle of a folder chip drops inside it.
          if (over !== undefined && 'folder' in over && !('folder' in drag.item) && Math.abs(event.clientX - (rect.left + rect.width / 2)) < rect.width / 4) {
            line.style.display = 'none';
            light(chip);
            target = { folder: over };
            return;
          }
          light(null);
          const rtl = this.#l10n.dir() === 'rtl';
          const after = event.clientX > rect.left + rect.width / 2 !== rtl;
          // The line sits in the bar's order, which below md leads with the
          // current item: the drop lands before the item that follows the
          // line there, at that item's stored place, else at the end.
          const order = bar.order().filter((entry) => entry !== drag!.item.id);
          const at = order.indexOf(id);
          const before = order[at + (after ? 1 : 0)];
          const rest = items.filter((item) => item !== drag!.item);
          target = at === -1 ? null : { index: before === undefined ? rest.length : rest.findIndex((item) => item.id === before) };
          line.style.display = target === null ? 'none' : 'block';
          line.style.left = `${(after !== rtl ? rect.right : rect.left) - 1}px`;
          line.style.top = `${rect.top + (rect.height - 20) / 2}px`;
        },
      ],
      [
        element,
        'dragleave',
        (event) => {
          if (!element.contains(event.relatedTarget as Node | null)) {
            clear();
          }
        },
      ],
      [
        element,
        'drop',
        (event) => {
          if (drag !== null && target !== null) {
            event.preventDefault();
            this.#drop(drag, target);
          }
          clear();
        },
      ],
      [
        element,
        'dragend',
        () => {
          if (drag !== null) {
            this.#dragDrop.ɵend(drag.id);
            drag = null;
          }
          clear();
        },
      ],
      // The folder menu a held pin opened takes the drop too.
      [
        document,
        'dragover',
        (event) => {
          if (menuFolder(event.target) !== null) {
            event.preventDefault();
          }
        },
      ],
      [
        document,
        'drop',
        (event) => {
          const folder = menuFolder(event.target);
          if (drag !== null && folder !== null) {
            event.preventDefault();
            this.#drop(drag, { folder });
            this.menu().close();
          }
        },
      ],
    ];
    for (const [on, type, listener] of listeners) {
      on.addEventListener(type, listener as EventListener);
    }
    return () => {
      clearTimeout(timer);
      line.remove();
      for (const [on, type, listener] of listeners) {
        on.removeEventListener(type, listener as EventListener);
      }
    };
  }

  /** Moves a dragged item where it was dropped, and announces where it went. */
  #drop(drag: TmBarDrag, drop: TmBarDrop): void {
    this.#dragDrop.ɵmarkDropped(drag.id);
    const label = nameOf(drag.item);
    if ('folder' in drop) {
      tmPinsVia('drag', () => this.#pins.move(drag.item.id, { folderId: drop.folder.id, index: drop.folder.items.length }));
      void this.#announcer.announce(this.text('movedTo', { label, folder: drop.folder.folder }), 'polite');
      return;
    }
    tmPinsVia('drag', () => this.#pins.move(drag.item.id, { folderId: null, index: drop.index }));
    const count = untracked(this.#pins.items).length;
    // The position the bar shows it at, which below md may differ from the stored one.
    const shown = this.#bar?.order().indexOf(drag.item.id) ?? -1;
    void this.#announcer.announce(this.text('moved', { label, index: (shown === -1 ? drop.index : shown) + 1, count }), 'polite');
  }
}
