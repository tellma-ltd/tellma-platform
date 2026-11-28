// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';

import { TmGridColumn } from '@tellma/core-ui/grid';
import { TmTreeGrid } from '@tellma/core-ui/tree-grid';

/** One node of the generated tree. */
interface Node {
  readonly id: number;
  readonly parentId: number | null;
  readonly name: string;
  readonly code: string;
  readonly balance: number;
  readonly active: boolean;
}

/** The number of roots; each heads a chain of a child and a grandchild. */
const ROOTS = 7000;

/** One deterministic node (code, balance and active derive from the id). */
function node(id: number, parentId: number | null, name: string): Node {
  return {
    id,
    parentId,
    name,
    code: String(id),
    balance: ((id * 7919) % 90000) / 10,
    active: id % 4 !== 0,
  };
}

/**
 * The tree in depth-first order: 7,000 roots, each with one child that has
 * one child of its own (21,000 rows). Fully expanded, two of every three
 * rows are branches and draw an expander: the per-row cost the scroll
 * probe watches.
 */
function buildTree(): readonly Node[] {
  const nodes: Node[] = [];
  let nextId = 1;
  for (let r = 1; r <= ROOTS; r++) {
    const rootId = nextId++;
    const childId = nextId++;
    nodes.push(node(rootId, null, `Group ${r}`));
    nodes.push(node(childId, rootId, `Group ${r}.1`));
    nodes.push(node(nextId++, childId, `Account ${r}.1.1`));
  }
  return nodes;
}

/**
 * Readonly tm-tree-grid over a 21,000-row tree, fully expanded: the scroll
 * probe's subject, where two of every three rendered rows carry an expander.
 */
@Component({
  imports: [TmTreeGrid, TmGridColumn],
  template: `
    <h2>Tree grid (large, expanded)</h2>

    <tm-tree-grid
      class="demo-grid"
      gridId="tree-grid-large"
      data-testid="tree-grid-large"
      [data]="rows"
      [rowId]="nodeId"
      [parentId]="nodeParentId"
      readonly
    >
      <tm-grid-column key="name" header="Name" [flex]="2" [minWidth]="240" />
      <tm-grid-column key="code" header="Code" [width]="90" />
      <tm-grid-column key="balance" type="number" header="Balance" [width]="120" />
      <tm-grid-column key="active" type="boolean" header="Active" [width]="90" />
    </tm-tree-grid>
  `,
  styles: `
    .demo-grid {
      display: block;
      block-size: 60vh;
    }
  `,
})
export class TreeGridLargeStory {
  /** The generated rows, built once. */
  readonly rows = buildTree();

  /** The row identity. */
  readonly nodeId = (row: Node): number => row.id;
  /** The parent's identity, or `null` for a root. */
  readonly nodeParentId = (row: Node): number | null => row.parentId;
}
