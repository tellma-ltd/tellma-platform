// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, inject, input, signal } from '@angular/core';
import { CircleCheckBig, CircleX, Info, TriangleAlert } from 'lucide';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';
import { TmIcon } from '@tellma/core-ui/icon';

/** The severity of a {@link TmAlert}. */
export type TmAlertKind = 'info' | 'success' | 'warning' | 'error';

/** How a {@link TmAlert} announces itself to assistive technology. */
export type TmAlertLive = 'off' | 'polite' | 'assertive';

/** The glyph of each kind, drawn through tm-icon. */
const KIND_GLYPHS = {
  info: Info,
  success: CircleCheckBig,
  warning: TriangleAlert,
  error: CircleX,
} as const;

/**
 * Page- and section-level status message: a static kind glyph plus
 * projected content in a tinted, bordered wrapper. A visually-hidden
 * localized kind prefix ("Error:", "Warning:", …) precedes the content so
 * the severity survives screen-reader linearization — color is never the
 * only signal.
 *
 * For alerts inserted dynamically (a failed save), set `live`: `polite`
 * renders `role="status"`, `assertive` renders `role="alert"`. Because an
 * element that arrives in the DOM WITH its content is unreliably announced
 * across screen-reader/browser pairs, a live alert's region is inserted
 * empty and populated in a follow-up microtask — a content change inside
 * an existing region is the dependable trigger. The default `off` keeps
 * statically-present alerts silent.
 *
 * There is no dismiss affordance — visibility is the consumer's state.
 * Field errors keep the form-field's compact pattern and grid cell errors
 * keep the grid overlay; this component is for page/section messaging
 * (form-top error summaries, empty-state warnings, banner notices).
 *
 * @tmGroup indicator
 * @tmA11yNotes The kind is conveyed by a visually-hidden localized prefix,
 *   not color alone; `live` renders role="status"/"alert" and the region
 *   populates a microtask after insertion so dynamic alerts announce
 *   reliably.
 */
@Component({
  selector: 'tm-alert',
  imports: [TmIcon],
  template: `
    <!-- The glyph is chosen by MEANING, not by tone alone. Circle-x marks
         something that failed or was refused with nothing to correct in
         place; the caution triangle marks a decision the reader has not
         made yet. A field error, which can be fixed where it stands, uses
         circle-! instead and lives in tm-form-field. -->
    <tm-icon class="tm-alert__icon" [svg]="icon()" />
    <div class="tm-alert__content" [attr.role]="role()">
      @if (revealed() || live() === 'off') {
        <span class="tm-alert__sr-kind">{{ kindPrefix() }}</span>
        @if (heading(); as headingText) {
          <strong class="tm-alert__heading">{{ headingText }}</strong>
        }
        <ng-content />
      }
    </div>
  `,
  styleUrl: './tm-alert.css',
  host: {
    class: 'tm-alert',
    '[class.tm-alert--info]': 'kind() === "info"',
    '[class.tm-alert--success]': 'kind() === "success"',
    '[class.tm-alert--warning]': 'kind() === "warning"',
    '[class.tm-alert--error]': 'kind() === "error"',
  },
})
export class TmAlert {
  private readonly translate = inject(TM_UI_TRANSLATE);

  /** The severity — drives the glyph, the tint, and the hidden prefix. */
  readonly kind = input.required<TmAlertKind>();
  /**
   * Announcement mode for dynamically inserted alerts: `polite` renders
   * `role="status"`, `assertive` renders `role="alert"`. Default `off` —
   * statically-present alerts stay silent.
   */
  readonly live = input<TmAlertLive>('off');
  /** Optional bold first line rendered before the projected content. */
  readonly heading = input<string | undefined>(undefined);

  /**
   * Content gate for live alerts: the region enters the DOM empty and the
   * microtask flip populates it — a mutation inside an existing live
   * region, the announcement trigger that works across screen-reader/
   * browser pairs. Non-live alerts render synchronously (`live() === 'off'`
   * bypasses the gate in the template).
   */
  protected readonly revealed = signal(false);

  /** The `role` attribute per the announcement mode (null when off). */
  protected readonly role = computed(() =>
    this.live() === 'polite' ? 'status' : this.live() === 'assertive' ? 'alert' : null,
  );

  /** The kind's glyph data. */
  protected readonly icon = computed(() => KIND_GLYPHS[this.kind()]);

  /** The localized visually-hidden kind prefix ("Error:", …). */
  protected readonly kindPrefix = computed(() => this.translate(`alert.${this.kind()}`)());

  constructor() {
    queueMicrotask(() => this.revealed.set(true));
  }
}
