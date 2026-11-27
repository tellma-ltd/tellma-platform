// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ErrorHandler,
  inject,
  Injector,
  signal,
  type Signal,
  type Type,
} from '@angular/core';
import {
  form,
  required,
  requiredError,
  submit,
  validate,
  type FieldTree,
  type SchemaPathTree,
  type ValidationError,
} from '@angular/forms/signals';
import {
  TmApiError,
  TmDisplayNames,
  TmLocalization,
  TmServerText,
  type TmActionFieldDescriptor,
  type TmEntityActionDescriptor,
  type TmId,
  type TmJsonObject,
  type TmLazyComponent,
  type TmProblemError,
  type TmStringTree,
} from '@tellma/core';
import { tmSplitErrorPath, tmWalkErrorPath } from '@tellma/core/error-paths';
import { TM_UI_TRANSLATE } from '@tellma/core-ui';
import { TmAlert } from '@tellma/core-ui/alert';
import { TmBusyOverlay } from '@tellma/core-ui/busy-overlay';
import { TmButton } from '@tellma/core-ui/button';
import { TmReference } from '@tellma/core-ui/copy';
import { TM_MODAL_DATA, TmModal, TmModalFooter, TmModalRef } from '@tellma/core-ui/modal';

import { ɵtmRunAction } from './tm-action-run';
import {
  ɵTM_ACTION_DIALOG,
  ɵtmLoad,
  type TmActionDialogContext,
  type TmActionDialogRunOptions,
  type TmEntityActionOptions,
  type TmEntityActionOutcome,
} from './tm-entity-actions';

// An entity action's dialog (spec 0031 §10.4), which `TmEntityActions.dialog`
// loads on first use: a `TmModal` titled with the action's label, holding
// either the dialog the action's declaration names or one generated from its
// arguments' fields, and the context both run the action through. A run goes
// under the busy overlay and through the confirmation loop; the server's
// argument errors land on the dialog's form by the error-path walk (§8.5),
// and every other failure lists in an alert atop the dialog, with the
// call's reference. Its strings ship here, under `tm.actionDialog`, since
// only this chunk reads them. A generated form's fields are the editing
// entry point's, loaded with `import()` before the modal opens, so this
// chunk never reaches the editors statically.

/**
 * The action dialog's English strings, under `tm.actionDialog`. Internal:
 * the dialog registers them.
 */
export const ɵTM_ACTION_DIALOG_STRINGS_EN = {
  failed: "The action couldn't be completed.",
  item: '{label}: {message}',
  outcomeUnknown: {
    title: "We couldn't confirm this",
    message: 'The connection dropped before the action finished. Check whether it went through before you run it again.',
  },
} as const satisfies TmStringTree;

/** What the dialog loads from `@tellma/core/entity-editing`, which it reaches only with `import()`. */
export interface ɵTmEntityEditingModule {
  /** A labelled value editor, one per generated argument. */
  readonly ɵTmValueFormField: Type<unknown>;
}

/** The kind of a server error placed on a dialog's field. */
const SERVER_ERROR = 'tm.server';

/** What the dialog host is opened with. */
interface TmActionDialogData {
  /** The entity. */
  readonly entity: string;
  /** The action. */
  readonly action: TmEntityActionDescriptor;
  /** The rows. */
  readonly ids: readonly TmId[];
  /** The invoker's initial arguments and request options. */
  readonly options: TmEntityActionOptions;
  /** The declared dialog's component; `null` for the generated one. */
  readonly component: Type<unknown> | null;
  /** The generated form's labelled value editor; `null` with a declared dialog. */
  readonly field: Type<unknown> | null;
  /** The outcome of the last successful run, which a dismissal still reports. */
  readonly ran: { outcome: TmEntityActionOutcome | null };
}

/** One argument of the generated dialog. */
interface TmGeneratedField {
  /** The argument's metadata. */
  readonly field: TmActionFieldDescriptor;
  /** Its label. */
  readonly label: Signal<string>;
  /** Its field in the dialog's form. */
  readonly tree: FieldTree<unknown>;
}

/** A server error on a generated field, as the run that reported it recorded it. */
interface TmPlacedError {
  /** The field's value at that run, as JSON: the error shows while the value is that one. */
  readonly value: string;
  /** The message. */
  readonly message: Signal<string>;
}

/** A failure as the dialog's alert shows it. */
interface TmShownFailure {
  /** The alert's heading. */
  readonly heading: Signal<string>;
  /** The problem's detail; empty for none. */
  readonly detail: Signal<string>;
  /** The errors no field shows. */
  readonly items: readonly Signal<string>[];
  /** The call's reference for support; empty for none. */
  readonly traceId: string;
}

/** The generated forms' per-dialog ids. */
let nextId = 0;

/**
 * A field's value as JSON, to compare with the value a server error was
 * reported for.
 *
 * @param value - The value.
 * @returns Its JSON.
 */
function json(value: unknown): string {
  return JSON.stringify(value ?? null);
}

/**
 * Opens an action's dialog and resolves once it closes. Internal: the
 * dialog's decision in the run's chunk calls it.
 *
 * @param injector - The root injector.
 * @param entity - The entity.
 * @param action - The action.
 * @param ids - The rows.
 * @param options - The initial arguments and request options.
 * @param editing - A generated form's editors, already loading; loaded here when absent.
 * @returns The outcome the dialog closed with, else that of its last successful run; `null` when nothing ran.
 * @throws TmApiError of kind `offline` when a declared dialog's code, or a generated form's editors, cannot load.
 */
export async function ɵtmOpenActionDialog(
  injector: Injector,
  entity: string,
  action: TmEntityActionDescriptor,
  ids: readonly TmId[],
  options: TmEntityActionOptions,
  editing?: Promise<ɵTmEntityEditingModule>,
): Promise<TmEntityActionOutcome | null> {
  injector.get(TmLocalization).ɵregisterStrings('actionDialog', ɵTM_ACTION_DIALOG_STRINGS_EN);
  // A declared dialog's code loads before the modal opens.
  const declared: TmLazyComponent | undefined = action.declaration?.dialog;
  const loaded = declared === undefined ? null : await ɵtmLoad(declared);
  const component = loaded === null ? null : typeof loaded === 'function' ? loaded : loaded.default;
  // A generated form's editors load before the modal opens, from the editing entry point.
  const field =
    component === null
      ? (await (editing ?? ɵtmLoad(() => import('@tellma/core/entity-editing' as string) as Promise<ɵTmEntityEditingModule>))).ɵTmValueFormField
      : null;
  const ran: TmActionDialogData['ran'] = { outcome: null };
  const ref = injector.get(TmModal).open<TmEntityActionOutcome | null>(ɵTmActionDialog, {
    size: 'md',
    title: action.label(),
    data: { entity, action, ids, options, component, field, ran } satisfies TmActionDialogData,
  });
  const result = await ref.closed;
  // A dismissal, or a close with no outcome, after a run that stayed open still reports that run.
  return (result.via === 'api' ? result.value : null) ?? ran.outcome;
}

/**
 * An action dialog's body: the alert of the last failed run, then the
 * declared dialog, or the generated form, one field per argument, and its
 * footer of Cancel and the action. Internal: opened by the action dialog.
 */
@Component({
  selector: 'tm-action-dialog',
  imports: [NgComponentOutlet, TmAlert, TmButton, TmModalFooter, TmReference],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: grid;
      gap: var(--space-4);
    }
    .tm-action-dialog__failure p,
    .tm-action-dialog__failure ul {
      margin: var(--space-1) 0 0;
    }
    .tm-action-dialog__failure ul {
      padding-inline-start: var(--space-5);
    }
    .tm-action-dialog__reference {
      margin-block-start: var(--space-2);
    }
    .tm-action-dialog__form {
      display: grid;
      gap: var(--space-4);
    }
  `,
  template: `
    <!-- The last failed run's errors that no field shows, and its reference -->
    @if (failure(); as shown) {
      <tm-alert class="tm-action-dialog__failure" kind="error" live="assertive" [heading]="shown.heading()">
        @if (shown.detail(); as detail) {
          <p>{{ detail }}</p>
        }
        @if (shown.items.length > 0) {
          <ul>
            @for (item of shown.items; track $index) {
              <li>{{ item() }}</li>
            }
          </ul>
        }
        @if (shown.traceId) {
          <tm-reference class="tm-action-dialog__reference" [value]="shown.traceId" />
        }
      </tm-alert>
    }
    @if (data.component; as component) {
      <!-- The declared dialog, which injects the context -->
      <ng-container *ngComponentOutlet="component; injector: outlet" />
    } @else {
      <!-- The generated form: one field per argument, in order -->
      <form class="tm-action-dialog__form" novalidate [id]="formId" (submit)="apply($event)">
        @for (generated of fields; track generated.field.name) {
          <ng-container *ngComponentOutlet="data.field; inputs: { label: generated.label(), property: generated.field.value, field: generated.tree }" />
        }
      </form>
      <div tmModalFooter>
        <button tmButton type="button" variant="secondary" (click)="context.close()">{{ cancel() }}</button>
        <button tmButton type="submit" [attr.form]="formId" [variant]="data.action.destructive ? 'danger' : 'primary'" [pending]="context.running()">
          {{ data.action.label() }}
        </button>
      </div>
    }
  `,
})
export class ɵTmActionDialog {
  readonly #injector = inject(Injector);
  readonly #ref = inject<TmModalRef<TmEntityActionOutcome | null>>(TmModalRef);
  readonly #busy = inject(TmBusyOverlay);
  readonly #localization = inject(TmLocalization);
  readonly #serverText = inject(TmServerText);
  readonly #names = inject(TmDisplayNames);
  readonly #running = signal(false);
  readonly #failure = signal<TmShownFailure | null>(null);

  /** The server's errors on the generated form's fields, by argument, from the last run. */
  readonly #placed = signal<Readonly<Record<string, readonly TmPlacedError[]>>>({});

  /** The action, its rows and the invoker's options. */
  protected readonly data = inject<TmActionDialogData>(TM_MODAL_DATA);

  /** The last failed run's errors that no field shows. */
  protected readonly failure = this.#failure.asReadonly();

  /** Cancel's label, the component library's. */
  protected readonly cancel = inject(TM_UI_TRANSLATE)('confirm.cancel');

  /** The generated form's id, which its submit button names. */
  protected readonly formId = `tm-action-dialog-${nextId++}`;

  /** The context the dialog runs the action through. */
  protected readonly context: TmActionDialogContext = {
    entity: this.data.entity,
    action: this.data.action,
    ids: this.data.ids,
    initial: this.data.options.arguments ?? {},
    running: this.#running.asReadonly(),
    run: (args, options) => this.#run(args, options ?? {}),
    close: (outcome) => this.#ref.close(outcome ?? null),
  };

  /** The declared dialog's injector, which provides the context. */
  protected readonly outlet = Injector.create({ providers: [{ provide: ɵTM_ACTION_DIALOG, useValue: this.context }], parent: this.#injector });

  /** The generated form's model: each argument's initial value, else its empty one. */
  readonly #model = signal<Readonly<Record<string, unknown>>>(
    Object.fromEntries(
      (this.data.action.fields ?? []).map((field) => [
        field.name,
        this.context.initial[field.name] ?? (field.value.array ? [] : field.value.type === 'bool' && !field.value.nullable ? false : null),
      ]),
    ),
  );

  /**
   * The generated form: each argument required as its metadata says (a
   * yes/no needs only an answer, since "no" is one), and the last run's
   * server errors on each, while its value is the one they were reported for.
   */
  readonly #form = form(this.#model, (root) => {
    const paths = root as unknown as Readonly<Record<string, SchemaPathTree<unknown>>>;
    for (const field of this.data.action.fields ?? []) {
      const path = paths[field.name]!;
      if (field.required && field.value.type === 'bool') {
        validate(path, ({ value }) => (value() === null || value() === undefined ? requiredError() : undefined));
      } else if (field.required) {
        required(path);
      }
      validate(path, ({ value }) => {
        const now = json(value());
        return (this.#placed()[field.name] ?? []).filter((placed) => placed.value === now).map((placed) => ({ kind: SERVER_ERROR, message: placed.message() }));
      });
    }
  });

  /** The generated dialog's fields, in the metadata's order. */
  protected readonly fields: readonly TmGeneratedField[] = (this.data.action.fields ?? []).map((field) => ({
    field,
    label: field.label,
    tree: (this.#form as unknown as Readonly<Record<string, FieldTree<unknown>>>)[field.name]!,
  }));

  /**
   * Runs the generated dialog's action, once its own checks pass.
   *
   * @param event - The form's submit event.
   */
  protected apply(event: Event): void {
    event.preventDefault();
    const state = this.#form();
    state.markAsTouched();
    // A server error does not stop a retry; a client check does.
    if (!state.errorSummary().some((error) => error.kind !== SERVER_ERROR)) {
      void this.#run(this.#model() as TmJsonObject, { form: this.#form as FieldTree<unknown> as FieldTree<TmJsonObject> });
    }
  }

  /**
   * Runs the action under the busy overlay, then closes with the outcome
   * or shows why it failed.
   *
   * @param args - The arguments.
   * @param options - The form, whether to stay open, a subset of rows and the failures to rethrow.
   * @returns The outcome; `null` when errors show.
   */
  async #run<A>(args: A, options: TmActionDialogRunOptions<A>): Promise<TmEntityActionOutcome | null> {
    const { entity, action, ids } = this.data;
    let sent: readonly TmId[] = options.ids ?? ids;
    // Every error of the last run clears as this one starts.
    this.#failure.set(null);
    this.#placed.set({});
    this.#running.set(true);
    try {
      const outcome = await this.#busy.run(
        ɵtmRunAction(this.#injector, entity, action.name, options.ids ?? ids, { ...this.data.options, arguments: args as TmJsonObject }, (rows) => (sent = rows)),
      );
      // A declined warning keeps the dialog open, as it was; a success is reported however the dialog closes.
      if (outcome.kind !== 'declined') {
        this.data.ran.outcome = outcome;
        if (!options.keepOpen) {
          this.#ref.close(outcome);
        }
      }
      return outcome;
    } catch (error) {
      if (error instanceof TmApiError && options.rethrow?.includes(error.kind)) {
        throw error;
      }
      if (!(error instanceof TmApiError)) {
        // Not the server's answer: reported as the app's error, and shown as a failure.
        this.#injector.get(ErrorHandler).handleError(error);
      }
      await this.#show(error, options.form as FieldTree<unknown> | undefined, args as Readonly<Record<string, unknown>>, sent);
      return null;
    } finally {
      this.#running.set(false);
    }
  }

  /**
   * Shows a failed run: its argument errors on the form's fields, the rest
   * in the alert.
   *
   * @param error - The failure.
   * @param tree - The form the argument errors land on.
   * @param args - The arguments the run sent, whose values the generated form's errors are recorded for.
   * @param sent - The rows the last send carried, which a row error's index points into.
   */
  async #show(error: unknown, tree: FieldTree<unknown> | undefined, args: Readonly<Record<string, unknown>>, sent: readonly TmId[]): Promise<void> {
    const { entity, action } = this.data;
    const problem = error instanceof TmApiError ? error.problem : null;
    const generated = this.data.component === null;
    const placed: ValidationError.WithFieldTree[] = [];
    const recorded: Record<string, TmPlacedError[]> = {};
    const items: Signal<string>[] = [];
    // An item that names a field or a row reads "{label}: {message}".
    const listed = (label: (() => string) | null, message: Signal<string>): Signal<string> =>
      label === null ? message : computed(() => this.#localization.ɵtext('tm.actionDialog.item', { label: label(), message: message() })());

    // --- Each validation item: on its field, else listed with what it names ---
    const errors = problem?.['errors'];
    for (const item of Array.isArray(errors) ? (errors as readonly TmProblemError[]) : []) {
      const path = typeof item['path'] === 'string' ? tmSplitErrorPath(item['path']) : null;
      if (path?.[0] === 'arguments') {
        const field = action.fields?.find((candidate) => candidate.name === path[1]);
        const message = this.#serverText.error(item);
        if (generated && field !== undefined && path.length === 2) {
          // The generated form records the error with the value it was sent for (spec 0031 §8.5 step 3).
          (recorded[field.name] ??= []).push({ value: json(args[field.name]), message });
          continue;
        }
        const target = generated || tree === undefined || path.length < 2 ? null : tmWalkErrorPath(tree, path.slice(1));
        if (target?.resolved) {
          placed.push({ kind: SERVER_ERROR, message: message(), fieldTree: target.field });
        } else {
          items.push(listed(field === undefined ? null : field.label, message));
        }
      } else if ((path?.[0] === 'ids' && typeof path[1] === 'number') || path?.[0] === 'id') {
        // A row's error, by its index in the rows sent.
        const row = sent[path[0] === 'id' ? 0 : (path[1] as number)];
        const message = this.#serverText.error(item, entity);
        if (row === undefined) {
          items.push(message);
        } else {
          this.#names.resolve(entity, [row]);
          const name = this.#names.label(entity, row);
          items.push(listed(() => name() ?? String(row), message));
        }
      } else {
        items.push(this.#serverText.error(item, entity));
      }
    }
    this.#placed.set(recorded);
    if (tree !== undefined && placed.length > 0) {
      // A declared dialog's form: each error clears once its field is edited.
      await submit(tree, { action: () => Promise.resolve(placed), ignoreValidators: 'all' });
    }

    // --- The alert: the problem's title and detail, unless every error is on a field ---
    const onFields = placed.length + Object.keys(recorded).length;
    if (error instanceof TmApiError && error.kind === 'validation' && items.length === 0 && onFields > 0) {
      return;
    }
    const text = problem === null ? null : this.#serverText.problem(problem);
    const offline = error instanceof TmApiError && error.kind === 'offline';
    // A write whose answer never came may have gone through: it is worded apart from one never sent.
    const words = offline ? (error.code === 'outcome-unknown' ? 'tm.actionDialog.outcomeUnknown' : 'tm.errors.offline') : null;
    const failed = this.#localization.ɵtext('tm.actionDialog.failed');
    this.#failure.set({
      heading: words !== null ? this.#localization.ɵtext(`${words}.title`) : computed(() => text?.().title ?? failed()),
      detail: words !== null ? this.#localization.ɵtext(`${words}.message`) : computed(() => text?.().detail ?? ''),
      items,
      traceId: error instanceof TmApiError ? error.traceId : '',
    });
  }
}
