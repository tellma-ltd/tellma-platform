// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
  ElementRef,
  inject,
  InjectionToken,
  type Signal,
  type Type,
  viewChild,
  ViewContainerRef,
} from '@angular/core';
import { TmShortcuts, TmTelemetry } from '@tellma/core';
import { TmCounter } from '@tellma/core-ui/counter';
import { TmIcon } from '@tellma/core-ui/icon';
import { tmAriaKeyShortcuts, type TmKeys } from '@tellma/core-ui/keys';
import { TmProgressRing } from '@tellma/core-ui/progress';
import Bell from 'lucide/dist/esm/icons/bell.mjs';
import Inbox from 'lucide/dist/esm/icons/inbox.mjs';

import { TmAttention, type TmPanel } from './tm-attention';

// The bell's and the tray's buttons (spec 0028 §6.1, §7.1): one component
// under both elements, which reads its panel from its element's name, so the
// first frame carries one compiled template and one stylesheet. 30 × 30 icon
// buttons in the app bar, each with an overlay counter in its badge's tone,
// and the bell with the job ring while jobs run. A press toggles the panel
// on the attention model at once; each button renders its panel's host from
// a chunk of their own, created beside the button. The chunk loads when the
// browser is first idle after the button renders, and on the first hover or
// focus, so a press finds it in memory and the panel opens in the press's
// frame; a press that beats it opens the panel as soon as it lands, since
// the host follows the model's open panel. The shortcuts toggle the same
// way.

/** What the panels' chunk exports. Internal. */
export interface TmAttentionPanelsChunk {
  /** The host of one panel, created beside its button with the `panel` and `anchor` inputs. */
  readonly TmAttentionPanelHost: Type<unknown>;
}

/** Loads the panels' chunk. Internal: a test delays it or makes it fail. */
export const TM_ATTENTION_PANELS_LOAD = new InjectionToken<() => Promise<TmAttentionPanelsChunk>>('TM_ATTENTION_PANELS_LOAD', {
  providedIn: 'root',
  factory: () => () => import('./tm-attention-panels'),
});

/** Each panel's shortcut: its id, its keys and the string key of its label. */
const SHORTCUTS: Readonly<Record<TmPanel, { readonly id: string; readonly keys: TmKeys; readonly labelKey: string }>> = {
  bell: { id: 'tm.attention.notifications', keys: 'Alt+Shift+U', labelKey: 'tm.attention.bell.label' },
  tray: { id: 'tm.attention.assignments', keys: 'Alt+Shift+Q', labelKey: 'tm.attention.tray.label' },
};

/**
 * The app bar's bell and tray, `tm-bell-button` and `tm-tray-button`: one
 * component under both elements, its panel named by the element. The bell
 * shows the user's notifications and running jobs, its counter what arrived
 * since it was last opened and a ring while jobs run; the tray shows the
 * work waiting on the user, its counter the open assignments, in the accent
 * tone while some are new. A press, Alt+Shift+U (the bell) or Alt+Shift+Q
 * (the tray) toggles the panel.
 *
 * @example
 * ```html
 * <tm-bell-button />
 * <tm-tray-button />
 * ```
 */
@Component({
  selector: 'tm-bell-button, tm-tray-button',
  imports: [TmCounter, TmIcon, TmProgressRing],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      #button
      type="button"
      class="tm-attention-button"
      aria-haspopup="dialog"
      [attr.aria-label]="badge().label"
      [attr.aria-expanded]="expanded()"
      [attr.aria-controls]="expanded() ? controls : null"
      [attr.aria-keyshortcuts]="keys"
      (pointerenter)="preload()"
      (focus)="preload()"
      (click)="press()"
    >
      <!-- The bell's job ring, under the icon and the counter. -->
      @if (ring()) {
        <tm-progress-ring class="tm-attention-button__ring" [diameter]="30" />
      }
      <tm-icon [svg]="icon" />
      <tm-counter placement="overlay" [value]="badge().count" [tone]="badge().tone" />
    </button>
    <!-- The panel's host, from the panels' chunk. -->
    <ng-container #host />
  `,
  // The app bar's 30 px icon button, tinted from the bar's own ink.
  styles: `
    :host {
      display: inline-flex;
      flex: none;
      position: relative;
    }

    .tm-attention-button {
      position: relative;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      inline-size: 30px;
      block-size: 30px;
      padding: 0;
      border: 0;
      border-radius: var(--radius-md);
      background: transparent;
      color: inherit;
      cursor: pointer;
    }

    .tm-attention-button:hover,
    .tm-attention-button[aria-expanded='true'] {
      background: color-mix(in srgb, currentColor 10%, transparent);
    }

    .tm-attention-button:focus-visible {
      outline: var(--focus-ring-width) solid currentColor;
      outline-offset: 1px;
    }

    .tm-attention-button__ring {
      position: absolute;
      inset: 0;
    }
  `,
})
export class TmAttentionButton {
  readonly #attention = inject(TmAttention);
  readonly #load = inject(TM_ATTENTION_PANELS_LOAD);
  readonly #telemetry = inject(TmTelemetry);
  readonly #window = inject(DOCUMENT).defaultView;
  /** The button's element. */
  private readonly buttonRef = viewChild.required<ElementRef<HTMLButtonElement>>('button');
  /** Where the panels' host is created. */
  private readonly hostRef = viewChild.required('host', { read: ViewContainerRef });
  #loading: Promise<void> | null = null;
  #ended = false;

  /** The button's panel, by its element: `tm-tray-button` is the tray's. */
  readonly #panel: TmPanel = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement.localName === 'tm-tray-button' ? 'tray' : 'bell';

  /** The button's icon. */
  protected readonly icon = this.#panel === 'bell' ? Bell : Inbox;

  /** The button's badge. */
  protected readonly badge: Signal<{ readonly count: number | null; readonly tone: 'accent' | 'attention' | 'neutral'; readonly label: string }> =
    this.#panel === 'bell' ? this.#attention.bell : this.#attention.tray;

  /** Whether the job ring shows: on the bell, while jobs run. */
  protected readonly ring = computed(() => this.#panel === 'bell' && this.#attention.bell().jobsRunning > 0);

  /** Whether the button's panel is open. */
  protected readonly expanded = computed(() => this.#attention.open() === this.#panel);

  /** The `aria-keyshortcuts` of the panel's shortcut. */
  protected readonly keys = tmAriaKeyShortcuts(SHORTCUTS[this.#panel].keys);

  /** The id of the panel the button controls while it is open. */
  protected readonly controls = `tm-attention-${this.#panel}-panel`;

  /** Creates the button, which registers its panel's shortcut and loads the panels once the browser is idle. */
  constructor() {
    const destroyRef = inject(DestroyRef);
    destroyRef.onDestroy(() => (this.#ended = true));
    // The shortcut toggles the panel from anywhere, a field included, but
    // not over a modal (spec 0027 Appendix A).
    const shortcuts = inject(TmShortcuts);
    afterNextRender(() => {
      shortcuts.register(
        { ...SHORTCUTS[this.#panel], scope: 'global', inFields: true, overModals: false, run: () => this.#toggle('shortcut') },
        destroyRef,
      );
      const idle = (run: () => void): unknown => (this.#window?.requestIdleCallback ? this.#window.requestIdleCallback(run) : setTimeout(run));
      idle(() => this.preload());
    });
  }

  /** Starts loading the panels' chunk, as a hover or focus does. */
  protected preload(): void {
    if (this.#loading !== null) {
      return;
    }
    this.#loading = this.#load().then(
      (chunk) => {
        if (this.#ended) {
          return;
        }
        const ref = this.hostRef().createComponent(chunk.TmAttentionPanelHost);
        ref.setInput('panel', this.#panel);
        ref.setInput('anchor', this.buttonRef().nativeElement);
        // Renders the host at once, so a press that waited for the chunk opens the panel in this frame.
        ref.changeDetectorRef.detectChanges();
      },
      (error: unknown) => {
        // Offline, or after a deploy removed the chunk: the next hover, focus or press tries again.
        this.#telemetry.error(error, { source: 'tm-attention-buttons' });
        this.#loading = null;
      },
    );
  }

  /** A press: toggles the panel at once; the host opens it in this frame once its chunk is in. */
  protected press(): void {
    this.#toggle('click');
  }

  /** Toggles the panel, loading the chunk when it is not in yet. */
  #toggle(via: 'click' | 'shortcut'): void {
    this.#attention.toggle(this.#panel, via);
    this.preload();
  }
}
