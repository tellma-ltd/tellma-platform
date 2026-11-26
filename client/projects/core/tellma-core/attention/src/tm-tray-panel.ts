// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LocationStrategy } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, ElementRef, inject, signal, untracked, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { TmEntities, type TmEntityType, type TmId, TmLocalization, type TmPageAction, TmPageContexts, TmTelemetry, TmUrls } from '@tellma/core';
import { type TmAssignmentActivation, TmAssignmentList, TmAttentionText } from '@tellma/core/attention-rows';
import { TmCounter } from '@tellma/core-ui/counter';
import { TmEmptyState } from '@tellma/core-ui/empty-state';
import { TM_MODAL_DATA } from '@tellma/core-ui/modal';

import { TmAttention } from './tm-attention';
import { type TmAttentionPanelData, tmItemPosition } from './tm-attention-panel-data';

// The tray's panel (spec 0028 §7.2): the heading with the open count, the
// user's open assignments through the tray's assignment list, and the
// footer link. Rows assigned since the user last opened the tray are marked
// "New" against the seenAt the panel opened with, which the panel's own
// `seen` does not move while it is open. Choosing a row closes the panel and
// opens the row's target; nothing is marked, since the count drops when the
// work completes or moves on. An open panel publishes its overlay page
// context, `panel:tray`.

/** The assignments' entity, whose search page the footer opens. */
const ASSIGNMENT = 'core.Assignment';

/**
 * The tray's panel: the work waiting on the user. Created by the panels'
 * host in a popover or an end drawer.
 */
@Component({
  selector: 'tm-tray-panel',
  imports: [TmAssignmentList, TmCounter, TmEmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'tm-attention-panel', id: 'tm-attention-tray-panel' },
  template: `
    <!-- The header: the heading and its open count (the drawer's own title stands in for the heading). -->
    <div class="tm-attention-panel__header">
      @if (!drawer) {
        <h2 #heading class="tm-attention-panel__heading" tabindex="-1">
          {{ text('tray.heading') }}
          @if (open(); as count) {
            <tm-counter [value]="count" aria-hidden="true" />
            <span class="tm-attention-panel__hidden">{{ text('tray.open', { count }) }}</span>
          }
        </h2>
      } @else if (open(); as count) {
        <span class="tm-attention-panel__count">{{ text('tray.open', { count }) }}</span>
      }
    </div>
    <!-- The rows: skeletons before any document, the empty state, or the list. -->
    <div class="tm-attention-panel__body">
      @if (assignments(); as summary) {
        @if (summary.latest.length === 0) {
          <tm-empty-state size="panel" icon="inbox" [heading]="text('tray.empty')" />
        } @else {
          <tm-assignment-list
            variant="tray"
            party="to-me"
            [label]="text('tray.list')"
            [items]="summary.latest"
            [related]="summary.related"
            [seenAt]="seenAt"
            [fallback]="fallback()"
            (activated)="activated($event)"
          />
        }
      } @else {
        <tm-assignment-list variant="tray" party="to-me" [loading]="true" />
      }
    </div>
    <!-- The footer: every assignment, on its page, once the server routes the assignments. -->
    @if (all(); as link) {
      <div class="tm-attention-panel__footer">
        <a class="tm-attention-panel__see-all" [attr.href]="link.href" (click)="follow($event, link.url)">{{ text('tray.seeAll') }}</a>
      </div>
    }
  `,
})
export class TmTrayPanel {
  readonly #attention = inject(TmAttention);
  readonly #text = inject(TmAttentionText);
  readonly #l10n = inject(TmLocalization);
  readonly #urls = inject(TmUrls);
  readonly #router = inject(Router);
  readonly #telemetry = inject(TmTelemetry);
  readonly #host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  /** The heading, absent in the drawer. */
  private readonly headingRef = viewChild<ElementRef<HTMLElement>>('heading');

  /** Whether the panel sits in the end drawer, whose title stands in for the heading. */
  protected readonly drawer = (inject(TM_MODAL_DATA, { optional: true }) as TmAttentionPanelData | null)?.drawer === true;

  /** The stored assignments; `undefined` before any copy. */
  protected readonly assignments = this.#attention.assignments;

  /** The open count; `null` before any copy. */
  protected readonly open = computed(() => this.assignments()?.open || null);

  /** The `seenAt` the panel opened with, against which rows are marked "New". */
  protected readonly seenAt = untracked(this.assignments)?.seenAt ?? null;

  readonly #location = inject(LocationStrategy);
  readonly #entities = inject(TmEntities);

  /** The footer's page of every assignment; `null` while the server routes no assignments. */
  protected readonly all = computed(() => {
    if (!this.#entities.summary(ASSIGNMENT)?.segment) {
      return null;
    }
    const url = this.#urls.search(ASSIGNMENT);
    return { url, href: this.#location.prepareExternalUrl(url) };
  });

  /** Where focus goes when the last row leaves while focused: the heading, else the panel. */
  protected readonly fallback = computed(() => this.headingRef()?.nativeElement ?? this.#host);

  /** Creates the panel: its page context and its first focus. */
  constructor() {
    this.#publish(inject(DestroyRef));
    // Opening focuses the first row; after the popover's and the drawer's own first focus.
    afterNextRender({
      read: () => this.#host.querySelector<HTMLElement>('[data-tm-row] [data-tm-row-control]')?.focus(),
    });
  }

  /**
   * A string of the panels'.
   *
   * @param key - The key under `tm.attentionPanels`.
   * @param params - The message's parameters.
   * @returns The text.
   */
  protected text(key: string, params?: Readonly<Record<string, unknown>>): string {
    return this.#l10n.ɵtext(`tm.attentionPanels.${key}`, params)();
  }

  /**
   * A row the user chose: the panel closes as the list opens its target.
   *
   * @param activation - The row and its place.
   */
  protected activated(activation: TmAssignmentActivation): void {
    this.#telemetry.event('tm.attention.item-opened', { panel: 'tray', kind: 'assignment', position: tmItemPosition(activation.position - 1) });
    this.#close();
  }

  /**
   * The footer link's click: a plain click closes the panel and opens the page in the app.
   *
   * @param event - The click.
   * @param url - The page's app URL.
   */
  protected follow(event: MouseEvent, url: string): void {
    if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
      return;
    }
    event.preventDefault();
    this.#close();
    void this.#router.navigateByUrl(url);
  }

  /** Closes the panel. */
  #close(): void {
    if (untracked(this.#attention.open) === 'tray') {
      this.#attention.toggle('tray', 'click');
    }
  }

  /** Publishes the panel's overlay page context while it is open. */
  #publish(destroyRef: DestroyRef): void {
    const latest = computed(() => this.assignments()?.latest ?? []);
    const captions = new Map<TmEntityType<'core.Assignment'>, ReturnType<TmAttentionText['caption']>>();
    const caption = (row: TmEntityType<'core.Assignment'>): ReturnType<TmAttentionText['caption']> => {
      let made = captions.get(row);
      if (made === undefined) {
        made = untracked(() => this.#text.caption(row));
        captions.set(row, made);
      }
      return made;
    };
    const seenAt = Date.parse(this.seenAt ?? '');
    const open: TmPageAction = {
      id: 'attention.open-item',
      labelKey: 'tm.attentionPanels.actions.openItem',
      group: 'menu',
      enabled: signal(true).asReadonly(),
      destructive: false,
      disabledReason: signal<string | null>(null).asReadonly(),
      params: { type: 'object', properties: { id: { type: 'integer' } }, required: ['id'] },
      invoke: (args) => {
        const id = (args as { readonly id?: unknown }).id;
        const index = untracked(latest).findIndex((row) => row['id'] === id);
        const row = untracked(latest)[index];
        const targetId = row?.['targetId'];
        const url =
          row !== undefined && (typeof targetId === 'string' || typeof targetId === 'number')
            ? this.#urls.target(String(row['resource'] ?? ''), targetId as TmId)
            : null;
        if (url === null) {
          return Promise.resolve({ outcome: 'failed', error: new Error(`The tray shows no assignment ${String(id)} with a page.`) });
        }
        this.activated({ row: row!, position: index + 1 });
        void this.#router.navigateByUrl(url);
        return Promise.resolve({ outcome: 'done' });
      },
    };
    inject(TmPageContexts).register(
      {
        kind: 'custom',
        routeTemplate: 'panel:tray',
        overlay: true,
        title: computed(() => this.text('tray.heading')),
        status: computed(() => (this.assignments() === undefined ? 'loading' : 'ready')),
        state: computed(() => {
          const summary = this.assignments();
          return { open: summary?.open ?? null, new: summary?.new ?? null };
        }),
        visible: computed(() => {
          const rows = latest();
          return {
            columns: [
              { path: 'Resource', label: 'Resource' },
              { path: 'Title', label: this.text('tray.heading') },
              { path: 'State', label: 'State' },
            ],
            rows: rows.map((row) => ({
              id: typeof row['id'] === 'number' ? row['id'] : null,
              cells: {
                Resource: String(row['resource'] ?? ''),
                Title: caption(row)().title,
                State: Date.parse(String(row['assignedAt'] ?? '')) > seenAt && row['assignedById'] !== row['assigneeId'] ? 'new' : 'open',
              },
            })),
            total: rows.length,
            truncated: false,
          };
        }),
        errors: signal([]).asReadonly(),
        actions: signal([open]).asReadonly(),
      },
      destroyRef,
    );
  }
}
