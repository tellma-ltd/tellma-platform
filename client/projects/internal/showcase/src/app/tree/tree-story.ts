// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, signal } from '@angular/core';

import { TmBadge } from '@tellma/core-ui/badge';
import { TmTree, TmTreeNode } from '@tellma/core-ui/tree';

import { HeldResponses, HeldResponsesControls } from '../held-responses';

/** A field of the story's document entity. */
export interface StoryField {
  /** The field's path, unique in the tree: `'Agent.Code'`. */
  readonly key: string;
  /** The field's name. */
  readonly label: string;
  /** A navigation's target entity. */
  readonly target?: string;
  /** A navigation's fields, listed at once. */
  readonly children?: readonly StoryField[];
  /** A navigation whose fields load through the data source. */
  readonly lazy?: 'loads' | 'fails';
  /** Whether the field is drawn muted, as context around a match. */
  readonly context?: boolean;
}

/** The fields of an entity reached through a navigation at `prefix`. */
function agentFields(prefix: string): readonly StoryField[] {
  return [
    { key: `${prefix}.Code`, label: 'Code' },
    { key: `${prefix}.Name`, label: 'Name' },
  ];
}

/** The document entity's fields: navigations, plain fields, and two that load. */
export const STORY_FIELDS: readonly StoryField[] = [
  {
    key: 'Agent',
    label: 'Agent',
    target: 'Agent',
    children: [
      ...agentFields('Agent'),
      {
        key: 'Agent.Parent',
        label: 'Parent',
        target: 'Agent',
        children: agentFields('Agent.Parent'),
      },
    ],
  },
  {
    key: 'Lines',
    label: 'Lines',
    target: 'Line',
    children: [
      { key: 'Lines.Entries', label: 'Entries', target: 'Entry', lazy: 'loads' },
      { key: 'Lines.Archive', label: 'Archive', target: 'Entry', lazy: 'fails' },
    ],
  },
  { key: 'Amount', label: 'Amount' },
  { key: 'Code', label: 'Code' },
  { key: 'Date', label: 'Date', context: true },
];

/**
 * tm-tree in every state its section names: lazy branches that load and
 * fail through a held data source, the three selection modes, context
 * nodes, and a custom row with a badge.
 */
@Component({
  imports: [HeldResponsesControls, TmBadge, TmTree, TmTreeNode],
  template: `
    <h2>Tree</h2>
    <p class="toolbar"><app-held-responses [gate]="gate" name="children" /></p>

    <!-- No selection: Enter or a double-click on a leaf activates. -->
    <h3>Fields</h3>
    <tm-tree
      data-testid="tree-fields"
      [nodes]="fields"
      [nodeKey]="keyOf"
      [label]="labelOf"
      [children]="childrenOf"
      [context]="contextOf"
      [(expanded)]="expanded"
      ariaLabel="Fields"
      (activate)="activated.set($event.key)"
    />
    <p data-testid="tree-activated">Activated: {{ activated() ?? 'none' }}</p>
    <p data-testid="tree-expanded">Expanded: {{ expandedText() }}</p>

    <!-- Single selection. -->
    <h3>Pick one field</h3>
    <tm-tree
      data-testid="tree-single"
      [nodes]="fields"
      [nodeKey]="keyOf"
      [label]="labelOf"
      [children]="childrenOf"
      selection="single"
      [(selected)]="single"
      ariaLabel="Pick one field"
      size="sm"
    />
    <p data-testid="tree-single-selected">Selected: {{ [...single()].join(', ') }}</p>

    <!-- Multiple selection, with a custom row naming each navigation's target. -->
    <h3>Pick columns</h3>
    <tm-tree
      data-testid="tree-multiple"
      [nodes]="fields"
      [nodeKey]="keyOf"
      [label]="labelOf"
      [children]="childrenOf"
      selection="multiple"
      [(selected)]="multiple"
      [(expanded)]="multipleExpanded"
      ariaLabel="Pick columns"
      size="lg"
    >
      <ng-template tmTreeNode [tmTreeNodeOf]="fields" let-node>
        {{ node.label }}
        @if (node.target) {
          <tm-badge>{{ node.target }}</tm-badge>
        }
      </ng-template>
    </tm-tree>
    <p data-testid="tree-multiple-selected">Selected: {{ [...multiple()].join(', ') }}</p>
  `,
  styles: `
    .toolbar {
      display: flex;
      gap: 12px;
      align-items: center;
    }
    tm-tree {
      max-inline-size: 360px;
    }
  `,
})
export class TreeStory {
  /** The lazy branches' latency gate. */
  protected readonly gate = new HeldResponses();

  /** The roots. */
  protected readonly fields = STORY_FIELDS;

  /** A field's key. */
  protected readonly keyOf = (node: StoryField): string => node.key;

  /** A field's label. */
  protected readonly labelOf = (node: StoryField): string => node.label;

  /** Whether a field is drawn as context. */
  protected readonly contextOf = (node: StoryField): boolean => node.context === true;

  /** A field's children: listed, loaded through the gate, or failing after it. */
  protected readonly childrenOf = (
    node: StoryField,
  ): readonly StoryField[] | Promise<readonly StoryField[]> | null => {
    if (node.lazy === undefined) {
      return node.children ?? null;
    }
    return this.gate.wait(300).then(() => {
      if (node.lazy === 'fails') {
        throw new Error(`${node.label} is unavailable.`);
      }
      return agentFields(node.key);
    });
  };

  /** The first tree's expanded keys. */
  protected readonly expanded = signal<ReadonlySet<string>>(new Set());

  /** The expanded keys, as text. */
  protected readonly expandedText = computed(() => [...this.expanded()].join(', ') || 'none');

  /** The last activated field's key. */
  protected readonly activated = signal<string | null>(null);

  /** The single-selection tree's selection. */
  protected readonly single = signal<ReadonlySet<string>>(new Set());

  /** The multiple-selection tree's selection. */
  protected readonly multiple = signal<ReadonlySet<string>>(new Set(['Code']));

  /** The multiple-selection tree's expanded keys: open, so both states show. */
  protected readonly multipleExpanded = signal<ReadonlySet<string>>(new Set(['Agent']));
}
