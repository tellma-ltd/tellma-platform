// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { NgTemplateOutlet } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  Injector,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { TmLocalization, TmNavigation, TmPreferences, tmPinsKey, type TmPin, type TmPinItem, type TmPinsDoc, TmUrls } from '@tellma/core';
import { TmButton } from '@tellma/core-ui/button';
import type { TmDragPayload } from '@tellma/core-ui/drag-drop';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TmIcon, TmIconRegistry } from '@tellma/core-ui/icon';
import { TmInput } from '@tellma/core-ui/input';
import { TmMenu, type TmMenuItem } from '@tellma/core-ui/menu';
import { TmModalFooter, TmModalRef } from '@tellma/core-ui/modal';
import { tmPushEscapeDismissal } from '@tellma/core-ui/overlay';
import { TmReorderEmpty, TmReorderItem, TmReorderList } from '@tellma/core-ui/reorder-list';
import { TmSwitch } from '@tellma/core-ui/switch';
import { TmTooltip } from '@tellma/core-ui/tooltip';
import ChevronDown from 'lucide/dist/esm/icons/chevron-down.mjs';
import Folder from 'lucide/dist/esm/icons/folder.mjs';
import FolderInput from 'lucide/dist/esm/icons/folder-input.mjs';
import Lock from 'lucide/dist/esm/icons/lock.mjs';
import Pencil from 'lucide/dist/esm/icons/pencil.mjs';
import Trash from 'lucide/dist/esm/icons/trash.mjs';

import {
  TM_PIN_LIMITS,
  tmNewPinId,
  tmPinFolderNameProblem,
  tmPinHref,
  tmPinLabel,
  tmPinsAddFolder,
  tmPinsMove,
  tmPinsOf,
  tmPinsRemove,
  tmPinsRemoveFolder,
  tmPinsRenameFolder,
  tmPinsUpdate,
  tmPinsView,
} from './tm-pin-rules';
import { TmPins, tmPinsVia } from './tm-pins';

// The Manage pins dialog (spec 0027 §8.10), a deferred chunk the shell's
// TmModal opens: a reorder list of the top-level pins and folders, each
// folder holding a reorder list of its pins, which takes pins dropped from
// the outer list and gives its own back; each row's icon, label, Rename (an
// inline field), Move to and Remove, a lock on a revoked pin; the switch that
// shows the pinned bar, which applies at once; and New folder, Cancel and
// Save. The dialog edits a copy: Save replaces the user's pins with it, and
// Cancel or Escape drop it. Its rows' icons are the launcher's names, which
// it registers when they are missing, and its strings are registered here,
// under `tm.managePins`.

/** The scope of the dialog's strings under `tm`. */
const SCOPE = 'managePins';

/** The dialog's English strings, under `tm.managePins`. */
export const TM_MANAGE_PINS_STRINGS_EN = {
  /** The list with no pins, and a folder with none. */
  empty: 'Nothing pinned yet.',
  emptyFolder: 'No pages in this folder',
  /** A folder's disclosure. */
  pages: 'Pages in {folder}',
  /** Each row's buttons. */
  rename: 'Rename {label}',
  moveTo: 'Move {label} to',
  remove: 'Remove {label}',
  /** The rename field's name. */
  name: 'Name',
  /** The Move to menu's top level. */
  noFolder: 'No folder',
  /** A revoked pin's line. */
  noAccess: 'No access',
  unavailable: 'Unavailable',
  /** New folder, the name it starts with, and why it is disabled. */
  newFolder: 'New folder',
  newFolderNamed: 'New folder {index}',
  foldersFull: 'You have {count} folders, the most a user can have.',
  /** The switch, and the footer's buttons. */
  showBar: 'Show the pinned bar',
  cancel: 'Cancel',
  save: 'Save',
  /** Why a name is refused. */
  blank: 'Enter a name.',
  long: 'Use 40 characters or fewer.',
  duplicate: 'A folder with this name exists',
  /** What Move to announces. */
  movedTo: 'Moved {label} to {folder}',
  movedOut: 'Moved {label} out of its folder',
} as const;

/** The drag kind of a pin row, which the top level and every folder's list take. */
const PIN_KIND = 'tm.pin';

/** The drag kind of a folder row, which only the top level takes. */
const FOLDER_KIND = 'tm.pin.folder';

/** The icon of a route kind with no entry's own. */
const KIND_ICONS: Readonly<Record<string, string>> = {
  search: 'list',
  list: 'list',
  details: 'file-text',
  report: 'chart-column',
  home: 'layout-dashboard',
  create: 'plus',
  core: 'settings',
};

/** What a pin's row shows: its label, whether it opens, and its icon. */
interface TmManagedPin {
  /** The pin's label, by the bar's rule. */
  readonly label: string;
  /** Whether the pin opens. */
  readonly state: 'ok' | 'no-access' | 'unavailable';
  /** Its icon's registered name. */
  readonly icon: string;
}

/**
 * The Manage pins dialog: the user's pins and folders to reorder, rename,
 * file and remove, saved together, and the pinned bar's switch.
 */
@Component({
  selector: 'tm-manage-pins-dialog',
  imports: [
    NgTemplateOutlet,
    TmButton,
    TmFormField,
    TmIcon,
    TmInput,
    TmMenu,
    TmModalFooter,
    TmReorderEmpty,
    TmReorderItem,
    TmReorderList,
    TmSwitch,
    TmTooltip,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The top level: pins and folders. -->
    <div class="tm-manage-pins__body">
      <tm-reorder-list
        [items]="doc().items"
        (itemsChange)="setItems(null, $event)"
        [itemKey]="key"
        [itemLabel]="label"
        [itemKind]="kind"
        [accepts]="topKinds"
        [ariaLabel]="title"
        (received)="receive(null, $event.payload, $event.index)"
      >
        <ng-template tmReorderItem let-item [tmReorderItemOf]="doc().items">
          @if (folderOf(item); as folder) {
            <!-- A folder: its name, its disclosure, Rename and Remove, then its own list. -->
            <div class="tm-manage-pins__row" [attr.data-tm-row]="folder.id">
              <button
                tmButton
                variant="ghost"
                size="sm"
                class="tm-manage-pins__icon-button"
                [attr.aria-expanded]="!collapsed().has(folder.id)"
                [attr.aria-label]="text('pages', { folder: folder.folder })"
                (click)="toggle(folder.id)"
              >
                <tm-icon [svg]="collapsed().has(folder.id) ? folderIcon : chevron" />
              </button>
              <ng-container *ngTemplateOutlet="name; context: { $implicit: folder.id, label: folder.folder }" />
              <ng-container *ngTemplateOutlet="actions; context: { $implicit: folder.id, label: folder.folder, folder: true }" />
            </div>
            @if (!collapsed().has(folder.id)) {
              <tm-reorder-list
                class="tm-manage-pins__folder"
                [items]="folder.items"
                (itemsChange)="setItems(folder.id, $event)"
                [itemKey]="key"
                [itemLabel]="label"
                [itemKind]="kind"
                [accepts]="folderKinds"
                [ariaLabel]="folder.folder"
                (received)="receive(folder.id, $event.payload, $event.index)"
              >
                <ng-template tmReorderItem let-pin [tmReorderItemOf]="folder.items">
                  <ng-container *ngTemplateOutlet="pinRow; context: { $implicit: pin }" />
                </ng-template>
                <ng-template tmReorderEmpty>
                  <p class="tm-manage-pins__empty">{{ text('emptyFolder') }}</p>
                </ng-template>
              </tm-reorder-list>
            }
          } @else {
            <ng-container *ngTemplateOutlet="pinRow; context: { $implicit: item }" />
          }
        </ng-template>
        <ng-template tmReorderEmpty>
          <p class="tm-manage-pins__empty">{{ text('empty') }}</p>
        </ng-template>
      </tm-reorder-list>
    </div>

    <!-- A pin's row: its icon, its label or the rename field, a lock when it does not open, and its buttons. -->
    <ng-template #pinRow let-pin>
      @let shown = managed().get(pin.id);
      <div class="tm-manage-pins__row" [attr.data-tm-row]="pin.id">
        <tm-icon class="tm-manage-pins__icon" [name]="shown?.icon ?? 'file'" />
        <ng-container *ngTemplateOutlet="name; context: { $implicit: pin.id, label: shown?.label ?? pin.title }" />
        @if (shown && shown.state !== 'ok') {
          <span class="tm-manage-pins__revoked"><tm-icon [svg]="lock" />{{ text(shown.state === 'no-access' ? 'noAccess' : 'unavailable') }}</span>
        }
        <ng-container *ngTemplateOutlet="actions; context: { $implicit: pin.id, label: shown?.label ?? pin.title, folder: false }" />
      </div>
    </ng-template>

    <!-- A row's label, or its rename field while it is renamed. -->
    <ng-template #name let-id let-label="label">
      @if (renaming() === id) {
        <tm-form-field class="tm-manage-pins__label" [error]="renameError()">
          <input
            #field
            tmInput
            [attr.aria-label]="text('name')"
            [(value)]="draft"
            (keydown.enter)="commitRename()"
            (blur)="commitRename(true)"
          />
        </tm-form-field>
      } @else {
        <span class="tm-manage-pins__label" dir="auto">{{ label }}</span>
      }
    </ng-template>

    <!-- A row's buttons: Rename, Move to (a pin's), Remove. -->
    <ng-template #actions let-id let-label="label" let-folder="folder">
      <button
        tmButton
        variant="ghost"
        size="sm"
        class="tm-manage-pins__icon-button"
        data-tm-rename
        [attr.aria-label]="text('rename', { label })"
        [tmTooltip]="text('rename', { label })"
        (click)="rename(id, $event)"
      >
        <tm-icon [svg]="pencil" />
      </button>
      @if (!folder) {
        <button
          #moveButton
          tmButton
          variant="ghost"
          size="sm"
          class="tm-manage-pins__icon-button"
          aria-haspopup="menu"
          [attr.aria-expanded]="moveOpenFor(moveButton)"
          [attr.aria-label]="text('moveTo', { label })"
          [tmTooltip]="text('moveTo', { label })"
          (click)="openMove(moveButton, id, label)"
        >
          <tm-icon [svg]="folderInput" />
        </button>
      }
      <button
        tmButton
        variant="ghost"
        size="sm"
        class="tm-manage-pins__icon-button"
        [attr.aria-label]="text('remove', { label })"
        [tmTooltip]="text('remove', { label })"
        (click)="remove(id, $event)"
      >
        <tm-icon [svg]="trash" />
      </button>
    </ng-template>

    <!-- The Move to menu, shared by the pins' buttons. -->
    <tm-menu #moveMenu [items]="moveItems()" [aria-label]="moveLabel()" />

    <!-- The bar's switch, which applies at once, above the footer. -->
    <tm-switch class="tm-manage-pins__switch" [checked]="visible()" (checkedChange)="pins.setVisible($event)">{{ text('showBar') }}</tm-switch>
    <div tmModalFooter>
      <button #newFolderButton tmButton variant="ghost" class="tm-manage-pins__new" [disabledReason]="foldersFull()" (click)="newFolder()">
        {{ text('newFolder') }}
      </button>
      <button tmButton variant="ghost" (click)="ref.close()">{{ text('cancel') }}</button>
      <button tmButton variant="primary" [disabledReason]="frozen()" (click)="save()">{{ text('save') }}</button>
    </div>
  `,
  styles: `
    /* A fixed height, which the shell's placeholder takes: only the list scrolls. */
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      block-size: 26rem;
    }

    .tm-manage-pins__body {
      flex: 1 1 0;
      min-block-size: 0;
      overflow: auto;
    }

    .tm-manage-pins__row {
      display: flex;
      flex: 1 1 auto;
      align-items: center;
      gap: var(--space-2);
      min-inline-size: 0;
    }

    .tm-manage-pins__label {
      flex: 1 1 auto;
      min-inline-size: 0;
      overflow: hidden;
      white-space: nowrap;
      text-overflow: ellipsis;
    }

    .tm-manage-pins__icon {
      flex: none;
      color: var(--text-muted);
    }

    .tm-manage-pins__icon-button {
      flex: none;
      inline-size: var(--tm-button-block-size);
      --tm-button-padding-x: 0;
    }

    .tm-manage-pins__revoked {
      display: inline-flex;
      flex: none;
      align-items: center;
      gap: var(--space-1);
      color: var(--text-muted);
      font-size: var(--text-xs);
      --icon-size: 12px;
    }

    .tm-manage-pins__folder {
      display: block;
      margin-inline-start: var(--space-6);
    }

    .tm-manage-pins__empty {
      margin: 0;
      padding: var(--space-2);
      color: var(--text-muted);
    }

    .tm-manage-pins__new {
      margin-inline-end: auto;
    }
  `,
})
export class TmManagePinsDialog {
  readonly #l10n = inject(TmLocalization);
  readonly #navigation = inject(TmNavigation);
  readonly #urls = inject(TmUrls);
  readonly #announcer = inject(LiveAnnouncer);
  readonly #injector = inject(Injector);
  readonly #changeDetector = inject(ChangeDetectorRef);
  readonly #host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  // Releases the rename field's Escape, which cancels the rename rather than closing the dialog.
  #releaseEscape: (() => void) | null = null;
  /** The Move to menu. */
  private readonly menu = viewChild.required<TmMenu>('moveMenu');
  /** New folder, which focus falls back to when the last row goes. */
  private readonly newFolderButton = viewChild.required<ElementRef<HTMLElement>>('newFolderButton');

  /** The user's pins, whose bar switch and Save the dialog uses. */
  protected readonly pins = inject(TmPins);

  /** The modal the dialog is in. */
  protected readonly ref = inject(TmModalRef);

  /** Why Save is disabled: a frozen tenant's reason, else `null`. */
  protected readonly frozen = this.pins.ɵfrozen;

  /** The rows' glyphs. */
  protected readonly lock = Lock;
  protected readonly pencil = Pencil;
  protected readonly trash = Trash;
  protected readonly folderInput = FolderInput;
  protected readonly folderIcon = Folder;
  protected readonly chevron = ChevronDown;

  /** The kinds the top level and a folder's list take. */
  protected readonly topKinds = [PIN_KIND, FOLDER_KIND];
  protected readonly folderKinds = [PIN_KIND];

  /** The lists' name: the dialog's title. */
  protected readonly title = this.#l10n.ɵtext('tm.pins.manage')();

  /** The copy the dialog edits: the pins as the dialog opened on them. */
  protected readonly doc = signal<TmPinsDoc>(tmPinsView(untracked(inject(TmPreferences).get(tmPinsKey))));

  /** Whether the bar shows. */
  protected readonly visible = this.pins.visible;

  /** The folders whose pins are hidden. */
  protected readonly collapsed = signal<ReadonlySet<string>>(new Set());

  /** The row being renamed, its draft and why the draft is refused. */
  protected readonly renaming = signal<string | null>(null);
  protected readonly draft = signal('');
  protected readonly renameError = signal('');

  /** The pin the Move to menu files. */
  protected readonly moving = signal<{ readonly id: string; readonly label: string } | null>(null);

  /** What each pin's row shows, by id. */
  protected readonly managed = computed(() => {
    const tenantId = this.#urls.tenantId() ?? 0;
    const entry = (href: string): string | undefined => this.#navigation.byHref(href)?.label;
    return new Map(
      tmPinsOf(this.doc().items).map((pin): [string, TmManagedPin] => {
        const href = tmPinHref(tenantId, pin.url);
        const path = href.split(/[?#]/, 1)[0];
        const match = this.#urls.describe(href);
        const icon = this.#navigation.byHref(path)?.icon ?? (match?.kind === 'page' ? match.page?.icon : undefined) ?? KIND_ICONS[match?.kind ?? ''] ?? 'file';
        return [pin.id, { label: tmPinLabel(pin, entry, tenantId), state: this.#navigation.resolve(href), icon }];
      }),
    );
  });

  /** Why New folder is disabled: the folders at their cap; `null` below it. */
  protected readonly foldersFull = computed(() =>
    this.doc().items.filter((item) => 'folder' in item).length >= TM_PIN_LIMITS.folders ? this.text('foldersFull', { count: TM_PIN_LIMITS.folders }) : null,
  );

  /** The Move to menu's name. */
  protected readonly moveLabel = computed(() => {
    const moving = this.moving();
    return moving === null ? null : this.text('moveTo', { label: moving.label });
  });

  /** The Move to menu's rows: the top level, then each folder; where the pin is, disabled. */
  protected readonly moveItems = computed((): readonly TmMenuItem[] => {
    const moving = this.moving();
    if (moving === null) {
      return [];
    }
    const items = this.doc().items;
    const top = items.some((item) => item.id === moving.id);
    return [
      { id: '', label: this.text('noFolder'), disabled: top, action: () => this.#moveTo(moving, null) },
      ...items.flatMap((item) =>
        'folder' in item
          ? [{ id: item.id, label: item.folder, disabled: item.items.some((pin) => pin.id === moving.id), action: () => this.#moveTo(moving, item.id) }]
          : [],
      ),
    ];
  });

  /** A row's key. */
  protected readonly key = (item: TmPinItem): string => item.id;

  /** A row's name: a folder's, else the pin's label. */
  protected readonly label = (item: TmPinItem): string => ('folder' in item ? item.folder : (this.managed().get(item.id)?.label ?? item.title));

  /** A row's drag kind: a folder stays at the top level, a pin goes into any list. */
  protected readonly kind = (item: TmPinItem): string => ('folder' in item ? FOLDER_KIND : PIN_KIND);

  /** Creates the dialog, which registers its strings and the icon names its rows draw. */
  constructor() {
    this.#l10n.ɵregisterStrings(SCOPE, TM_MANAGE_PINS_STRINGS_EN);
    const registry = inject(TmIconRegistry);
    void import('../launcher/tm-launcher-icons').then((chunk) => chunk.tmRegisterLauncherIcons(registry));
    inject(DestroyRef).onDestroy(() => this.#releaseEscape?.());
  }

  /**
   * A string of the dialog's scope.
   *
   * @param key - The key under `tm.managePins`.
   * @param params - The message's parameters.
   * @returns The text.
   */
  protected text(key: string, params?: Readonly<Record<string, unknown>>): string {
    return this.#l10n.ɵtext(`tm.${SCOPE}.${key}`, params)();
  }

  /**
   * A top-level row's folder, or `null` for a pin.
   *
   * @param item - The row.
   * @returns The folder.
   */
  protected folderOf(item: TmPinItem): Extract<TmPinItem, { readonly folder: string }> | null {
    return 'folder' in item ? item : null;
  }

  /**
   * A list's new order: the top level's, or a folder's.
   *
   * @param folderId - The folder, or `null` for the top level.
   * @param items - The list's items, in order.
   */
  protected setItems(folderId: string | null, items: readonly TmPinItem[]): void {
    this.doc.update((doc) => ({
      v: 1,
      items: folderId === null ? items : doc.items.map((item) => (item.id === folderId && 'folder' in item ? { ...item, items: items as readonly TmPin[] } : item)),
    }));
  }

  /**
   * A row dropped from another list: a pin into a folder, or out of one.
   *
   * @param folderId - The list it was dropped on: a folder, or `null` for the top level.
   * @param payload - The drag's payload, which carries the row.
   * @param index - Where it was dropped.
   */
  protected receive(folderId: string | null, payload: TmDragPayload, index: number): void {
    const item = payload.data as TmPinItem;
    if (payload.kind === PIN_KIND && tmPinsOf(untracked(this.doc).items).includes(item as TmPin)) {
      this.doc.update((doc) => tmPinsMove(doc, item.id, { folderId, index }));
    }
  }

  /**
   * Shows or hides a folder's pins.
   *
   * @param id - The folder.
   */
  protected toggle(id: string): void {
    this.collapsed.update((collapsed) => {
      const next = new Set(collapsed);
      if (!next.delete(id)) {
        next.add(id);
      }
      return next;
    });
  }

  // --- Rename ---

  /**
   * Rename: the row's label becomes a field holding it, selected.
   *
   * @param id - The pin or folder.
   * @param event - The click.
   */
  protected rename(id: string, event: Event): void {
    const row = (event.currentTarget as HTMLElement).closest('.tm-manage-pins__row');
    const item = this.#item(id);
    if (item === null) {
      return;
    }
    this.renaming.set(id);
    this.draft.set(this.label(item));
    this.renameError.set('');
    this.#releaseEscape ??= tmPushEscapeDismissal(() => this.#endRename(true));
    afterNextRender(() => row?.querySelector('input')?.select(), { injector: this.#injector });
  }

  /**
   * Enter, or the field's blur: a valid name is kept; a refused one shows
   * why, and on blur is dropped.
   *
   * @param blurred - Whether focus left the field.
   */
  protected commitRename(blurred = false): void {
    const id = untracked(this.renaming);
    const item = id === null ? null : this.#item(id);
    if (id === null || item === null) {
      return;
    }
    const draft = untracked(this.draft);
    const doc = untracked(this.doc);
    if ('folder' in item) {
      const problem = tmPinFolderNameProblem(doc.items, draft, id);
      if (problem !== null) {
        if (blurred) {
          this.#endRename(false);
        } else {
          this.renameError.set(this.text(problem));
        }
        return;
      }
      this.doc.set(tmPinsRenameFolder(doc, id, draft));
    } else if (draft !== this.label(item)) {
      // A blank name gives the pin its derived label back.
      if ([...draft.trim()].length > TM_PIN_LIMITS.label) {
        if (blurred) {
          this.#endRename(false);
        } else {
          this.renameError.set(this.text('long'));
        }
        return;
      }
      this.doc.set(tmPinsUpdate(doc, id, { label: draft }));
    }
    this.#endRename(!blurred);
  }

  /** Ends the rename; focus goes back to the row's Rename when it was in the field. */
  #endRename(refocus: boolean): void {
    const id = untracked(this.renaming);
    this.renaming.set(null);
    this.#releaseEscape?.();
    this.#releaseEscape = null;
    if (refocus && id !== null) {
      afterNextRender(() => this.#renameButton(id)?.focus(), { injector: this.#injector });
    }
  }

  // --- Move to, Remove, New folder ---

  /**
   * Whether the Move to menu is open from a pin's button.
   *
   * @param button - The pin's Move to button.
   * @returns Whether it is.
   */
  protected moveOpenFor(button: HTMLElement): boolean {
    return this.menu().ɵisOpenFor(button);
  }

  /**
   * Opens the Move to menu for a pin.
   *
   * @param button - The pin's Move to button.
   * @param id - The pin.
   * @param label - Its label.
   */
  protected openMove(button: HTMLElement, id: string, label: string): void {
    const menu = this.menu();
    if (menu.ɵisOpenFor(button)) {
      menu.close();
      return;
    }
    this.moving.set({ id, label });
    // The menu reads its rows as it opens: render them first.
    this.#changeDetector.detectChanges();
    menu.open(button, { restoreFocus: button });
  }

  /** Files a pin at the end of a folder, or of the top level, and announces it. */
  #moveTo(moving: { readonly id: string; readonly label: string }, folderId: string | null): void {
    const doc = untracked(this.doc);
    const folder = folderId === null ? null : (doc.items.find((item) => item.id === folderId) as Extract<TmPinItem, { readonly folder: string }>);
    this.doc.set(tmPinsUpdate(doc, moving.id, { folderId }));
    const message = folder === null ? this.text('movedOut', { label: moving.label }) : this.text('movedTo', { label: moving.label, folder: folder.folder });
    void this.#announcer.announce(message, 'polite');
  }

  /**
   * Removes a pin, or a folder whose pins take its place; focus goes to the
   * next row, else the previous one, else New folder.
   *
   * @param id - The pin or folder.
   * @param event - The click.
   */
  protected remove(id: string, event: Event): void {
    // The row's own list item: a folder's pin sits inside the folder's item too.
    const row = (event.currentTarget as HTMLElement).closest('li');
    const at = this.#handles().findIndex((handle) => handle.closest('li') === row);
    this.doc.update((doc) => (doc.items.some((item) => item.id === id && 'folder' in item) ? tmPinsRemoveFolder(doc, id) : tmPinsRemove(doc, id)));
    afterNextRender(
      () => {
        const now = this.#handles();
        (now[at] ?? now[at - 1] ?? this.newFolderButton().nativeElement).focus();
      },
      { injector: this.#injector },
    );
  }

  /** New folder: a folder at the end of the top level, named to rename at once. */
  protected newFolder(): void {
    const doc = untracked(this.doc);
    let index = 1;
    let name = this.text('newFolder');
    while (tmPinFolderNameProblem(doc.items, name) !== null) {
      name = this.text('newFolderNamed', { index: ++index });
    }
    const id = tmNewPinId(new Set([...doc.items.map((item) => item.id), ...tmPinsOf(doc.items).map((pin) => pin.id)]));
    this.doc.set(tmPinsAddFolder(doc, { id, name }));
    afterNextRender(() => this.#renameButton(id)?.click(), { injector: this.#injector });
  }

  /** Save: the copy replaces the user's pins, overwriting any change made meanwhile. */
  protected save(): void {
    tmPinsVia('dialog', () => this.pins.replace(untracked(this.doc)));
    this.ref.close();
  }

  /** A row's item by id: a top-level pin or folder, or a pin in a folder. */
  #item(id: string): TmPinItem | null {
    const items = untracked(this.doc).items;
    return items.find((item) => item.id === id) ?? tmPinsOf(items).find((pin) => pin.id === id) ?? null;
  }

  /** The reorder handles, in document order: one per row, folders' pins included. */
  #handles(): HTMLElement[] {
    return [...this.#host.querySelectorAll<HTMLElement>('.tm-reorder-list__handle')];
  }

  /** A row's Rename. */
  #renameButton(id: string): HTMLElement | null {
    return this.#host.querySelector<HTMLElement>(`[data-tm-row="${id}"] [data-tm-rename]`);
  }
}
