// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Directive, inject, input, TemplateRef } from '@angular/core';

/** What a row template sees about its node. */
export interface TmTreeNodeState {
  /** The node's depth: 1 for a root. */
  readonly level: number;
  /** Whether the node is expanded. */
  readonly expanded: boolean;
  /** Whether the node is selected. */
  readonly selected: boolean;
  /** Whether the node's children are loading. */
  readonly loading: boolean;
  /** Whether the node has no children. */
  readonly leaf: boolean;
  /** Whether the node is a context node, drawn muted around the nodes that matter. */
  readonly context: boolean;
}

/** The template context of a row (`ng-template[tmTreeNode]`). */
export interface TmTreeNodeContext<T = unknown> {
  /** The node. */
  readonly $implicit: T;
  /** The node's state. */
  readonly state: TmTreeNodeState;
}

/**
 * A row's content after the twisty: `<ng-template tmTreeNode let-node
 * let-state="state">`. Without one, a row shows the tree's `label`.
 *
 * Bind `tmTreeNodeOf` to the tree's `nodes` for a typed `node`; it is read
 * only by the template type checker.
 *
 * @tmGroup collection
 */
@Directive({ selector: 'ng-template[tmTreeNode]' })
export class TmTreeNode<T = unknown> {
  /** The row template. */
  readonly template = inject<TemplateRef<TmTreeNodeContext<T>>>(TemplateRef);

  /** The tree's nodes, so the template checker types `node`; unused at runtime. */
  readonly tmTreeNodeOf = input<readonly T[] | undefined>(undefined);

  /** Narrows the template context for type-checked bindings. */
  static ngTemplateContextGuard<T>(
    _dir: TmTreeNode<T>,
    // eslint-disable-next-line @typescript-eslint/no-unused-vars -- type-position-only parameter
    _ctx: unknown,
  ): _ctx is TmTreeNodeContext<T> {
    return true;
  }
}
