// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import { LocationStrategy } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  input,
  output,
  type Signal,
  signal,
  untracked,
} from '@angular/core';
import { Router } from '@angular/router';
import {
  TM_NOW,
  TmDisplayName,
  type TmDisplayNameParts,
  TmDisplayNames,
  TmEntities,
  type TmEntityType,
  type TmId,
  type TmJson,
  TmLocalization,
  TmUrls,
} from '@tellma/core';
import { TmL10n } from '@tellma/core-ui';
import { TmIcon, type TmIconSource } from '@tellma/core-ui/icon';
import { TmRelativeTime } from '@tellma/core-ui/relative-time';
import { TmSkeleton } from '@tellma/core-ui/skeleton';
import FileText from 'lucide/dist/esm/icons/file-text.mjs';
import Send from 'lucide/dist/esm/icons/send.mjs';

import { ɵTmAttentionRowStyles } from './tm-attention-row-styles';
import { ɵTmAttentionSkeletonRows } from './tm-attention-skeleton-rows';
import { type TmAssignmentCaption, TmAttentionText, type TmTextSegment } from './tm-attention-text';
import { tmRegisterAttentionRowsStrings } from './tm-attention-rows-strings';
import { TmRovingRows } from './tm-roving-rows';

// One row anatomy for the tray and the home lists (spec 0028 §7.3). A row's
// title is its caption's "{entity} {code}" (tray) or its code (home), the
// row's one link, to the assignment's target. Its second line says why the
// row is there: the comment, the caption's detail and amount, or who
// assigned it. Names render from their display-name parts in
// `tm-display-name`, and a comment in a `dir="auto"` isolate, each split out
// of its translated message so the plain text keeps the translation's
// order. Rows to the user that arrived after the `seenAt` the list was given,
// and that the user did not assign themselves, are marked "New". The list
// is one tab stop with roving rows; it fills its container and scrolls
// inside. The row's box is the attention rows' shared anatomy, which the
// home variant lays on one line of the dashboard's list-row height (spec
// 0035 §15.3), so ten rows fill a tile; its loading rows are the dashboard
// frames' bars, which the home skeleton and a loading tile draw too.
// Hovering or focusing a row preloads its target's page.

/** Which rows a list shows: those assigned to the user, or by the user. */
export type TmAssignmentParty = 'to-me' | 'by-me';

/** A list's row anatomy: the tray's, or the home widgets'. */
export type TmAssignmentListVariant = 'tray' | 'home';

/** A row the user activated, and its place in the list. */
export interface TmAssignmentActivation {
  /** The assignment. */
  readonly row: TmEntityType<'core.Assignment'>;
  /** Its place in the list, counting from 1. */
  readonly position: number;
}

/** One piece of a row's second line: text, a person's display name, or a comment. */
interface LinePiece {
  /** The piece's plain text, which the row's accessible name reads. */
  readonly text: string;
  /** The display name's parts, for a person; absent for text and comments. */
  readonly parts?: TmDisplayNameParts | null;
  /** Whether the piece is a comment, which renders in its own `dir="auto"` isolate. */
  readonly comment?: boolean;
}

/** One row, as the list renders it. */
interface RowView {
  /** The assignment. */
  readonly row: TmEntityType<'core.Assignment'>;
  /** Its id, which keys the row. */
  readonly key: string;
  /** The target's app URL, or `null` when the target has no page. */
  readonly url: string | null;
  /** The link's address, as the router writes it. */
  readonly href: string | null;
  /** The row icon's registered name, or `null` to draw `svg`. */
  readonly iconName: string | null;
  /** The row icon's data, drawn when it has no registered name. */
  readonly iconSvg: TmIconSource;
  /** The caption: title, detail and amount, which follow the language. */
  readonly caption: Signal<TmAssignmentCaption>;
  /** The title's pieces: the caption's (tray) or the code alone (home). */
  readonly title: Signal<readonly TmTextSegment[]>;
  /** The second line, which follows the language and the names. */
  readonly line: Signal<readonly LinePiece[]>;
  /** When it was assigned, as ISO 8601. */
  readonly assignedAt: string;
}

/** A row's member as non-empty text, else `undefined`. */
function textOf(row: TmEntityType<'core.Assignment'>, member: keyof TmEntityType<'core.Assignment'>): string | undefined {
  const value: unknown = row[member];
  return typeof value === 'string' && value !== '' ? value : undefined;
}

/** A row's member as an id, else `null`. */
function idOf(row: TmEntityType<'core.Assignment'>, member: keyof TmEntityType<'core.Assignment'>): number | null {
  const value: unknown = row[member];
  return typeof value === 'number' ? value : null;
}

/** The users' entity, whose related rows name the assigners and the assignees. */
const USER = 'core.User';

/** How often the rows' accessible names re-read their relative times. */
const MINUTE_MS = 60_000;

/**
 * The attention lists' rows of assignments: the tray's, and the home
 * page's widgets of work assigned to and by the user. Each row is a real
 * link to the assignment's target; choosing it navigates there, and a
 * modified click opens it in a new tab.
 *
 * @example
 * ```html
 * <tm-assignment-list
 *   variant="tray" party="to-me"
 *   [items]="summary.latest" [related]="summary.related" [seenAt]="openedSeenAt"
 *   (activated)="close()" />
 * ```
 */
@Component({
  selector: 'tm-assignment-list',
  imports: [TmDisplayName, TmIcon, TmRelativeTime, TmSkeleton, ɵTmAttentionRowStyles, ɵTmAttentionSkeletonRows, TmRovingRows],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'tm-assignment-list',
    '[class.tm-assignment-list--home]': 'variant() === "home"',
  },
  template: `
    <tm-attention-row-styles />
    @if (loading() && variant() === 'home') {
      <!-- The home lists loading: five bars in the dashboard's list rows, read as one busy text inside the tile's own region. -->
      <div class="tm-assignment-list__loading" aria-busy="true">
        <span class="tm-assignment-list__loading-text">{{ text('loading') }}</span>
        <tm-skeleton class="tm-dashboard-frame__rows" shape="line" [lines]="5" />
      </div>
    } @else if (loading()) {
      <!-- Loading: three rows of the final geometry, read as one busy list. -->
      <tm-attention-skeleton-rows [label]="text('loading')" />
    } @else {
      <!-- The rows: one tab stop, each row's link its only control. -->
      <ul class="tm-attention-rows" tmRovingRows [attr.aria-label]="label()" [fallback]="fallback()">
        @for (view of rows(); track view.key; let index = $index) {
          <li data-tm-row [attr.data-tm-id]="view.key">
            <a
              class="tm-attention-row"
              data-tm-row-control
              [attr.href]="view.href"
              [attr.aria-disabled]="view.href === null ? 'true' : null"
              [attr.aria-label]="rowLabel(view)"
              (click)="follow($event, view, index)"
              (pointerenter)="preload(view)"
              (focus)="preload(view)"
            >
              <tm-icon class="tm-attention-row__icon" [name]="view.iconName ?? undefined" [svg]="view.iconName === null ? view.iconSvg : undefined" />
              <span class="tm-attention-row__main">
                <!-- The title, its code in an isolate. -->
                <span class="tm-attention-row__title">
                  @for (segment of view.title(); track $index) {
                    @if (segment.arg) {<bdi>{{ segment.text }}</bdi>} @else {{{ segment.text }}}
                  }
                </span>
                <!-- The second line: names from their parts, a comment in its own isolate. -->
                <span class="tm-attention-row__line">
                  @for (piece of view.line(); track $index) {
                    @if (piece.parts !== undefined) {<tm-display-name [parts]="piece.parts" />} @else if (piece.comment) {<bdi dir="auto">{{ piece.text }}</bdi>} @else {{{ piece.text }}}
                  }
                </span>
              </span>
              <!-- The end: the amount on the home lists, the time, and the "New" mark. -->
              <span class="tm-attention-row__end">
                @if (variant() === 'home' && view.caption().amount; as amount) {
                  <bdi class="tm-assignment-list__amount">{{ amount }}</bdi>
                }
                <tm-relative-time [value]="view.assignedAt" textStyle="narrow" />
                @if (fresh(view)) {
                  <span class="tm-assignment-list__new" aria-hidden="true">{{ text('new') }}</span>
                }
              </span>
            </a>
          </li>
        }
      </ul>
    }
  `,
  styles: `
    :host {
      display: block;
      block-size: 100%;
      overflow: auto;
      overscroll-behavior: contain;
    }

    /* The list's own touches on the shared row: a bold title and a quiet icon. */
    .tm-attention-row__title {
      font-weight: var(--weight-semibold);
    }

    .tm-attention-row__icon {
      color: var(--text-muted);
    }

    /* The loading text, read but not drawn: the bars draw the state. */
    .tm-assignment-list__loading-text {
      position: absolute;
      inline-size: 1px;
      block-size: 1px;
      margin: 0;
      padding: 0;
      overflow: hidden;
      clip-path: inset(50%);
      white-space: nowrap;
    }

    .tm-assignment-list__amount {
      color: var(--text-body);
      font-variant-numeric: tabular-nums;
    }

    /* The "New" mark: an accent dot, and the word in the link ink, which meets AA in both themes. */
    .tm-assignment-list__new {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      color: var(--text-link);
      font-weight: var(--weight-semibold);
    }

    /* The home rows: one line of the dashboard's list-row height, the code, the second line and the end side by side. */
    :host(.tm-assignment-list--home) .tm-attention-row {
      align-items: center;
      block-size: var(--tm-dashboard-list-row, 31px);
      min-block-size: 0;
      padding-block: 0;
      padding-inline: var(--card-header-padding-x);
      border-radius: 0;
    }

    :host(.tm-assignment-list--home) .tm-attention-row__icon {
      margin-block-start: 0;
    }

    :host(.tm-assignment-list--home) .tm-attention-row__main {
      flex-direction: row;
      align-items: baseline;
      gap: var(--space-2);
    }

    :host(.tm-assignment-list--home) .tm-attention-row__title {
      flex: 0 1 auto;
      max-inline-size: 50%;
    }

    :host(.tm-assignment-list--home) .tm-attention-row__line {
      flex: 1 1 0;
      min-inline-size: 0;
    }

    :host(.tm-assignment-list--home) .tm-attention-row__end {
      flex-direction: row;
      align-items: center;
      gap: var(--space-2);
    }

    .tm-assignment-list__new::before {
      content: '';
      inline-size: 6px;
      block-size: 6px;
      border-radius: var(--radius-full);
      background: var(--accent);
    }
  `,
})
export class TmAssignmentList {
  readonly #text = inject(TmAttentionText);
  readonly #l10n = inject(TmLocalization);
  readonly #numbers = inject(TmL10n);
  readonly #names = inject(TmDisplayNames);
  readonly #entities = inject(TmEntities);
  readonly #urls = inject(TmUrls);
  readonly #router = inject(Router);
  readonly #location = inject(LocationStrategy);
  readonly #now = inject(TM_NOW);

  /** Each assignment's view, kept while the row stays, so its signals are made once. */
  #views = new Map<TmEntityType<'core.Assignment'>, RowView>();

  /** The variant and party the views were made for. */
  #viewsFor = '';

  /** Ticks every minute, so the rows' accessible names keep their relative times current. */
  readonly #minute = signal(0);

  /** The assignments, newest first. */
  readonly items = input<readonly TmEntityType<'core.Assignment'>[]>([]);

  /** The rows the assignments name, by entity, such as a summary's related users. */
  readonly related = input<Readonly<Record<string, readonly TmJson[]>>>({});

  /** Whether the rows are assigned to the user, or by the user. */
  readonly party = input<TmAssignmentParty>('to-me');

  /** The row anatomy. */
  readonly variant = input<TmAssignmentListVariant>('tray');

  /** Rows to the user assigned after it, and not by themselves, show "New"; `null` marks none. */
  readonly seenAt = input<string | null>(null);

  /** Whether the list shows skeleton rows of its final geometry while there are no rows yet. */
  readonly loading = input(false);

  /** Where focus goes when the last row leaves while focused. */
  readonly fallback = input<HTMLElement | null>(null);

  /** The list's accessible name, such as "Assignments". */
  readonly label = input<string | null>(null);

  /** A row the user chose with a plain click or Enter, before the list navigates to its target. */
  readonly activated = output<TmAssignmentActivation>();

  /** The rows, newest first. */
  protected readonly rows: Signal<readonly RowView[]> = computed(() => {
    const items = this.items();
    const variant = this.variant();
    const party = this.party();
    // The views are made outside the reactive read: a view's own signals
    // track what they read when the template reads them.
    return untracked(() => {
      // A change of variant or party remakes every view; otherwise a row keeps its view.
      const kept = this.#viewsFor === `${variant}:${party}` ? this.#views : new Map<TmEntityType<'core.Assignment'>, RowView>();
      const views = new Map<TmEntityType<'core.Assignment'>, RowView>();
      for (const row of items) {
        views.set(row, kept.get(row) ?? this.#view(row, variant, party));
      }
      this.#views = views;
      this.#viewsFor = `${variant}:${party}`;
      return [...views.values()];
    });
  });

  /** Creates the list, which holds the related rows' names and keeps its accessible names' times current. */
  constructor() {
    tmRegisterAttentionRowsStrings(this.#l10n);
    // The names come from the related rows the summary carries.
    effect(() => {
      const related = this.related();
      untracked(() => this.#names.rememberRelated(related));
    });
    const timer = setInterval(() => this.#minute.update((tick) => tick + 1), MINUTE_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  /**
   * A string of the list's.
   *
   * @param key - The key under `tm.attentionRows.list`.
   * @returns The text.
   */
  protected text(key: string): string {
    return this.#l10n.ɵtext(`tm.attentionRows.list.${key}`)();
  }

  /**
   * Whether a row shows "New": a row to the user assigned after the list's
   * `seenAt`, by someone else.
   *
   * @param view - The row.
   * @returns Whether it is new.
   */
  protected fresh(view: RowView): boolean {
    const seenAt = this.seenAt();
    const { row } = view;
    return (
      this.party() === 'to-me' &&
      seenAt !== null &&
      Date.parse(view.assignedAt) > Date.parse(seenAt) &&
      idOf(row, 'assignedById') !== idOf(row, 'assigneeId')
    );
  }

  /**
   * A row link's accessible name: its title, its second line, its time and
   * its "New" mark, joined with the language's separator.
   *
   * @param view - The row.
   * @returns The name.
   */
  protected rowLabel(view: RowView): string {
    this.#minute();
    const caption = view.caption();
    const line = view.line()
      .map((piece) => piece.text)
      .join('');
    const time = this.#numbers.formatRelativeTime(view.assignedAt, { now: this.#now(), style: 'long' });
    const parts = [
      this.variant() === 'tray' ? caption.title : view.title().map((segment) => segment.text).join(''),
      line,
      this.variant() === 'home' ? caption.amount : null,
      time,
      this.fresh(view) ? this.text('new') : null,
    ];
    return parts.filter((part) => part !== null && part !== '').join(this.#l10n.ɵtext('tm.attentionRows.separator')());
  }

  /**
   * Preloads a row's target page at idle, before the user opens it.
   *
   * @param view - The row.
   */
  protected preload(view: RowView): void {
    if (view.url !== null) {
      this.#urls.preload(view.url);
    }
  }

  /**
   * A row link's click: a plain click navigates in the app and tells the
   * owner; a click that opens a new tab or window stays the browser's.
   *
   * @param event - The click.
   * @param view - The row.
   * @param index - Its index in the list.
   */
  protected follow(event: MouseEvent, view: RowView, index: number): void {
    if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
      return;
    }
    event.preventDefault();
    if (view.url === null) {
      return;
    }
    this.activated.emit({ row: view.row, position: index + 1 });
    void this.#router.navigateByUrl(view.url);
  }

  // --- The rows ---

  /** Makes a row's view: its link, its icon and the signals of its text. */
  #view(row: TmEntityType<'core.Assignment'>, variant: TmAssignmentListVariant, party: TmAssignmentParty): RowView {
    const resource = textOf(row, 'resource') ?? '';
    const targetId = row['targetId'];
    // A long id arrives as its decimal text.
    const url = typeof targetId === 'string' || typeof targetId === 'number' ? this.#urls.target(resource, targetId as TmId) : null;
    const declared: unknown = party === 'by-me' && variant === 'home' ? null : this.#entities.declaration(resource)?.['icon'];
    const icon = typeof declared === 'string' ? declared : null;
    const caption = this.#text.caption(row);
    const code = computed(() => caption().titleSegments.filter((segment) => segment.arg === 'code'));
    return {
      row,
      key: String(row['id']),
      url,
      href: url === null ? null : this.#location.prepareExternalUrl(url),
      iconName: icon,
      iconSvg: party === 'by-me' && variant === 'home' ? { node: Send, mirrorInRtl: true } : FileText,
      caption,
      title: variant === 'tray' ? computed(() => caption().titleSegments) : code,
      line: this.#line(row, variant, party, caption),
      assignedAt: textOf(row, 'assignedAt') ?? '',
    };
  }

  /**
   * A row's second line. Tray: the comment after who assigned it, or alone
   * when the system did, "Returned by" on a return; else
   * the detail and amount; else who assigned it, or "Assigned automatically";
   * a row by the user names the assignee instead. Home: the detail, else the
   * comment.
   */
  #line(
    row: TmEntityType<'core.Assignment'>,
    variant: TmAssignmentListVariant,
    party: TmAssignmentParty,
    caption: Signal<TmAssignmentCaption>,
  ): Signal<readonly LinePiece[]> {
    const comment = textOf(row, 'comment');
    const assigner = idOf(row, 'assignedById');
    const assignee = idOf(row, 'assigneeId');
    const person = (id: number | null): { readonly parts: Signal<TmDisplayNameParts | null>; readonly label: Signal<string | null> } => ({
      parts: id === null ? signal(null) : this.#names.labelParts(USER, id),
      label: id === null ? signal(null) : this.#names.label(USER, id),
    });
    const from = person(assigner);
    const to = person(assignee);
    // A message whose names and comment render in isolates of their own.
    const message = (key: string, args: Readonly<Record<string, unknown>>): Signal<readonly LinePiece[]> => {
      const segments = this.#text.isolate(`tm.attentionRows.list.${key}`, args, Object.keys(args));
      return computed(() =>
        segments().map((segment): LinePiece => {
          const named = segment.arg === 'assigner' ? from : segment.arg === 'assignee' ? to : null;
          return named === null
            ? { text: segment.text, comment: segment.arg === 'comment' }
            : { text: named.label() ?? '', parts: named.parts() };
        }),
      );
    };
    const plain = (text: Signal<string | null>): Signal<readonly LinePiece[]> => computed(() => [{ text: text() ?? '' }]);
    // --- The home lists: the detail, else the comment ---
    if (variant === 'home') {
      const withComment = comment === undefined ? null : message('quote', { comment });
      return computed(() => {
        const detail = caption().detail;
        return detail !== null ? [{ text: detail }] : (withComment?.() ?? []);
      });
    }
    // --- The tray: a row by the user names the assignee ---
    if (party === 'by-me') {
      return message('with', { assignee: '' });
    }
    // A comment with no one who assigned it, a system's, shows alone rather than after an empty name.
    if (comment !== undefined) {
      return assigner === null ? message('quote', { comment }) : message(row['isReturn'] === true ? 'returned' : 'comment', { assigner: '', comment });
    }
    const assigned = assigner === null ? plain(computed(() => this.text('automatic'))) : message('assignedBy', { assigner: '' });
    return computed(() => {
      const { detail, amount } = caption();
      if (detail !== null || amount !== null) {
        const text = [detail, amount].filter((part) => part !== null).join(this.#l10n.ɵtext('tm.attentionRows.list.joiner')());
        return [{ text }];
      }
      return assigned();
    });
  }
}
