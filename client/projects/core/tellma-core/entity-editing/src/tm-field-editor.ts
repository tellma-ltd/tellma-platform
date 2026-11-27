// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  Directive,
  inject,
  Injector,
  input,
  type InputSignal,
  type Signal,
  signal,
  type TemplateRef,
} from '@angular/core';
import { type FieldState, FormField, type FieldTree } from '@angular/forms/signals';
import { TM_UI_TRANSLATE, tmResolveFieldErrors } from '@tellma/core-ui';
import { TM_FORM_FIELD_CONTROL, TmFormField } from '@tellma/core-ui/form-field';
import type { TmId, TmJsonObject, TmNavigationDescriptor, TmPropertyDescriptor } from '@tellma/core';

import { TmEntityField } from './tm-entity-field';
import { ɵTmFormFieldDelegate } from './tm-form-field-delegate';
import { TmValueEditor } from './tm-value-editor';

// The details kit's tm-field in edit and create mode (spec 0031 §6.2): the
// edit side, which the field loads with this entry point on first use, so
// a details page in view mode never downloads the edit controls.
// `tm-form-field` labels the control and shows the hint and the focus-only
// error popover. A navigation takes the entity field on its key, with the
// field's filter and Create…; a value takes the value editor; a layout's
// own control renders in a labelled group that carries the field's state.

/** What a details field's editor edits, as the field describes its cell. */
export interface ɵTmFieldEditorCell {
  /** The label, in the active language. */
  readonly label: Signal<string>;
  /** The member the control writes, such as `currencyId`; `null` for none. */
  readonly json: string | null;
  /** What the editor edits: the property, or the navigation whose key the entity field writes; `null` for nothing. */
  readonly edits: TmPropertyDescriptor | TmNavigationDescriptor | null;
}

/** What a details field's editor renders with, beside the cell. */
export interface ɵTmFieldEditorEditing {
  /** The Signal Forms field the control binds to. */
  readonly node: FieldTree<unknown>;
  /** The help text under the field, or empty. */
  readonly hint: Signal<string>;
  /** The layout's own control, or `null` for the kit's. */
  readonly template: Signal<TemplateRef<unknown> | null>;
  /** A navigation picker's Queryex predicate. */
  readonly filter: Signal<string | null>;
  /** Whether a navigation picker offers Create…. */
  readonly offerCreate: Signal<boolean>;
  /** Whether the record is being created, so a write-once value edits. */
  readonly creating: Signal<boolean>;
  /** An image's or a file's blob as the read related it, which names the file; `undefined` for any other value. */
  readonly blob?: Signal<TmJsonObject | null | undefined>;
}

/** Counts the groups, so each has its own id. */
let nextGroup = 0;

/**
 * The labelled group around a layout's own control: `tm-form-field`
 * labels it and describes it with the hint and the errors, and reads the
 * bound field's state through it. Internal: the details field's editor renders it.
 */
@Directive({
  selector: '[tmFieldEditGroup]',
  providers: [{ provide: TM_FORM_FIELD_CONTROL, useFactory: () => inject(ɵTmFieldEditGroup).control }],
  host: {
    role: 'group',
    '[id]': 'id',
    '[attr.aria-labelledby]': 'control.labelledBy()',
    '[attr.aria-describedby]': 'control.ownDescribedBy()',
  },
})
export class ɵTmFieldEditGroup {
  /** The bound field. */
  readonly tmFieldEditGroup: InputSignal<FieldTree<unknown>> = input.required<FieldTree<unknown>>();

  /** The group's id. */
  protected readonly id = `tm-field-edit-${nextGroup++}`;

  /** The bound field's state. */
  readonly #state = computed<FieldState<unknown>>(() => this.tmFieldEditGroup()());

  /** The control `tm-form-field` labels: the group itself, with the field's state and errors. */
  readonly control: ɵTmFormFieldDelegate = new ɵTmFormFieldDelegate(
    signal(undefined),
    {
      controlId: signal(this.id).asReadonly(),
      state: this.#state,
      errors: tmResolveFieldErrors(
        computed(() => this.#state().errors()),
        inject(TM_UI_TRANSLATE),
      ),
      local: signal([]).asReadonly(),
      busy: signal(false).asReadonly(),
    },
    inject(Injector),
  );
}

/** A details field's edit-mode control, in its form field. Internal: the details kit's `tm-field` loads and renders it. */
@Component({
  selector: 'tm-field-editor',
  imports: [FormField, NgTemplateOutlet, TmEntityField, TmFormField, TmValueEditor, ɵTmFieldEditGroup],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'tm-field-editor' },
  styles: `
    :host {
      display: block;
    }
  `,
  template: `
    @let current = cell();
    @let field = editing();
    <!-- One form field per kind of control, each in the block that creates its control, so the control's inputs are set before the field reads it. -->
    @if (field.template(); as template) {
      <!-- The layout's own control, in a group the field labels. -->
      <tm-form-field [label]="current.label()" [hint]="field.hint()">
        <div [tmFieldEditGroup]="field.node">
          <ng-container [ngTemplateOutlet]="template" [ngTemplateOutletContext]="{ $implicit: field.node, property: current.edits }" />
        </div>
      </tm-form-field>
    } @else if (navigation(); as target) {
      <!-- A navigation: the entity field on its key. -->
      <tm-form-field [label]="current.label()" [hint]="field.hint()">
        <tm-entity-field [entity]="target.target" [filter]="field.filter()" [create]="field.offerCreate()" [formField]="idNode()" />
      </tm-form-field>
    } @else if (property(); as edited) {
      <!-- A value: the control its type takes. -->
      <tm-form-field [label]="current.label()" [hint]="field.hint()">
        <tm-value-editor [property]="edited" [field]="field.node" [path]="path()" [create]="field.creating()" [blob]="field.blob?.()" />
      </tm-form-field>
    }
  `,
})
export class ɵTmFieldEditor {
  /** The cell. */
  readonly cell: InputSignal<ɵTmFieldEditorCell> = input.required<ɵTmFieldEditorCell>();

  /** What the cell edits with. */
  readonly editing: InputSignal<ɵTmFieldEditorEditing> = input.required<ɵTmFieldEditorEditing>();

  /** The navigation whose key the entity field writes, or `null`. */
  protected readonly navigation = computed<TmNavigationDescriptor | null>(() => {
    const edits = this.cell().edits;
    return edits !== null && 'foreignKey' in edits ? edits : null;
  });

  /** The property the value editor edits, or `null`. */
  protected readonly property = computed<TmPropertyDescriptor | null>(() => {
    const edits = this.cell().edits;
    return edits !== null && !('foreignKey' in edits) ? edits : null;
  });

  /** The field as an id, for the entity field. */
  protected readonly idNode = computed(() => this.editing().node as FieldTree<TmId | null>);

  /** The field's path from the record, under which the record's uploads list an upload: a field is a member of the record itself. */
  protected readonly path = computed(() => this.cell().json ?? undefined);
}
