// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import {
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  Injectable,
  Injector,
  signal,
  untracked,
  type Signal,
} from '@angular/core';

import { TmApiError } from '../api/tm-api-error';
import { TM_DEV_MODE } from '../app/tm-dev-mode';
import type { TmJson } from '../json/tm-json';
import { TmLocalization } from '../l10n/tm-localization';
import { TmShortcuts } from '../nav/tm-shortcuts';
import { TmTelemetry } from '../observe/tm-telemetry';
import type { TmPageContext } from './tm-page-context';
import type { TmActionOrigin, TmActionResult, TmPageAction } from './tm-page-action';

// The page-context registry and the action dispatcher (spec 0026 §9.2,
// §9.4, §9.5). One root registry holds every live context and every action
// registered through a page scope or outside one. It publishes each context
// with its actions merged, keeps the top context's shortcuts live, and runs
// every invocation through the dispatcher's steps, whatever its origin.
// `TmPageContexts` and `TmPageActions` are its public faces: a page scope
// provides its own instances of both, bound to the scope.

declare const ngDevMode: unknown;

/**
 * One page scope: the injector region of a routed page or of a
 * context-bearing dialog, which owns one context and the actions registered
 * inside it. Internal: `provideTmPageScope` provides it.
 */
@Injectable()
export class TmPageScope {
  /** The scope's host element, inside the dialog for a dialog's scope; `null` outside a component. */
  readonly element: Element | null = inject(ElementRef, { optional: true })?.nativeElement ?? null;

  /** Creates the scope, whose contexts and actions end with it. */
  constructor() {
    const registry = inject(TmPageRegistry);
    inject(DestroyRef).onDestroy(() => registry.ɵdrop(this));
  }
}

/** One registered context: the context, its scope, and the context as published, its actions merged. */
interface TmContextEntry {
  /** The context as its publisher registered it. */
  readonly context: TmPageContext;
  /** The scope it was registered in, or `null` outside any. */
  readonly scope: TmPageScope | null;
  /** The context as the registry publishes it. */
  readonly view: TmPageContext;
  /** Ends the registration's hold on its `DestroyRef`. */
  readonly off: () => void;
}

/** One action registered through `TmPageActions`: the action and its scope. */
interface TmActionEntry {
  /** The action, its defaults applied. */
  readonly action: TmPageAction;
  /** The scope it was registered in, or `null` outside any, which adds it to the routed page's context. */
  readonly scope: TmPageScope | null;
  /** Ends the registration's hold on its `DestroyRef`. */
  readonly off: () => void;
}

/** The result for an action no context offers. */
const NOT_FOUND: TmActionResult = { outcome: 'refused', reason: 'not-found' };

/** An action id: a lowercase scope, then one to four lowercase kebab-case parts. */
const ACTION_ID = /^[a-z][a-z0-9]*(\.[a-z0-9][a-z0-9-]*){1,4}$/;

/** Toolbar actions first, then by order (default 100); `sort` keeps the registration order among equals. */
function byPlace(a: TmPageAction, b: TmPageAction): number {
  return (a.group === 'menu' ? 1 : 0) - (b.group === 'menu' ? 1 : 0) || (a.order ?? 100) - (b.order ?? 100);
}

/** Whether an action's arguments schema requires members, so the action can take no shortcut. */
function requiresArgs(action: TmPageAction): boolean {
  const required = action.params?.['required'];
  return Array.isArray(required) && required.length > 0;
}

/**
 * Throws the development error for a malformed action: a malformed id, a
 * destructive action without its confirmation, or a shortcut on an action
 * whose arguments require members. The registry calls it only in
 * development, so production drops it with its messages.
 *
 * @param action - The action.
 * @throws Error when the action is malformed.
 */
function checkAction(action: TmPageAction): void {
  if (!ACTION_ID.test(action.id)) {
    throw new Error(
      `The page action id "${action.id}" must be a lowercase scope and one to four kebab-case parts, such as "acme.credit-review.suggest-limits".`,
    );
  }
  if (action.destructive && action.confirm === undefined) {
    throw new Error(`The destructive page action "${action.id}" needs a confirm with its title and message keys.`);
  }
  if (action.shortcut !== undefined && requiresArgs(action)) {
    throw new Error(`The page action "${action.id}" requires arguments, so it can take no shortcut.`);
  }
}

/**
 * The page contexts and actions of the app, and the dispatcher that runs
 * every invocation. Internal: `TmPageContexts` and `TmPageActions` read and
 * write it.
 */
@Injectable({ providedIn: 'root' })
export class TmPageRegistry {
  readonly #injector = inject(Injector);
  readonly #telemetry = inject(TmTelemetry);
  readonly #devMode = inject(TM_DEV_MODE);

  /** The live contexts, in registration order. */
  readonly #contexts = signal<readonly TmContextEntry[]>([]);
  /** The live actions registered through `TmPageActions`, in registration order. */
  readonly #actions = signal<readonly TmActionEntry[]>([]);
  /** The ids being invoked. */
  readonly #running = signal<ReadonlySet<string>>(new Set());
  /** The ids whose confirmation is open: busy too, though not yet running. */
  readonly #confirming = new Set<string>();

  /** The newest live routed context's entry. */
  readonly #current = computed(() => {
    const contexts = this.#contexts();
    for (let index = contexts.length - 1; index >= 0; index--) {
      if (!contexts[index]!.context.overlay) {
        return contexts[index]!;
      }
    }
    return null;
  });

  /** The routed page's context, its actions merged. */
  readonly current: Signal<TmPageContext | null> = computed(() => this.#current()?.view ?? null);

  /** The open context-bearing dialogs' contexts, bottom to top, their actions merged. */
  readonly overlays: Signal<readonly TmPageContext[]> = computed(() =>
    this.#contexts()
      .filter((entry) => entry.context.overlay)
      .map((entry) => entry.view),
  );

  /** The topmost dialog's context, else the routed page's. */
  readonly top: Signal<TmPageContext | null> = computed(() => this.overlays().at(-1) ?? this.current());

  /** The ids being invoked. */
  readonly running: Signal<ReadonlySet<string>> = this.#running.asReadonly();

  /** Creates the registry, which keeps the top context's shortcuts live from now on. */
  constructor() {
    const shortcuts = inject(TmShortcuts);
    // --- The top context's shortcuts, live while it is on top ---
    // A change of top context ends every registration of the one before; a
    // change of its actions ends the gone ones' and adds the new ones'. An
    // action whose shortcut was refused keeps its place, so it is not
    // refused again at every change of the list.
    let shown: TmPageContext | null = null;
    const live = new Map<TmPageAction, () => void>();
    effect(() => {
      const top = this.top();
      const actions = top?.actions() ?? [];
      untracked(() => {
        if (top !== shown) {
          live.forEach((remove) => remove());
          live.clear();
          shown = top;
        }
        const wanted = actions.filter((action) => action.shortcut !== undefined);
        for (const [action, remove] of live) {
          if (!wanted.includes(action)) {
            remove();
            live.delete(action);
          }
        }
        // A dialog's shortcuts belong to its host, so they run while it is the top modal.
        const dialog = top?.overlay ? (this.#contexts().find((entry) => entry.view === top)?.scope?.element ?? null) : null;
        let failure: unknown;
        for (const action of wanted) {
          if (live.has(action)) {
            continue;
          }
          live.set(action, () => undefined);
          try {
            // Such an action takes no shortcut; development says so.
            if (requiresArgs(action)) {
              if (typeof ngDevMode !== 'undefined' && ngDevMode && this.#devMode()) {
                checkAction(action);
              }
              continue;
            }
            const remove = shortcuts.ɵregisterPage(
              {
                id: action.id,
                keys: action.shortcut!,
                labelKey: action.labelKey,
                scope: 'page',
                run: () => void this.invoke(action.id, {}, 'shortcut'),
              },
              dialog,
            );
            if (remove !== null) {
              live.set(action, remove);
            }
          } catch (error) {
            // Development refused it: the others still register, then the first refusal throws.
            failure ??= error;
          }
        }
        if (failure !== undefined) {
          throw failure;
        }
      });
    });
  }

  /**
   * Registers a context in a scope until `destroyRef` ends or the scope does.
   *
   * @param context - The context.
   * @param scope - The scope, or `null` outside any.
   * @param destroyRef - What ends the registration.
   */
  ɵregister(context: TmPageContext, scope: TmPageScope | null, destroyRef: DestroyRef): void {
    const entry: TmContextEntry = {
      context,
      scope,
      view: { ...context, actions: computed(() => this.#merged(entry)) },
      off: destroyRef.onDestroy(() => this.#contexts.update((list) => list.filter((held) => held !== entry))),
    };
    this.#contexts.update((list) => [...list, entry]);
  }

  /**
   * Adds an action to a scope's context, or to the routed page's outside any
   * scope, until `destroyRef` ends or the scope does.
   *
   * @param action - The action, its defaults applied.
   * @param scope - The scope, or `null` outside any.
   * @param destroyRef - What ends the registration.
   */
  ɵadd(action: TmPageAction, scope: TmPageScope | null, destroyRef: DestroyRef): void {
    if (typeof ngDevMode !== 'undefined' && ngDevMode && this.#devMode()) {
      checkAction(action);
    }
    const entry: TmActionEntry = {
      action,
      scope,
      off: destroyRef.onDestroy(() => this.#actions.update((list) => list.filter((held) => held !== entry))),
    };
    this.#actions.update((list) => [...list, entry]);
  }

  /**
   * Ends every registration of a destroyed scope.
   *
   * @param scope - The scope.
   */
  ɵdrop(scope: TmPageScope): void {
    const ends = (entry: { readonly scope: TmPageScope | null; readonly off: () => void }): boolean => {
      if (entry.scope === scope) {
        entry.off();
        return false;
      }
      return true;
    };
    this.#contexts.update((list) => list.filter(ends));
    this.#actions.update((list) => list.filter(ends));
  }

  /**
   * Invokes an action through the dispatcher's steps.
   *
   * @param id - The action's id.
   * @param args - Its arguments.
   * @param origin - Who invokes it.
   * @returns How the invocation ended.
   */
  async invoke(id: string, args: TmJson, origin: TmActionOrigin): Promise<TmActionResult> {
    const started = performance.now();
    // --- Step 1: the top context, then the lower dialogs, then the routed page ---
    const action = this.#find(id);
    if (action === undefined) {
      return NOT_FOUND;
    }
    const result = await this.#dispatch(action, args, origin);
    // --- Step 6: the usage signal, sampled ---
    this.#telemetry.event(
      'tm.action.invoked',
      { action: id, origin, outcome: result.outcome, ms: Math.round(performance.now() - started) },
      { sampled: true },
    );
    return result;
  }

  /** The context's actions: its own, the own actions of the routed contexts it replaced, then its scope's, in place order. */
  #merged(entry: TmContextEntry): readonly TmPageAction[] {
    const { context, scope } = entry;
    const contexts = this.#contexts();
    // The routed contexts registered before it in its scope, which it replaced: newest first.
    const replaced =
      context.overlay || scope === null
        ? []
        : contexts
            .slice(0, Math.max(contexts.indexOf(entry), 0))
            .filter((held) => held.scope === scope && !held.context.overlay)
            .reverse()
            .flatMap((held) => held.context.actions());
    // Actions registered outside any scope join the routed page's context.
    const current = this.#current();
    const scoped = this.#actions()
      .filter((held) => (held.scope === null ? entry === current : held.scope === scope))
      .map((held) => held.action);
    return [...context.actions(), ...replaced, ...scoped].sort(byPlace);
  }

  /** The action with the id in the top context, then the lower dialogs', then the routed page's. */
  #find(id: string): TmPageAction | undefined {
    return untracked(() => {
      const current = this.current();
      for (const context of [...this.overlays()].reverse().concat(current === null ? [] : [current])) {
        const action = context.actions().find((held) => held.id === id);
        if (action !== undefined) {
          return action;
        }
      }
      return undefined;
    });
  }

  /** Steps 2 to 5 for a found action. */
  async #dispatch(action: TmPageAction, args: TmJson, origin: TmActionOrigin): Promise<TmActionResult> {
    const { id } = action;
    let refusal = this.#gate(action, origin);
    if (refusal !== null) {
      return refusal;
    }

    // --- Step 4: a destructive action asks first, whoever invokes it ---
    // The id is busy while the question is open, so a second invocation
    // cannot ask again; the action is checked again once answered, since the
    // page may have changed meanwhile.
    if (action.destructive) {
      if (typeof ngDevMode !== 'undefined' && ngDevMode && this.#devMode()) {
        checkAction(action);
      }
      this.#confirming.add(id);
      try {
        if (!(await this.#confirm(action))) {
          return { outcome: 'cancelled' };
        }
      } catch (error) {
        return { outcome: 'failed', error };
      } finally {
        this.#confirming.delete(id);
      }
      if (this.#find(id) !== action) {
        return NOT_FOUND;
      }
      refusal = this.#gate(action, origin);
      if (refusal !== null) {
        return refusal;
      }
    }

    // --- Step 5: run it, busy meanwhile ---
    this.#running.update((ids) => new Set(ids).add(id));
    try {
      return await action.invoke(args, origin);
    } catch (error) {
      // An API error is the action's to present; anything else is a fault worth reporting.
      if (!(error instanceof TmApiError)) {
        this.#telemetry.error(error, { action: id });
      }
      return { outcome: 'failed', error };
    } finally {
      this.#running.update((ids) => {
        const rest = new Set(ids);
        rest.delete(id);
        return rest;
      });
    }
  }

  /** Steps 2 and 3: a disabled action is refused with its reason, announced to a user; a busy one is refused. */
  #gate(action: TmPageAction, origin: TmActionOrigin): TmActionResult | null {
    return untracked(() => {
      if (!action.enabled()) {
        const message = action.disabledReason();
        if (message === null) {
          return { outcome: 'refused', reason: 'disabled' };
        }
        if (origin === 'user' || origin === 'shortcut') {
          void this.#injector.get(LiveAnnouncer).announce(message, 'polite');
        }
        return { outcome: 'refused', reason: 'disabled', message };
      }
      return this.#running().has(action.id) || this.#confirming.has(action.id) ? { outcome: 'refused', reason: 'busy' } : null;
    });
  }

  /**
   * Asks the user to confirm a destructive action, in `TmConfirm`'s danger
   * form, which loads on the first question. The confirm button carries the
   * action's label. Without its confirmation keys, which only production
   * reaches, the action's label and description stand in.
   */
  async #confirm(action: TmPageAction): Promise<boolean> {
    const { titleKey, messageKey, args } = action.confirm ?? {
      titleKey: action.labelKey,
      messageKey: action.descriptionKey ?? action.labelKey,
    };
    const l10n = this.#injector.get(TmLocalization);
    const text = (key: string): string => untracked(l10n.ɵtext(key, args && untracked(args)));
    const { TmConfirm } = await import('@tellma/core-ui/confirm');
    return this.#injector.get(TmConfirm).confirm({
      title: text(titleKey),
      message: text(messageKey),
      confirmLabel: text(action.labelKey),
      destructive: true,
    });
  }
}
