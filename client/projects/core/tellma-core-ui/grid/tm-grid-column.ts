// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { booleanAttribute, contentChild, Directive, input } from '@angular/core';

import type {
  TmLabelResolution,
  TmParseContext,
  TmParseError,
  TmPasteContext,
} from '@tellma/core-ui/contracts';
import type { TmGridColumnType } from '@tellma/core-ui/grid-engine';

import { TmGridDisplayDef, TmGridEditorDef, TmGridHeaderDef } from './tm-grid-templates';

let nextColumnId = 0;

/**
 * One grid column, declared as a content child of `tm-grid`/`tm-tree-grid`
 * in display order. Definition-only: it renders nothing itself.
 *
 * `type` is a defaults bundle — it selects the built-in formatter, parser,
 * editor, alignment, and clipboard behavior in one word; `format`, `parse`,
 * and the `*tmGridDisplay`/`*tmGridEditor` templates are per-concern
 * overrides. A column without `key` is an accessor column (`value`) and is
 * always readonly.
 *
 * @tmGroup grid
 * @tmA11yNotes The header text names the column's cells and its editors.
 */
// Definition-only ELEMENT directive by design: columns are declared as
// `<tm-grid-column>` content children in display order, mirroring the
// template-definition shape.
// eslint-disable-next-line @angular-eslint/directive-selector -- see above
@Directive({ selector: 'tm-grid-column' })
export class TmGridColumn<T = unknown, V = unknown> {
  /** Stable identity when `key` is absent (accessor columns). */
  readonly generatedId = `tm-grid-col-${nextColumnId++}`;

  /**
   * The model property this column reads and writes (also the child-field
   * key in editable mode). Omitted ⇒ accessor column via `value`, readonly.
   */
  readonly key = input<string | undefined>(undefined);
  /** The built-in behavior bundle. */
  readonly type = input<TmGridColumnType>('text');
  /** The header label (or project a `*tmGridHeader` template for rich headers). */
  readonly header = input('');
  /** Accessor for computed columns (`key` omitted). */
  readonly value = input<((row: T) => V) | undefined>(undefined);
  /**
   * Display-string override. This string is the cell's text representation:
   * what copy exports, what find searches, what announcements speak.
   */
  readonly format = input<((value: V, row: T) => string) | undefined>(undefined);
  /**
   * Text→value conversion for typed paste and text-editor commits.
   * Required for `date` columns (no date adapter exists yet).
   */
  readonly parse = input<((text: string, ctx: TmParseContext) => V | TmParseError) | undefined>(
    undefined,
  );
  /** The column's cleared value — what Delete and error-clearing write. */
  readonly defaultValue = input<V | undefined>(undefined);
  /** `enum` columns: the options list. */
  readonly options = input<readonly unknown[] | undefined>(undefined);
  /** `enum` columns: maps an option to its display label. */
  readonly optionLabel = input<((option: never) => string) | undefined>(undefined);
  /** `enum` columns: maps an option to the value written to the model. */
  readonly optionValue = input<((option: never) => V) | undefined>(undefined);
  /**
   * Batched async label→value resolution for `enum`/`entity` paste: one
   * call per column per paste with the distinct unresolved labels.
   */
  readonly resolvePastedLabels = input<
    ((labels: string[], ctx: TmPasteContext) => Promise<ReadonlyMap<string, TmLabelResolution<V>>>) | undefined
  >(undefined);
  /** Column- or per-cell-level editability (bound field state still wins). */
  readonly readonly = input<boolean | ((row: T) => boolean)>(false);
  /** Fixed width in px (user resize converts a column to this). */
  readonly width = input<number | undefined>(undefined);
  /** Proportional share of leftover space (emitted as a `fr` track). */
  readonly flex = input<number | undefined>(undefined);
  /** Minimum width in px for proportional columns. */
  readonly minWidth = input<number | undefined>(undefined);
  /**
   * Cell alignment — logical (`start`/`end`/`center`) or physical
   * (`left`/`right`; numerals stay right-aligned in RTL locales too).
   * Defaults by type: `number`/`date` → `right`, `boolean` → `center`,
   * else `start`.
   */
  readonly align = input<'start' | 'end' | 'center' | 'left' | 'right' | undefined>(undefined);
  /** Marks the column that renders the tree hierarchy (defaults to the first). */
  readonly hierarchy = input(false, { transform: booleanAttribute });

  /** Custom static display template. */
  readonly displayDef = contentChild(TmGridDisplayDef<T, V>);
  /** Custom editor template. */
  readonly editorDef = contentChild(TmGridEditorDef<T, V>);
  /** Custom header template. */
  readonly headerDef = contentChild(TmGridHeaderDef);
}
