// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, untracked, type Signal } from '@angular/core';
import type { TmDataType, TmEntities, TmPermissions } from '@tellma/core';
import { tmQx, tmQxPrint, type TmQx } from '@tellma/core/entity';
import { tmSearchNormalize } from '@tellma/core/search-normalize';

// The path picker's tree (spec 0029 §11.2, §11.3), built from the local
// descriptors so a branch never waits on a request: each node's children
// are built the first time it shows and kept until the inputs change. The
// tree shows queryable scalars, each multilingual group as one node,
// navigations as branches, image and file navigations, and with date parts
// each date field's parts; it hides foreign keys, non-queryable properties,
// twins beyond the tenant's languages and every path the user cannot reach.

/** One field the path picker offers. */
export interface TmPathNode {
  /** The column key or Queryex path, such as `Parent.Manager`; a date part's Queryex text. */
  readonly key: string;
  /** A scalar, a multilingual group, a navigation, or an image or file navigation. */
  readonly kind: 'value' | 'text' | 'navigation' | 'image' | 'file';
  /** The value's type; `null` for a navigation, an image or a file. */
  readonly type: TmDataType | null;
  /** A navigation's target entity, such as `gl.Center`. */
  readonly target?: string;
  /** The field's label, such as "Parent center". */
  readonly label: Signal<string>;
  /** The path's segment labels joined with " / ", such as "Parent center / Code". */
  readonly pathLabel: Signal<string>;
  /** A date part's Queryex text, which is also its key, such as `startOfMonth(PostingDate)`. */
  readonly expr?: string;
}

/** What the tree is built from. */
export interface ɵTmPathTreeOptions {
  /** The entity registry, its Entities chunk loaded. */
  readonly entities: TmEntities;
  /** The user's permissions. */
  readonly permissions: TmPermissions;
  /** A picker string by its key under `tm.pathPicker`. */
  readonly text: (key: string, params?: Readonly<Record<string, unknown>>) => Signal<string>;
  /** The root entity. */
  readonly root: string;
  /** Column keys or Queryex paths. */
  readonly mode: 'columns' | 'fields';
  /** Whether date fields branch into their parts. */
  readonly dateParts: boolean;
  /** Which nodes a consumer takes; every one when omitted. */
  readonly accept: ((node: TmPathNode) => boolean) | undefined;
  /** The active calendar's code. */
  readonly calendar: string;
  /** The UI language, which orders the labels. */
  readonly language: string;
}

/** What a branch's children are built from. */
type TmBranch =
  | { readonly kind: 'entity'; readonly entity: string; readonly prefix: string; readonly steps: number }
  | {
      readonly kind: 'twins';
      readonly entity: string;
      readonly group: string;
      readonly prefix: string;
      /** The slash label the twins hang from: the group's parent's, since a twin's label names the group. */
      readonly parentPath: Signal<string> | null;
    }
  | { readonly kind: 'parts'; readonly path: string; readonly type: TmDataType };

/** One date part: its string, the function it applies, its type and whether the function takes a calendar. */
interface TmDatePart {
  /** The part's string under `tm.pathPicker.part`. */
  readonly part: string;
  /** The Queryex function. */
  readonly fn: string;
  /** The part's type. */
  readonly type: TmDataType;
  /** Whether the function takes a calendar's code. */
  readonly calendar: boolean;
  /** Whether only a date-time or an instant has the part. */
  readonly timeOnly?: true;
}

/** A date field's parts, in the order they show. */
const DATE_PARTS: readonly TmDatePart[] = [
  { part: 'year', fn: 'startOfYear', type: 'date', calendar: true },
  { part: 'quarter', fn: 'startOfQuarter', type: 'date', calendar: true },
  { part: 'month', fn: 'startOfMonth', type: 'date', calendar: true },
  { part: 'week', fn: 'startOfWeek', type: 'date', calendar: false },
  { part: 'day', fn: 'startOfDay', type: 'date', calendar: false, timeOnly: true },
  { part: 'quarterOfYear', fn: 'quarter', type: 'integer', calendar: true },
  { part: 'monthOfYear', fn: 'month', type: 'integer', calendar: true },
  { part: 'dayOfWeek', fn: 'weekday', type: 'integer', calendar: false },
];

/** The calendars Queryex binds by code; under any other, the parts are Gregorian and say so. */
const QUERYEX_CALENDARS: ReadonlySet<string> = new Set(['uq', 'et']);

/** The fields that close every list, in this order: the key and the audit fields. */
const LAST: readonly string[] = ['Id', 'CreatedAt', 'CreatedBy', 'ModifiedAt', 'ModifiedBy'];

/** The most navigation steps a search reaches. */
const SEARCH_STEPS = 3;

/** The most matches a search shows. */
const SEARCH_MATCHES = 200;

/** A search's result: the matches inside their ancestors. */
export interface ɵTmPathSearch {
  /** The roots that match or lead to a match. */
  readonly roots: readonly TmPathNode[];
  /** Each kept branch's kept children, by key. */
  readonly children: ReadonlyMap<string, readonly TmPathNode[]>;
  /** The keys of the ancestors kept only for their matches. */
  readonly context: ReadonlySet<string>;
  /** The keys of every kept branch, expanded. */
  readonly expanded: ReadonlySet<string>;
  /** How many nodes match. */
  readonly count: number;
}

/**
 * A root entity's fields as a tree, built lazily. Internal: the path
 * picker builds one per change of its inputs.
 */
export class ɵTmPathTree {
  readonly #options: ɵTmPathTreeOptions;
  readonly #collator: Intl.Collator;
  /** Each built branch's children, by its key; the root's under ''. */
  readonly #built = new Map<string, readonly TmPathNode[]>();
  /** Each branch node's recipe. */
  readonly #branches = new WeakMap<TmPathNode, TmBranch>();
  /** Every node built so far, by key. */
  readonly #nodes = new Map<string, TmPathNode>();

  /**
   * Prepares the tree; nothing is built until it shows.
   *
   * @param options - What the tree is built from.
   */
  constructor(options: ɵTmPathTreeOptions) {
    this.#options = options;
    this.#collator = new Intl.Collator(options.language);
  }

  /** The root entity's fields. */
  roots(): readonly TmPathNode[] {
    return this.#children('', { kind: 'entity', entity: this.#options.root, prefix: '', steps: 0 }, null);
  }

  /**
   * A node's children, built on first ask; `null` for a leaf.
   *
   * @param node - The node.
   * @returns The children, or `null`.
   */
  children(node: TmPathNode): readonly TmPathNode[] | null {
    const branch = this.#branches.get(node);
    return branch === undefined ? null : this.#children(node.key, branch, node.pathLabel);
  }

  /**
   * A node the tree has built, by key.
   *
   * @param key - The key.
   * @returns The node, or `undefined` for one not built.
   */
  node(key: string): TmPathNode | undefined {
    return this.#nodes.get(key);
  }

  /**
   * Whether the consumer takes a node; a node the tree never built is no
   * node of its own to refuse.
   *
   * @param node - The node.
   * @returns Whether it is accepted.
   */
  accepts(node: TmPathNode | undefined): boolean {
    return node === undefined || (this.#options.accept?.(node) ?? true);
  }

  /**
   * The Queryex text a node stands for: its path, a navigation's foreign
   * key path, or a date part's expression.
   *
   * @param node - The node.
   * @returns The text.
   */
  text(node: TmPathNode): string {
    if (node.expr !== undefined) {
      return node.expr;
    }
    if (node.kind === 'navigation' || node.kind === 'image' || node.kind === 'file') {
      const resolution = this.#options.entities.resolve(this.#options.root, node.key);
      return tmQxPrint(tmQx.path(resolution?.fkPath ?? node.key));
    }
    return tmQxPrint(tmQx.path(node.key));
  }

  /**
   * Searches the tree by label and name, three navigation steps deep, for
   * at most 200 matches, each kept inside its ancestors.
   *
   * @param query - The search text.
   * @returns The result, or `null` for a blank query.
   */
  search(query: string): ɵTmPathSearch | null {
    const needle = tmSearchNormalize(query).trim();
    if (needle === '') {
      return null;
    }
    const children = new Map<string, readonly TmPathNode[]>();
    const context = new Set<string>();
    const expanded = new Set<string>();
    let count = 0;

    // Depth first, in tree order, so the cap keeps the first matches a reader meets.
    const walk = (nodes: readonly TmPathNode[]): TmPathNode[] => {
      const kept: TmPathNode[] = [];
      for (const node of nodes) {
        if (count >= SEARCH_MATCHES) {
          break;
        }
        const name = node.expr === undefined ? (node.key.split('.').at(-1) ?? node.key) : '';
        const matches = tmSearchNormalize(node.label()).includes(needle) || tmSearchNormalize(name).includes(needle);
        if (matches) {
          count++;
        }
        const branch = this.#branches.get(node);
        const deeper = branch !== undefined && (branch.kind !== 'entity' || branch.steps <= SEARCH_STEPS);
        const inner = deeper ? walk(this.children(node) ?? []) : [];
        if (inner.length > 0) {
          children.set(node.key, inner);
          expanded.add(node.key);
        }
        if (matches || inner.length > 0) {
          kept.push(node);
          if (!matches) {
            context.add(node.key);
          }
        }
      }
      return kept;
    };
    const roots = walk(this.roots());
    return { roots, children, context, expanded, count };
  }

  /**
   * A branch's children, built once.
   *
   * @param key - The branch's key; '' for the root.
   * @param branch - The branch's recipe.
   * @param parentPath - The branch's slash label; `null` at the root.
   * @returns The children.
   */
  #children(key: string, branch: TmBranch, parentPath: Signal<string> | null): readonly TmPathNode[] {
    let children = this.#built.get(key);
    if (children === undefined) {
      // Built outside any reactive read: the picker rebuilds the whole tree when its inputs change.
      children = untracked(() =>
        branch.kind === 'entity'
          ? this.#entityChildren(branch, parentPath)
          : branch.kind === 'twins'
            ? this.#twins(branch)
            : this.#parts(branch, parentPath),
      );
      this.#built.set(key, children);
      for (const child of children) {
        this.#nodes.set(child.key, child);
      }
    }
    return children;
  }

  /**
   * An entity's fields under a path: its scalars and groups, then its
   * navigations, each reachable, in label order with the key and the audit
   * fields last.
   *
   * @param branch - The entity and the path's prefix.
   * @param parentPath - The prefix's slash label, or `null` at the root.
   * @returns The fields.
   */
  #entityChildren(branch: Extract<TmBranch, { kind: 'entity' }>, parentPath: Signal<string> | null): readonly TmPathNode[] {
    const { entities, mode, dateParts } = this.#options;
    const descriptor = entities.get(branch.entity);
    const nodes: TmPathNode[] = [];

    // --- Scalars and groups; a foreign key's navigation stands for it, and a twin sits under its group ---
    for (const property of descriptor.properties) {
      const slot = property.multilingual?.slot;
      if (!property.queryable || property.foreignKeyOf !== undefined || (slot !== undefined && slot > 1)) {
        continue;
      }
      const key = branch.prefix + property.name;
      if (!this.#reachable(key)) {
        continue;
      }
      if (slot === 1) {
        // The group, labelled without a language's symbol.
        const group = property.multilingual!.group;
        const label = entities.resolve(branch.entity, property.name)?.label ?? property.label;
        const node = this.#node({ key, kind: 'text', type: property.type, label }, parentPath);
        const twins = descriptor.properties.some((other) => other.multilingual?.group === group && other.multilingual.slot > 1);
        if (mode === 'columns' && twins) {
          this.#branches.set(node, { kind: 'twins', entity: branch.entity, group, prefix: branch.prefix, parentPath });
        }
        if (this.accepts(node) || this.#branches.has(node)) {
          nodes.push(node);
        }
        continue;
      }
      const node = this.#node({ key, kind: 'value', type: property.type, label: property.label }, parentPath);
      if (mode === 'fields' && dateParts && (property.type === 'date' || property.type === 'datetime' || property.type === 'instant')) {
        this.#branches.set(node, { kind: 'parts', path: key, type: property.type });
      }
      if (this.accepts(node) || this.#branches.has(node)) {
        nodes.push(node);
      }
    }

    // --- Navigations: branches into their targets; images and files into their blobs only for Queryex paths ---
    for (const navigation of descriptor.navigations) {
      const foreignKey = descriptor.property(navigation.foreignKey);
      if (foreignKey?.queryable === false) {
        continue;
      }
      const key = branch.prefix + navigation.name;
      if (!this.#reachable(key)) {
        continue;
      }
      const policy = navigation.target === 'core.Blob' ? foreignKey?.blob : undefined;
      const kind = policy === undefined ? 'navigation' : policy.image !== null ? 'image' : 'file';
      const node = this.#node({ key, kind, type: null, target: navigation.target, label: navigation.label }, parentPath);
      if (kind === 'navigation' || mode === 'fields') {
        this.#branches.set(node, { kind: 'entity', entity: navigation.target, prefix: `${key}.`, steps: branch.steps + 1 });
      }
      if (this.accepts(node) || this.#branches.has(node)) {
        nodes.push(node);
      }
    }
    return this.#sorted(nodes, branch.prefix);
  }

  /**
   * A group's twins, as column keys, in slot order.
   *
   * @param branch - The group.
   * @returns The twins.
   */
  #twins(branch: Extract<TmBranch, { kind: 'twins' }>): readonly TmPathNode[] {
    const descriptor = this.#options.entities.get(branch.entity);
    return descriptor.properties
      .filter((property) => property.multilingual?.group === branch.group && property.multilingual.slot > 1)
      .filter((property) => this.#reachable(branch.prefix + property.name))
      .map((property) => this.#node({ key: branch.prefix + property.name, kind: 'value', type: property.type, label: property.label }, branch.parentPath))
      .filter((node) => this.accepts(node));
  }

  /**
   * A date field's parts. A function that takes a calendar gets the active
   * one's code when Queryex binds it; under any other calendar but the
   * Gregorian, those parts are Gregorian and their labels say so.
   *
   * @param branch - The date field.
   * @param parentPath - The field's slash label.
   * @returns The parts.
   */
  #parts(branch: Extract<TmBranch, { kind: 'parts' }>, parentPath: Signal<string> | null): readonly TmPathNode[] {
    const { calendar, text } = this.#options;
    const code = QUERYEX_CALENDARS.has(calendar) ? calendar : null;
    const labelled = calendar !== 'gc' && code === null;
    // An instant's parts are read in the user's time zone.
    const value: TmQx = branch.type === 'instant' ? tmQx.call('local', tmQx.path(branch.path)) : tmQx.path(branch.path);
    return DATE_PARTS.filter((part) => !part.timeOnly || branch.type !== 'date')
      .map((part) => {
        const expr = tmQxPrint(part.calendar && code !== null ? tmQx.call(part.fn, value, tmQx.str(code)) : tmQx.call(part.fn, value));
        const name = text(`part.${part.part}`);
        const gregorian = part.calendar && labelled ? computed(() => text('gregorian', { part: name() })()) : name;
        return this.#node({ key: expr, kind: 'value', type: part.type, label: gregorian, expr }, parentPath);
      })
      .filter((node) => this.accepts(node));
  }

  /**
   * A node with its slash label.
   *
   * @param fields - The node's own fields.
   * @param parentPath - Its parent's slash label, or `null` at the root.
   * @returns The node.
   */
  #node(fields: Omit<TmPathNode, 'pathLabel'>, parentPath: Signal<string> | null): TmPathNode {
    const { label } = fields;
    return { ...fields, pathLabel: parentPath === null ? label : computed(() => `${parentPath()} / ${label()}`) };
  }

  /**
   * Whether the user may read a path from the root.
   *
   * @param key - The path.
   * @returns Whether it is reachable.
   */
  #reachable(key: string): boolean {
    return this.#options.permissions.reachable(this.#options.root, key)();
  }

  /**
   * Fields in label order, the key and the audit fields last.
   *
   * @param nodes - The fields.
   * @param prefix - Their path's prefix.
   * @returns The ordered fields.
   */
  #sorted(nodes: readonly TmPathNode[], prefix: string): readonly TmPathNode[] {
    const rank = (node: TmPathNode): number => {
      const index = LAST.indexOf(node.key.slice(prefix.length));
      return index < 0 ? -1 : index;
    };
    return [...nodes].sort((a, b) => rank(a) - rank(b) || this.#collator.compare(a.label(), b.label()));
  }
}
