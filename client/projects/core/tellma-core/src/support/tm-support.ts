// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  computed,
  type DestroyRef,
  inject,
  Injectable,
  InjectionToken,
  Injector,
  type Signal,
  type Type,
  untracked,
} from '@angular/core';

import { TmApi } from '../api/tm-api';
import type { TmApiError, TmApiErrorKind } from '../api/tm-api-error';
import { TM_DEV_MODE } from '../app/tm-dev-mode';
import type { TmPageContext } from '../context/tm-page-context';
import type { TmJson, TmJsonObject } from '../json/tm-json';
import { TmTelemetry } from '../observe/tm-telemetry';
import { type TmDiagnosticsTrigger, TmRequestLog } from '../requests/tm-request-log';
import { TmSession } from '../session/tm-session';
import { TmLaunchCache } from '../state/launch/tm-launch-cache';
import { TmUrls } from '../urls/tm-urls';

// The support facade (spec 0026 §12.1): what the session menu, the About
// dialog's Diagnostics panel and the 5xx dialog call. It stays small, since
// the shell reaches it: the support dialog, the dump's builder and the panel
// are the lazy @tellma/core/support (worker group on-demand, spec 0024 §5.3).
// Between the two sits the support host, a lazy chunk of the primary that
// the facade preloads with @tellma/core/page-snapshot when the app's startup
// starts it at idle: once
// both are in, a press opens the dialog in its own frame, with a placeholder
// in the dialog's final size, and snapshots the page contexts before any
// await (§10.4 step 1); the support entry point then loads into the dialog.
// Presses that come before they land collapse to one per kind, the last,
// which runs the moment they do; a load that fails is tried again on the
// next press. The facade also keeps the
// dump's contributed sections. The client state's (spec 0025 §15) comes from
// the support host's chunk when it lands, so its builder is not in the first
// frame, and its name is reserved for it until then.

/** The error the support dialog opened from, as the dump records it. */
export interface TmSupportReported {
  /** The failed request's trace id. */
  readonly traceId: string;
  /** The error's kind. */
  readonly kind: TmApiErrorKind;
  /** The error's code. */
  readonly code: string;
}

/** The page contexts of a snapshot, serialised one way. */
export interface TmSupportContexts<C> {
  /** The routed page's context, or `null` before the first page. */
  readonly current: C | null;
  /** The open context-bearing dialogs' contexts, bottom to top. */
  readonly overlays: readonly C[];
}

/**
 * The tab as the support dialog opened over it: the page contexts read in
 * the press, both with page data and without, and the page's address.
 * Internal: the support host takes it, and the support entry point builds
 * the dump from it.
 *
 * @typeParam C - A context's serialised form.
 */
export interface TmSupportSnapshot<C = TmJsonObject> {
  /** When the snapshot was taken, in epoch milliseconds. */
  readonly at: number;
  /** The contexts with page data. */
  readonly withData: TmSupportContexts<C>;
  /** The contexts without page data, the user's text masked. */
  readonly withoutData: TmSupportContexts<C>;
  /** The router's URL, its query included, such as `/17/customers?q=al%20faris`. */
  readonly url: string;
  /** The routed page's route template, or empty before the first page. */
  readonly routeTemplate: string;
  /** How many entries the tab's session history holds. */
  readonly historyLength: number;
  /** The error the dialog opened from, when one did. */
  readonly reported?: TmSupportReported;
}

/** One contributed section of the support dump. Internal. */
export interface TmSupportSection {
  /** The section's name, its key under the dump's `sections`. */
  readonly name: string;
  /** Reads the section, synchronously, while the dump is built. */
  readonly read: () => TmJson;
}

/** The page-snapshot entry point, as far as the support host reads it. Internal. */
export interface TmPageSnapshotModule {
  /**
   * Reads a page context once into plain JSON.
   *
   * @param context - The context.
   * @param options - Whether page data is included, and the injector that resolves entity labels.
   * @returns The snapshot.
   */
  tmSerializePageContext(context: TmPageContext, options: { readonly includeData: boolean; readonly injector?: Injector }): TmJsonObject;
}

/** The support host's chunk, as far as the facade reads it. Internal. */
export interface TmSupportHostChunk {
  /**
   * Snapshots the contexts and opens the support dialog over them, in the caller's frame.
   *
   * @param injector - The root injector.
   * @param pages - The page-snapshot entry point.
   * @param error - The error the dialog opens from, if any.
   */
  ɵtmOpenSupport(injector: Injector, pages: TmPageSnapshotModule, error: TmApiError | undefined): void;
  /**
   * Snapshots the contexts, then builds the dump without page data and downloads it.
   *
   * @param injector - The root injector.
   * @param pages - The page-snapshot entry point.
   * @returns A promise that settles once the download has started.
   */
  ɵtmDownloadSupport(injector: Injector, pages: TmPageSnapshotModule): Promise<void>;
  /**
   * The client state's section of the dump, named `state`.
   *
   * @param injector - The root injector, which the section reads the state's services from.
   * @returns The section.
   */
  ɵtmStateSection(injector: Injector): TmSupportSection;
  /**
   * The line the support dialog and the About dialog's Diagnostics tab show
   * when the support entry point cannot load, whose `kind` input names which.
   */
  readonly ɵTmSupportOffline: Type<unknown>;
}

/** The options of the support entry point's dump builder. Internal. */
export interface TmSupportDumpBuild {
  /** Whether the dump carries page data. */
  readonly includeData: boolean;
  /** The injector the dump reads the app's services from. */
  readonly injector?: Injector;
}

/** The support entry point, as far as the primary reads it. Internal. */
export interface TmSupportModule {
  /** The support dialog's content, whose inputs are `snapshot` and `error`. */
  readonly TmSupportDialog: Type<unknown>;
  /** The Diagnostics panel, which the About dialog's Diagnostics tab shows. */
  readonly TmDiagnosticsPanel: Type<unknown>;
  /**
   * Builds the dump from a snapshot.
   *
   * @param snapshot - The snapshot.
   * @param options - Whether page data is included, and the injector.
   * @returns The dump.
   */
  readonly tmBuildSupportDump: (snapshot: TmSupportSnapshot, options: TmSupportDumpBuild) => Promise<TmJsonObject>;
  /**
   * Downloads a dump as a file.
   *
   * @param dump - The dump.
   * @param injector - The injector the download reads the document from.
   */
  readonly tmDownloadSupportDump: (dump: TmJsonObject, injector?: Injector) => void;
}

/** What the support host needs before a press can run in its own frame. Internal. */
export interface TmSupportTools {
  /** The support host's chunk. */
  readonly host: TmSupportHostChunk;
  /** The page-snapshot entry point. */
  readonly pages: TmPageSnapshotModule;
}

/**
 * Loads the support host's chunk and the page-snapshot entry point.
 * Internal: the facade's loader, which a test replaces. The entry point's
 * specifier is cast so that the package build does not report the import
 * back as a cycle.
 */
export const TM_SUPPORT_TOOLS_LOAD = new InjectionToken<() => Promise<TmSupportTools>>('TM_SUPPORT_TOOLS_LOAD', {
  providedIn: 'root',
  factory: () => () =>
    Promise.all([
      import('./tm-support-host') as Promise<TmSupportHostChunk>,
      import('@tellma/core/page-snapshot' as string) as Promise<TmPageSnapshotModule>,
    ]).then(([host, pages]) => ({ host, pages })),
});

/**
 * Loads the support entry point. Internal: the support host's loader, which
 * a test replaces. The specifier is cast so that the package build does not
 * report the import back as a cycle.
 */
export const TM_SUPPORT_LOAD = new InjectionToken<() => Promise<TmSupportModule>>('TM_SUPPORT_LOAD', {
  providedIn: 'root',
  factory: () => () => import('@tellma/core/support' as string) as Promise<TmSupportModule>,
});

/** A contributed section's name: lowercase, a letter first, at most 32 characters. */
const SECTION_NAME = /^[a-z][a-z0-9-]{0,31}$/;

/**
 * The names a contributed section may not take: those the dump's truncation
 * steps use, and the client state's, which the support host's chunk adds.
 */
const RESERVED: ReadonlySet<string> = new Set(['telemetry', 'sections', 'state']);

/**
 * The support facade: Report a problem, Copy diagnostics, the diagnostic
 * file, and the support dump's contributed sections. Nothing here throws to
 * its caller in production; in development, a section name that is
 * malformed or taken throws an error named `TmSupportError`.
 *
 * - **`canSend`** is true when the distribution enables support requests,
 *   the route is a tenant's, the session is an active member's, the server
 *   is reachable and the tenant is `Active`.
 * - **`open`** shows the support dialog in the press's frame.
 * - **`registerDumpSection`** adds a block to every dump: names, tags,
 *   counts and states, never page data. Its reader runs synchronously while
 *   a dump is built; one that throws yields `{ "error": "<errorClass>" }`,
 *   and one over 64 KB `{ "truncated": true }`.
 *
 * @example
 * ```ts
 * const support = inject(TmSupport);
 * support.registerDumpSection('acme-exposure', () => ({ runs: runs.length }), inject(DestroyRef));
 * support.open();
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmSupport {
  readonly #injector = inject(Injector);
  readonly #telemetry = inject(TmTelemetry);
  readonly #devMode = inject(TM_DEV_MODE);
  readonly #loadTools = inject(TM_SUPPORT_TOOLS_LOAD);

  /** The contributed sections by name, in registration order. */
  readonly #sections = new Map<string, TmSupportSection>();

  /** The client state's section, once the support host's chunk has landed. */
  #state: TmSupportSection | null = null;

  /** The tools' load, while it runs or once it succeeded; `null` before the first and after a failure. */
  #tools: Promise<TmSupportTools> | null = null;

  /** The tools, once loaded. */
  #loaded: TmSupportTools | null = null;

  /** The press of each kind waiting for the tools, at most one: a later press replaces it. */
  readonly #pending = new Map<string, { action: (tools: TmSupportTools) => unknown; readonly settled: Promise<void> }>();

  /** Whether the user can send a support request now. */
  readonly canSend: Signal<boolean> = computed(() => {
    const launch = this.#injector.get(TmLaunchCache);
    const tenantId = this.#injector.get(TmUrls).tenantId();
    if (launch.distribution()?.support.enabled !== true || tenantId === null) {
      return false;
    }
    // An active member's session: signed in, the tenant's envelope read, the membership not deactivated.
    const member =
      this.#injector.get(TmSession).state() === 'active' &&
      launch.docs.me() !== undefined &&
      launch.tenants()?.find((tenant) => tenant.id === tenantId)?.isActive !== false;
    return member && this.#injector.get(TmApi).online() && launch.docs.tenant()?.state === 'Active';
  });

  /**
   * Preloads the support host and the page snapshot, so that a first press
   * opens the dialog in its own frame. Internal: the app's startup calls it
   * at idle; a failed load is tried again on the next press.
   */
  ɵstart(): void {
    this.#load().catch(() => undefined);
  }

  /**
   * Opens the support dialog: in this frame once the support host has
   * loaded, else the moment it lands. The page contexts are snapshotted
   * before the dialog's content loads, so the dialog is not in them. Presses
   * made before the host lands open one dialog, from the last press.
   *
   * @param options - The error the dialog opens from, such as the 5xx dialog's.
   */
  open(options?: { readonly error?: TmApiError }): void {
    void this.#run((tools) => tools.host.ɵtmOpenSupport(this.#injector, tools.pages, options?.error), 'tm.support.open');
  }

  /**
   * Copies the diagnostics text to the clipboard inside the user's gesture,
   * and toasts the outcome.
   *
   * @param trigger - What the user pressed, for telemetry.
   */
  copyDiagnostics(trigger: TmDiagnosticsTrigger = 'button'): void {
    this.#injector.get(TmRequestLog).copy(trigger);
  }

  /**
   * Downloads the dump as a file, without page data. Presses made before the
   * support host lands download one file.
   *
   * @returns A promise that settles once the download has started or failed; it never rejects.
   */
  downloadDump(): Promise<void> {
    return this.#run((tools) => tools.host.ɵtmDownloadSupport(this.#injector, tools.pages), 'tm.support.download');
  }

  /**
   * Adds a section to every dump, until the owner is destroyed.
   *
   * @param name - The section's name, its key under `sections`: matching
   *   `^[a-z][a-z0-9-]{0,31}$`, other than `telemetry`, `sections` and
   *   `state`, and unused.
   * @param read - Reads the section's JSON, synchronously.
   * @param destroyRef - The owner, whose end removes the section; without
   *   one, the section lasts as long as the app.
   */
  registerDumpSection(name: string, read: () => TmJson, destroyRef?: DestroyRef): void {
    if (!SECTION_NAME.test(name) || RESERVED.has(name) || this.#sections.has(name)) {
      if (typeof ngDevMode !== 'undefined' && ngDevMode && this.#devMode()) {
        throw Object.assign(new Error(`"${name}" cannot name a support dump section: it is malformed, reserved or already registered.`), {
          name: 'TmSupportError',
        });
      }
      // In production the first registration keeps the name.
      return;
    }
    const section: TmSupportSection = { name, read };
    this.#sections.set(name, section);
    destroyRef?.onDestroy(() => this.#sections.get(name) === section && this.#sections.delete(name));
  }

  /**
   * The contributed sections: the client state's first, once the support
   * host's chunk has landed, then the others in registration order.
   * Internal: the dump's builder reads them.
   *
   * @returns The sections.
   */
  ɵsections(): readonly TmSupportSection[] {
    return this.#state === null ? [...this.#sections.values()] : [this.#state, ...this.#sections.values()];
  }

  /**
   * Loads the tools once; a failed load is tried again on the next call.
   *
   * @returns The tools.
   */
  #load(): Promise<TmSupportTools> {
    this.#tools ??= this.#loadTools().then(
      (tools) => {
        // The client state's section comes with the host's chunk.
        this.#state ??= tools.host.ɵtmStateSection(this.#injector);
        return (this.#loaded = tools);
      },
      (error: unknown) => {
        this.#tools = null;
        throw error;
      },
    );
    return this.#tools;
  }

  /**
   * Runs an action with the tools: now, synchronously, when they are
   * loaded, else once they land. Before they land, a later action of the
   * same kind replaces the waiting one, so presses never stack. A failure
   * goes to telemetry, never to the caller.
   *
   * @param action - The action.
   * @param where - The action's kind, for telemetry.
   * @returns A promise that settles once the action has run or failed.
   */
  #run(action: (tools: TmSupportTools) => unknown, where: string): Promise<void> {
    const report = (error: unknown): void => this.#telemetry.error(error, { where });
    // The action's synchronous part runs before the first await, inside the caller's frame.
    const go = async (tools: TmSupportTools, act: (tools: TmSupportTools) => unknown): Promise<void> => {
      try {
        await untracked(() => act(tools));
      } catch (error) {
        report(error);
      }
    };
    if (this.#loaded !== null) {
      return go(this.#loaded, action);
    }
    // --- Before the tools land: one waiting press per kind ---
    const waiting = this.#pending.get(where);
    if (waiting !== undefined) {
      waiting.action = action;
      return waiting.settled;
    }
    const entry: { action: (tools: TmSupportTools) => unknown; settled: Promise<void> } = { action, settled: Promise.resolve() };
    entry.settled = this.#load().then(
      (tools) => {
        this.#pending.delete(where);
        return go(tools, entry.action);
      },
      (error: unknown) => {
        this.#pending.delete(where);
        report(error);
      },
    );
    this.#pending.set(where, entry);
    return entry.settled;
  }
}
