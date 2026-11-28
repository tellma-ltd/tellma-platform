// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  Injector,
  input,
  linkedSignal,
  model,
  output,
  signal,
  type Signal,
} from '@angular/core';
import { TmEntities, TmLaunchCache, TmLocalization, TmPermissions } from '@tellma/core';
import type { TmControlSize } from '@tellma/core-ui';
import type { TmDragPayload } from '@tellma/core-ui/drag-drop';
import { TmIcon, type TmIconSource } from '@tellma/core-ui/icon';
import { TmSearchField } from '@tellma/core-ui/search-field';
import { TmTree, TmTreeNode } from '@tellma/core-ui/tree';
import Calendar from 'lucide/dist/esm/icons/calendar.mjs';
import Hash from 'lucide/dist/esm/icons/hash.mjs';
import Image from 'lucide/dist/esm/icons/image.mjs';
import Languages from 'lucide/dist/esm/icons/languages.mjs';
import Link2 from 'lucide/dist/esm/icons/link-2.mjs';
import List from 'lucide/dist/esm/icons/list.mjs';
import Paperclip from 'lucide/dist/esm/icons/paperclip.mjs';
import ToggleLeft from 'lucide/dist/esm/icons/toggle-left.mjs';
import Type from 'lucide/dist/esm/icons/type.mjs';

import { ɵTM_PATH_PICKER_STRINGS_EN } from './tm-path-picker-strings';
import { ɵTmPathTree, type TmPathNode } from './tm-path-tree';

// The path tree picker (spec 0029 §11): a root entity's fields on `tm-tree`,
// navigations as branches, built from the local descriptors so a branch
// never waits on a request. A search field above filters the tree three
// navigation steps deep; each field drags as a `tm.path` payload whose text
// is always valid Queryex, and Enter or a double-click activates it, the
// path that needs no drag.

/** The glyph of each value type; any other shows as text. */
const TYPE_ICONS: Readonly<Partial<Record<string, TmIconSource>>> = {
  integer: Hash,
  decimal: Hash,
  float: Hash,
  string: Type,
  enum: List,
  bool: ToggleLeft,
  date: Calendar,
  datetime: Calendar,
  instant: Calendar,
};

/** The glyph of each kind of node that is not a scalar. */
const KIND_ICONS: Readonly<Record<'text' | 'navigation' | 'image' | 'file', TmIconSource>> = {
  text: Languages,
  navigation: Link2,
  image: Image,
  file: Paperclip,
};

/** The drag payload kind of a field. */
const PATH_KIND = 'tm.path';

/**
 * Lists a root entity's fields as a tree, navigations as branches, for a
 * column chooser, a filter or a report's fields. In `columns` mode the keys
 * are column keys and each multilingual group branches into its twins; in
 * `fields` mode they are Queryex paths, image and file navigations branch
 * into their blobs, and with `dateParts` each date field branches into its
 * parts in the active calendar. Fields the user cannot read never show.
 *
 * The search field filters by label and name, three navigation steps deep,
 * showing at most 200 matches inside their muted ancestors. A field drags,
 * when `draggable`, as a `tm.path` payload whose text is valid Queryex: the
 * path, a navigation's foreign-key path, or a date part's expression.
 *
 * @example
 * ```html
 * <tm-path-picker root="acme.SalesInvoiceLine" mode="fields" draggable dateParts
 *   ariaLabel="Fields" (activate)="filter.insertAtCaret(picker.text($event))" #picker />
 * ```
 *
 * @tmA11yNotes The APG tree view of `tm-tree`, after a search field.
 *   Enter, or a double-click on a leaf, activates a field: the non-drag
 *   path for every drop. In `multiple` selection, Enter and the checkbox
 *   toggle the field instead. A polite status reads each search's count.
 */
@Component({
  selector: 'tm-path-picker',
  imports: [TmIcon, TmSearchField, TmTree, TmTreeNode],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './tm-path-picker.css',
  host: { class: 'tm-path-picker' },
  template: `
    <!-- The search field, which filters the tree as it is typed. -->
    <tm-search-field class="tm-path-picker__search" [(value)]="query" [ariaLabel]="searchLabel()" [placeholder]="searchLabel()" [size]="size()" />

    <!-- The fields. -->
    <tm-tree
      class="tm-path-picker__tree"
      [nodes]="roots()"
      [nodeKey]="keyOf"
      [label]="labelOf"
      [children]="children()"
      [selection]="selection()"
      [selected]="selected()"
      (selectedChange)="select($event)"
      [expanded]="expanded()"
      (expandedChange)="setExpanded($event)"
      [context]="contextOf()"
      [dragData]="dragData()"
      [ariaLabel]="ariaLabel() ?? fieldsLabel()"
      [size]="size()"
      (activate)="onActivate($event)"
    >
      <ng-template tmTreeNode [tmTreeNodeOf]="roots()" let-node>
        <span class="tm-path-picker__row" [attr.data-kind]="node.kind">
          <tm-icon class="tm-path-picker__icon" [svg]="iconOf(node)" />
          <span class="tm-path-picker__label">{{ node.label() }}</span>
          @if (node.kind === 'navigation' && node.target) {
            <span class="tm-path-picker__pill">{{ targetLabel(node.target)() }}</span>
          }
        </span>
      </ng-template>
    </tm-tree>

    <!-- The search's count, read politely. -->
    <span class="tm-path-picker__status" role="status">{{ announcement() }}</span>
  `,
})
export class TmPathPicker {
  readonly #injector = inject(Injector);
  readonly #entities = inject(TmEntities);
  readonly #permissions = inject(TmPermissions);
  readonly #l10n = inject(TmLocalization);

  /** The entity whose fields show, such as `acme.SalesInvoiceLine`. */
  readonly root = input.required<string>();
  /** Column keys (`columns`) or Queryex paths (`fields`); `fields` when omitted. */
  readonly mode = input<'columns' | 'fields'>('fields');
  /** Whether fields are selected, one or many; `none` when omitted. */
  readonly selection = input<'none' | 'single' | 'multiple'>('none');
  /** The selected fields' keys. */
  readonly selected = model<ReadonlySet<string>>(new Set());
  /** Whether fields drag as `tm.path` payloads; they do not when omitted. */
  readonly draggable = input(false, { transform: booleanAttribute });
  /** In `fields` mode, whether date fields branch into their parts; they do not when omitted. */
  readonly dateParts = input(false, { transform: booleanAttribute });
  /** Which fields the consumer takes, such as only numeric ones for a measure; every one when omitted. */
  readonly accept = input<((node: TmPathNode) => boolean) | undefined>(undefined);
  /** The search field's text. */
  readonly query = model('');
  /** The tree's accessible name; "Fields" when omitted. */
  readonly ariaLabel = input<string | undefined>(undefined);
  /** The size step of the search field and the rows. */
  readonly size = input<TmControlSize | undefined>(undefined);
  /** Emits a field on Enter, or on a double-click on a leaf; never in `multiple` selection. */
  readonly activate = output<TmPathNode>();

  /** A picker string. */
  readonly #text = (key: string, params?: Readonly<Record<string, unknown>>): Signal<string> => this.#l10n.ɵtext(`tm.pathPicker.${key}`, params);

  /** The search field's name and placeholder. */
  protected readonly searchLabel = computed(() => this.#text('search')());
  /** The tree's default name. */
  protected readonly fieldsLabel = computed(() => this.#text('fields')());

  /**
   * The tree, rebuilt whenever what it is built from changes: the root,
   * the mode, the inputs that shape it, the tenant's languages, the
   * user's access, the calendar and the UI language.
   */
  readonly #tree = computed(() => {
    if (!this.#entities.loaded()) {
      return null;
    }
    const root = this.root();
    if (this.#entities.summary(root) === undefined) {
      return null;
    }
    // The shape and the access, which the tree reads when it builds.
    this.#entities.get(root);
    this.#injector.get(TmLaunchCache).docs.me();
    return new ɵTmPathTree({
      entities: this.#entities,
      permissions: this.#permissions,
      text: this.#text,
      root,
      mode: this.mode(),
      dateParts: this.dateParts(),
      accept: this.accept(),
      calendar: this.#l10n.calendarCode(),
      language: this.#l10n.language(),
    });
  });

  /** The search's result, or `null` without a query. */
  readonly #search = computed(() => this.#tree()?.search(this.query()) ?? null);

  /** The roots the tree shows: every field, or the search's. */
  protected readonly roots = computed<readonly TmPathNode[]>(() => {
    const search = this.#search();
    return search !== null ? search.roots : (this.#tree()?.roots() ?? []);
  });

  /** Each node's children: the search's kept ones, else every one. */
  protected readonly children = computed(() => {
    const tree = this.#tree();
    const search = this.#search();
    return (node: TmPathNode): readonly TmPathNode[] | null => search?.children.get(node.key) ?? tree?.children(node) ?? null;
  });

  /** The expanded keys: the search's ancestors while it runs, the reader's own otherwise. */
  protected readonly expanded = linkedSignal<ReadonlySet<string>>(() => this.#search()?.expanded ?? this.#browsed());
  /** The keys the reader expanded outside a search. */
  readonly #browsed = signal<ReadonlySet<string>>(new Set());

  /** Which rows are muted ancestors of a search's matches. */
  protected readonly contextOf = computed(() => {
    const context = this.#search()?.context;
    return context === undefined ? undefined : (node: TmPathNode): boolean => context.has(node.key);
  });

  /** Each accepted field's drag payload, when the fields drag. */
  protected readonly dragData = computed(() => {
    const tree = this.#tree();
    if (!this.draggable() || tree === null) {
      return undefined;
    }
    return (node: TmPathNode): TmDragPayload | null =>
      tree.accepts(node) ? { kind: PATH_KIND, data: node, text: tree.text(node), label: node.pathLabel() } : null;
  });

  /** The search's count, read politely; nothing without a query. */
  protected readonly announcement = computed(() => {
    const search = this.#search();
    if (search === null) {
      return '';
    }
    return search.count === 0 ? this.#text('none')() : this.#text('count', { count: search.count })();
  });

  /** A node's key. */
  protected readonly keyOf = (node: TmPathNode): string => node.key;

  /** A node's label. */
  protected readonly labelOf = (node: TmPathNode): string => node.label();

  constructor() {
    this.#l10n.ɵregisterStrings('pathPicker', ɵTM_PATH_PICKER_STRINGS_EN);
  }

  /**
   * The Queryex text a field stands for, as its drag carries it: the
   * path, a navigation's foreign-key path, or a date part's expression.
   *
   * @param node - The field.
   * @returns The text.
   */
  text(node: TmPathNode): string {
    return this.#tree()?.text(node) ?? node.key;
  }

  /**
   * A node's glyph.
   *
   * @param node - The node.
   * @returns The glyph.
   */
  protected iconOf(node: TmPathNode): TmIconSource {
    return node.kind === 'value' ? (TYPE_ICONS[node.type ?? ''] ?? Type) : KIND_ICONS[node.kind];
  }

  /**
   * A navigation target's label, for the row's pill.
   *
   * @param target - The target entity.
   * @returns Its label.
   */
  protected targetLabel(target: string): Signal<string> {
    return this.#entities.label(target);
  }

  /**
   * Keeps the reader's expansion outside a search.
   *
   * @param keys - The expanded keys.
   */
  protected setExpanded(keys: ReadonlySet<string>): void {
    this.expanded.set(keys);
    if (this.#search() === null) {
      this.#browsed.set(keys);
    }
  }

  /**
   * Takes a selection, dropping the fields the consumer refuses.
   *
   * @param keys - The selected keys.
   */
  protected select(keys: ReadonlySet<string>): void {
    const tree = this.#tree();
    const refused = tree === null ? [] : [...keys].filter((key) => !tree.accepts(tree.node(key)));
    this.selected.set(refused.length === 0 ? keys : new Set([...keys].filter((key) => !refused.includes(key))));
  }

  /**
   * Emits an activated field the consumer takes.
   *
   * @param node - The field.
   */
  protected onActivate(node: TmPathNode): void {
    if (this.#tree()?.accepts(node) ?? false) {
      this.activate.emit(node);
    }
  }
}
