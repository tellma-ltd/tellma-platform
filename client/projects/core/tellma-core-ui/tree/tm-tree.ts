// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { Tree, TreeItem, TreeItemGroup } from '@angular/aria/tree';
import { NgTemplateOutlet } from '@angular/common';
import {
  afterRenderEffect,
  Component,
  computed,
  contentChild,
  ElementRef,
  inject,
  input,
  isDevMode,
  model,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { Check, ChevronDown, ChevronRight } from 'lucide';

import { TM_UI_TRANSLATE, type TmControlSize, ɵtmControlSize } from '@tellma/core-ui';
import { TmDragSource, type TmDragPayload } from '@tellma/core-ui/drag-drop';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmSpinner } from '@tellma/core-ui/spinner';

import { ɵTmTreeClicks } from './internal/tm-tree-clicks';
import { TmTreeNode, type TmTreeNodeContext, type TmTreeNodeState } from './tm-tree-node';

/** The open-node count above which a dev build warns: the tree does not virtualize. */
const OPEN_NODE_WARNING = 500;

/** Numbers each tree's element ids. */
let nextTreeId = 0;

/**
 * What the tree knows about one node's children: none (a leaf), the list,
 * a load in flight, or a load that failed and is retried on the next
 * expansion.
 */
type ChildrenEntry<T> =
  | { readonly kind: 'leaf' }
  | { readonly kind: 'ready'; readonly children: readonly T[] }
  | { readonly kind: 'pending'; readonly node: T }
  | { readonly kind: 'failed'; readonly node: T };

/** The entry every leaf shares. */
const LEAF = { kind: 'leaf' } as const;

/** Whether a `children` result is a promise rather than a list. */
function isPromise<T>(value: readonly T[] | Promise<readonly T[]>): value is Promise<readonly T[]> {
  return typeof (value as { then?: unknown }).then === 'function';
}

/**
 * A tree whose children load on first show. It can be selectable and
 * draggable. Nodes are any objects: `nodeKey` names each one uniquely in
 * the tree, `label` gives its text, and `children` its children.
 *
 * `children(node)` runs once per node, when the node first shows, so a
 * leaf (`null` or an empty list) draws no twisty. A list shows at once; a
 * promise shows a spinner in the reserved slot beside the twisty while the
 * node is expanded and loading, then the children. Results are cached per
 * key until `nodes` or `children` changes identity. A load that fails
 * while its node is expanded collapses the node and announces "Couldn't
 * load {label}"; the next expansion tries again.
 *
 * A group renders only while its node is expanded, so a deep tree costs
 * only what is open. Nothing is virtualized: keep the open nodes to
 * hundreds (a dev build warns above 500).
 *
 * Selection (`single` or `multiple`) writes `selected`; expansion writes
 * `expanded`, both as sets of node keys. A click on a row's content
 * selects it in `single` and toggles it in `multiple`; the twisty expands
 * and collapses. Enter, or a double-click on a leaf, emits `activate`
 * (never in `multiple`, where Enter and Space toggle); a double-click on a
 * branch expands or collapses it.
 *
 * With `dragData`, each row's content is a `tmDragSource`: a drag never
 * changes selection or focus.
 *
 * @tmGroup collection
 * @tmA11yNotes The APG tree view: one tab stop. Up and Down move; Right
 *   expands, else moves to the first child; Left collapses, else moves to
 *   the parent (both mirrored in right-to-left text); Home and End; type to
 *   jump by label. Enter activates (and selects in `single`); Space selects
 *   in `single` and toggles in `multiple`. A loading node is `aria-busy`
 *   and its name ends with "Loading". Every drag has a path that does not
 *   drag: `activate`, or selection.
 */
@Component({
  selector: 'tm-tree',
  imports: [
    NgTemplateOutlet,
    TmDragSource,
    TmIcon,
    TmSpinner,
    Tree,
    TreeItem,
    TreeItemGroup,
    ɵTmTreeClicks,
  ],
  template: `
    <!-- The tree: aria's roving focus, typeahead, arrow keys and selection
         over the rows below; this component owns the clicks. -->
    <ul
      ngTree
      #tree="ngTree"
      class="tm-tree__root"
      selectionMode="explicit"
      [tmTreeClicks]="selection()"
      [multi]="selection() === 'multiple'"
      [wrap]="false"
      [value]="selectedKeys()"
      (valueChange)="onValueChange($event)"
      [attr.aria-label]="ariaLabel()"
      (keydown)="onKeydown($event)"
    >
      <ng-container
        [ngTemplateOutlet]="levelTemplate"
        [ngTemplateOutletContext]="{ $implicit: nodes(), parent: tree, level: 1 }"
      />
    </ul>

    <!-- The one "Loading" text every loading node's name ends with. -->
    <span [id]="loadingId" hidden>{{ loadingLabel() }}</span>

    <!-- One level of the tree, recursively: each node's row, then, for a
         branch, a group that aria renders only while the node is expanded. -->
    <ng-template #levelTemplate let-list let-parent="parent" let-level="level">
      @for (node of list; track keyOf(node)) {
        @let key = keyOf(node);
        @let entry = entryOf(node);
        @let open = expanded().has(key);
        @let loading = open && entry.kind === 'pending';
        <li
          ngTreeItem
          #item="ngTreeItem"
          class="tm-tree__item"
          [parent]="parent"
          [value]="key"
          [label]="labelOf(node)"
          [selectable]="selection() !== 'none'"
          [expanded]="open"
          (expandedChange)="setExpanded(key, $event)"
          [attr.data-key]="key"
          [attr.aria-labelledby]="loading ? contentId(key) + ' ' + loadingId : contentId(key)"
          [attr.aria-busy]="loading ? 'true' : null"
        >
          <!-- The visible row: indent, twisty, the reserved spinner slot,
               the multiple-selection glyph, then the content. The row is the
               drag source, so a nested item never starts a second drag. -->
          <div
            class="tm-tree__row"
            [class.tm-tree__row--context]="isContext(node)"
            [style.--tm-tree-level]="level"
            [tmDragSource]="dragPayload(node)"
            (dblclick)="onDoubleClick($event, node, key)"
          >
            <span class="tm-tree__twisty" aria-hidden="true">
              @if (entry.kind !== 'leaf') {
                <tm-icon [svg]="open ? expandedGlyph : collapsedGlyph" />
              }
            </span>
            @if (reservesSpinner()) {
              <span class="tm-tree__spinner-slot" aria-hidden="true">
                @if (loading) {
                  <tm-spinner />
                }
              </span>
            }
            @if (selection() === 'multiple') {
              <span class="tm-tree__check" aria-hidden="true">
                <tm-icon [svg]="checkGlyph" />
              </span>
            }
            <span class="tm-tree__content" [id]="contentId(key)">
              @if (nodeTemplate(); as custom) {
                <ng-container
                  [ngTemplateOutlet]="custom.template"
                  [ngTemplateOutletContext]="contextOf(node, key, level, entry, loading)"
                />
              } @else {
                {{ labelOf(node) }}
              }
            </span>
          </div>
          @if (entry.kind !== 'leaf') {
            <ul role="group" class="tm-tree__group">
              <ng-template ngTreeItemGroup [ownedBy]="item" #group="ngTreeItemGroup">
                <ng-container
                  [ngTemplateOutlet]="levelTemplate"
                  [ngTemplateOutletContext]="{
                    $implicit: entry.kind === 'ready' ? entry.children : [],
                    parent: group,
                    level: level + 1,
                  }"
                />
              </ng-template>
            </ul>
          }
        </li>
      }
    </ng-template>
  `,
  styleUrl: './tm-tree.css',
  host: {
    class: 'tm-tree',
    '[attr.data-size]': 'resolvedSize()',
    '[attr.data-selection]': 'selection()',
  },
})
export class TmTree<T> {
  /** The tree's host element. */
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  /** The library's string resolver. */
  private readonly translate = inject(TM_UI_TRANSLATE);

  /** Announces a failed load. */
  private readonly announcer = inject(LiveAnnouncer);

  /** The roots. Default none. */
  readonly nodes = input<readonly T[]>([]);

  /** A node's key, unique in the tree, such as `'Parent.Manager.Code'`. */
  readonly nodeKey = input.required<(node: T) => string>();

  /** A node's text: the default row content and the typeahead text. */
  readonly label = input.required<(node: T) => string>();

  /**
   * A node's children: a list, a promise of one, or `null` for a leaf (an
   * empty list is a leaf too). Unset, every node is a leaf.
   */
  readonly children = input<((node: T) => readonly T[] | Promise<readonly T[]> | null) | undefined>(
    undefined,
  );

  /** `none`, `single` or `multiple`. Default `none`. */
  readonly selection = input<'none' | 'single' | 'multiple'>('none');

  /** The selected nodes' keys. */
  readonly selected = model<ReadonlySet<string>>(new Set());

  /** The expanded nodes' keys. */
  readonly expanded = model<ReadonlySet<string>>(new Set());

  /** Whether a node is a context node, drawn muted around the nodes that matter. */
  readonly context = input<((node: T) => boolean) | undefined>(undefined);

  /** A node's drag payload, or `null` for a node that does not drag. Set, rows are drag sources. */
  readonly dragData = input<((node: T) => TmDragPayload | null) | undefined>(undefined);

  /** The tree's accessible name. */
  readonly ariaLabel = input.required<string>();

  /** The rows' size step. Unset, the workspace default (`lg` on a phone). */
  readonly size = input<TmControlSize | undefined>(undefined);

  /** Enter on a node, or a double-click on a leaf; never in `multiple`. */
  readonly activate = output<T>();

  /** A row template, `ng-template[tmTreeNode]`; without one, rows show `label`. */
  protected readonly nodeTemplate = contentChild<TmTreeNode<T>>(TmTreeNode);

  /** The aria tree. */
  private readonly ariaTree = viewChild.required<Tree<string>>('tree');

  /** The size step the rows render at. */
  protected readonly resolvedSize = ɵtmControlSize(this.size);

  /** The collapsed glyph, mirrored in right-to-left text so it points along the line. */
  protected readonly collapsedGlyph = { node: ChevronRight, mirrorInRtl: true };

  /** The expanded glyph. */
  protected readonly expandedGlyph = ChevronDown;

  /** The multiple-selection glyph's check, shown on a selected row. */
  protected readonly checkGlyph = Check;

  /** This tree's id prefix. */
  private readonly idPrefix = `tm-tree-${nextTreeId++}`;

  /** The id of the hidden "Loading" text. */
  protected readonly loadingId = `${this.idPrefix}-loading`;

  /** "Loading", in the active language. */
  protected readonly loadingLabel = computed(() => this.translate('tree.loading')());

  /** The selected keys as aria's value. */
  protected readonly selectedKeys = computed(() => [...this.selected()]);

  /** Whether the rows reserve the spinner slot: whenever children can load lazily. */
  protected readonly reservesSpinner = computed(() => this.children() !== undefined);

  /** Each node's children, by key. */
  private readonly cache = new Map<string, ChildrenEntry<T>>();

  /** Each rendered node, by key, for the events that name a row. */
  private readonly nodesByKey = new Map<string, T>();

  /** Each key's content id. */
  private readonly contentIds = new Map<string, string>();

  /** Each key's memoized row-template context, so an unchanged row keeps its context. */
  private readonly contexts = new Map<string, TmTreeNodeContext<T>>();

  /** The `nodes` and `children` the cache was filled for; either changing empties it. */
  private cachedFor: readonly [readonly T[], unknown] | null = null;

  /** Bumped by every cache change a render must see: a load settling, a retry. */
  private readonly cacheVersion = signal(0);

  /** Bumped with each emptying of the cache, so a load that settles afterwards is ignored. */
  private generation = 0;

  /** Whether the dev-mode open-node warning has fired. */
  private warned = false;

  constructor() {
    // Dev builds warn once when the open nodes pass what an unvirtualized
    // tree should hold.
    if (isDevMode()) {
      afterRenderEffect({
        read: () => {
          this.cacheVersion();
          this.expanded();
          this.nodes();
          if (this.warned) {
            return;
          }
          const open = this.host.querySelectorAll('[role="treeitem"]').length;
          if (open > OPEN_NODE_WARNING) {
            this.warned = true;
            console.warn(
              `tm-tree: ${open} nodes are open. The tree does not virtualize; keep open nodes under ${OPEN_NODE_WARNING}.`,
            );
          }
        },
      });
    }
  }

  // ---- Template helpers ----

  /** A node's key. */
  protected keyOf(node: T): string {
    return this.nodeKey()(node);
  }

  /** A node's label. */
  protected labelOf(node: T): string {
    return this.label()(node);
  }

  /** Whether a node is a context node. */
  protected isContext(node: T): boolean {
    return this.context()?.(node) ?? false;
  }

  /** A node's drag payload, or `null` when rows do not drag. */
  protected dragPayload(node: T): TmDragPayload | null {
    return this.dragData()?.(node) ?? null;
  }

  /** The id of a node's content, which names its row. */
  protected contentId(key: string): string {
    let id = this.contentIds.get(key);
    if (id === undefined) {
      id = `${this.idPrefix}-node-${this.contentIds.size}`;
      this.contentIds.set(key, id);
    }
    return id;
  }

  /**
   * What the tree knows about a node's children, loading them the first
   * time the node shows. Reading the cache version makes a render follow
   * each load that settles.
   */
  protected entryOf(node: T): ChildrenEntry<T> {
    this.cacheVersion();
    const nodes = this.nodes();
    const children = this.children();
    if (this.cachedFor === null || this.cachedFor[0] !== nodes || this.cachedFor[1] !== children) {
      this.cache.clear();
      this.nodesByKey.clear();
      this.contexts.clear();
      this.generation += 1;
      this.cachedFor = [nodes, children];
    }
    const key = this.keyOf(node);
    this.nodesByKey.set(key, node);
    let entry = this.cache.get(key);
    if (entry === undefined) {
      entry = this.load(node, key);
      this.cache.set(key, entry);
    }
    return entry;
  }

  /** A row template's context: the same object while nothing about the row changed. */
  protected contextOf(
    node: T,
    key: string,
    level: number,
    entry: ChildrenEntry<T>,
    loading: boolean,
  ): TmTreeNodeContext<T> {
    const state: TmTreeNodeState = {
      level,
      expanded: this.expanded().has(key),
      selected: this.selected().has(key),
      loading,
      leaf: entry.kind === 'leaf',
      context: this.isContext(node),
    };
    const previous = this.contexts.get(key);
    if (
      previous !== undefined &&
      previous.$implicit === node &&
      (Object.keys(state) as (keyof TmTreeNodeState)[]).every(
        (name) => previous.state[name] === state[name],
      )
    ) {
      return previous;
    }
    const context = { $implicit: node, state };
    this.contexts.set(key, context);
    return context;
  }

  // ---- Events ----

  /** aria wrote the selection. */
  protected onValueChange(keys: string[]): void {
    this.selected.set(new Set(keys));
  }

  /**
   * A node expanded or collapsed (aria, or a double-click). Expanding a
   * node whose load failed tries the load again.
   */
  protected setExpanded(key: string, open: boolean): void {
    const current = this.expanded();
    if (current.has(key) === open) {
      return;
    }
    const next = new Set(current);
    if (open) {
      next.add(key);
    } else {
      next.delete(key);
    }
    this.expanded.set(next);
    const entry = this.cache.get(key);
    if (open && entry?.kind === 'failed') {
      this.cache.set(key, this.load(entry.node, key));
      this.cacheVersion.update((version) => version + 1);
    }
  }

  /** Enter on a row emits `activate`, except in `multiple`, where it toggles. */
  protected onKeydown(event: KeyboardEvent): void {
    if (
      event.key !== 'Enter' ||
      event.altKey ||
      event.ctrlKey ||
      event.metaKey ||
      event.shiftKey ||
      this.selection() === 'multiple'
    ) {
      return;
    }
    const node = this.nodeOfRow(event.target);
    // Only Enter on the row itself: a control inside a row template keeps its own Enter.
    if (node !== undefined && (event.target as Element).getAttribute('role') === 'treeitem') {
      this.activate.emit(node);
    }
  }

  /**
   * A double-click on a branch expands or collapses it; on a leaf it emits
   * `activate`, except in `multiple`. The twisty's own clicks already
   * toggled, so a double-click there does nothing more.
   */
  protected onDoubleClick(event: MouseEvent, node: T, key: string): void {
    const target = event.target instanceof Element ? event.target : null;
    if (target !== null && target.closest('.tm-tree__twisty') !== null) {
      return;
    }
    if (this.cache.get(key)?.kind !== 'leaf') {
      this.setExpanded(key, !this.expanded().has(key));
    } else if (this.selection() !== 'multiple') {
      this.activate.emit(node);
    }
  }

  // ---- Loading ----

  /**
   * Runs `children` for a node: a leaf, a list, or a promise whose
   * settling updates the cache (unless the cache was emptied since).
   */
  private load(node: T, key: string): ChildrenEntry<T> {
    const children = this.children();
    if (children === undefined) {
      return LEAF;
    }
    const result = untracked(() => children(node));
    if (result === null) {
      return LEAF;
    }
    if (!isPromise(result)) {
      return result.length === 0 ? LEAF : { kind: 'ready', children: result };
    }
    const entry: ChildrenEntry<T> = { kind: 'pending', node };
    const generation = this.generation;
    const current = (): boolean => generation === this.generation && this.cache.get(key) === entry;
    result.then(
      (list) => {
        if (!current()) {
          return;
        }
        this.cache.set(key, list.length === 0 ? LEAF : { kind: 'ready', children: list });
        if (list.length === 0) {
          this.setExpanded(key, false);
        }
        this.cacheVersion.update((version) => version + 1);
      },
      () => {
        if (!current()) {
          return;
        }
        this.cache.set(key, { kind: 'failed', node });
        // A load the user is waiting on collapses its node and says so; a
        // node nobody opened fails quietly and loads again when opened.
        if (this.expanded().has(key)) {
          this.setExpanded(key, false);
          void this.announcer.announce(
            this.translate('tree.loadFailed', { label: this.labelOf(node) })(),
            'polite',
          );
        }
        this.cacheVersion.update((version) => version + 1);
      },
    );
    return entry;
  }

  /** The node a row element (or an element inside one) shows, if it is this tree's. */
  private nodeOfRow(target: EventTarget | Element | null | undefined): T | undefined {
    const row = target instanceof Element ? target.closest('[role="treeitem"]') : null;
    if (row === null || !this.ariaTree().element.contains(row)) {
      return undefined;
    }
    const key = row.getAttribute('data-key');
    return key === null ? undefined : this.nodesByKey.get(key);
  }
}
