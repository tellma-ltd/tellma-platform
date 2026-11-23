// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  Component,
  createEnvironmentInjector,
  DestroyRef,
  EnvironmentInjector,
  inject,
  signal,
  type Signal,
  type WritableSignal,
} from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { TmJson } from '../json/tm-json';
import type { TmActionResult, TmPageAction, TmPageActionInput } from './tm-page-action';
import { TmPageActions } from './tm-page-actions';
import type { TmPageContext } from './tm-page-context';
import { TmPageContexts } from './tm-page-contexts';
import { provideTmPageScope } from './tm-page-scope';

// The page-context specs' shared pieces: contexts and actions with writable
// signals, lifetimes the specs end, and a scoped host component.

/** A context whose signals a spec writes. */
export interface TmTestContext extends TmPageContext {
  /** The title, writable. */
  readonly title: WritableSignal<string>;
  /** The publisher's own actions, writable. */
  readonly actions: WritableSignal<readonly TmPageAction[]>;
}

/**
 * A routed context, or a dialog's with `overlay`, whose title and own actions
 * a spec writes.
 *
 * @param routeTemplate - Its route template.
 * @param patch - Members to set.
 * @param actions - Its own actions.
 * @returns The context.
 */
export function tmTestContext(
  routeTemplate: string,
  patch: Partial<Omit<TmPageContext, 'title' | 'actions'>> = {},
  actions: readonly TmPageAction[] = [],
): TmTestContext {
  return {
    kind: 'custom',
    routeTemplate,
    status: signal<'loading' | 'ready' | 'error'>('ready'),
    state: signal<TmJson>({}),
    errors: signal([]),
    ...patch,
    title: signal(routeTemplate),
    actions: signal(actions),
  };
}

/** An action that counts its invocations and answers `done`, or what `answer` returns. */
export interface TmTestAction extends TmPageAction {
  /** Each invocation's arguments and origin, oldest first. */
  readonly calls: { readonly args: TmJson; readonly origin: string }[];
}

/**
 * A complete page action, as a context publishes it.
 *
 * @param id - Its id.
 * @param patch - Members to set.
 * @param answer - What an invocation returns; default `done`.
 * @returns The action.
 */
export function tmTestAction(
  id: string,
  patch: Partial<TmPageAction> = {},
  answer: () => Promise<TmActionResult> = () => Promise.resolve({ outcome: 'done' }),
): TmTestAction {
  const calls: { args: TmJson; origin: string }[] = [];
  return {
    id,
    labelKey: `${id}.label`,
    group: 'menu',
    enabled: signal(true),
    destructive: false,
    disabledReason: signal<string | null>(null),
    calls,
    invoke: (args, origin) => {
      calls.push({ args, origin });
      return answer();
    },
    ...patch,
  };
}

/**
 * An action as `TmPageActions.register` takes it.
 *
 * @param id - Its id.
 * @param patch - Members to set.
 * @returns The input.
 */
export function tmTestInput(id: string, patch: Partial<TmPageActionInput> = {}): TmPageActionInput {
  return { id, labelKey: `${id}.label`, invoke: () => Promise.resolve({ outcome: 'done' }), ...patch };
}

/**
 * A lifetime a spec ends by destroying it.
 *
 * @returns The lifetime's injector.
 */
export function tmLifetime(): EnvironmentInjector {
  return createEnvironmentInjector([], TestBed.inject(EnvironmentInjector));
}

/** The ids of a context's merged actions. */
export function tmIds(context: Signal<TmPageContext | null> | TmPageContext | null): string[] {
  const read = typeof context === 'function' ? context() : context;
  return (read?.actions() ?? []).map((action) => action.id);
}

/** A component with a page scope of its own, whose children the spec projects. */
@Component({ selector: 'tm-test-scope', template: '<ng-content />', providers: [provideTmPageScope()] })
export class TmTestScope {
  /** The scope's contexts. */
  readonly contexts = inject(TmPageContexts);
  /** The scope's actions. */
  readonly actions = inject(TmPageActions);
  /** The scope's lifetime. */
  readonly destroyRef = inject(DestroyRef);
}
