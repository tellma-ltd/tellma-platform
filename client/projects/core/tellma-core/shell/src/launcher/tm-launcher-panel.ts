// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
  effect,
  type ElementRef,
  inject,
  signal,
  type TemplateRef,
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
  tmRecentPageIcon,
  TmTelemetry,
  TmUrls,
  type TmLauncherItem,
  type TmLauncherProvider,
  type TmPin,
} from '@tellma/core';
import { tmSearchNormalize } from '@tellma/core/search-normalize';
import { TmBreakpoints } from '@tellma/core-ui';
import { TmIcon } from '@tellma/core-ui/icon';
import { tmAriaKeyShortcuts } from '@tellma/core-ui/kbd';
import { TmModal, type TmModalRef } from '@tellma/core-ui/modal';
import { tmPushEscapeDismissal, ɵtmOpenModals, ɵtmRaiseTopLayer, ɵtmWatchModals } from '@tellma/core-ui/overlay';
import { Search, X } from 'lucide';

import { tmPinHref, tmPinLabel, tmPinsView } from '../pins/tm-pin-rules';
import {
  type TmLauncherCandidate,
  tmLauncherIndex,
  tmLauncherMarks,
  tmLauncherMatch,
  tmLauncherRows,
  type TmLauncherSegment,
  tmLauncherTokens,
} from './tm-launcher-index';
import { TM_LAUNCHER_RECENTS, TmLauncher, type TmLauncherCloseReason, type TmLauncherSurface, type TmLauncherVia } from './tm-launcher';
import { TmNavTermsReader } from './tm-nav-terms';

// The launcher's surface (spec 0027 §9), the deferred chunk TmLauncher
// creates once at the end of the body:
//
// - from lg up, a `popover="manual"` layer under the shell's rows, whose
//   background is the scrim over the page alone, holding the full-width
//   panel; the app bar's search field is its input;
// - at md, the same panel with a field of its own at its top;
// - below md, a TmModal of size lg labelled "Menu", full screen below sm,
//   with a 44 px field beside Close.
//
// The panel shows the browse state (the pinned band below md or while the
// pinned bar is hidden, the recently visited band, the sections as columns)
// or, with text, the typed state (the matching pages, then each provider's
// bucket as its answer arrives). One listbox holds a group per band, section
// and bucket; the input keeps DOM focus and points at the active option
// (the APG combobox). Pages recompute on every keystroke; the providers run
// on the edges of a burst of typing, as TmSearch's search output times them,
// each run aborted when the next one starts.

/** How long a burst of typing waits for its next key before the providers run again. */
const BURST_MS = 50;

/** How long after the last keystroke the results are announced. */
const ANNOUNCE_MS = 500;

/** One option of the listbox. */
interface TmOption {
  /** The element's id, stable for its key. */
  readonly id: string;
  /** The URL it opens. */
  readonly href: string;
  /** The URL as a tree, which an unmodified click navigates to. */
  readonly link: UrlTree;
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

/** A provider's answer, as a bucket shows it. */
interface TmBucket {
  /** The provider. */
  readonly provider: TmLauncherProvider;
  /** Its items, at most the context's limit. */
  readonly items: readonly TmLauncherItem[];
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
 * buckets. TmLauncher creates it once and opens and closes it.
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
    <!-- Below md: the Menu modal's content, the field beside Close, then the body. -->
    <ng-template #sheet>
      <div class="tm-launcher__sheet" (keydown)="sheetKeydown($event)">
        <div class="tm-launcher__sheet-head">
          <label class="tm-launcher__field">
            <tm-icon [svg]="searchIcon" />
            <input type="text" class="tm-launcher__sheet-input" autocomplete="off" spellcheck="false" dir="auto" [placeholder]="searchLabel()" [attr.aria-label]="searchLabel()" [value]="query()" [attr.aria-activedescendant]="activeId()" [attr.aria-controls]="listboxId" [attr.aria-describedby]="hintId" aria-autocomplete="list" aria-expanded="true" role="combobox" [attr.aria-keyshortcuts]="keyShortcuts" (input)="typed($event)" (keydown)="keydown($event)" (keyup)="keyup()" (compositionstart)="composing.set(true)" (compositionend)="compositionEnd()" />
          </label>
          <button type="button" class="tm-launcher__close" [attr.cdkFocusInitial]="sheetVia() === 'menu' ? '' : null" [attr.aria-label]="text('close')" (click)="launcher.close('escape')">
            <tm-icon [svg]="closeIcon" />
          </button>
        </div>
        <div class="tm-launcher__scroll">
          <ng-container [ngTemplateOutlet]="body" />
        </div>
      </div>
    </ng-template>
    <!-- The listbox: a group per band, section and bucket, each labelled by its heading. -->
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

    /* The panel: full width, its body scrolling within the viewport. */
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

    /* Below md: the modal's content, its own header hidden but naming the dialog. */
    .tm-launcher-sheet .tm-modal__header {
      position: absolute;
      clip-path: inset(50%);
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
    }

    .tm-launcher-sheet .tm-modal__body {
      display: flex;
      flex-direction: column;
      padding: 0;
      overflow: hidden;
    }

    .tm-launcher__sheet {
      flex: 1 1 auto;
      min-block-size: 0;
    }

    .tm-launcher__sheet-head {
      display: flex;
      flex: none;
      align-items: center;
      border-block-end: var(--border-width) solid var(--divider);
    }

    .tm-launcher__sheet-head .tm-launcher__field {
      block-size: 44px;
      border: 0;
    }

    .tm-launcher__close {
      display: inline-flex;
      flex: none;
      align-items: center;
      justify-content: center;
      inline-size: 44px;
      block-size: 44px;
      padding: 0;
      border: 0;
      background: transparent;
      color: var(--text-secondary);
      cursor: pointer;
      --icon-size: var(--modal-icon-size);
    }

    .tm-launcher__close:focus-visible {
      outline: var(--focus-ring-width) solid transparent;
      box-shadow: var(--focus-ring);
    }

    /* Full screen below sm, its entries 44 px tall. */
    @media (width < 576px) {
      .tm-modal-panel.tm-launcher-sheet {
        inline-size: 100vw;
        block-size: 100dvh;
        max-inline-size: 100vw;
        max-block-size: 100dvh;
      }

      .tm-modal-panel.tm-launcher-sheet > cdk-dialog-container {
        border-radius: 0;
      }

      .tm-launcher__sheet .tm-launcher__option {
        min-block-size: 44px;
      }
    }

    @media (forced-colors: active) {
      .tm-launcher__option[aria-selected='true'] {
        outline: 2px solid Highlight;
      }
    }
  `,
})
export class TmLauncherPanel implements TmLauncherSurface {
  readonly #l10n = inject(TmLocalization);
  readonly #navigation = inject(TmNavigation);
  readonly #recents = inject(TM_LAUNCHER_RECENTS);
  readonly #prefs = inject(TmPreferences);
  readonly #urls = inject(TmUrls);
  readonly #router = inject(Router);
  readonly #modal = inject(TmModal);
  readonly #entities = inject(TmEntities);
  readonly #terms = inject(TmNavTermsReader);
  readonly #providers = [...inject(TM_LAUNCHER_PROVIDERS)].sort((a, b) => a.order - b.order);
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
  readonly #buckets = signal<readonly TmBucket[]>([]);
  readonly #routed = signal(pathOf(this.#router.url));
  readonly #pinsDoc = this.#prefs.get(tmPinsKey);
  readonly #barVisible = this.#prefs.get(tmPinnedBarVisibleKey);
  /** The scrim layer. */
  private readonly layer = viewChild.required<ElementRef<HTMLElement>>('layer');
  /** The panel, while it renders. */
  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');
  /** The panel's own field, at md. */
  private readonly ownField = viewChild<ElementRef<HTMLInputElement>>('ownField');
  /** The modal's content. */
  private readonly sheet = viewChild.required<TemplateRef<unknown>>('sheet');
  #returnTo: HTMLElement | null = null;
  #sheetRef: TmModalRef<void> | null = null;
  #releaseEscape: (() => void) | null = null;
  #modalsAtOpen: ReadonlySet<HTMLElement> = new Set();
  #abort: AbortController | null = null;
  #lastRun = '';
  #trailing: ReturnType<typeof setTimeout> | undefined;
  #holding = false;
  #announceTimer: ReturnType<typeof setTimeout> | undefined;
  #announced = false;

  /** The launcher this surface shows. */
  protected readonly launcher = inject(TmLauncher);

  /** The listbox's id. */
  readonly listboxId = `${this.#base}-list`;

  /** The hidden instructions' id. */
  readonly hintId = `${this.#base}-hint`;

  /** Which surface shows: the panel with the app bar's field, the panel with its own, the modal, or none. */
  protected readonly kind = this.#kind.asReadonly();

  /** Whether the app bar's search field is the input: the panel from lg up. */
  readonly usesField = computed(() => this.#kind() === 'panel');

  /** What opened the modal, which decides whether Close or the field takes focus. */
  protected readonly sheetVia = signal<TmLauncherVia | null>(null);

  /** Whether an IME composition is in progress in the input, which holds the providers. */
  protected readonly composing = signal(false);

  /** The search glyph. */
  protected readonly searchIcon = Search;

  /** The close glyph. */
  protected readonly closeIcon = X;

  /** The input's `aria-keyshortcuts`. */
  protected readonly keyShortcuts = tmAriaKeyShortcuts('Mod+K');

  /** The launcher's text. */
  protected readonly query = this.launcher.query;

  /** The launcher's state. */
  protected readonly state = this.launcher.state;

  /** The field's name and placeholder: "Search". */
  protected readonly searchLabel = this.#l10n.ɵtext('tm.tenantShell.bar.search');

  // --- The candidates and the index: built when the typed state first reads them ---

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
    const collator = new Intl.Collator(language);
    const byLabel = (a: TmLauncherCandidate, b: TmLauncherCandidate): number => collator.compare(a.label, b.label);
    const sections = new Map(this.#navigation.sections().map((section) => [section.id, section.label]));
    const entries = this.#navigation.items().map((item): TmLauncherCandidate => {
      const own = this.#terms.read(item, language);
      const english = language === 'en' ? null : this.#terms.read(item, 'en');
      return {
        kind: 'entry',
        key: `entry:${item.id}`,
        href: item.href,
        label: item.label,
        icon: item.icon,
        description: item.section === undefined ? null : (sections.get(item.section) ?? null),
        terms: [own.label, ...own.synonyms, ...(english === null ? [] : [english.label, ...english.synonyms])],
        create:
          item.createHref !== null && item.entity !== undefined
            ? { href: item.createHref, label: this.text('create', { label: this.#entities.label(item.entity)() }) }
            : null,
      };
    });
    const pinned = this.text('pinned');
    const pins = this.#pins()
      .filter(({ href }) => this.#navigation.byHref(href) === undefined)
      .map(({ pin, href, label }): TmLauncherCandidate => ({
        kind: 'pin',
        key: `pin:${pin.id}`,
        href,
        label,
        icon: this.#navigation.byHref(pathOf(href))?.icon ?? 'pin',
        description: pinned,
        terms: [label, pin.title],
        create: null,
      }))
      .sort(byLabel);
    const pinHrefs = new Set(this.#pins().map(({ href }) => href));
    const recent = this.text('recent');
    const recents = this.#recents
      .pages()
      .filter((page) => this.#navigation.byHref(page.url) === undefined && !pinHrefs.has(page.url))
      .map((page): TmLauncherCandidate => {
        const title = page.title();
        return {
          kind: 'recent',
          key: `recent:${page.url}`,
          href: page.url,
          label: title,
          icon: page.icon ?? tmRecentPageIcon(page.kind),
          description: recent,
          terms: [title],
          create: null,
        };
      })
      .sort(byLabel);
    return tmLauncherIndex([...entries, ...pins, ...recents]);
  });

  // --- The groups each state shows ---

  /** The browse state: the pinned band below md or while the bar is hidden, the recently visited band, then the sections. */
  readonly #browse = computed((): readonly TmGroup[] => {
    const groups: TmGroup[] = [];
    const pins = this.#pins();
    if ((!this.#md() || !this.#barVisible()) && pins.length > 0) {
      groups.push(
        this.#group(
          'pinned',
          'band',
          this.text('pinned'),
          null,
          pins.map(({ pin, href, label }) => this.#option(`pin:${pin.id}`, href, this.#navigation.byHref(pathOf(href))?.icon ?? 'pin', label, 'pinned')),
        ),
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
          recents.map((page) => this.#option(`recent:${page.url}`, page.url, page.icon ?? tmRecentPageIcon(page.kind), page.title(), 'recents')),
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
   * state's with it. They stay as they were as the launcher closes, so a
   * choice whose navigation closed it first still reports its position; the
   * template reads them only while it is open.
   */
  protected readonly groups = computed(() => (this.launcher.query().trim() === '' ? this.#browse() : this.#typed()));

  /** Every option, in reading order. */
  readonly #options = computed(() => this.groups().flatMap((group) => group.options));

  /** The active option's id: the one the keys or the pointer chose for this text, else the first row of the typed state. */
  readonly activeId = computed(() => {
    const options = this.#options();
    const chosen = this.#active();
    if (chosen !== null && chosen.query === this.launcher.query() && options.some((option) => option.id === chosen.id)) {
      return chosen.id;
    }
    return this.state() === 'typed' ? (options[0]?.id ?? null) : null;
  });

  /** Creates the surface, which follows the router and the modals for as long as the app runs. */
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
      if (untracked(this.#kind) !== 'closed' && ɵtmOpenModals().some((host) => !this.#modalsAtOpen.has(host))) {
        this.launcher.close('route');
      }
    });
    effect(() => {
      const wide = this.#md();
      untracked(() => {
        const kind = this.#kind();
        if (kind !== 'closed' && (kind === 'sheet') === wide) {
          this.launcher.close('route');
        }
      });
    });
    // --- Every change of the text: the providers' edges and the announcement ---
    effect(() => {
      const query = this.launcher.query();
      untracked(() => this.#queryChanged(query));
    });
    // --- The active option's page starts loading, however it became active ---
    effect(() => {
      const id = this.activeId();
      const option = id === null ? undefined : untracked(this.#options).find((candidate) => candidate.id === id);
      if (option !== undefined) {
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
  protected text(key: string, params?: Readonly<Record<string, unknown>>): string {
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
    this.#lastRun = '';
    const field = this.launcher.ɵfield;
    // --- Below md: the Menu modal ---
    if (!untracked(this.#md)) {
      this.sheetVia.set(via);
      const ref = this.#modal.open(this.sheet(), {
        size: 'lg',
        title: this.#l10n.ɵtext('tm.tenantShell.bar.menu')(),
        showClose: false,
        panelClass: 'tm-launcher-sheet',
      });
      // Set once the modal is up, so the modal watcher does not count it as another dialog.
      this.#kind.set('sheet');
      this.#sheetRef = ref;
      void ref.closed.then((result) => {
        if (this.#sheetRef === ref) {
          this.#sheetRef = null;
          this.launcher.close(result.via === 'backdrop' ? 'outside' : 'escape');
        }
      });
    } else {
      // --- From md up: the panel under the shell's rows ---
      this.#kind.set(untracked(this.#lg) && field !== null ? 'panel' : 'field');
      this.#cdr.detectChanges();
      this.layer().nativeElement.showPopover();
      // The toasts stay above the panel.
      ɵtmRaiseTopLayer();
      this.#releaseEscape = tmPushEscapeDismissal(() => this.launcher.close('escape'));
      this.#document.addEventListener('pointerdown', this.#outside, true);
      if (untracked(this.#kind) === 'panel' && field !== null) {
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
    this.#modalsAtOpen = new Set(ɵtmOpenModals());
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
    // --- Stop what the open started ---
    this.#releaseEscape?.();
    this.#releaseEscape = null;
    this.#document.removeEventListener('pointerdown', this.#outside, true);
    const field = this.launcher.ɵfield;
    if (field !== null) {
      this.#listen(field, false);
    }
    this.#abort?.abort();
    this.#abort = null;
    this.#buckets.set([]);
    clearTimeout(this.#trailing);
    this.#trailing = undefined;
    clearTimeout(this.#announceTimer);
    this.#holding = false;
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
      returnTo?.focus();
    } else if (reason === 'outside') {
      // A press on something focusable keeps its focus; one on the scrim or the page's text gives it back.
      setTimeout(() => {
        const active = this.#document.activeElement;
        if (active === null || active === this.#document.body) {
          returnTo?.focus();
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
  protected typed(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.launcher.setQuery(input.value);
  }

  /**
   * The combobox keys: Down and Up over every option, Right
   * and Left across a band's chips or between sections with the input
   * empty, PageDown and PageUp between groups, Home and End in the browse
   * state, Enter to open (Mod or Shift for a new tab), Tab to close.
   *
   * @param event - The key.
   */
  protected keydown(event: KeyboardEvent): void {
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
        this.#holding ||= event.repeat;
        return;
    }
    event.preventDefault();
    if (next !== undefined) {
      this.#activate(next);
      this.#document.getElementById(next.id)?.scrollIntoView({ block: 'nearest' });
    }
  }

  /** The release of a held key runs the providers on what the hold typed. */
  protected keyup(): void {
    if (this.#holding) {
      this.#holding = false;
      this.#flush();
    }
  }

  /** The end of a composition is the input it stood for. */
  protected compositionEnd(): void {
    this.composing.set(false);
    this.#queryChanged(untracked(this.launcher.query));
  }

  /**
   * Mod+K in the modal, where the shortcut registry stands aside: the field's
   * text is selected.
   *
   * @param event - The key.
   */
  protected sheetKeydown(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && event.code === 'KeyK') {
      event.preventDefault();
      const input = (event.currentTarget as HTMLElement).querySelector('input');
      input?.focus();
      input?.select();
    }
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
   * the launcher.
   *
   * @param event - The click.
   * @param option - The row.
   */
  protected clicked(event: MouseEvent, option: TmOption): void {
    if (event.button > 1) {
      return;
    }
    const newTab = event.button === 1 || event.ctrlKey || event.metaKey || event.shiftKey;
    this.#report(option, newTab);
    this.launcher.close('chosen');
  }

  // --- Choosing, moving and the providers ---

  /** Opens an option here, or in a new tab, and closes the launcher. */
  #choose(option: TmOption, newTab: boolean): void {
    this.#report(option, newTab);
    this.launcher.close('chosen');
    if (newTab) {
      this.#document.defaultView?.open(option.href, '_blank', 'noopener');
    } else {
      void this.#router.navigateByUrl(option.link);
    }
  }

  /** Reports a choice: `tm.launcher.chosen`, and for one opened here, `tm.shell.navigated`. */
  #report(option: TmOption, newTab: boolean): void {
    const position = untracked(this.#options).findIndex((candidate) => candidate.id === option.id) + 1;
    const query = untracked(this.launcher.query);
    this.#telemetry.event(
      'tm.launcher.chosen',
      { bucket: option.bucket, position: positionBand(position), queryLength: lengthBand(query), newTab },
      { sampled: true },
    );
    if (!newTab) {
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
   * Answers a change of the text while open: the providers run at once on
   * the first change after a pause, then once the burst of typing ends; the
   * results are announced half a second after the last change.
   */
  #queryChanged(query: string): void {
    if (untracked(this.#kind) === 'closed') {
      return;
    }
    // --- The announcement ---
    clearTimeout(this.#announceTimer);
    this.#announced = false;
    if (query.trim() !== '') {
      this.#announceTimer = setTimeout(() => this.#announce(), ANNOUNCE_MS);
    }
    // --- The providers' edges, held while a composition or a held key types ---
    if (this.composing() || this.#holding) {
      return;
    }
    const leading = this.#trailing === undefined;
    clearTimeout(this.#trailing);
    this.#trailing = setTimeout(() => {
      this.#trailing = undefined;
      this.#flush();
    }, BURST_MS);
    if (leading) {
      this.#flush();
    }
  }

  /** Runs the providers on the text, unless they last ran on the same text. */
  #flush(): void {
    const query = untracked(this.launcher.query).trim();
    if (query === this.#lastRun) {
      return;
    }
    this.#lastRun = query;
    this.#abort?.abort();
    this.#buckets.set([]);
    const tenantId = this.#urls.tenantId();
    const folded = tmSearchNormalize(query).length;
    if (tenantId === null || query === '') {
      this.#abort = null;
      return;
    }
    const abort = new AbortController();
    this.#abort = abort;
    const context = { tenantId, language: this.#l10n.language(), limit: 5, signal: abort.signal };
    for (const provider of this.#providers) {
      if (folded < (provider.minLength ?? 2)) {
        continue;
      }
      // A provider that throws, fails or answers nothing renders no bucket.
      void Promise.resolve()
        .then(() => provider.search(query, context))
        .then(
          (items) => {
            if (abort.signal.aborted || items.length === 0) {
              return;
            }
            const bucket = { provider, items: items.slice(0, context.limit) };
            // Buckets keep the providers' order, so a late answer lands in its place below the rows above it.
            this.#buckets.update((buckets) =>
              [...buckets, bucket].sort((a, b) => this.#providers.indexOf(a.provider) - this.#providers.indexOf(b.provider)),
            );
            if (this.#announced) {
              this.#announcer.announce(this.#bucketText(bucket), 'polite');
            }
          },
          () => undefined,
        );
    }
  }

  /** Announces the pages that match, then each bucket answered so far; reports a query that matched nothing. */
  #announce(): void {
    this.#announced = true;
    const pages = untracked(this.#typed)[0]!;
    const count = pages.options.filter((option) => option.bucket !== 'create').length;
    if (count === 0) {
      this.#telemetry.event('tm.launcher.no-results', { queryLength: lengthBand(untracked(this.launcher.query)) }, { sampled: true });
    }
    const parts = [count === 0 ? this.text('nothing') : this.text('count', { count }), ...untracked(this.#buckets).map((bucket) => this.#bucketText(bucket))];
    this.#announcer.announce(parts.join('. '), 'polite');
  }

  /** A bucket's announcement: "{bucket}: {n} results". */
  #bucketText(bucket: TmBucket): string {
    return this.text('bucketCount', { bucket: this.#l10n.ɵtext(bucket.provider.labelKey)(), count: bucket.items.length });
  }

  // --- The input, the outside press and the model's pieces ---

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

  /** An option; its id stays the same for its key. */
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
    } = {},
  ): TmOption {
    let id = this.#ids.get(key);
    if (id === undefined) {
      id = `${this.#base}-o${this.#ids.size}`;
      this.#ids.set(key, id);
    }
    return {
      id,
      href,
      link: this.#router.parseUrl(href),
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
