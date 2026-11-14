// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import { TmDropTarget, type TmDragPayload, type TmDrop } from '@tellma/core-ui/drag-drop';
import { TmReorderList } from '@tellma/core-ui/reorder-list';
import { TmTree } from '@tellma/core-ui/tree';

import type { StoryColumn } from '../reorder-list/reorder-list-story';
import { STORY_FIELDS, type StoryField } from '../tree/tree-story';

/**
 * The drag-drop layer across components: a field tree whose leaves drag
 * their paths, a text area that takes a path at the drop caret, a reorder
 * list that takes fields as new columns, and a plain drop target that
 * records what lands on it.
 */
@Component({
  imports: [TmDropTarget, TmReorderList, TmTree],
  template: `
    <h2>Drag and drop</h2>
    <div class="layout">
      <!-- The source: leaves drag their path; navigations do not drag. -->
      <tm-tree
        data-testid="drag-tree"
        [nodes]="fields"
        [nodeKey]="keyOf"
        [label]="labelOf"
        [children]="childrenOf"
        [dragData]="dragOf"
        [(expanded)]="expanded"
        ariaLabel="Fields"
      />

      <div class="targets">
        <!-- A text field: the path's text lands at the drop caret. -->
        <label class="field">
          Expression
          <textarea data-testid="drop-textarea" rows="3">Sum() + </textarea>
        </label>

        <!-- A reorder list taking fields as columns. -->
        <tm-reorder-list
          data-testid="drop-columns"
          [(items)]="columns"
          [itemKey]="columnKey"
          [itemLabel]="columnLabel"
          [accepts]="['tm.path']"
          ariaLabel="Columns"
          (received)="addColumn($event.payload, $event.index)"
        />
        <p data-testid="drop-columns-order">Columns: {{ columnsText() }}</p>

        <!-- A plain target that records each drop. -->
        <div
          class="well"
          data-testid="drop-well"
          tmDropTarget
          [accepts]="['tm.path']"
          (dropped)="onWell($event)"
        >
          Drop a field here
        </div>
        <p data-testid="drop-well-log">Dropped: {{ wellLog().join(', ') || 'nothing' }}</p>
      </div>
    </div>
  `,
  styles: `
    .layout {
      display: flex;
      flex-wrap: wrap;
      gap: 24px;
      align-items: flex-start;
    }
    tm-tree {
      inline-size: 240px;
    }
    .targets {
      display: flex;
      flex-direction: column;
      gap: 12px;
      inline-size: 320px;
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }
    textarea {
      font: inherit;
    }
    .well {
      padding: 16px;
      border: 1px dashed var(--border-strong);
      border-radius: 6px;
      text-align: center;
    }
  `,
})
export class DragDropStory {
  /** The tree's roots: the story's plain fields. */
  protected readonly fields = STORY_FIELDS.filter((field) => field.key !== 'Lines');

  /** A field's key. */
  protected readonly keyOf = (node: StoryField): string => node.key;

  /** A field's label. */
  protected readonly labelOf = (node: StoryField): string => node.label;

  /** A field's listed children. */
  protected readonly childrenOf = (node: StoryField): readonly StoryField[] | null =>
    node.children ?? null;

  /** A leaf's drag payload: its path as text; a navigation does not drag. */
  protected readonly dragOf = (node: StoryField): TmDragPayload | null =>
    node.children !== undefined
      ? null
      : { kind: 'tm.path', data: node, text: node.key, label: node.label };

  /** The tree's expanded keys: the agent open, so nested leaves are reachable. */
  protected readonly expanded = signal<ReadonlySet<string>>(new Set(['Agent']));

  /** The reorder list's columns. */
  protected readonly columns = signal<readonly StoryColumn[]>([
    { key: 'Name', label: 'Name' },
    { key: 'Date', label: 'Date' },
  ]);

  /** A column's key. */
  protected readonly columnKey = (column: StoryColumn): string => column.key;

  /** A column's label. */
  protected readonly columnLabel = (column: StoryColumn): string => column.label;

  /** The columns, as text. */
  protected columnsText(): string {
    return this.columns()
      .map((column) => column.key)
      .join(', ');
  }

  /** Inserts a dropped field as a column, unless it is already one. */
  protected addColumn(payload: TmDragPayload, index: number): void {
    if (this.columns().some((column) => column.key === payload.text)) {
      return;
    }
    const next = [...this.columns()];
    next.splice(index, 0, { key: payload.text, label: payload.label });
    this.columns.set(next);
  }

  /** The paths dropped on the well. */
  protected readonly wellLog = signal<readonly string[]>([]);

  /** Records a drop on the well. */
  protected onWell(drop: TmDrop): void {
    this.wellLog.update((log) => [...log, drop.payload.text]);
  }
}
