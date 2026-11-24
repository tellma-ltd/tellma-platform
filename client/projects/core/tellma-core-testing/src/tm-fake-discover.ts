// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, signal, type Signal } from '@angular/core';
import { TmApiError, type TmApiErrorKind } from '@tellma/core';
import {
  tmQxParameters,
  type TmQueryexDiscoverRequest,
  type TmQueryexDiscoverResult,
  type TmQueryexSite,
} from '@tellma/core/expression-editor';

import { TmFakeApi, type TmFakeApiCall } from './tm-fake-api';

// The `queryex/discover` route of the fake API, scripted for the expression
// editor's tests: every request recorded, each answered by the script (by
// default, the parameters each clause names with no types and no problem),
// answers held until a test releases them, and failures of any kind.

/** How a {@link TmFakeDiscover} answers a request: with a result, or from the request. */
export type TmFakeDiscoverAnswer =
  | TmQueryexDiscoverResult
  | ((request: TmQueryexDiscoverRequest) => TmQueryexDiscoverResult | Promise<TmQueryexDiscoverResult>);

/** The wire names of a request's clauses, with their texts. */
function clauses(request: TmQueryexDiscoverRequest): readonly [string, string][] {
  const named: [string, string | undefined][] = [
    ['select', request.select],
    ...(request.filters ?? []).map((filter, index): [string, string] => [`filters[${index}]`, filter]),
    ['having', request.having],
    ['orderBy', request.orderBy],
    ['measures', request.measures],
  ];
  return named.filter((entry): entry is [string, string] => entry[1] !== undefined);
}

/**
 * The answer the fake gives when no script says otherwise: no problem, no
 * path, and each parameter the clauses name with its sites, its confirmed
 * type if any, and no inferred one.
 *
 * @param request - The request.
 * @returns The answer.
 */
function defaultAnswer(request: TmQueryexDiscoverRequest): TmQueryexDiscoverResult {
  const parameters = new Map<string, { name: string; sites: TmQueryexSite[] }>();
  for (const [clause, text] of clauses(request)) {
    for (const parameter of tmQxParameters(text)) {
      const folded = parameter.name.toUpperCase();
      let entry = parameters.get(folded);
      if (entry === undefined) {
        parameters.set(folded, (entry = { name: parameter.name, sites: [] }));
      }
      entry.sites.push(...parameter.sites.map((site) => ({ clause, ...site })));
    }
  }
  const confirmed = new Map((request.parameters ?? []).map((parameter) => [parameter.name.toUpperCase(), parameter.type]));
  return {
    diagnostics: [],
    parameters: [...parameters.entries()].map(([folded, entry]) => ({
      ...entry,
      conflicts: [],
      ...(confirmed.has(folded) && { type: confirmed.get(folded)! }),
    })),
    paths: [],
    usesAggregation: request.aggregate === true,
  };
}

/**
 * Scripts the `queryex/discover` route of the {@link TmFakeApi}: it records
 * every request, answers each from the script, can hold the answers until
 * released, and can fail the calls with any `TmApiError` kind. Injecting it
 * installs it; it replaces any earlier script of the route.
 *
 * @example
 * ```ts
 * const discover = TestBed.inject(TmFakeDiscover);
 * discover.respond((request) => ({ diagnostics: [], parameters: [], paths: [], usesAggregation: false }));
 * discover.hold();
 * // … type into the editor; the check waits …
 * discover.release();
 * expect(discover.requests().at(-1)?.filters).toEqual(['Code = @Code']);
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmFakeDiscover {
  readonly #api = inject(TmFakeApi);
  readonly #requests = signal<readonly TmQueryexDiscoverRequest[]>([]);
  #answer: TmFakeDiscoverAnswer = defaultAnswer;
  #failure: TmApiErrorKind | null = null;
  #holding = false;
  readonly #held: (() => void)[] = [];

  /** Every request received, oldest first, aborted ones included. */
  readonly requests: Signal<readonly TmQueryexDiscoverRequest[]> = this.#requests.asReadonly();

  /** Installs the route on the fake API. */
  constructor() {
    this.#api.respond('queryex/discover', (call: TmFakeApiCall) => this.#respond(call));
  }

  /**
   * Scripts the answers: one result for every request, or a function of
   * the request.
   *
   * @param answer - The answer.
   */
  respond(answer: TmFakeDiscoverAnswer): void {
    this.#answer = answer;
  }

  /**
   * Fails every later call with a `TmApiError` of a kind, such as
   * `offline`; `null` answers again.
   *
   * @param kind - The kind, or `null`.
   */
  fail(kind: TmApiErrorKind | null): void {
    this.#failure = kind;
  }

  /** Holds every later answer until {@link TmFakeDiscover.release}. */
  hold(): void {
    this.#holding = true;
  }

  /** Lets every held answer go, in order, and stops holding. */
  release(): void {
    this.#holding = false;
    for (const go of this.#held.splice(0)) {
      go();
    }
  }

  /** How many answers are held. */
  get heldCount(): number {
    return this.#held.length;
  }

  /**
   * Records a call and answers it from the script, once released.
   *
   * @param call - The call.
   * @returns The answer.
   */
  async #respond(call: TmFakeApiCall): Promise<TmQueryexDiscoverResult> {
    const request = call.body as TmQueryexDiscoverRequest;
    this.#requests.update((requests) => [...requests, request]);
    if (this.#holding) {
      await new Promise<void>((resolve) => this.#held.push(resolve));
    }
    if (this.#failure !== null) {
      throw new TmApiError({ kind: this.#failure });
    }
    const answer = this.#answer;
    return typeof answer === 'function' ? answer(request) : answer;
  }
}
