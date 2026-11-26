// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { NgTemplateOutlet } from '@angular/common';
import {
  ApplicationRef,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  type ComponentRef,
  computed,
  createComponent,
  DestroyRef,
  DOCUMENT,
  effect,
  type ElementRef,
  type EnvironmentInjector,
  inject,
  InjectionToken,
  Injector,
  signal,
  type TemplateRef,
  type Type,
  untracked,
  viewChild,
  ViewEncapsulation,
} from '@angular/core';
import { NavigationEnd, NavigationStart, Router, RouterLink, type UrlTree } from '@angular/router';
import {
  TM_LAUNCHER_PROVIDERS,
  TmEntities,
  TmLocalization,
  TmNavigation,
  TmPreferences,
  tmPinnedBarVisibleKey,
  tmPinsKey,
  TmTelemetry,
  TmUrls,
  type TmPin,
  ɵtmRecentPageIcon,
} from '@tellma/core';
import type { TmRecents } from '@tellma/core/recents';
import { TmBreakpoints } from '@tellma/core-ui';
import { TmIcon, TmIconRegistry } from '@tellma/core-ui/icon';
import { tmAriaKeyShortcuts, tmKeysMatch } from '@tellma/core-ui/kbd';
import { TmModal, type TmModalRef } from '@tellma/core-ui/modal';
import { tmPushEscapeDismissal, ɵtmOpenModals, ɵtmRaiseTopLayer, ɵtmWatchModals } from '@tellma/core-ui/overlay';
import { Search } from 'lucide';

import { tmPinHref, tmPinLabel, tmPinsView } from '../pins/tm-pin-rules';
import { TM_LAUNCHER_COMMANDS } from './tm-launcher-commands';
import {
  tmLauncherCandidates,
  tmLauncherIndex,
  tmLauncherMarks,
  tmLauncherMatch,
  tmLauncherRows,
  type TmLauncherSegment,
  tmLauncherTokens,
} from './tm-launcher-index';
import type { TmLauncherBucket, TmLauncherRuns, TmLauncherRunsHost } from './tm-launcher-runs';
import type { TmLauncherSheetHost } from './tm-launcher-sheet';
import { TmLauncher, type TmLauncherCloseReason, type TmLauncherSurface, type TmLauncherVia } from './tm-launcher';
import { TmNavTermsReader } from './tm-nav-terms';

// The launcher's surface (spec 0027 §9), the deferred chunk TmLauncher
// creates once at the end of the body:
//
// - from lg up, a `popover="manual"` layer under the shell's rows, whose
//   background is the scrim over the page alone, holding the full-width
//   panel; the app bar's search field is its input;
// - at md, the same panel with a field of its own at its top;
// - below md, a TmModal of size lg labelled "Menu", full screen below sm,
//   with a 44 px field beside Close: the tm-launcher-sheet chunk, loaded
//   once the viewport is below md.
//
// The panel shows the browse state (the pinned band below md or while the
// pinned bar is hidden, the recently visited band, the sections as columns)
// or, with text, the typed state (the matching pages, then each provider's
// bucket as its answer arrives). One listbox holds a group per band, section
// and bucket; the input keeps DOM focus and points at the active option
// (the APG combobox). Pages recompute on every keystroke, synchronously;
// the providers' runs are the tm-launcher-runs chunk, loaded only when the
// app registers a provider.

/** How long after the last keystroke the results are announced. */
const ANNOUNCE_MS = 500;

/** The recent pages, as the surface reads them. Internal: the surface's creation provides them. */
export const TM_LAUNCHER_RECENTS = new InjectionToken<Pick<TmRecents, 'pages'>>('TM_LAUNCHER_RECENTS');

/** One option of the listbox. */
interface TmOption {
  /** The element's id, stable for its key. */
  readonly id: string;
  /** The URL it opens, or `''` for a command. */
  readonly href: string;
  /** The URL as a tree, which an unmodified click navigates to, or `null` for a command. */
  readonly link: UrlTree | null;
  /** The command it runs instead of opening a page, or `null`. */
  readonly run: (() => void) | null;
  /** Its icon's registered name. */
  readonly icon: string;
  /** The label, in runs, a prefix-matched word's start marked. */
  readonly segments: readonly TmLauncherSegment[];
  /** The accessible name when it differs from the label, else `null`. */
  readonly name: string | null;
  /** The muted line beside the label, or `null`. */
  readonly description: string | null;
  /** What `tm.launcher.chosen` reports it as: `pages`, `create`, `pinned`, `recents` or a provider's id. */
  readonly bucket: string;
  /** Whether it is the routed page's entry, shown selected without being active. */
  readonly current: boolean;
  /** Whether a pin opens the same page. */
  readonly pinned: boolean;
}

/** One group of the listbox: a band of chips, a section, the Pages of the typed state, or a provider's bucket. */
interface TmGroup {
  /** The group's id; its heading's is the id with `-h`. */
  readonly id: string;
  /** What it is, which decides its layout and its arrow keys. */
  readonly kind: 'band' | 'section' | 'pages' | 'bucket';
  /** The heading. */
  readonly label: string;
  /** The heading's icon, or `null`. */
  readonly icon: string | null;
  /** The options, in reading order. */
  readonly options: readonly TmOption[];
  /** The line the Pages heading shows when nothing matched, else `null`. */
  readonly empty: string | null;
}

/** A URL's path, without its query or fragment. */
function pathOf(url: string): string {
  return url.split(/[?#]/, 1)[0]!;
}

/** `tm.launcher.chosen`'s position band of a 1-based position. */
function positionBand(position: number): string {
  return position === 1 ? '1' : position <= 3 ? '2-3' : position <= 10 ? '4-10' : '11+';
}

/** `tm.launcher.chosen`'s and `tm.launcher.no-results`' length band of the query. */
function lengthBand(query: string): string {
  const length = query.trim().length;
  return length === 0 ? '0' : length <= 2 ? '1-2' : length <= 5 ? '3-5' : '6+';
}

/** Ids for the panel's elements, unique per app. */
let nextPanel = 0;

/**
 * The launcher's surface: the overlay panel from md up, the Menu modal below
 * it, their browse and typed states, the combobox keyboard and the provider
 * buckets. TmLauncher has it created once and opens and closes it.
 */
@Component({
  selector: 'tm-launcher-panel',
  imports: [NgTemplateOutlet, RouterLink, TmIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // The modal's content lives in the overlay container and restyles the
  // modal's own header and body, so the rules are unencapsulated; every
  // selector is scoped by a tm-launcher class.
  encapsulation: ViewEncapsulation.None,
  // Out of every layout: the host renders nothing in place.
  host: { style: 'position: absolute' },
  template: `
    <!-- The instructions that describe the input. -->
    <span class="tm-launcher__hint" hidden [id]="hintId">{{ text('hint') }}</span>
    <!-- From md up: the scrim over the page, holding the panel. -->
    <div #layer popover="manual" class="tm-launcher">
      @if (kind() === 'panel' || kind() === 'field') {
        <div #panel class="tm-launcher__panel">
          @if (kind() === 'field') {
            <!-- At md: the panel's own field. -->
            <label class="tm-launcher__field">
              <tm-icon [svg]="searchIcon" />
              <input #ownField type="text" class="tm-launcher__own-input" autocomplete="off" spellcheck="false" dir="auto" [placeholder]="searchLabel()" [attr.aria-label]="searchLabel()" [value]="query()" [attr.aria-activedescendant]="activeId()" [attr.aria-controls]="listboxId" [attr.aria-describedby]="hintId" aria-autocomplete="list" aria-expanded="true" role="combobox" [attr.aria-keyshortcuts]="keyShortcuts" (input)="typed($event)" (keydown)="keydown($event)" (keyup)="keyup()" (compositionstart)="composing.set(true)" (compositionend)="compositionEnd()" />
            </label>
          }
          <div class="tm-launcher__scroll">
            <ng-container [ngTemplateOutlet]="body" />
          </div>
          <!-- The keys, from md up; the hidden instructions say them to assistive technology. -->
          <p class="tm-launcher__footer" aria-hidden="true">{{ text('footer') }}</p>
        </div>
      }
    </div>
    <!--
      The listbox: a group per band, section and bucket, each labelled by its
      heading. Below md, the Menu modal's content renders it.
    -->
    <ng-template #body>
      <div role="listbox" class="tm-launcher__list" [id]="listboxId" [attr.aria-label]="text('results')" [class.tm-launcher__list--typed]="state() === 'typed'">
        @for (group of groups(); track group.id) {
          <div role="group" [class]="'tm-launcher__group tm-launcher__group--' + group.kind" [attr.aria-labelledby]="group.id + '-h'">
            <div class="tm-launcher__heading" [id]="group.id + '-h'">
              @if (group.icon; as icon) {
                <tm-icon [name]="icon" />
              }
              <span>{{ group.label }}</span>
            </div>
            <!-- A page's option is a router link; a command's is a link with no href, which a click or Enter runs. -->
            @for (option of group.options; track option.id) {
              <a
                role="option"
                tabindex="-1"
                class="tm-launcher__option"
                [id]="option.id"
                [routerLink]="option.link"
                [class.tm-launcher__option--current]="option.current"
                [attr.aria-selected]="option.id === activeId()"
                [attr.aria-label]="option.name"
                (mousedown)="$event.preventDefault()"
                (pointermove)="hover(option)"
                (click)="clicked($event, option)"
                (auxclick)="clicked($event, option)"
              >
                <tm-icon [name]="option.icon" />
                <!-- The label's runs, each in an element of its own so no white space comes between them. -->
                <span class="tm-launcher__label">
                  @for (segment of option.segments; track $index) {
                    @if (segment.mark) {
                      <mark>{{ segment.text }}</mark>
                    } @else {
                      <span>{{ segment.text }}</span>
                    }
                  }
                </span>
                @if (option.description; as description) {
                  <span class="tm-launcher__description">{{ description }}</span>
                }
                @if (option.pinned) {
                  <tm-icon class="tm-launcher__pin" name="pin" />
                }
              </a>
            }
            @if (group.empty; as empty) {
              <p class="tm-launcher__empty">{{ empty }}</p>
            }
          </div>
        }
      </div>
    </ng-template>
  `,
  styles: `
    /* The layer: from the shell's rows to the bottom, its background the scrim over the page. */
    .tm-launcher {
      position: fixed;
      inset: var(--tm-shell-top) 0 0;
      inline-size: auto;
      block-size: auto;
      margin: 0;
      padding: 0;
      border: 0;
      overflow: hidden;
      background: rgb(0 23 34 / 0.34);
      color: var(--text-body);
    }

    /* The panel, and the Menu modal's content: a column whose body scrolls. */
    .tm-launcher__panel,
    .tm-launcher__sheet {
      display: flex;
      flex-direction: column;
      font: var(--weight-regular) 13px / var(--leading-ui) var(--font-ui);
    }

    .tm-launcher__panel {
      max-block-size: calc(100dvh - var(--tm-shell-top) - 24px);
      border-block-end: var(--border-width) solid var(--divider);
      background: var(--surface-card);
      box-shadow: var(--shadow-lg);
    }

    .tm-launcher__scroll {
      flex: 1 1 auto;
      min-block-size: 0;
      overflow: auto;
      overscroll-behavior: contain;
      padding: var(--space-3) var(--space-4);
    }

    /* A field of the launcher's own: 36 px in the panel, 44 px in the modal. */
    .tm-launcher__field {
      display: flex;
      flex: 1 1 auto;
      align-items: center;
      gap: var(--space-2);
      min-inline-size: 0;
      block-size: 36px;
      padding-inline: var(--space-4);
      border-block-end: var(--border-width) solid var(--divider);
      --icon-size: 16px;
    }

    .tm-launcher__field input {
      flex: 1 1 auto;
      min-inline-size: 0;
      padding: 0;
      border: 0;
      background: transparent;
      color: inherit;
      font: inherit;
      font-size: var(--text-sm);
      outline: none;
    }

    .tm-launcher__field:focus-within {
      box-shadow: inset 0 -2px 0 var(--focus-ring-color);
    }

    /* The bands: chips that wrap. */
    .tm-launcher__group--band {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-1) var(--space-2);
      margin-block-end: var(--space-3);
    }

    .tm-launcher__group--band .tm-launcher__heading {
      flex: 0 0 100%;
    }

    .tm-launcher__group--band .tm-launcher__option {
      max-inline-size: 240px;
      block-size: 24px;
      padding-inline: var(--space-2);
      border: var(--border-width) solid var(--divider);
      border-radius: var(--radius-full);
    }

    /* The sections: one column below md, three to xl, four from it. */
    .tm-launcher__list:not(.tm-launcher__list--typed) {
      columns: 1;
      column-gap: var(--space-6);
    }

    .tm-launcher__group--band {
      column-span: all;
    }

    .tm-launcher__group--section {
      break-inside: avoid;
      margin-block-end: var(--space-4);
    }

    .tm-launcher__heading {
      display: flex;
      align-items: center;
      gap: var(--space-1);
      margin-block-end: var(--space-1);
      color: var(--text-strong);
      font-size: var(--text-xs);
      font-weight: var(--weight-semibold);
      --icon-size: 14px;
    }

    .tm-launcher__group--section .tm-launcher__heading span {
      text-decoration: underline;
    }

    .tm-launcher__group--band .tm-launcher__heading,
    .tm-launcher__group--pages .tm-launcher__heading,
    .tm-launcher__group--bucket .tm-launcher__heading {
      color: var(--text-muted);
    }

    /* An option: an icon, the label, a muted description, and a pinned glyph. */
    .tm-launcher__option {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      min-block-size: 22px;
      padding-inline: var(--space-1);
      border-radius: var(--radius-sm);
      color: var(--text-body);
      text-decoration: none;
      cursor: pointer;
      --icon-size: 14px;
    }

    .tm-launcher__group--pages .tm-launcher__option,
    .tm-launcher__group--bucket .tm-launcher__option {
      min-block-size: 28px;
      padding-inline: var(--space-2);
    }

    .tm-launcher__group--pages,
    .tm-launcher__group--bucket {
      margin-block-end: var(--space-3);
    }

    .tm-launcher__label {
      min-inline-size: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .tm-launcher__label mark {
      background: transparent;
      color: var(--text-strong);
      font-weight: var(--weight-semibold);
    }

    .tm-launcher__description {
      flex: none;
      color: var(--text-muted);
      font-size: var(--text-xs);
    }

    .tm-launcher__pin {
      flex: none;
      color: var(--text-muted);
      --icon-size: 12px;
    }

    .tm-launcher__option--current {
      background: var(--surface-selected);
    }

    .tm-launcher__option[aria-selected='true'] {
      background: var(--surface-hover);
      box-shadow: inset 0 0 0 1px var(--focus-ring-color);
    }

    .tm-launcher__empty {
      margin: 0;
      padding: var(--space-1) var(--space-2);
      color: var(--text-muted);
    }

    .tm-launcher__footer {
      display: none;
      margin: 0;
      padding: var(--space-2) var(--space-4);
      border-block-start: var(--border-width) solid var(--divider);
      color: var(--text-muted);
      font-size: var(--text-xs);
    }

    @media (width >= 768px) {
      .tm-launcher__list:not(.tm-launcher__list--typed) {
        columns: 3;
      }

      .tm-launcher__footer {
        display: block;
      }
    }

    @media (width >= 1200px) {
      .tm-launcher__list:not(.tm-launcher__list--typed) {
        columns: 4;
      }
    }

    @media (forced-colors: active) {
      .tm-launcher__option[aria-selected='true'] {
        outline: 2px solid Highlight;
      }
    }
  `,
})
export class TmLauncherPanel implements TmLauncherSurface, TmLauncherSheetHost {
  readonly #l10n = inject(TmLocalization);
  readonly #navigation = inject(TmNavigation);
  readonly #recents = inject(TM_LAUNCHER_RECENTS);
  readonly #prefs = inject(TmPreferences);
  readonly #urls = inject(TmUrls);
  readonly #router = inject(Router);
  readonly #injector = inject(Injector);
  readonly #entities = inject(TmEntities);
  readonly #terms = inject(TmNavTermsReader);
  readonly #providers = [...inject(TM_LAUNCHER_PROVIDERS)].sort((a, b) => a.order - b.order);
  readonly #commands = inject(TM_LAUNCHER_COMMANDS);
  readonly #telemetry = inject(TmTelemetry);
  readonly #announcer = inject(LiveAnnouncer);
  readonly #document = inject(DOCUMENT);
  readonly #cdr = inject(ChangeDetectorRef);
  readonly #md = inject(TmBreakpoints).atLeast('md');
  readonly #lg = inject(TmBreakpoints).atLeast('lg');
  readonly #base = `tm-launcher-${nextPanel++}`;
  readonly #ids = new Map<string, string>();
  readonly #kind = signal<'closed' | 'panel' | 'field' | 'sheet'>('closed');
  readonly #active = signal<{ readonly query: string; readonly id: string } | null>(null);
  readonly #buckets = signal<readonly TmLauncherBucket[]>([]);
  readonly #routed = signal(pathOf(this.#router.url));
  readonly #pinsDoc = this.#prefs.get(tmPinsKey);
  readonly #barVisible = this.#prefs.get(tmPinnedBarVisibleKey);
  readonly #waiting = signal(false);
  /** The scrim layer. */
  private readonly layer = viewChild.required<ElementRef<HTMLElement>>('layer');
  /** The panel, while it renders. */
  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');
  /** The panel's own field, at md. */
  private readonly ownField = viewChild<ElementRef<HTMLInputElement>>('ownField');
  #returnTo: HTMLElement | null = null;
  #sheetRef: TmModalRef<void> | null = null;
  // The Menu modal's chunk, loading or in; `null` until the viewport is first below md, and after a failed load.
  #sheetChunk: Promise<typeof import('./tm-launcher-sheet') | null> | null = null;
  // The providers' runs, once their chunk is in; `null` while no provider is registered.
  #runs: TmLauncherRuns | null = null;
  #releaseEscape: (() => void) | null = null;
  #modalsAtOpen: ReadonlySet<HTMLElement> = new Set();
  #announceTimer: ReturnType<typeof setTimeout> | undefined;
  #announced = false;

  /** The launcher this surface shows. */
  readonly launcher = inject(TmLauncher);

  /** The listbox's id. */
  readonly listboxId = `${this.#base}-list`;

  /** The hidden instructions' id. */
  readonly hintId = `${this.#base}-hint`;

  /** The listbox, which the panel and the Menu modal's content render. */
  readonly body = viewChild.required<TemplateRef<unknown>>('body');

  /** Which surface shows: the panel with the app bar's field, the panel with its own, the modal, or none. */
  protected readonly kind = this.#kind.asReadonly();

  /** Whether the app bar's search field is the input: the panel from lg up. */
  readonly usesField = computed(() => this.#kind() === 'panel');

  /** Whether an open waits for the Menu modal's chunk. */
  readonly waiting = this.#waiting.asReadonly();

  /** What opened the modal, which decides whether Close or the field takes focus. */
  readonly sheetVia = signal<TmLauncherVia | null>(null);

  /** Whether an IME composition is in progress in the input, which holds the providers. */
  readonly composing = signal(false);

  /** The search glyph. */
  protected readonly searchIcon = Search;

  /** The input's `aria-keyshortcuts`. */
  readonly keyShortcuts = tmAriaKeyShortcuts('Mod+K');

  /** The launcher's text. */
  protected readonly query = this.launcher.query;

  /** The launcher's state. */
  protected readonly state = this.launcher.state;

  /** The field's name and placeholder: "Search". */
  readonly searchLabel = this.#l10n.ɵtext('tm.tenantShell.bar.search');

  // --- The candidates and the index: built on the first open ---

  /** The routed tenant's pins the user may open, each with its URL and label, in the bar's order. */
  readonly #pins = computed(() => {
    const t = this.#urls.tenantId();
    if (t === null) {
      return [];
    }
    const label = (href: string): string | undefined => this.#navigation.byHref(href)?.label;
    return tmPinsView(this.#pinsDoc())
      .items.flatMap((item): readonly TmPin[] => ('folder' in item ? item.items : [item]))
      .map((pin) => ({ pin, href: tmPinHref(t, pin.url), label: tmPinLabel(pin, label, t) }))
      .filter(({ href }) => this.#navigation.resolve(href) === 'ok');
  });

  /** The page index: the entries, then the pins and recent pages no entry covers, each of those by label. */
  readonly #index = computed(() => {
    const language = this.#l10n.language();
    return tmLauncherIndex(
      tmLauncherCandidates({
        language,
        entries: this.#navigation.items(),
        sections: new Map(this.#navigation.sections().map((section) => [section.id, section.label])),
        pins: this.#pins().map(({ pin, href, label }) => ({ id: pin.id, href, label, title: pin.title })),
        recents: this.#recents.pages().map((page) => ({ url: page.url, title: page.title(), icon: page.icon ?? ɵtmRecentPageIcon(page.kind) })),
        pinned: this.text('pinned'),
        recent: this.text('recent'),
        terms: (entry, of) => this.#terms.read(entry, of),
        createLabel: (entity) => this.text('create', { label: this.#entities.label(entity)() }),
        entry: (href) => this.#navigation.byHref(href),
      }),
    );
  });

  // --- The groups each state shows ---

  /**
   * The browse state: the pinned band below md or while the bar is hidden,
   * its pins then the pins service's commands; the recently visited band;
   * then the sections.
   */
  readonly #browse = computed((): readonly TmGroup[] => {
    const groups: TmGroup[] = [];
    const pins = this.#pins();
    const commands = this.#commands();
    if ((!this.#md() || !this.#barVisible()) && pins.length + commands.length > 0) {
      groups.push(
        this.#group('pinned', 'band', this.text('pinned'), null, [
          ...pins.map(({ pin, href, label }) => this.#option(`pin:${pin.id}`, href, this.#navigation.byHref(pathOf(href))?.icon ?? 'pin', label, 'pinned')),
          ...commands.map((command) => this.#option(`command:${command.id}`, '', command.icon, command.label, 'pinned', { run: () => command.run() })),
        ]),
      );
    }
    const recents = this.#recents.pages();
    if (recents.length > 0) {
      groups.push(
        this.#group(
          'recent',
          'band',
          this.text('recent'),
          null,
          recents.map((page) => this.#option(`recent:${page.url}`, page.url, page.icon ?? ɵtmRecentPageIcon(page.kind), page.title(), 'recents')),
        ),
      );
    }
    // The routed page's entry: the one whose URL is the longest that the routed path starts with.
    const routed = this.#routed();
    const pinHrefs = new Set(pins.map(({ href }) => href));
    let current: string | null = null;
    for (const section of this.#navigation.sections()) {
      for (const entry of section.entries) {
        if ((routed === entry.href || routed.startsWith(`${entry.href}/`)) && entry.href.length > (current?.length ?? 0)) {
          current = entry.href;
        }
      }
    }
    for (const section of this.#navigation.sections()) {
      groups.push(
        this.#group(
          `section:${section.id}`,
          'section',
          section.label,
          section.icon,
          section.entries.map((entry) => {
            const pinned = pinHrefs.has(entry.href);
            return this.#option(`entry:${entry.id}`, entry.href, entry.icon, entry.label, 'pages', {
              current: entry.href === current,
              pinned,
              name: pinned ? this.text('pinnedEntry', { label: entry.label }) : null,
            });
          }),
        ),
      );
    }
    return groups;
  });

  /** The typed state: the matching pages, with their create rows, then the buckets in order. */
  readonly #typed = computed((): readonly TmGroup[] => {
    const query = this.launcher.query();
    const tokens = tmLauncherTokens(query);
    const rows = tmLauncherRows(tmLauncherMatch(this.#index(), tokens));
    const buckets = { entry: 'pages', pin: 'pinned', recent: 'recents' } as const;
    const pages = this.#group(
      'pages',
      'pages',
      this.text('pages'),
      null,
      rows.map(({ kind, candidate, match }) =>
        kind === 'create'
          ? this.#option(`create:${candidate.key}`, candidate.create!.href, 'plus', candidate.create!.label, 'create')
          : this.#option(candidate.key, candidate.href, candidate.icon, candidate.label, buckets[candidate.kind], {
              segments: match === 'prefix' ? tmLauncherMarks(candidate.label, tokens) : undefined,
              description: candidate.description,
            }),
      ),
      rows.length === 0 ? this.text('noMatch', { query: query.trim() }) : null,
    );
    return [
      pages,
      ...this.#buckets().map(({ provider, items }) =>
        this.#group(
          `bucket:${provider.id}`,
          'bucket',
          this.#l10n.ɵtext(provider.labelKey)(),
          null,
          items.map((item) =>
            this.#option(`provider:${provider.id}:${item.id}`, item.href, item.icon ?? 'link', item.label, provider.id, {
              description: item.description ?? null,
            }),
          ),
        ),
      ),
    ];
  });

  /**
   * The groups the text shows: the browse state's without text, the typed
   * state's with it. They stay as they were as the launcher closes, the
   * buckets included until the next open, so a choice whose navigation
   * closed it first still reports its position; the template reads them only
   * while it is open.
   */
  protected readonly groups = computed(() => (this.launcher.query().trim() === '' ? this.#browse() : this.#typed()));

  /** Every option, in reading order. */
  readonly #options = computed(() => this.groups().flatMap((group) => group.options));

  /**
   * The active option's id: the one the keys, the pointer or the first
   * showing chose for this text, else the first row of the typed state.
   */
  readonly activeId = computed(() => {
    const options = this.#options();
    const chosen = this.#active();
    if (chosen !== null && chosen.query === this.launcher.query() && options.some((option) => option.id === chosen.id)) {
      return chosen.id;
    }
    return this.state() === 'typed' ? (options[0]?.id ?? null) : null;
  });

  /** Creates the surface, which follows the router, the modals and the breakpoints for as long as the app runs. */
  constructor() {
    // --- A path change, a dialog opening or a breakpoint crossing closes the launcher ---
    const navigations = this.#router.events.subscribe((event) => {
      if (event instanceof NavigationStart && pathOf(event.url) !== pathOf(this.#router.url)) {
        this.launcher.close('route');
      } else if (event instanceof NavigationEnd) {
        this.#routed.set(pathOf(event.urlAfterRedirects));
      }
    });
    const modals = ɵtmWatchModals(() => {
      // While the Menu modal's chunk loads, its own opening is not another dialog's.
      const loading = untracked(this.#kind) === 'sheet' && this.#sheetRef === null;
      if (untracked(this.#kind) !== 'closed' && !loading && ɵtmOpenModals().some((host) => !this.#modalsAtOpen.has(host))) {
        this.launcher.close('route');
      }
    });
    effect(() => {
      const kind = this.#surfaceFor(this.#md(), this.#lg());
      untracked(() => {
        // An open surface that the new width would not show closes: the modal from md, the panel's input that hides.
        const open = this.#kind();
        if (open !== 'closed' && open !== kind) {
          this.launcher.close('route');
        }
        // The Menu modal's chunk loads once the viewport is below md.
        if (kind === 'sheet') {
          void this.#loadSheet();
        }
      });
    });
    // --- The providers' runs, a chunk loaded only when a provider is registered ---
    if (this.#providers.length > 0) {
      void this.#loadRuns();
    }
    // --- Every change of the text: the providers' edges and the announcement ---
    effect(() => {
      const query = this.launcher.query();
      untracked(() => this.#queryChanged(query));
    });
    // --- The active row stays where it first showed for this text, whatever answers later ---
    effect(() => {
      const id = this.activeId();
      untracked(() => {
        const query = this.launcher.query();
        const chosen = this.#active();
        if (id !== null && this.state() === 'typed' && (chosen?.query !== query || chosen.id !== id)) {
          this.#active.set({ query, id });
        }
      });
    });
    // --- The active option's page starts loading, however it became active ---
    effect(() => {
      const id = this.activeId();
      const option = id === null ? undefined : untracked(this.#options).find((candidate) => candidate.id === id);
      if (option !== undefined && option.run === null) {
        untracked(() => this.#urls.preload(option.href));
      }
    });
    inject(DestroyRef).onDestroy(() => {
      navigations.unsubscribe();
      modals();
      this.close('route');
    });
  }

  /**
   * A string of the launcher's.
   *
   * @param key - The key under `tm.launcher`.
   * @param params - The message's parameters.
   * @returns The text.
   */
  text(key: string, params?: Readonly<Record<string, unknown>>): string {
    return this.#l10n.ɵtext(`tm.launcher.${key}`, params)();
  }

  /** {@inheritDoc TmLauncherSurface.open} */
  open(via: TmLauncherVia, reopened: boolean, returnTo: HTMLElement | null): void {
    if (reopened) {
      // A click in the field it already types in places the caret; any other open selects the text.
      if (via !== 'field') {
        this.#focusInput(true);
      }
      return;
    }
    this.#returnTo = returnTo;
    this.#active.set(null);
    this.#buckets.set([]);
    this.#runs?.restart();
    this.#modalsAtOpen = new Set(ɵtmOpenModals());
    const field = this.launcher.ɵfield;
    const kind = this.#surfaceFor(untracked(this.#md), untracked(this.#lg));
    if (kind === 'sheet') {
      // --- Below md: the Menu modal, once its chunk is in ---
      this.sheetVia.set(via);
      this.#kind.set('sheet');
      // Until the modal is up, which then answers Escape itself, Escape closes the launcher.
      this.#releaseEscape = tmPushEscapeDismissal(() => this.launcher.close('escape'));
      void this.#openSheet();
    } else {
      // --- From md up: the panel under the shell's rows ---
      this.#kind.set(kind);
      this.#cdr.detectChanges();
      this.layer().nativeElement.showPopover();
      // The toasts stay above the panel.
      ɵtmRaiseTopLayer();
      this.#releaseEscape = tmPushEscapeDismissal(() => this.launcher.close('escape'));
      this.#document.addEventListener('pointerdown', this.#outside, true);
      if (kind === 'panel' && field !== null) {
        this.#listen(field, true);
        // A press on the field opened it: the press's release would place the caret over the selection.
        if (via === 'field' && !field.matches(':focus-visible')) {
          const keep = (event: Event): void => event.preventDefault();
          field.addEventListener('mouseup', keep, { once: true });
          setTimeout(() => field.removeEventListener('mouseup', keep), 1_000);
        }
      }
      this.#focusInput(true);
    }
    // The page index builds on the first open, so the first keystroke finds it.
    untracked(this.#index);
    if (untracked(this.state) === 'typed') {
      this.#queryChanged(untracked(this.launcher.query));
    }
  }

  /** {@inheritDoc TmLauncherSurface.close} */
  close(reason: TmLauncherCloseReason): void {
    const kind = untracked(this.#kind);
    if (kind === 'closed') {
      return;
    }
    this.#kind.set('closed');
    this.#waiting.set(false);
    // --- Stop what the open started ---
    this.#releaseEscape?.();
    this.#releaseEscape = null;
    this.#document.removeEventListener('pointerdown', this.#outside, true);
    const field = this.launcher.ɵfield;
    if (field !== null) {
      this.#listen(field, false);
    }
    this.#runs?.stop();
    clearTimeout(this.#announceTimer);
    // --- Hide the surface; focus goes back as spec 0027 §5.3 says ---
    if (kind === 'sheet') {
      // The modal returns focus to what had it before the open.
      const ref = this.#sheetRef;
      this.#sheetRef = null;
      ref?.close();
      return;
    }
    const layer = this.layer().nativeElement;
    if (layer.matches(':popover-open')) {
      layer.hidePopover();
    }
    const returnTo = this.#returnTo;
    if (reason === 'escape' || (reason === 'tab' && kind === 'field')) {
      // From the panel's own field, Tab goes on from the control that opened it.
      this.#restore(returnTo);
    } else if (reason === 'outside') {
      // A press on something focusable keeps its focus; one on the scrim or the page's text gives it back.
      setTimeout(() => {
        const active = this.#document.activeElement;
        if (active === null || active === this.#document.body) {
          this.#restore(returnTo);
        }
      });
    }
  }

  // --- The input's events ---

  /**
   * The field's text, handed to the launcher.
   *
   * @param event - The input event.
   */
  typed(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.launcher.setQuery(input.value);
  }

  /**
   * The combobox keys: Down and Up over every option, Right and Left across
   * a band's chips or between sections with the input empty, PageDown and
   * PageUp between groups, Home and End in the browse state, Enter to open
   * (Mod or Shift for a new tab) or to run a command, Tab to close.
   *
   * @param event - The key.
   */
  keydown(event: KeyboardEvent): void {
    // A composition's keys are the composition's, never commands.
    if (event.isComposing || event.keyCode === 229) {
      return;
    }
    const plain = !event.ctrlKey && !event.metaKey && !event.altKey && !event.shiftKey;
    const options = untracked(this.#options);
    const groups = untracked(this.groups);
    const active = options.find((option) => option.id === untracked(this.activeId));
    const empty = (event.target as HTMLInputElement).value === '';
    const browse = untracked(this.state) === 'browse';
    let next: TmOption | undefined;
    switch (event.key) {
      case 'ArrowDown':
      case 'ArrowUp': {
        if (!plain) {
          return;
        }
        const at = active === undefined ? -1 : options.indexOf(active);
        next = event.key === 'ArrowDown' ? options[Math.min(at + 1, options.length - 1)] : options[at === -1 ? options.length - 1 : Math.max(at - 1, 0)];
        break;
      }
      case 'ArrowRight':
      case 'ArrowLeft': {
        if (!plain || !empty || active === undefined) {
          return;
        }
        // Direction-mapped: the reading direction's next is the end side.
        const forward = (event.key === 'ArrowRight') === (this.#l10n.dir() === 'ltr');
        next = this.#across(groups, active, forward ? 1 : -1);
        break;
      }
      case 'PageDown':
      case 'PageUp': {
        if (!plain) {
          return;
        }
        const filled = groups.filter((group) => group.options.length > 0);
        const at = active === undefined ? -1 : filled.findIndex((group) => group.options.includes(active));
        const to = event.key === 'PageDown' ? Math.min(at + 1, filled.length - 1) : at === -1 ? 0 : Math.max(at - 1, 0);
        next = filled[to]?.options[0];
        break;
      }
      case 'Home':
      case 'End':
        if (!plain || !browse) {
          return;
        }
        next = event.key === 'Home' ? options[0] : options[options.length - 1];
        break;
      case 'Enter':
        if (active === undefined || event.altKey) {
          return;
        }
        event.preventDefault();
        this.#choose(active, event.ctrlKey || event.metaKey || event.shiftKey);
        return;
      case 'Tab':
        // In the modal, Tab moves between the field and Close.
        if (untracked(this.#kind) !== 'sheet') {
          this.launcher.close('tab');
        }
        return;
      default:
        // A held key's repeats hold the providers until it is released.
        if (event.repeat) {
          this.#runs?.hold();
        }
        return;
    }
    event.preventDefault();
    if (next !== undefined) {
      this.#activate(next);
      this.#document.getElementById(next.id)?.scrollIntoView({ block: 'nearest' });
    }
  }

  /**
   * Mod+K in the Menu modal, where the shortcut registry stands aside: the
   * field's text is selected. Mod is ⌘ on Apple platforms, where Ctrl+K stays
   * the field's own.
   *
   * @param event - The key.
   */
  sheetKeydown(event: KeyboardEvent): void {
    if (tmKeysMatch(event, 'Mod+K')) {
      event.preventDefault();
      const input = (event.currentTarget as HTMLElement).querySelector('input');
      input?.focus();
      input?.select();
    }
  }

  /** The release of a held key runs the providers on what the hold typed. */
  keyup(): void {
    this.#runs?.release();
  }

  /** The end of a composition is the input it stood for. */
  compositionEnd(): void {
    this.composing.set(false);
    this.#queryChanged(untracked(this.launcher.query));
  }

  // --- The pointer ---

  /**
   * Hover activates a row.
   *
   * @param option - The row.
   */
  protected hover(option: TmOption): void {
    if (untracked(this.activeId) !== option.id) {
      this.#activate(option);
    }
  }

  /**
   * A click on a row: the router opens an unmodified click here, and the
   * browser opens a modified or middle click in a new tab; either closes
   * the launcher. A command's row runs the command.
   *
   * @param event - The click.
   * @param option - The row.
   */
  protected clicked(event: MouseEvent, option: TmOption): void {
    if (event.button > 1) {
      return;
    }
    if (option.run !== null) {
      // A command runs on a plain click; it has no page to open in a new tab.
      event.preventDefault();
      if (event.button === 0) {
        this.#choose(option, false);
      }
      return;
    }
    const newTab = event.button === 1 || event.ctrlKey || event.metaKey || event.shiftKey;
    this.#report(option, newTab);
    this.launcher.close('chosen');
    this.#keepFocus(option, newTab);
  }

  // --- Choosing, moving and the providers ---

  /** Runs a command, or opens an option here or in a new tab, and closes the launcher. */
  #choose(option: TmOption, newTab: boolean): void {
    const run = option.run;
    this.#report(option, newTab && run === null);
    this.launcher.close('chosen');
    if (run !== null) {
      // A command may open a dialog of its own, which takes focus; until then, the opener has it.
      this.#keepFocus(option, true);
      run();
    } else if (newTab) {
      this.#document.defaultView?.open(option.href, '_blank', 'noopener');
      this.#keepFocus(option, true);
    } else {
      this.#keepFocus(option, false);
      if (option.link !== null) {
        void this.#router.navigateByUrl(option.link);
      }
    }
  }

  /**
   * After a choice that leaves the page where it is (a new tab, a command,
   * the routed page itself), focus goes back to the control that opened the
   * launcher when the closed surface took it with it.
   */
  #keepFocus(option: TmOption, stays: boolean): void {
    if (!stays && pathOf(option.href) !== pathOf(this.#router.url)) {
      return;
    }
    const active = this.#document.activeElement;
    if (active === null || active === this.#document.body || this.layer().nativeElement.contains(active)) {
      this.#restore(this.#returnTo);
    }
  }

  /** Gives focus back to a control without the focus opening the launcher again. */
  #restore(control: HTMLElement | null): void {
    if (control !== null) {
      this.launcher.ɵrestore(control);
    }
  }

  /** Reports a choice: `tm.launcher.chosen`, and for a page opened here, `tm.shell.navigated`. */
  #report(option: TmOption, newTab: boolean): void {
    const position = untracked(this.#options).findIndex((candidate) => candidate.id === option.id) + 1;
    const query = untracked(this.launcher.query);
    this.#telemetry.event(
      'tm.launcher.chosen',
      { bucket: option.bucket, position: positionBand(position), queryLength: lengthBand(query), newTab },
      { sampled: true },
    );
    if (!newTab && option.run === null) {
      const source = option.bucket === 'pinned' ? 'pin' : option.bucket === 'recents' ? 'recent' : 'launcher';
      this.#telemetry.event('tm.shell.navigated', { source }, { sampled: true });
    }
  }

  /** Makes an option active for the current text; its page starts loading. */
  #activate(option: TmOption): void {
    this.#active.set({ query: untracked(this.launcher.query), id: option.id });
  }

  /**
   * The option across from the active one: the next or previous chip in a
   * band, or the same index in the next or previous section, clamped; the
   * active one when there is none.
   */
  #across(groups: readonly TmGroup[], active: TmOption, step: 1 | -1): TmOption {
    const at = groups.findIndex((group) => group.options.includes(active));
    const group = groups[at]!;
    const index = group.options.indexOf(active);
    if (group.kind === 'band') {
      return group.options[Math.min(Math.max(index + step, 0), group.options.length - 1)]!;
    }
    for (let g = at + step; g >= 0 && g < groups.length; g += step) {
      const target = groups[g]!;
      if (target.kind === group.kind && target.options.length > 0) {
        return target.options[Math.min(index, target.options.length - 1)]!;
      }
    }
    return active;
  }

  /**
   * Answers a change of the text while open: the providers' runs hear of it,
   * and the results are announced half a second after the last change.
   */
  #queryChanged(query: string): void {
    if (untracked(this.#kind) === 'closed') {
      return;
    }
    clearTimeout(this.#announceTimer);
    this.#announced = false;
    if (query.trim() !== '') {
      this.#announceTimer = setTimeout(() => this.#announce(), ANNOUNCE_MS);
    }
    this.#runs?.changed();
  }

  /** Announces the pages that match, then each bucket answered so far; reports a query that matched nothing. */
  #announce(): void {
    this.#announced = true;
    const pages = untracked(this.#typed)[0]!;
    const count = pages.options.filter((option) => option.bucket !== 'create').length;
    if (count === 0) {
      this.#telemetry.event('tm.launcher.no-results', { queryLength: lengthBand(untracked(this.launcher.query)) }, { sampled: true });
    }
    const runs = this.#runs;
    const buckets = runs === null ? [] : untracked(this.#buckets).map((bucket) => runs.text(bucket));
    this.#announcer.announce([count === 0 ? this.text('nothing') : this.text('count', { count }), ...buckets].join('. '), 'polite');
  }

  // --- The deferred pieces: the providers' runs and the Menu modal ---

  /** Loads the providers' runs; once they are in, an open typed state runs them on its text. */
  async #loadRuns(): Promise<void> {
    try {
      const { TmLauncherRuns } = await import('./tm-launcher-runs');
      const host: TmLauncherRunsHost = {
        query: this.launcher.query,
        composing: this.composing,
        buckets: this.#buckets,
        l10n: this.#l10n,
        urls: this.#urls,
        answered: (bucket) => {
          if (this.#announced) {
            this.#announcer.announce(this.#runs!.text(bucket), 'polite');
          }
        },
      };
      this.#runs = new TmLauncherRuns(this.#providers, host);
      if (untracked(this.#kind) !== 'closed' && untracked(this.state) === 'typed') {
        this.#runs.changed();
      }
    } catch (error: unknown) {
      // Offline, or after a deploy removed the chunk: the pages still answer, and the buckets stay away.
      this.#telemetry.error(error, { source: 'tm-launcher' });
    }
  }

  /** Loads the Menu modal's chunk once; `null` when it fails, so the next open tries again. */
  #loadSheet(): Promise<typeof import('./tm-launcher-sheet') | null> {
    this.#sheetChunk ??= import('./tm-launcher-sheet').then(
      (chunk) => chunk,
      (error: unknown) => {
        this.#telemetry.error(error, { source: 'tm-launcher' });
        this.#sheetChunk = null;
        return null;
      },
    );
    return this.#sheetChunk;
  }

  /** Opens the Menu modal once its chunk is in, the app bar's skeleton showing meanwhile; a failed load closes the launcher. */
  async #openSheet(): Promise<void> {
    this.#waiting.set(true);
    const chunk = await this.#loadSheet();
    if (untracked(this.#kind) !== 'sheet' || this.#sheetRef !== null) {
      return;
    }
    this.#waiting.set(false);
    this.#releaseEscape?.();
    this.#releaseEscape = null;
    // A dialog that opened while the chunk loaded wins, as it would over the open modal.
    if (chunk === null || ɵtmOpenModals().some((host) => !this.#modalsAtOpen.has(host))) {
      this.launcher.close('route');
      return;
    }
    const ref = this.#injector.get(TmModal).open<void>(chunk.TmLauncherSheet, {
      size: 'lg',
      title: this.#l10n.ɵtext('tm.tenantShell.bar.menu')(),
      showClose: false,
      panelClass: 'tm-launcher-sheet',
      data: this,
    });
    this.#sheetRef = ref;
    this.#modalsAtOpen = new Set(ɵtmOpenModals());
    void ref.closed.then((result) => {
      if (this.#sheetRef === ref) {
        this.#sheetRef = null;
        this.launcher.close(result.via === 'backdrop' ? 'outside' : 'escape');
      }
    });
  }

  // --- The input, the outside press and the model's pieces ---

  /** The surface a width shows: the modal below md, the panel with the app bar's field from lg, else the panel with its own. */
  #surfaceFor(md: boolean, lg: boolean): 'panel' | 'field' | 'sheet' {
    return !md ? 'sheet' : lg && this.launcher.ɵfield !== null ? 'panel' : 'field';
  }

  /** Focuses the input and selects its text: the app bar's field, or the panel's own. */
  #focusInput(select: boolean): void {
    const input = untracked(this.#kind) === 'panel' ? this.launcher.ɵfield : (this.ownField()?.nativeElement ?? null);
    input?.focus();
    if (select) {
      input?.select();
    }
  }

  /** Listens to the app bar's field, which the panel's own template does not render, or stops. */
  #listen(field: HTMLInputElement, on: boolean): void {
    const add = on ? field.addEventListener.bind(field) : field.removeEventListener.bind(field);
    add('keydown', this.#fieldKeydown);
    add('keyup', this.#fieldKeyup);
    add('compositionstart', this.#fieldCompositionStart);
    add('compositionend', this.#fieldCompositionEnd);
  }

  /** The app bar's field's keys. */
  readonly #fieldKeydown = (event: KeyboardEvent): void => this.keydown(event);

  /** The app bar's field's key releases. */
  readonly #fieldKeyup = (): void => this.keyup();

  /** The app bar's field's composition start. */
  readonly #fieldCompositionStart = (): void => this.composing.set(true);

  /** The app bar's field's composition end. */
  readonly #fieldCompositionEnd = (): void => this.compositionEnd();

  /** A press outside the panel and the controls that open it closes the launcher. */
  readonly #outside = (event: PointerEvent): void => {
    const target = event.target;
    if (
      target instanceof Node &&
      (this.panel()?.nativeElement.contains(target) || (target instanceof Element && target.closest('[data-tm-launcher-trigger]') !== null))
    ) {
      return;
    }
    this.launcher.close('outside');
  };

  /** A group. */
  #group(id: string, kind: TmGroup['kind'], label: string, icon: string | null, options: readonly TmOption[], empty: string | null = null): TmGroup {
    return { id: `${this.#base}-${id}`, kind, label, icon, options, empty };
  }

  /** An option; its id stays the same for its key. A command's has no URL. */
  #option(
    key: string,
    href: string,
    icon: string,
    label: string,
    bucket: string,
    extra: {
      readonly segments?: readonly TmLauncherSegment[];
      readonly description?: string | null;
      readonly current?: boolean;
      readonly pinned?: boolean;
      readonly name?: string | null;
      readonly run?: () => void;
    } = {},
  ): TmOption {
    let id = this.#ids.get(key);
    if (id === undefined) {
      id = `${this.#base}-o${this.#ids.size}`;
      this.#ids.set(key, id);
    }
    const run = extra.run ?? null;
    return {
      id,
      href,
      link: run === null ? this.#router.parseUrl(href) : null,
      run,
      icon,
      segments: extra.segments ?? [{ text: label, mark: false }],
      name: extra.name ?? null,
      description: extra.description ?? null,
      bucket,
      current: extra.current ?? false,
      pinned: extra.pinned ?? false,
    };
  }
}

/**
 * Creates the launcher's surface, closed, at the end of the body, outside
 * the shell's landmarks, with the icons it draws registered and the recent
 * pages provided to it.
 *
 * @param injector - The app's injector.
 * @param registerIcons - Registers the icon names it draws that the app has not.
 * @param recents - The recent pages' service, whose entry point is lazy to the shell's.
 * @returns The surface's component.
 */
export function tmCreateLauncherSurface(
  injector: EnvironmentInjector,
  registerIcons: (registry: TmIconRegistry) => void,
  recents: Type<TmRecents>,
): ComponentRef<TmLauncherSurface> {
  registerIcons(injector.get(TmIconRegistry));
  const elementInjector = Injector.create({ providers: [{ provide: TM_LAUNCHER_RECENTS, useValue: injector.get(recents) }], parent: injector });
  const ref = createComponent(TmLauncherPanel, { environmentInjector: injector, elementInjector });
  injector.get(DOCUMENT).body.appendChild(ref.location.nativeElement as HTMLElement);
  injector.get(ApplicationRef).attachView(ref.hostView);
  // Renders the surface at once, so the open that waited finds its views.
  ref.changeDetectorRef.detectChanges();
  return ref;
}
