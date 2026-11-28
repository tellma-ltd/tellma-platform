// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  Injectable,
  input,
  untracked,
  viewChild,
  ViewEncapsulation,
} from '@angular/core';
import { TmBanners, TmLocalization, TmSupport } from '@tellma/core';
import { TmBreakpoints } from '@tellma/core-ui';
import { TmIconRegistry } from '@tellma/core-ui/icon';
import { TmModal, type TmModalRef } from '@tellma/core-ui/modal';
import { TmPopover, TmPopoverContent } from '@tellma/core-ui/popover';
import CheckCheck from 'lucide/dist/esm/icons/check-check.mjs';

import { TmAttention, type TmPanel } from './tm-attention';
import type { TmAttentionPanelData } from './tm-attention-panel-data';
import { tmRegisterAttentionPanelsStrings } from './tm-attention-panels-strings';
import { TmBellPanel } from './tm-bell-panel';
import { TmTrayPanel } from './tm-tray-panel';

// The bell's and the tray's panels' chunk (spec 0028 §6.2–§6.4, §7.2): each
// button creates one host here, beside itself, once the chunk loads. The
// host follows the attention model's open panel: from lg up it opens a
// popover below its button, aligned to the button's inline-end edge; below
// lg an end drawer, titled with the panel's heading. Escape, an outside
// click, the drawer's close and choosing a row close the panel on the model
// too. The chunk also adds the `attention` section to the support dump.

/**
 * Adds the `attention` section to the support dump, once per app: the open
 * panel, the badges and the banners the user can see.
 */
@Injectable({ providedIn: 'root' })
export class TmAttentionDump {
  /** Creates the section. */
  constructor() {
    const attention = inject(TmAttention);
    const banners = inject(TmBanners);
    inject(TmSupport).registerDumpSection('attention', () => {
      const bell = attention.bell();
      const tray = attention.tray();
      return {
        open: attention.open(),
        bell: { count: bell.count, tone: bell.tone, jobs: bell.jobsRunning },
        tray: { count: tray.count, tone: tray.tone },
        banners: banners.ɵshowable().map((banner) => ({ id: banner.id, level: banner.level })),
      };
    });
  }
}

/**
 * The host of one attention panel, created beside its button: the panel's
 * popover, and the end drawer it opens below `lg`. Internal.
 */
@Component({
  selector: 'tm-attention-panel-host',
  imports: [TmBellPanel, TmPopover, TmPopoverContent, TmTrayPanel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // The popover's panel and the drawer's content live in the overlay layer,
  // so the panels' rules are unencapsulated; every selector is scoped by a
  // tm-attention-panel class.
  encapsulation: ViewEncapsulation.None,
  // Out of the bar's flow: the popover opens in the top layer, so the host
  // renders nothing in place and takes no flex gap beside its button.
  host: { style: 'position: absolute' },
  template: `
    <!-- From lg up: the panel's popover at its button. -->
    <tm-popover size="md" align="end" panelClass="tm-attention-panel__popover" [aria-label]="title()" (closed)="closed()">
      <ng-template tmPopoverContent>
        @if (panel() === 'bell') {
          <tm-bell-panel />
        } @else {
          <tm-tray-panel />
        }
      </ng-template>
    </tm-popover>
  `,
  styles: `
    /* The popover: the panel's own header, lists and footer, at most 560 px and the room under the shell. */
    .tm-popover__panel.tm-attention-panel__popover {
      display: flex;
      flex-direction: column;
      padding: 0;
      overflow: hidden;
      max-block-size: min(var(--tm-popover-block-cap, 100dvh), 560px, calc(100dvh - var(--tm-shell-top, 0px) - 16px));
    }

    /* The panel: a fixed header and footer around the scrolling lists. Its rows are the attention rows' shared anatomy. */
    .tm-attention-panel,
    .tm-attention-panel__body {
      display: flex;
      flex: 1 1 auto;
      flex-direction: column;
      min-block-size: 0;
    }

    .tm-attention-panel,
    .tm-attention-panel__heading {
      outline: none;
    }

    .tm-modal-panel .tm-attention-panel {
      block-size: 100%;
    }

    .tm-attention-panel__header,
    .tm-attention-panel__footer {
      display: flex;
      flex: none;
      align-items: center;
      gap: var(--space-1);
      padding: var(--space-2) var(--space-3);
    }

    .tm-attention-panel__header {
      min-block-size: 2.75rem;
      border-block-end: var(--border-width) solid var(--divider);
    }

    .tm-attention-panel__footer {
      border-block-start: var(--border-width) solid var(--divider);
    }

    /* The rows of an icon or a badge beside text: the heading, a job's title line and the status line. */
    .tm-attention-panel__heading,
    .tm-attention-panel__title-line,
    .tm-attention-panel__status {
      display: flex;
      align-items: center;
      gap: var(--space-2);
    }

    .tm-attention-panel__heading {
      margin: 0;
      color: var(--text-strong);
      font-size: var(--text-base);
      font-weight: var(--weight-semibold);
    }

    .tm-attention-panel__spacer {
      flex: 1;
    }

    .tm-attention-panel__body {
      overflow: auto;
      overscroll-behavior: contain;
      padding: var(--space-1);
    }

    /* The small secondary texts: the drawer's count, the jobs' heading and the status line. */
    .tm-attention-panel__count,
    .tm-attention-panel__section,
    .tm-attention-panel__status {
      margin: 0;
      color: var(--text-secondary);
    }

    .tm-attention-panel__section,
    .tm-attention-panel__status {
      padding: var(--space-1) var(--space-2);
      font-size: var(--text-xs);
    }

    .tm-attention-panel__section {
      padding-block-start: var(--space-2);
      font-weight: var(--weight-semibold);
    }

    .tm-attention-panel__status {
      justify-content: space-between;
    }

    /* A job's title line: the title, then its badge or percent. */
    .tm-attention-panel__title-line {
      min-inline-size: 0;
    }

    .tm-attention-panel__percent {
      margin-inline-start: auto;
      color: var(--text-secondary);
      font-variant-numeric: tabular-nums;
    }

    /* Unread rows: a bold title and a dot. */
    .tm-attention-panel__notice--unread .tm-attention-row__title {
      font-weight: var(--weight-semibold);
    }

    .tm-attention-panel__dot {
      inline-size: 8px;
      block-size: 8px;
      border-radius: var(--radius-full);
      background: var(--accent);
    }

    /* Where the pointer hovers, Stop and Clear show on the row's hover and focus; hidden, they keep their box and their place in the tree. */
    @media (hover: hover) {
      .tm-attention-row:not(:hover, :focus-within) .tm-attention-panel__hover {
        opacity: 0;
      }
    }

    .tm-attention-panel__more,
    .tm-attention-panel__see-all {
      border-radius: var(--radius-sm);
      color: var(--text-link);
      font-weight: var(--weight-semibold);
      text-decoration: none;
    }

    .tm-attention-panel__more:focus-visible,
    .tm-attention-panel__see-all:focus-visible {
      outline: var(--focus-ring-width) solid transparent;
      box-shadow: var(--focus-ring);
    }

    .tm-attention-panel__more {
      padding: var(--space-1) var(--space-2) var(--space-2);
      font-size: var(--text-xs);
    }

    /* The pinned "{n} new" over rows that arrived while the list was scrolled down. */
    .tm-attention-panel__fresh {
      position: sticky;
      inset-block-start: 0;
      z-index: 1;
      align-self: center;
    }

    .tm-attention-panel__hidden {
      position: absolute;
      clip-path: inset(50%);
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      white-space: nowrap;
    }
  `,
})
export class TmAttentionPanelHost {
  readonly #attention = inject(TmAttention);
  readonly #l10n = inject(TmLocalization);
  readonly #modal = inject(TmModal);
  readonly #wide = inject(TmBreakpoints).atLeast('lg');
  /** The popover the panel opens in from lg. */
  private readonly popoverRef = viewChild.required(TmPopover);

  /** The drawer the panel is open in, below `lg`. */
  #drawer: TmModalRef<void> | null = null;

  /** Releases the button as the popover's trigger, so a press on it is the button's own toggle. */
  #release: (() => void) | null = null;

  /** The panel the host shows. */
  readonly panel = input.required<TmPanel>();

  /** The panel's button, which the popover opens at. */
  readonly anchor = input.required<HTMLElement>();

  /** The panel's name: its heading. */
  protected readonly title = computed(() =>
    this.#l10n.ɵtext(this.panel() === 'bell' ? 'tm.attention.bell.label' : 'tm.attention.tray.label')(),
  );

  /** Creates the host, which opens and closes its panel with the model. */
  constructor() {
    tmRegisterAttentionPanelsStrings(this.#l10n);
    inject(TmAttentionDump);
    // The empty states draw their icons by name; the attention buttons registered the tray's.
    inject(TmIconRegistry).register({ 'check-check': CheckCheck });
    effect(() => {
      const open = this.#attention.open() === this.panel();
      untracked(() => (open ? this.#show() : this.#hide()));
    });
    inject(DestroyRef).onDestroy(() => {
      this.#release?.();
      this.#drawer?.close();
    });
  }

  /** The popover closed itself (Escape, an outside click, focus leaving): the model closes too. */
  protected closed(): void {
    this.#closeModel();
  }

  /** Shows the panel: the popover at its button from `lg` up, else the end drawer. */
  #show(): void {
    const anchor = this.anchor();
    if (this.#wide()) {
      const popover = this.popoverRef();
      this.#release ??= popover.ɵregisterTrigger(anchor);
      popover.open(anchor);
      return;
    }
    if (this.#drawer !== null) {
      return;
    }
    const data: TmAttentionPanelData = { drawer: true };
    const ref = this.#modal.open(this.panel() === 'bell' ? TmBellPanel : TmTrayPanel, {
      placement: 'end',
      size: 'sm',
      title: this.title(),
      data,
      // Focus returns to the button however the drawer opened: a shortcut from a field, or a tap that focuses nothing.
      returnFocus: anchor,
    });
    this.#drawer = ref;
    void ref.closed.then(() => {
      if (this.#drawer === ref) {
        this.#drawer = null;
        this.#closeModel();
      }
    });
  }

  /** Hides the panel wherever it shows. */
  #hide(): void {
    this.popoverRef().close();
    const drawer = this.#drawer;
    this.#drawer = null;
    drawer?.close();
  }

  /** Closes the panel on the model, when it is still this host's. */
  #closeModel(): void {
    if (untracked(this.#attention.open) === untracked(this.panel)) {
      this.#attention.toggle(untracked(this.panel), 'click');
    }
  }
}
