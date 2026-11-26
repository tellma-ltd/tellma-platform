// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import {
  computed,
  createEnvironmentInjector,
  DestroyRef,
  EnvironmentInjector,
  inject,
  Injectable,
  Injector,
  signal,
  untracked,
  type Signal,
  type ViewContainerRef,
} from '@angular/core';
import { Router } from '@angular/router';
import { TmConfirm } from '@tellma/core-ui/confirm';
import { TmErrorDialog, type TmErrorDialogButton, type TmErrorDialogOptions, type TmErrorDialogResult } from '@tellma/core-ui/error-dialog';
import { TmIconRegistry } from '@tellma/core-ui/icon';
import {
  TM_NOW,
  TmApiError,
  TmClientStore,
  TmEntities,
  type TmJson,
  TmLaunch,
  TmLaunchCache,
  TmLocalization,
  TmPageContexts,
  TmPermissions,
  type TmProblemError,
  TmServerText,
  TmSession,
  TmShellMain,
  TmTelemetry,
  TmUrls,
  ɵTmClientMetadataStore,
} from '@tellma/core';
import CircleAlert from 'lucide/dist/esm/icons/circle-alert.mjs';
import Clock from 'lucide/dist/esm/icons/clock.mjs';
import Lock from 'lucide/dist/esm/icons/lock.mjs';
import RefreshCw from 'lucide/dist/esm/icons/refresh-cw.mjs';
import SearchX from 'lucide/dist/esm/icons/search-x.mjs';

import { TmErrorState } from './tm-error-state';
import { TM_ERROR_STRINGS_EN } from './tm-error-strings';
import { TmErrorUpdates } from './tm-error-updates';
import { TM_VIEW_ERRORS, type TmErrorAction, type TmErrorContext, type TmErrorView } from './tm-error-view';

// The error presenter (spec 0024 §8.3, §11): one table turns a `TmApiError`
// into an error view, which a page renders inline for a read through
// `tm-error-state`, and which `present` shows for a write or an action in
// one of two shared dialogs: the 5xx dialog, with Try again or Reload, and
// the 400 dialog, with Close alone. Both are 0023's `TmErrorDialog`, so one
// shows at a time and a later error replaces an earlier one's content. The
// kinds that end a tenant's use cross pages, and `present` handles them on
// reads too: `tenant-not-found` sweeps the tenant's data and goes to the
// picker, and `tenant-unavailable` replaces the shell's main area. A
// step-up asks before it signs in again.

/** One row of the table: how a kind and code read. */
interface TmErrorRow {
  /** The tone. */
  readonly tone: TmErrorView['tone'];
  /** The icon. */
  readonly icon: string;
  /** The title, or a fixed text. */
  readonly title: Signal<string>;
  /** The message. */
  readonly message: Signal<string>;
  /** The actions. */
  readonly actions: readonly TmErrorAction[];
  /** The raw permission pair of a `forbidden` refusal. */
  readonly permission?: string;
}

/** The icons the views use, which the presenter registers unless a feature drew its own. */
const ICONS = { 'circle-alert': CircleAlert, clock: Clock, lock: Lock, 'refresh-cw': RefreshCw, 'search-x': SearchX };

/** The kinds whose write or action shows the 5xx dialog; the rest show the 400 dialog. */
const FIVE_XX: ReadonlySet<string> = new Set(['offline', 'unavailable', 'client-outdated', 'server']);

// Spec 0026 §4.1.
/** The kinds the presenter reports to telemetry when it shows them. */
function reported(error: TmApiError): boolean {
  return (
    error.kind === 'server' ||
    error.kind === 'unavailable' ||
    error.kind === 'partial-failure' ||
    (error.kind === 'offline' && error.code === 'outcome-unknown')
  );
}

/** A promise that never settles: the page is reloading. */
function reloading(): Promise<never> {
  return new Promise<never>(() => undefined);
}

/** A problem's `arguments`, as an object of plain values. */
function argumentsOf(error: TmApiError): Readonly<Record<string, TmJson>> {
  const args = error.problem?.['arguments'];
  return typeof args === 'object' && args !== null && !Array.isArray(args) ? (args as Readonly<Record<string, TmJson>>) : {};
}

/** A string argument, or `undefined`. */
function textArgument(args: Readonly<Record<string, TmJson>>, name: string): string | undefined {
  const value = args[name];
  return typeof value === 'string' && value !== '' ? value : undefined;
}

/**
 * Turns an API error into what the user sees: the view a page renders
 * inline for a failed read, and the dialogs a failed write or action shows.
 *
 * @example
 * ```ts
 * const presenter = inject(TmErrorPresenter);
 * try {
 *   await api.write('centers/save', { entities });
 * } catch (error) {
 *   if (error instanceof TmApiError) {
 *     await presenter.present(error, { kind: 'save', entity: 'gl.Center', retry: () => save() });
 *   }
 * }
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmErrorPresenter {
  readonly #injector = inject(Injector);
  readonly #environment = inject(EnvironmentInjector);
  readonly #l10n = inject(TmLocalization);
  readonly #telemetry = inject(TmTelemetry);
  readonly #updates = inject(TmErrorUpdates);
  readonly #now = inject(TM_NOW);
  /** The tenants whose `tenant-not-found` is being handled, so a burst of refusals runs it once. */
  readonly #gone = new Set<number>();

  /** Registers the presenter's strings and the views' icons. */
  constructor() {
    this.#l10n.ɵregisterStrings('errors', TM_ERROR_STRINGS_EN);
    const icons = inject(TmIconRegistry);
    const missing = Object.entries(ICONS).filter(([name]) => icons.get(name) === undefined);
    if (missing.length > 0) {
      icons.register(Object.fromEntries(missing));
    }
  }

  /**
   * The view of an error, as the error table words it: for a page to render
   * inline with `tm-error-state`, and for the dialogs `present` shows.
   *
   * @param error - The error.
   * @param context - Where it happened.
   * @returns The view, which follows the language.
   */
  describe(error: TmApiError, context: TmErrorContext): TmErrorView {
    const row = this.#row(error, context);
    const server = this.#injector.get(TmServerText);
    const problems = (Array.isArray(error.problem?.['errors']) ? error.problem['errors'] : []) as readonly TmProblemError[];
    const items = problems.map((item) => server.error(item, context.entity));
    const view: TmErrorView = {
      tone: row.tone,
      icon: row.icon,
      title: row.title,
      message: row.message,
      items: computed(() => items.map((item) => item())),
      traceId: error.traceId === '' ? null : error.traceId,
      permission: row.permission ?? null,
      actions: signal(row.actions).asReadonly(),
    };
    TM_VIEW_ERRORS.set(view, error);
    return view;
  }

  /**
   * Shows an error. A write's or an action's shows in the 5xx dialog or the
   * 400 dialog; a read's shows only when it crosses pages (a tenant that is
   * gone or unavailable), and otherwise the page renders the view inline. A
   * step-up asks before it signs in again. A cancelled call and an ended
   * session show nothing: the expiry path has its own dialog.
   *
   * @param error - The error.
   * @param context - Where it happened, and what Try again runs.
   * @returns A promise that settles when the dialog closes, or at once when nothing shows.
   */
  async present(error: TmApiError, context: TmErrorContext & { readonly retry?: () => void }): Promise<void> {
    switch (error.kind) {
      case 'cancelled':
      case 'unauthenticated':
        return;
      case 'step-up':
        return this.#stepUp(error);
      case 'tenant-not-found':
        return this.#tenantGone(context.tenantId ?? untracked(this.#urls.tenantId));
      case 'tenant-unavailable':
        this.#tenantUnavailable(error, context);
        return;
    }
    if (context.kind === 'read') {
      return;
    }
    if (reported(error)) {
      this.#telemetry.error(error, { source: 'api' });
    }
    const view = this.describe(error, context);
    const release = this.#registerContext(error, view);
    try {
      await (FIVE_XX.has(error.kind) ? this.#fiveXx(error, view, context.retry) : this.#badRequest(error, view));
    } finally {
      release();
    }
  }

  /** The routed tenant's URLs. */
  get #urls(): TmUrls {
    return this.#injector.get(TmUrls);
  }

  // Spec 0024 §11's table.
  /** The table's row for an error. */
  #row(error: TmApiError, context: TmErrorContext): TmErrorRow {
    const args = argumentsOf(error);
    const tenant = (): string => this.#tenantName(context.tenantId ?? this.#urls.tenantId());
    switch (error.kind) {
      case 'offline':
        if (error.code === 'not-sent') {
          return this.#entry('warning', 'wifi-off', 'offline.title', 'notSent.message', ['close']);
        }
        if (error.code === 'outcome-unknown') {
          return this.#entry('warning', 'wifi-off', 'outcomeUnknown.title', 'outcomeUnknown.message', ['close']);
        }
        return this.#entry('warning', 'wifi-off', 'offline.title', 'offline.message', ['retry']);
      case 'forbidden':
        return this.#forbidden(args, context);
      case 'not-found': {
        const summary = context.entity === undefined ? undefined : this.#injector.get(TmEntities).summary(context.entity);
        const entity = (): string => (summary ? this.#l10n.ɵtext(summary.labelKey, undefined, summary.labelFallback)() : '');
        return this.#entry('info', 'search-x', summary ? 'missing.title' : 'missing.record', null, [], () => ({ entity: entity() }));
      }
      case 'read-only':
        return error.code === 'tenant-frozen'
          ? this.#entry('warning', 'lock', 'frozen.title', 'frozen.message', [], () => ({ tenant: tenant() }))
          : this.#entry('warning', 'lock', 'readOnly.title', 'readOnly.message', [], () => ({ tenant: tenant() }));
      case 'tenant-unavailable':
        return this.#entry('warning', 'lock', 'suspended.title', 'suspended.message', ['switch-tenant'], () => ({ tenant: tenant() }));
      case 'unavailable':
        return this.#entry('warning', 'clock', 'unavailable.title', 'unavailable.message', ['retry']);
      case 'client-outdated':
        return error.code === 'server-outdated'
          ? this.#entry('info', 'refresh-cw', 'serverOutdated.title', 'serverOutdated.message', ['retry'])
          : this.#entry('info', 'refresh-cw', 'clientOutdated.title', 'clientOutdated.message', ['reload']);
      case 'unauthenticated':
        return this.#entry('info', 'lock', 'ended.title', 'ended.message', ['sign-in']);
      case 'step-up':
        return this.#entry('info', 'lock', 'stepUp.title', 'stepUp.message', ['continue']);
      case 'server':
      case 'cancelled':
        return this.#entry('error', 'circle-alert', 'server.title', 'server.message', ['retry']);
      default:
        return this.#refused(error);
    }
  }

  /**
   * A `forbidden` refusal's row: it names the permission from the refusal's
   * arguments, the refused path's label, and the raw pair.
   */
  #forbidden(args: Readonly<Record<string, TmJson>>, context: TmErrorContext): TmErrorRow {
    const permissions = this.#injector.get(TmPermissions);
    const resource = textArgument(args, 'resource') ?? '';
    const action = textArgument(args, 'action');
    const permission = action === undefined ? permissions.label(resource) : permissions.label(resource, action);
    const raw = resource === '' ? undefined : action === undefined ? resource : `${resource} · ${action}`;
    const reason = textArgument(args, 'reason');
    let row: TmErrorRow;
    if (reason === 'restricted-path') {
      const path = textArgument(args, 'path') ?? '';
      const label = this.#pathLabel(context.entity, path);
      const resourceLabel = permissions.label(resource);
      row = this.#entry('error', 'lock', 'restrictedPath.title', 'restrictedPath.message', [], () => ({
        field: label(),
        resource: resourceLabel(),
      }));
    } else {
      const key = reason === 'outside-filter' ? 'outsideFilter' : 'forbidden';
      row = this.#entry('error', 'lock', `${key}.title`, `${key}.message`, [], () => ({ permission: permission() }));
    }
    return raw === undefined ? row : { ...row, permission: raw };
  }

  /**
   * A refusal the table does not list: the problem's own title, by its
   * `Problem_` key, else "Couldn't complete this", and its detail.
   */
  #refused(error: TmApiError): TmErrorRow {
    const problem = error.problem;
    const server = this.#injector.get(TmServerText);
    const text = problem === null ? null : server.problem(problem);
    const keyed = problem !== null && server.has(`Problem_${problem.code.replaceAll('-', '_')}`);
    const fallback = this.#l10n.ɵtext('tm.errors.badRequest.title');
    return {
      tone: 'error',
      icon: 'circle-alert',
      title: computed(() => (keyed && text ? text().title : fallback())),
      message: computed(() => text?.().detail ?? ''),
      actions: ['close'],
    };
  }

  /** A row over the table's keys, under `tm.errors`, with the display name among the parameters. */
  #entry(
    tone: TmErrorView['tone'],
    icon: string,
    title: string,
    message: string | null,
    actions: readonly TmErrorAction[],
    params: () => Readonly<Record<string, unknown>> = () => ({}),
  ): TmErrorRow {
    const l10n = this.#l10n;
    const text = (key: string | null): Signal<string> =>
      computed(() => (key === null ? '' : l10n.ɵtext(`tm.errors.${key}`, { displayName: l10n.displayName(), ...params() })()));
    return { tone, icon, title: text(title), message: text(message), actions };
  }

  /**
   * A refused path's label: the slash label of the path from the context's
   * entity, else the raw path, as when the entity model has not loaded.
   */
  #pathLabel(entity: string | undefined, path: string): Signal<string> {
    let resolved: Signal<string> | undefined;
    try {
      resolved = entity === undefined || path === '' ? undefined : this.#injector.get(TmEntities).resolve(entity, path)?.label;
    } catch {
      // The entity model is not loaded: the raw path shows.
      resolved = undefined;
    }
    return resolved ?? signal(path).asReadonly();
  }

  /** A tenant's name as the shell shows it: the routed tenant's settings, else its membership. */
  #tenantName(tenantId: number | null): string {
    const slot = this.#l10n.contentSlot();
    const launch = this.#injector.get(TmLaunchCache);
    if (tenantId !== null && tenantId === this.#urls.tenantId()) {
      const names = launch.docs.settings()?.names;
      const name = names?.[slot - 1] ?? names?.[0];
      if (name) {
        return name;
      }
    }
    const membership = launch.tenants()?.find((candidate) => candidate.id === tenantId);
    if (membership) {
      return [membership.name, membership.name2, membership.name3][slot - 1] ?? membership.name;
    }
    return tenantId === null ? '' : String(tenantId);
  }

  /**
   * The 5xx dialog: the table's words, the reference, the details, and Try
   * again or Reload. Reload gets the new version first: a failed download
   * shows again with the failure's message, and from the second failure
   * offers Reload anyway.
   */
  async #fiveXx(error: TmApiError, view: TmErrorView, retry: (() => void) | undefined): Promise<void> {
    const dialog = this.#injector.get(TmErrorDialog);
    const actions = untracked(view.actions);
    const text = (key: string): string => this.#l10n.ɵtext(`tm.errors.${key}`)();
    const state = { failures: 0, next: null as Promise<TmErrorDialogResult> | null };
    const reload: TmErrorDialogButton = {
      label: text('reload'),
      pendingLabel: text('updating'),
      run: async () => {
        const result = await this.#updates.update();
        if (result === 'ready' || result === 'disabled') {
          return reloading();
        }
        state.failures++;
        state.next = dialog.show(options());
        return false;
      },
    };
    const reloadAnyway: TmErrorDialogButton = {
      label: text('reloadAnyway'),
      run: () => {
        this.#updates.reload();
        return reloading();
      },
    };
    const options = (): TmErrorDialogOptions => ({
      title: untracked(view.title),
      message: state.failures > 0 ? text('updateFailed') : untracked(view.message),
      reference: view.traceId ?? undefined,
      details: this.#details(error, view),
      ...(actions.includes('reload')
        ? { primary: reload }
        : actions.includes('retry') && retry !== undefined
          ? { primary: { label: text('retry') } }
          : {}),
      ...(state.failures > 1 ? { secondary: reloadAnyway } : {}),
    });
    // A failed download replaces the dialog with the next one, which is then awaited.
    let shown = dialog.show(options());
    for (;;) {
      const answer = await shown;
      if (answer === 'replaced' && state.next !== null) {
        shown = state.next;
        state.next = null;
        continue;
      }
      if (answer === 'primary' && actions.includes('retry')) {
        retry?.();
      }
      return;
    }
  }

  /** The 400 dialog: the words, the server's items, the reference, and Close. */
  async #badRequest(error: TmApiError, view: TmErrorView): Promise<void> {
    const raw = view.permission;
    await this.#injector.get(TmErrorDialog).show({
      title: untracked(view.title),
      message: untracked(view.message),
      items: untracked(view.items),
      reference: view.traceId ?? undefined,
      ...(raw === null ? {} : { details: [{ label: this.#l10n.ɵtext('tm.errors.details.permission')(), value: raw, code: true }] }),
    });
  }

  /** The 5xx dialog's details: status, code, time, the page's route template, the UI version and any permission. */
  #details(error: TmApiError, view: TmErrorView): NonNullable<TmErrorDialogOptions['details']> {
    const label = (key: string): string => this.#l10n.ɵtext(`tm.errors.details.${key}`)();
    const route = untracked(this.#injector.get(TmPageContexts).current)?.routeTemplate ?? '';
    const version = this.#injector.get(ɵTmClientMetadataStore).peek()?.distribution.version ?? '';
    const details = [
      { label: label('status'), value: String(error.status), code: true },
      { label: label('code'), value: error.code, code: true },
      { label: label('time'), value: new Date(this.#now()).toISOString(), code: true },
      { label: label('route'), value: route, code: true },
      { label: label('version'), value: version, code: true },
      ...(view.permission === null ? [] : [{ label: label('permission'), value: view.permission, code: true }]),
    ];
    return details.filter((detail) => detail.value !== '');
  }

  /**
   * Registers the open dialog's overlay page context, so the support dump
   * and a future assistant see the error.
   *
   * @returns Ends the registration.
   */
  #registerContext(error: TmApiError, view: TmErrorView): () => void {
    const scope = createEnvironmentInjector([], this.#environment);
    this.#injector.get(TmPageContexts).register(
      {
        kind: 'custom',
        routeTemplate: 'dialog:error',
        overlay: true,
        title: view.title,
        status: signal<'error'>('error').asReadonly(),
        state: signal<TmJson>({ kind: error.kind, code: error.code, status: error.status }).asReadonly(),
        errors: computed(() => [
          { source: 'request' as const, code: error.code, message: view.title(), ...(view.traceId === null ? {} : { traceId: view.traceId }) },
        ]),
        actions: signal([]).asReadonly(),
      },
      scope.get(DestroyRef),
    );
    return () => scope.destroy();
  }

  /**
   * The step-up dialog: Continue signs in again with the
   * stronger authentication the call asked for, coming back to this page.
   */
  async #stepUp(error: TmApiError): Promise<void> {
    const text = (key: string): string => this.#l10n.ɵtext(`tm.errors.stepUp.${key}`)();
    const confirmed = await this.#injector.get(TmConfirm).confirm({
      title: text('title'),
      message: text('message'),
      confirmLabel: text('continue'),
    });
    if (confirmed) {
      const challenge = error.challenge;
      await this.#injector.get(TmSession).signIn(challenge ? { stepUp: challenge } : {});
    }
  }

  /**
   * A tenant that no longer exists for the user: its stored data goes, a
   * last tenant naming it clears, and a tab on it goes to the picker, with
   * a notice unless the user did not choose the tenant.
   */
  async #tenantGone(tenantId: number | null): Promise<void> {
    if (tenantId === null || this.#gone.has(tenantId)) {
      return;
    }
    this.#gone.add(tenantId);
    try {
      const store = this.#injector.get(TmClientStore);
      await store.sweep({ tenant: tenantId });
      if (store.boot.read().lastTenantId === tenantId) {
        store.boot.patch({ lastTenantId: null });
      }
      const automatic = this.#injector.get(TmLaunch).takeAutomatic(tenantId);
      this.#telemetry.event('tm.launch.tenant-gone', { automatic });
      // A sync of a tenant the tab is not on has nothing on screen to leave.
      const urls = this.#urls;
      if (untracked(urls.tenantId) === tenantId) {
        const url = automatic ? urls.picker() : urls.picker({ notice: 'tenant-not-found', from: tenantId });
        await this.#injector.get(Router).navigateByUrl(url, { replaceUrl: true });
      }
    } finally {
      this.#gone.delete(tenantId);
    }
  }

  /**
   * A suspended tenant: the shell's main area shows the tenant-state page,
   * the inline state with Switch tenant, in place of the route's page.
   */
  #tenantUnavailable(error: TmApiError, context: TmErrorContext): void {
    const routed = untracked(this.#urls.tenantId);
    const tenantId = context.tenantId ?? routed;
    if (tenantId === null || tenantId !== routed) {
      return;
    }
    this.#injector.get(TmShellMain).replace(TmErrorState, { view: this.describe(error, { ...context, tenantId }) });
  }
}

/**
 * Renders the inline state of a page whose chunk did not load because the
 * app was updated, for the route's own error view, which hands it its
 * Reload. Internal.
 *
 * @param slot - Where the state renders.
 * @returns The state.
 */
export function tmClientOutdatedState(slot: ViewContainerRef): TmErrorState {
  const state = slot.createComponent(TmErrorState);
  const error = new TmApiError({ kind: 'client-outdated', status: 412, code: 'client-outdated' });
  state.setInput('view', slot.injector.get(TmErrorPresenter).describe(error, { kind: 'read' }));
  return state.instance;
}
