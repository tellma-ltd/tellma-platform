// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  DOCUMENT,
  effect,
  inject,
  Injector,
  input,
  signal,
  type Signal,
  untracked,
} from '@angular/core';
import {
  TM_NOW,
  TM_TOAST_FEEDBACK_MS,
  TmApi,
  TmAppVersion,
  TmLeader,
  TmLocalization,
  TmRequestLog,
  type TmRequestRecord,
  TmSupport,
  TmTabBus,
  TmTelemetry,
  TmUrls,
  ɵTM_UPDATES_LOAD,
  ɵtmDiagnosticsLine,
  type ɵTmSupportSnapshot,
} from '@tellma/core';
import type { TmPageContextSnapshot } from '@tellma/core/page-snapshot';
import { TmBreakpoints } from '@tellma/core-ui';
import { TmButton } from '@tellma/core-ui/button';
import type { TmRowId } from '@tellma/core-ui/contracts';
import { TmCopyButton, TmReference } from '@tellma/core-ui/copy';
import { TmDescriptionList } from '@tellma/core-ui/description-list';
import { TmGrid, TmGridColumn, TmGridDisplayDef, TmGridEmptyDef, type TmGridRowActivation } from '@tellma/core-ui/grid';
import { TmSwitch } from '@tellma/core-ui/switch';
import { TmToasts } from '@tellma/core-ui/toast';

import { tmBuildSupportDump, tmDownloadSupportDump } from './tm-support-dump';
import { TM_SUPPORT_STRINGS_EN } from './tm-support-strings';

// The Diagnostics panel (spec 0026 §8.4): the About dialog's Diagnostics tab
// and, until the support dialog's own states land, the support dialog's
// content. A header names the builds and where the tab stands; a read-only
// grid lists the request log, newest first, with a switch that keeps only
// the slow and failed requests; Enter or a click on a row copies that
// request's line of the diagnostics text, in the gesture. A pending
// request's duration ticks each second while one is pending. Below `md`,
// the Time and Outcome columns fold into the Duration cell's second line.
// The buttons copy the whole diagnostics text, download the diagnostic
// file (without page data) and open the support dialog.

/** One row of the request grid. */
interface TmDiagnosticsRow {
  /** The request's trace id, the row's id. */
  readonly id: string;
  /** The request as the log holds it. */
  readonly record: TmRequestRecord;
  /** Its start, in the device's local time. */
  readonly time: string;
  /** How long it took, or has taken so far. */
  readonly duration: string;
  /** How it ended, or that it is pending. */
  readonly outcome: string;
  /** Whether it was slow or failed, which the row marks. */
  readonly flagged: boolean;
  /** The call: the method and the route. */
  readonly request: string;
}

/** The key the row copy's toasts share, so a second copy replaces the first's toast. */
const TOAST_KEY = 'tm.support.request-copied';

/** How often a pending request's duration ticks, in milliseconds. */
const TICK_MS = 1000;

/** The next panel's number, for its heading's id. */
let nextId = 0;

/**
 * The Diagnostics panel: the builds and the tab's state, the request log
 * as a read-only grid, and Copy diagnostics, Download diagnostic file and
 * Report a problem.
 *
 * @example
 * ```html
 * <!-- The About dialog's Diagnostics tab. -->
 * <tm-diagnostics />
 * ```
 */
@Component({
  selector: 'tm-diagnostics',
  imports: [TmButton, TmCopyButton, TmDescriptionList, TmGrid, TmGridColumn, TmGridDisplayDef, TmGridEmptyDef, TmReference, TmSwitch],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: grid;
      gap: var(--space-4);
      min-inline-size: 0;
    }
    .tm-diagnostics__value {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      overflow-wrap: anywhere;
    }
    .tm-diagnostics__code {
      font-family: var(--font-mono);
      unicode-bidi: isolate;
    }
    .tm-diagnostics__bar {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-2);
    }
    .tm-diagnostics__title {
      margin: 0;
      font-size: var(--text-md-font-size, 14px);
      font-weight: 600;
    }
    /* A fixed height, so the dialog does not grow as requests arrive. */
    .tm-diagnostics__grid {
      block-size: 16rem;
    }
    .tm-diagnostics__second {
      display: block;
      color: var(--text-secondary);
      font-size: var(--field-hint-font-size);
    }
    .tm-diagnostics__flagged {
      color: var(--text-danger, var(--text-body));
      font-weight: 600;
    }
    .tm-diagnostics__actions {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: var(--space-2);
    }
  `,
  template: `
    <!-- The header: the builds and where the tab stands. -->
    <dl tmDescriptionList columns="auto" [attr.aria-label]="text.header.label()">
      <div>
        <dt>{{ text.header.ui() }}</dt>
        <dd>
          <span class="tm-diagnostics__value">
            <bdi class="tm-diagnostics__code" dir="ltr">{{ ui || text.value.unknown() }}</bdi>
            @if (ui) {
              <tm-copy-button size="sm" [text]="ui" [label]="text.header.ui()" />
            }
          </span>
        </dd>
      </div>
      <div>
        <dt>{{ text.header.server() }}</dt>
        <dd><bdi class="tm-diagnostics__code" dir="ltr">{{ server() ?? text.value.unknown() }}</bdi></dd>
      </div>
      <div>
        <dt>{{ text.header.worker() }}</dt>
        <dd><bdi class="tm-diagnostics__code" dir="ltr">{{ worker() ?? text.value.none() }}</bdi></dd>
      </div>
      <div>
        <dt>{{ text.header.tenant() }}</dt>
        <dd>{{ tenant() }}</dd>
      </div>
      <div>
        <dt>{{ text.header.tab() }}</dt>
        <dd><bdi class="tm-diagnostics__code" dir="ltr">{{ tabId }}</bdi></dd>
      </div>
      <div>
        <dt>{{ text.header.connection() }}</dt>
        <dd>{{ online() ? text.value.online() : text.value.offline() }}</dd>
      </div>
      <div>
        <dt>{{ text.header.leader() }}</dt>
        <dd>{{ leader() ? text.value.thisTab() : text.value.otherTab() }}</dd>
      </div>
      <div>
        <dt>{{ text.header.hub() }}</dt>
        <dd>{{ hub() }}</dd>
      </div>
    </dl>

    <!-- The requests: the title, and the switch that keeps the slow and failed ones. -->
    <div class="tm-diagnostics__bar">
      <h3 class="tm-diagnostics__title" [id]="titleId">{{ text.requests.label() }}</h3>
      <tm-switch [(checked)]="flaggedOnly">{{ text.requests.slowFailed() }}</tm-switch>
    </div>
    <!-- The request log: one tab stop, arrow keys, and Enter or a click copies the row's line. -->
    <div class="tm-diagnostics__grid" role="group" [attr.aria-labelledby]="titleId">
      <tm-grid gridId="tm-diagnostics" readonly [data]="rows()" [rowId]="rowId" rowActivation="row" (rowActivate)="copyRow($event)">
        @if (wide()) {
          <tm-grid-column key="time" [header]="text.requests.time()" [width]="96" />
        }
        <tm-grid-column key="duration" [header]="text.requests.duration()" [width]="wide() ? 104 : 136">
          <span *tmGridDisplay="let value; rowId as id">
            @let row = rowOf(id);
            <span [class.tm-diagnostics__flagged]="row.flagged">{{ row.duration }}</span>
            @if (!wide()) {
              <!-- Narrow: the start and the outcome fold into a second line. -->
              <span class="tm-diagnostics__second">{{ row.time }} · {{ row.outcome }}</span>
            }
          </span>
        </tm-grid-column>
        @if (wide()) {
          <tm-grid-column key="outcome" [header]="text.requests.outcome()" [width]="104">
            <span *tmGridDisplay="let value; rowId as id" [class.tm-diagnostics__flagged]="rowOf(id).flagged">{{ rowOf(id).outcome }}</span>
          </tm-grid-column>
        }
        <tm-grid-column key="request" [header]="text.requests.request()" [width]="220">
          <bdi *tmGridDisplay="let value; rowId as id" class="tm-diagnostics__code" dir="ltr">{{ rowOf(id).request }}</bdi>
        </tm-grid-column>
        <tm-grid-column key="id" [header]="text.requests.reference()" [width]="320">
          <tm-reference *tmGridDisplay="let value; rowId as id" [value]="rowOf(id).id" />
        </tm-grid-column>
        <span *tmGridEmpty>{{ flaggedOnly() ? text.requests.emptyFiltered() : text.requests.empty() }}</span>
      </tm-grid>
    </div>

    <!-- The actions: the diagnostics text, the diagnostic file, and the support dialog. -->
    <div class="tm-diagnostics__actions">
      <button tmButton type="button" variant="secondary" (click)="copy()">{{ text.copy() }}</button>
      <button tmButton type="button" variant="secondary" [pending]="downloading()" (click)="download()">{{ text.download() }}</button>
      @if (reportable()) {
        <button tmButton type="button" variant="secondary" (click)="report()">{{ text.report() }}</button>
      }
    </div>
  `,
})
export class TmDiagnosticsPanel {
  readonly #injector = inject(Injector);
  readonly #l10n = inject(TmLocalization);
  readonly #log = inject(TmRequestLog);
  readonly #support = inject(TmSupport);
  readonly #version = inject(TmAppVersion);
  readonly #leader = inject(TmLeader);
  readonly #urls = inject(TmUrls);
  readonly #now = inject(TM_NOW);

  /**
   * The snapshot the support dialog took, from which Download builds the
   * file; without one, Download snapshots the tab when pressed.
   */
  readonly snapshot = input<ɵTmSupportSnapshot<TmPageContextSnapshot> | undefined>(undefined);

  /** Whether the panel offers Report a problem; the support dialog's own panel does not. */
  readonly reportable = input(true, { transform: booleanAttribute });

  /** The labels. */
  protected readonly text = this.#strings();

  /** The heading's id, which names the grid. */
  protected readonly titleId = `tm-diagnostics-${nextId++}`;

  /** The UI version. */
  protected readonly ui = this.#version.ui;

  /** The last server build seen. */
  protected readonly server = this.#version.server;

  /** The tab's id. */
  protected readonly tabId = inject(TmTabBus).tabId;

  /** Whether the server is reachable. */
  protected readonly online = inject(TmApi).online;

  /** Whether this tab leads the tenant. */
  protected readonly leader = this.#leader.isLeader;

  /** The routed tenant, or None. */
  protected readonly tenant: Signal<string> = computed(() => {
    const tenantId = this.#urls.tenantId();
    return tenantId === null ? this.text.value.none() : String(tenantId);
  });

  /** The realtime connection's state, in words. */
  protected readonly hub: Signal<string> = computed(() => this.#l10n.ɵtext(`tm.support.hub.${this.#leader.hubStatus().state}`)());

  /** The worker's version signal, once the update service has loaded; `null` without one. */
  readonly #workerVersion = signal<Signal<string | null> | null>(null);

  /** The version the worker serves the page, or `null` without a worker. */
  protected readonly worker: Signal<string | null> = computed(() => this.#workerVersion()?.() ?? null);

  /** Whether the grid keeps only the slow and failed requests. */
  protected readonly flaggedOnly = signal(false);

  /** Whether the viewport reaches `md`, where every column shows. */
  protected readonly wide = inject(TmBreakpoints).atLeast('md');

  /** Whether the diagnostic file is being built. */
  protected readonly downloading = signal(false);

  /** Ticks each second while a request is pending. */
  readonly #tick = signal(0);

  /** The request grid's rows, newest first. */
  protected readonly rows: Signal<readonly TmDiagnosticsRow[]> = computed(() => {
    this.#tick();
    const now = untracked(this.#now);
    const locale = this.#l10n.locale();
    const clock = new Intl.DateTimeFormat(locale, { hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' });
    const number = (decimals: number): Intl.NumberFormat =>
      new Intl.NumberFormat(locale, { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
    const settled = number(2);
    const pending = number(1);
    const flaggedOnly = this.flaggedOnly();
    return this.#log
      .recent()
      .filter((record) => !flaggedOnly || flagged(record))
      .map((record) => ({
        id: record.traceId,
        record,
        time: clock.format(record.startedAt),
        duration: this.#l10n.ɵtext('tm.support.seconds', {
          value:
            record.durationMs === null
              ? pending.format(Math.max(0, now - record.startedAt) / 1000)
              : settled.format(record.durationMs / 1000),
        })(),
        outcome: this.#l10n.ɵtext(`tm.support.outcome.${outcomeOf(record)}`)(),
        flagged: flagged(record),
        request: `${record.method} ${record.route}`,
      }));
  });

  /** A row's id: its trace id. */
  protected readonly rowId = (row: TmDiagnosticsRow): string => row.id;

  /** The rows by id, for the cells' templates. */
  readonly #byId: Signal<ReadonlyMap<TmRowId, TmDiagnosticsRow>> = computed(() => new Map(this.rows().map((row) => [row.id, row])));

  /**
   * A row by its id, for a cell's template.
   *
   * @param id - The row's id.
   * @returns The row.
   */
  protected rowOf(id: TmRowId): TmDiagnosticsRow {
    return this.#byId().get(id)!;
  }

  /** Registers the strings, loads the worker's version and ticks while a request is pending. */
  constructor() {
    // --- The worker's version, once the update service has loaded ---
    const injector = this.#injector;
    void injector
      .get(ɵTM_UPDATES_LOAD)()
      .then(
        (loaded) => loaded !== null && this.#workerVersion.set(injector.get(loaded.TmUpdates).version),
        () => undefined,
      );
    // --- A pending duration ticks each second ---
    effect((onCleanup) => {
      if (this.#log.recent().some((record) => record.durationMs === null)) {
        const timer = setInterval(() => this.#tick.update((tick) => tick + 1), TICK_MS);
        onCleanup(() => clearInterval(timer));
      }
    });
  }

  /**
   * Copies a row's line of the diagnostics text, inside the gesture, and toasts the outcome.
   *
   * @param activation - The row the user activated.
   */
  protected copyRow(activation: TmGridRowActivation<TmDiagnosticsRow>): void {
    let written: Promise<void>;
    try {
      const line = ɵtmDiagnosticsLine(activation.row.record, this.#now());
      written = this.#injector.get(DOCUMENT).defaultView!.navigator.clipboard.writeText(line);
    } catch (error) {
      // No clipboard here, such as on an insecure origin.
      written = Promise.reject(error);
    }
    const toasts = this.#injector.get(TmToasts);
    written.then(
      () => toasts.show({ tone: 'success', title: untracked(this.text.requests.copied), duration: TM_TOAST_FEEDBACK_MS, key: TOAST_KEY }),
      () => toasts.show({ tone: 'error', title: untracked(this.text.requests.copyFailed), duration: 'sticky', key: TOAST_KEY }),
    );
  }

  /** Copy diagnostics: the whole text, inside the gesture. */
  protected copy(): void {
    this.#support.copyDiagnostics('button');
  }

  /** Download diagnostic file: from the support dialog's snapshot, or a snapshot taken now; never with page data. */
  protected async download(): Promise<void> {
    const snapshot = this.snapshot();
    if (snapshot === undefined) {
      this.#support.downloadDump();
      return;
    }
    if (untracked(this.downloading)) {
      return;
    }
    this.downloading.set(true);
    try {
      tmDownloadSupportDump(await tmBuildSupportDump(snapshot, { includeData: false, injector: this.#injector }), this.#injector);
    } catch (error) {
      this.#injector.get(TmTelemetry).error(error, { where: 'tm.support.download' });
    } finally {
      this.downloading.set(false);
    }
  }

  /** Report a problem: the support dialog. */
  protected report(): void {
    this.#support.open();
  }

  /**
   * The panel's labels, as signals in the active language.
   *
   * @returns The labels, shaped as the English strings.
   */
  #strings(): TmSupportText {
    this.#l10n.ɵregisterStrings('support', TM_SUPPORT_STRINGS_EN);
    const of = (prefix: string, tree: object): object =>
      Object.fromEntries(
        Object.entries(tree).map(([key, value]) => [
          key,
          typeof value === 'string' ? this.#l10n.ɵtext(`${prefix}.${key}`) : of(`${prefix}.${key}`, value as object),
        ]),
      );
    return of('tm.support', TM_SUPPORT_STRINGS_EN) as TmSupportText;
  }
}

/** The panel's labels: the English strings' shape, each string a signal. */
type TmSupportText = TmTextTree<typeof TM_SUPPORT_STRINGS_EN>;

/** A string tree with each string replaced by its signal. */
type TmTextTree<T> = { readonly [K in keyof T]: T[K] extends string ? Signal<string> : TmTextTree<T[K]> };

/**
 * Whether a request was slow or failed.
 *
 * @param record - The request.
 * @returns Whether the slow and failed switch keeps it.
 */
function flagged(record: TmRequestRecord): boolean {
  return record.slow || record.outcome === 'error';
}

/**
 * A request's outcome, as the grid words it.
 *
 * @param record - The request.
 * @returns Its key under `tm.support.outcome`.
 */
function outcomeOf(record: TmRequestRecord): 'pending' | 'ok' | 'slow' | 'cancelled' | 'error' {
  if (record.outcome === 'pending' || record.outcome === 'cancelled' || record.outcome === 'error') {
    return record.outcome;
  }
  return record.slow ? 'slow' : 'ok';
}
