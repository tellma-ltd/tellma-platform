// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, inject, Injectable, InjectionToken, Injector, type Signal } from '@angular/core';
import type { FieldTree } from '@angular/forms/signals';
import {
  TmApiError,
  TmEntities,
  TmLaunchCache,
  TmLocalization,
  type TmActionConfirmation,
  type TmActionMetadata,
  type TmApiErrorKind,
  type TmEntityActionDeclaration,
  type TmEntityActionDescriptor,
  type TmEntityDescriptor,
  type TmId,
  type TmJson,
  type TmJsonObject,
} from '@tellma/core';

import { ɵtmEditable } from './tm-editor-kind';
import { ɵTM_ENTITY_STRINGS_EN } from './tm-entity-strings';

// Entity actions (spec 0031 §10.4): the requests an entity's actions take,
// built from their metadata so the details page and the search page's bulk
// actions run them alike, through the confirmation loop (§9.3). An action
// with arguments opens a dialog generated from their fields, or the one its
// declaration names. The run and the dialogs load on first use, since a
// page's first frame only lists the actions. The busy overlay and a
// destructive action's confirmation belong to the caller, which knows
// whether its trigger is still on screen; the dialog's own runs take the
// overlay themselves.

declare const ngDevMode: unknown;

/**
 * What an action that returns entities answers: the rows it changed, with
 * their related rows and extras. Members the server sends as `null` are
 * left out.
 *
 * @example
 * ```ts
 * const outcome = await actions.run('acme.Customer', 'set-credit-limit', [77], { arguments: { creditLimit: '250000' } });
 * if (outcome.kind === 'entities') {
 *   const [customer] = outcome.result.entities;
 * }
 * ```
 */
export interface TmEntitiesResult<T = TmJsonObject> {
  /** The rows' ids, in the stack's key type: numbers, or decimal text for a `long` key. */
  readonly ids: readonly TmId[];
  /** The rows. */
  readonly entities: readonly T[];
  /** The related rows, by entity name. */
  readonly related?: Readonly<Record<string, readonly TmJsonObject[]>>;
  /** The extras asked for, by name, such as `permissions`. */
  readonly extras?: Readonly<Record<string, TmJson>>;
}

/**
 * How an entity action ended: with the entities it changed, its own
 * result, a background job it started, or the user's decline of a warning.
 */
export type TmEntityActionOutcome =
  | { readonly kind: 'entities'; readonly result: TmEntitiesResult }
  | { readonly kind: 'own'; readonly result: TmJson }
  | { readonly kind: 'job'; readonly jobId: number }
  | { readonly kind: 'declined' };

/** How {@link TmEntityActions.run} sends an action. */
export interface TmEntityActionOptions {
  /** The action's arguments, by their wire names. */
  readonly arguments?: TmJsonObject;
  /** The extras an action that returns entities reads with them; the entity's extras by default. */
  readonly include?: readonly string[];
  /** Whether an action that returns entities returns them; `true` by default. A bulk caller passes `false` and re-reads its list. */
  readonly returnEntities?: boolean;
}

/** How an action dialog's {@link TmActionDialogContext.run} sends the action and shows what fails. */
export interface TmActionDialogRunOptions<A> {
  /** The dialog's form, on which the server's argument errors land. */
  readonly form?: FieldTree<A>;
  /** Whether the dialog stays open after success, to show the outcome; it closes by default. */
  readonly keepOpen?: boolean;
  /** The rows to run the action on, such as a subset to retry; the dialog's own rows by default. */
  readonly ids?: readonly TmId[];
  /** The failures that reject with their `TmApiError`, for the dialog to read, rather than listing in the dialog's alert. */
  readonly rethrow?: readonly TmApiErrorKind[];
}

/**
 * What an action dialog reads and drives: the action and its rows, the
 * arguments its invoker supplied, and the run that sends them. A declared
 * dialog injects it with {@link injectTmActionDialog}.
 */
export interface TmActionDialogContext<A = TmJsonObject> {
  /** The entity, such as `acme.Customer`. */
  readonly entity: string;
  /** The action. */
  readonly action: TmEntityActionDescriptor;
  /** The rows the action runs on. */
  readonly ids: readonly TmId[];
  /** The arguments the invoker supplied, else none: the dialog's form starts from them. */
  readonly initial: Partial<A>;
  /** Whether a run is in flight, so the dialog's action button shows it pending. */
  readonly running: Signal<boolean>;
  /**
   * Sends the action with the arguments, under the busy overlay and through
   * the server's warnings. On success the dialog closes with the outcome,
   * unless `keepOpen`, after which closing it any way still reports that
   * outcome; a declined warning keeps it open. The server's
   * argument errors land on `form`; every other failure lists in an alert
   * atop the dialog, except the kinds `rethrow` names.
   *
   * @param args - The arguments.
   * @param options - The form, whether to stay open, a subset of rows, and the failures to rethrow.
   * @returns The outcome; `null` when the dialog shows the errors.
   * @throws TmApiError of a kind `rethrow` names.
   */
  run(args: A, options?: TmActionDialogRunOptions<A>): Promise<TmEntityActionOutcome | null>;
  /**
   * Closes the dialog: `TmEntityActions.dialog` resolves to the outcome.
   *
   * @param outcome - The outcome to report; when omitted, the last successful run's, else `null` (nothing ran).
   */
  close(outcome?: TmEntityActionOutcome | null): void;
}

/** Which of an entity's actions {@link TmEntityActions.offered} lists. */
export interface TmOfferedActionsOptions {
  /** The record a details page shows, which each declaration's `visible` reads. */
  readonly record?: TmJsonObject;
  /** Whether the actions run over a search page's selection: single-target actions and those declared `bulk: false` are left out, and `visible` is not read. */
  readonly bulk?: boolean;
}

/** The context of the open action dialog. Internal: the dialog provides it to a declared dialog. */
export const ɵTM_ACTION_DIALOG = new InjectionToken<TmActionDialogContext<unknown>>('TM_ACTION_DIALOG');

/**
 * The context of the action dialog a component renders in, for a dialog
 * an action's declaration names in place of the generated one.
 *
 * @returns The context.
 * @throws Error outside an action dialog.
 *
 * @example
 * ```ts
 * export default class SetCreditLimitDialog {
 *   readonly #dialog = injectTmActionDialog<{ creditLimit: string | null }>();
 *   readonly model = signal({ creditLimit: this.#dialog.initial.creditLimit ?? null });
 *   readonly form = form(this.model);
 *   protected async apply(): Promise<void> {
 *     await this.#dialog.run(this.model(), { form: this.form });
 *   }
 * }
 * ```
 */
export function injectTmActionDialog<A = TmJsonObject>(): TmActionDialogContext<A> {
  const context = inject(ɵTM_ACTION_DIALOG, { optional: true });
  if (context === null) {
    throw new Error('injectTmActionDialog() is called outside an action dialog.');
  }
  return context as TmActionDialogContext<A>;
}

/**
 * Runs an entity's actions by their metadata: the request each takes, sent
 * through the server's warnings, and the dialog of an action with
 * arguments, generated from their fields or declared. It also lists the
 * actions a page offers and the confirmation a destructive one asks.
 *
 * @example
 * ```ts
 * const actions = inject(TmEntityActions);
 * const outcome = await actions.dialog('acme.Customer', 'set-credit-limit', [77]);
 * // posts customers/set-credit-limit with { ids: [77], returnEntities: true, include: ['permissions'], arguments: { creditLimit: '250000' } }
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmEntityActions {
  readonly #injector = inject(Injector);
  readonly #entities = inject(TmEntities);

  /** The entities whose unoffered actions were already reported in development. */
  readonly #warned = new Set<string>();

  /** Creates the service, registering its strings. */
  constructor() {
    inject(TmLocalization).ɵregisterStrings('entity', ɵTM_ENTITY_STRINGS_EN);
  }

  /**
   * The actions a page offers on an entity's records, those its
   * declaration lists first, in that order, then the rest in metadata order:
   * the entity's own actions, not built in, that take no arguments, whose
   * every argument takes a control, or that declare a dialog, and that the
   * declaration's `visible` admits for the record. Whether the user may run
   * each is the caller's to show, as a disabled action with its reason. In
   * development, the actions no page can offer are reported once per
   * entity, unless the declaration lists them. It needs `TmEntities.load()`.
   *
   * @param entity - The entity's name.
   * @param options - The record a details page shows, or a search page's selection.
   * @returns The actions offered.
   */
  offered(entity: string, options: TmOfferedActionsOptions = {}): readonly TmEntityActionDescriptor[] {
    const descriptor = this.#entities.get(entity);
    if (typeof ngDevMode !== 'undefined' && ngDevMode && !this.#warned.has(entity)) {
      this.#warned.add(entity);
      warnUnoffered(descriptor);
    }
    const { record, bulk } = options;
    const userId = record === undefined || bulk ? undefined : this.#injector.get(TmLaunchCache).docs.me()?.user.id;
    // The declaration's order first; the sort is stable, so the rest keep the metadata's.
    const entries = descriptor.declaration['actions'];
    const declared = Array.isArray(entries) ? (entries as readonly TmEntityActionDeclaration<string>[]).map((entry) => entry.name) : [];
    const rank = (action: TmEntityActionDescriptor): number => {
      const at = declared.indexOf(action.name);
      return at < 0 ? declared.length : at;
    };
    const offered = descriptor.actions.filter((action) => {
      const declaration = action.declaration;
      if (!ɵtmOfferable(action)) {
        return false;
      }
      if (bulk) {
        // A selection never runs a single-target action, and `visible` is the server's to judge per row.
        return !action.singleTarget && declaration?.bulk !== false;
      }
      const visible = declaration?.visible;
      return record === undefined || visible === undefined || (userId !== undefined && visible(record as never, { userId }));
    });
    return offered.sort((a, b) => rank(a) - rank(b));
  }

  /**
   * The confirmation a destructive action asks before it runs: its
   * declaration's, else "Run “{action}” on {n} items?" with "This can't be
   * undone.". Its arguments carry the action's label as `action` and the
   * row count as `n`. It needs `TmEntities.load()`.
   *
   * @param entity - The entity's name.
   * @param action - The action's name.
   * @param n - How many rows it runs on.
   * @returns The confirmation, for the page action that runs it.
   * @throws Error for an action the entity does not have.
   */
  confirmation(entity: string, action: string, n: number): TmActionConfirmation {
    const descriptor = this.#action(this.#entities.get(entity), action);
    const declared = descriptor.declaration?.confirm;
    return {
      titleKey: declared?.titleKey ?? 'tm.entity.confirmAction',
      messageKey: declared?.messageKey ?? 'tm.entity.confirmActionMessage',
      args: computed(() => ({ action: descriptor.label(), n, ...declared?.args?.() })),
    };
  }

  /**
   * Runs an action on rows at once, through the server's warnings: the
   * user confirms them, or continues without the rows they name. Its code
   * loads on first use.
   *
   * @param entity - The entity's name.
   * @param action - The action's name.
   * @param ids - The rows.
   * @param options - The arguments, the extras and whether to return the entities.
   * @returns The outcome; `declined` when the user declined a warning.
   * @throws TmApiError when the action fails, or an `offline` one when its code cannot load.
   */
  async run(entity: string, action: string, ids: readonly TmId[], options: TmEntityActionOptions = {}): Promise<TmEntityActionOutcome> {
    const { ɵtmRunAction } = await ɵtmLoad(() => import('./tm-action-run'));
    return ɵtmRunAction(this.#injector, entity, action, ids, options);
  }

  /**
   * Opens an action's dialog, generated from its arguments' fields or the
   * one its declaration names, filled from the supplied arguments; an
   * action with neither arguments nor a declared dialog runs at once. Its
   * code loads on first use.
   *
   * @param entity - The entity's name.
   * @param action - The action's name.
   * @param ids - The rows.
   * @param options - The initial arguments, the extras and whether to return the entities.
   * @returns The outcome of the dialog's last successful run, however it closed; `null` when nothing ran.
   * @throws Error for an action whose arguments no dialog can take; an `offline` TmApiError when the dialog's code cannot load.
   */
  async dialog(entity: string, action: string, ids: readonly TmId[], options: TmEntityActionOptions = {}): Promise<TmEntityActionOutcome | null> {
    const { ɵtmDialog } = await ɵtmLoad(() => import('./tm-action-run'));
    return ɵtmDialog(this.#injector, entity, action, ids, options);
  }

  /**
   * An entity's action by name.
   *
   * @param descriptor - The entity.
   * @param name - The action's name.
   * @returns The action.
   * @throws Error for an action the entity does not have.
   */
  #action(descriptor: TmEntityDescriptor, name: string): TmEntityActionDescriptor {
    const action = descriptor.actions.find((candidate) => candidate.name === name);
    if (action === undefined) {
      throw new Error(`The entity '${descriptor.name}' has no action '${name}'.`);
    }
    return action;
  }

}

/**
 * Reports, in development, the actions of an entity no page can offer that
 * its declaration does not list: an entry, such as one whose `visible` is
 * always false, says the omission is on purpose. A production build drops
 * it with its only call.
 *
 * @param descriptor - The entity.
 */
function warnUnoffered(descriptor: TmEntityDescriptor): void {
  const unoffered = descriptor.actions.filter((action) => !action.builtIn && action.declaration === undefined && !ɵtmOfferable(action));
  if (unoffered.length > 0) {
    const names = unoffered.map((action) => `'${action.name}'`).join(', ');
    console.warn(`[tellma] ${descriptor.name}: no page offers ${names}; declare a dialog, or list each in the declaration to leave it out.`);
  }
}

/**
 * Loads a chunk of the entity actions' code. Internal: the actions and their
 * dialogs load each part on first use.
 *
 * @param chunk - The chunk's `import()`.
 * @returns The chunk's module.
 * @throws TmApiError of kind `offline` when the chunk cannot load, as a call that never reached the server.
 */
export async function ɵtmLoad<T>(chunk: () => Promise<T>): Promise<T> {
  try {
    return await chunk();
  } catch {
    // Offline, or a deploy removed the chunk: nothing was sent.
    throw new TmApiError({ kind: 'offline', code: 'network' });
  }
}

/**
 * Whether an action takes no arguments. Internal: the actions and their
 * dialog read it.
 *
 * @param action - The action.
 * @returns Whether it takes none.
 */
export function ɵtmTakesNoArguments(action: TmActionMetadata): boolean {
  return action.arguments === undefined && (action.fields?.length ?? 0) === 0;
}

/**
 * Whether a page can offer an action at all: an entity action, not built
 * in, that takes no arguments, declares a dialog, or lists its arguments'
 * fields with a control for each. Internal: the actions and their dialog
 * read it.
 *
 * @param action - The action.
 * @returns Whether it can be offered.
 */
export function ɵtmOfferable(action: TmEntityActionDescriptor): boolean {
  const fields = action.fields ?? [];
  return (
    action.kind === 'entity' &&
    !action.builtIn &&
    (ɵtmTakesNoArguments(action) ||
      action.declaration?.dialog !== undefined ||
      (fields.length > 0 && fields.every((field) => ɵtmEditable(field.value))))
  );
}
