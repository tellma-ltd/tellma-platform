// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  assertInInjectionContext,
  computed,
  DestroyRef,
  effect,
  inject,
  signal,
  untracked,
  type Signal,
  type WritableSignal,
} from '@angular/core';
import { TmApi, TmApiError, TmServerText, TmTelemetry, type TmJson } from '@tellma/core';
import { tmQxLex } from '@tellma/core/entity';

import { tmQxParameters, ɵtmQxFold } from './tm-qx-text';
import type {
  TmQueryexDiscoverRequest,
  TmQueryexDiscoverResult,
  TmQueryexParameterInput,
  TmQueryexType,
  TmQueryexWireDiagnostic,
  TmQxPathUse,
} from './tm-queryex-wire';

// The draft of one query's Queryex (spec 0029 §10.1, §10.2): each clause's
// text, checked together by `queryex/discover` 250 ms after the last change
// so inference sees every clause. The lexer's problems show at once; the
// server's show once an answer arrives, and only on the clauses whose text
// still equals what was sent. Each change aborts the call in flight.

/** How long the draft waits after the last change before it checks, in milliseconds. */
const CHECK_DELAY_MS = 250;

/** The Queryex types a confirmed parameter may take; the others are refused by the server. */
const PARAMETER_TYPES: ReadonlySet<string> = new Set(['Bool', 'Numeric', 'String', 'Guid', 'Date', 'DateTime', 'DateTimeOffset']);

/**
 * A clause of a query: `filter` is sent as the first filter, `measures`
 * (with `having`) only when the query aggregates, and `constant` is
 * checked by the lexer alone and never sent.
 */
export type TmQxClause = 'select' | 'filter' | 'having' | 'orderBy' | 'measures' | 'constant';

/** Every clause, in the order the draft groups parameters by. */
const CLAUSES: readonly TmQxClause[] = ['select', 'filter', 'having', 'orderBy', 'measures', 'constant'];

/** The wire name of each clause the draft sends. */
const WIRE_CLAUSES: Readonly<Partial<Record<TmQxClause, string>>> = {
  select: 'select',
  filter: 'filters[0]',
  having: 'having',
  orderBy: 'orderBy',
  measures: 'measures',
};

/**
 * Where the draft's check stands: nothing to check, a check waiting or in
 * flight, checked, or not checked because the app is offline or the call
 * failed.
 */
export type TmQueryexStatus = 'idle' | 'checking' | 'checked' | 'offline' | 'failed';

/** One problem in a clause: the lexer's at once, the server's once checked. */
export interface TmQxDiagnostic {
  /** The code, such as `QX1001` or `Query.PathNotReachable`. */
  readonly code: string;
  /** The first UTF-16 code unit of the span, in the clause's text. */
  readonly start: number;
  /** The span's length in UTF-16 code units. */
  readonly length: number;
  /** The message in the active language; the code when no pack words it. */
  readonly message: Signal<string>;
}

/** One parameter the draft's clauses name. */
export interface TmQxParameter {
  /** The name, without `@`, as first spelled. */
  readonly name: string;
  /** The confirmed or inferred Queryex type, such as `Date`; `null` until known, or while the uses conflict. */
  readonly type: string | null;
  /** Whether its uses disagree on its type. */
  readonly conflicting: boolean;
  /** The entity whose key it is compared with, such as `core.User`. */
  readonly entity?: string;
}

/** The draft of one query's Queryex, shared by the editors of its clauses. */
export interface TmQueryexDraft {
  /** The entity the clauses read. */
  readonly root: Signal<string>;
  /**
   * A clause's text, which its editor writes.
   *
   * @param clause - The clause.
   * @returns The clause's text signal.
   */
  text(clause: TmQxClause): WritableSignal<string>;
  /** Where the check stands. */
  readonly status: Signal<TmQueryexStatus>;
  /** Checks again at once, as after an offline or failed check. */
  retry(): void;
  /**
   * A clause's problems, in text order: the lexer's at once, the server's
   * while the clause's text still equals what was checked.
   *
   * @param clause - The clause.
   * @returns The problems.
   */
  diagnostics(clause: TmQxClause): Signal<readonly TmQxDiagnostic[]>;
  /** The parameters every clause names: the names at once, the types once checked. */
  readonly parameters: Signal<readonly TmQxParameter[]>;
  /** The paths the last check found. */
  readonly paths: Signal<readonly TmQxPathUse[]>;
}

/** What a draft checks against. */
export interface TmQueryexDraftOptions {
  /** The entity the clauses read; an empty name checks nothing. */
  readonly root: Signal<string>;
  /** Whether the query aggregates, so `having` and `measures` are sent; it does not when omitted. */
  readonly aggregate?: Signal<boolean>;
  /** How the filter is validated: as a query compiles it, or as a role's save does; `query` when omitted. */
  readonly profile?: 'query' | 'permission';
  /** The parameter types the author confirmed. */
  readonly parameters?: Signal<readonly { readonly name: string; readonly type: string }[]>;
}

/** An applied answer and the texts it answered, by wire clause. */
interface TmAppliedAnswer {
  /** The text each sent clause had when it was sent. */
  readonly sent: ReadonlyMap<string, string>;
  /** The server's answer. */
  readonly result: TmQueryexDiscoverResult;
}

/**
 * The message key of a problem code: `Queryex_<code>` for the engine's
 * codes, the dotted code with underscores otherwise.
 *
 * @param code - The code.
 * @returns The key.
 */
function messageKey(code: string): string {
  return code.startsWith('QX') ? `Queryex_${code}` : code.replaceAll('.', '_');
}

/** The draft behind {@link injectTmQueryexDraft}. */
class TmQueryexDraftImpl implements TmQueryexDraft {
  readonly #api = inject(TmApi);
  readonly #server = inject(TmServerText);
  readonly #telemetry = inject(TmTelemetry);
  readonly #options: TmQueryexDraftOptions;

  /** Each clause's text. */
  readonly #texts = new Map<TmQxClause, WritableSignal<string>>(CLAUSES.map((clause) => [clause, signal('')]));

  /** Each clause's problems, built on first ask. */
  readonly #diagnostics = new Map<TmQxClause, Signal<readonly TmQxDiagnostic[]>>();

  /** The last answer applied. */
  readonly #answer = signal<TmAppliedAnswer | null>(null);

  /** Where the check stands. */
  readonly #status = signal<TmQueryexStatus>('idle');

  /** The pending check's timer. */
  #timer: ReturnType<typeof setTimeout> | undefined;

  /** Aborts the call in flight. */
  #controller: AbortController | null = null;

  /** The entity the clauses read. */
  readonly root: Signal<string>;

  /** Where the check stands. */
  readonly status: Signal<TmQueryexStatus> = this.#status.asReadonly();

  /** The paths the last check found. */
  readonly paths: Signal<readonly TmQxPathUse[]> = computed(() => this.#answer()?.result.paths ?? []);

  /**
   * The body the next check sends, with its clauses' texts, or `null` when
   * there is nothing to check. Its key changes only when the body does.
   */
  readonly #request = computed<{ readonly key: string; readonly body: TmQueryexDiscoverRequest; readonly sent: ReadonlyMap<string, string> } | null>(
    () => {
      const root = this.root();
      if (root === '') {
        return null;
      }
      const text = (clause: TmQxClause): string | undefined => {
        const value = this.text(clause)();
        // Blank text means the clause is absent.
        return value.trim() === '' ? undefined : value;
      };
      const sent = new Map<string, string>();
      const take = (clause: TmQxClause): string | undefined => {
        const value = text(clause);
        if (value !== undefined) {
          sent.set(WIRE_CLAUSES[clause]!, value);
        }
        return value;
      };

      // --- The permission profile: one filter, nothing else ---
      if (this.#options.profile === 'permission') {
        const filter = take('filter');
        return filter === undefined ? null : { key: JSON.stringify([root, filter]), body: { root, filters: [filter], profile: 'Permission' }, sent };
      }

      // --- A query: every non-blank clause; having and measures only when it aggregates ---
      const aggregate = this.#options.aggregate?.() ?? false;
      const select = take('select');
      const filter = take('filter');
      const orderBy = take('orderBy');
      const having = aggregate ? take('having') : undefined;
      const measures = aggregate ? take('measures') : undefined;
      if (sent.size === 0) {
        return null;
      }
      // A confirmed type the server takes, once per name.
      const parameters: TmQueryexParameterInput[] = [];
      const named = new Set<string>();
      for (const parameter of this.#options.parameters?.() ?? []) {
        const folded = ɵtmQxFold(parameter.name);
        if (PARAMETER_TYPES.has(parameter.type) && !named.has(folded)) {
          named.add(folded);
          parameters.push({ name: parameter.name, type: parameter.type as TmQueryexType });
        }
      }
      const body: TmQueryexDiscoverRequest = {
        root,
        ...(select !== undefined && { select }),
        ...(filter !== undefined && { filters: [filter] }),
        ...(having !== undefined && { having }),
        ...(orderBy !== undefined && { orderBy }),
        ...(measures !== undefined && { measures }),
        ...(aggregate && { aggregate: true }),
        ...(parameters.length > 0 && { parameters }),
      };
      return { key: JSON.stringify(body), body, sent };
    },
    { equal: (a, b) => a?.key === b?.key },
  );

  /** The parameters every clause names, with the confirmed types and the last answer's. */
  readonly parameters: Signal<readonly TmQxParameter[]> = computed(() => {
    // The confirmed types and the answer's, by folded name.
    const confirmed = new Map((this.#options.parameters?.() ?? []).map((parameter) => [ɵtmQxFold(parameter.name), parameter.type]));
    const inferred = new Map((this.#answer()?.result.parameters ?? []).map((info) => [ɵtmQxFold(info.name), info]));
    const parameters = new Map<string, TmQxParameter>();
    for (const clause of CLAUSES) {
      for (const { name } of tmQxParameters(this.text(clause)())) {
        const folded = ɵtmQxFold(name);
        if (!parameters.has(folded)) {
          const info = inferred.get(folded);
          parameters.set(folded, {
            name,
            type: confirmed.get(folded) ?? info?.type ?? null,
            conflicting: (info?.conflicts.length ?? 0) > 0,
            ...(info?.entity !== undefined && { entity: info.entity }),
          });
        }
      }
    }
    return [...parameters.values()];
  });

  /**
   * Starts the draft: a change restarts the wait and aborts the call in
   * flight, and the return of the connection checks again.
   *
   * @param options - What the draft checks against.
   */
  constructor(options: TmQueryexDraftOptions) {
    this.#options = options;
    this.root = options.root;

    // --- Each change of what would be sent restarts the wait ---
    effect(() => {
      const request = this.#request();
      untracked(() => this.#schedule(request === null ? null : CHECK_DELAY_MS));
    });

    // --- Back online, an offline check runs again ---
    effect(() => {
      if (this.#api.online() && untracked(this.#status) === 'offline') {
        untracked(() => this.retry());
      }
    });

    inject(DestroyRef).onDestroy(() => this.#stop());
  }

  /**
   * A clause's text, which its editor writes.
   *
   * @param clause - The clause.
   * @returns The clause's text signal.
   */
  text(clause: TmQxClause): WritableSignal<string> {
    return this.#texts.get(clause)!;
  }

  /**
   * A clause's problems: the lexer's, and the server's while the clause's
   * text equals what the applied answer checked, each once, in text order.
   *
   * @param clause - The clause.
   * @returns The problems.
   */
  diagnostics(clause: TmQxClause): Signal<readonly TmQxDiagnostic[]> {
    let diagnostics = this.#diagnostics.get(clause);
    if (diagnostics === undefined) {
      diagnostics = computed(() => {
        const text = this.text(clause)();
        const wire = WIRE_CLAUSES[clause];
        const answer = this.#answer();
        const server: readonly TmQueryexWireDiagnostic[] =
          wire !== undefined && answer !== null && answer.sent.get(wire) === text
            ? answer.result.diagnostics.filter((diagnostic) => diagnostic.clause === wire)
            : [];
        const seen = new Set<string>();
        const problems: TmQxDiagnostic[] = [];
        for (const problem of [...tmQxLex(text).diagnostics, ...server]) {
          const key = `${problem.code}\n${problem.start}\n${problem.length}`;
          if (!seen.has(key)) {
            seen.add(key);
            problems.push(this.#diagnostic(problem.code, problem.start, problem.length, 'arguments' in problem ? problem.arguments : undefined));
          }
        }
        return problems.sort((a, b) => a.start - b.start);
      });
      this.#diagnostics.set(clause, diagnostics);
    }
    return diagnostics;
  }

  /** Checks again at once, as after an offline or failed check. */
  retry(): void {
    this.#schedule(untracked(this.#request) === null ? null : 0);
  }

  /**
   * A problem with its message, which falls back to the code.
   *
   * @param code - The code.
   * @param start - The span's start.
   * @param length - The span's length.
   * @param args - The message's arguments.
   * @returns The problem.
   */
  #diagnostic(code: string, start: number, length: number, args: Readonly<Record<string, TmJson>> | undefined): TmQxDiagnostic {
    const key = messageKey(code);
    const text = this.#server.message(key, args);
    return { code, start, length, message: computed(() => (this.#server.has(key) ? text() : code)) };
  }

  /**
   * Aborts the call in flight and the pending wait, then waits `delay`
   * before checking; `null` checks nothing and leaves the draft idle.
   *
   * @param delay - The wait in milliseconds, or `null`.
   */
  #schedule(delay: number | null): void {
    this.#stop();
    if (delay === null) {
      this.#status.set('idle');
      return;
    }
    this.#status.set('checking');
    this.#timer = setTimeout(() => {
      this.#timer = undefined;
      void this.#check();
    }, delay);
  }

  /** Aborts the call in flight and clears the pending wait. */
  #stop(): void {
    clearTimeout(this.#timer);
    this.#timer = undefined;
    this.#controller?.abort();
    this.#controller = null;
  }

  /** Sends the current clauses and applies the answer, unless a change aborted it. */
  async #check(): Promise<void> {
    const request = untracked(this.#request);
    if (request === null) {
      this.#status.set('idle');
      return;
    }
    const controller = new AbortController();
    this.#controller = controller;
    const started = performance.now();
    try {
      const result = await this.#api.read<TmQueryexDiscoverResult>('queryex/discover', request.body, { signal: controller.signal });
      if (controller.signal.aborted) {
        return;
      }
      this.#controller = null;
      this.#answer.set({ sent: request.sent, result });
      this.#status.set('checked');
      this.#telemetry.timing('tm.queryex.discover', performance.now() - started, {
        clauses: Math.min(request.sent.size, 4),
        problems: result.diagnostics.length > 0 ? 'some' : 'none',
      });
    } catch (error: unknown) {
      if (controller.signal.aborted || (error instanceof TmApiError && error.kind === 'cancelled')) {
        return;
      }
      this.#controller = null;
      this.#status.set(error instanceof TmApiError && error.kind === 'offline' ? 'offline' : 'failed');
    }
  }
}

/**
 * Creates the draft of one query's Queryex, shared by the editors of its
 * clauses so inference sees every clause. It checks the clauses with
 * `queryex/discover` 250 ms after the last change, aborting the call in
 * flight on each change, and lives until the injector that created it is
 * destroyed. Call it in an injection context.
 *
 * @param options - The root, whether the query aggregates, the profile and the confirmed parameter types.
 * @returns The draft.
 *
 * @example
 * ```ts
 * readonly draft = injectTmQueryexDraft({ root: signal('acme.SalesInvoiceLine'), aggregate: this.aggregate });
 * ```
 * ```html
 * <tm-expression-editor clause="select" [draft]="draft" [(value)]="select" />
 * <tm-expression-editor clause="filter" [draft]="draft" [(value)]="filter" />
 * ```
 */
export function injectTmQueryexDraft(options: TmQueryexDraftOptions): TmQueryexDraft {
  assertInInjectionContext(injectTmQueryexDraft);
  // The effects run in the caller's injection context, and the draft dies with it.
  return new TmQueryexDraftImpl(options);
}
