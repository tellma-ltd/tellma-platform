// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, type Signal, viewChild } from '@angular/core';
import { ɵTM_ROUTE_ERROR_VIEWS, type TmContextError, TmLocalization, TmPageContexts } from '@tellma/core';

import type { TmNotificationSettingsBody } from './tm-notification-settings-body';
import { TmNotificationSettings } from './tm-notification-settings';

// The page of the hub's Notifications section (spec 0028 §10.3), which spec
// 0032 §8's hub renders at /{t}/me/notifications. It holds the section and
// publishes its page context (spec 0026 §9.3) from the first frame: kind
// `settings`, state `{ category: 'notifications', mode: 'edit', dirty }`,
// and two actions. The set action saves like a click; the test action sends
// a test message. The actions and the errors are the section body's, so
// they arrive with its chunk; until then the context is loading with no
// actions, and a chunk that did not load puts it in error, saying why. The
// context has no form: every change here saves itself, while a
// form's proposal must never save.

/**
 * The Notifications section of My settings as a page: the signed-in user's
 * section, and its page context with the `notifications.settings.set` and
 * `notifications.settings.test` actions. The hub renders it as the
 * section's body.
 *
 * @example
 * ```html
 * <tm-notification-settings-page />
 * ```
 */
@Component({
  selector: 'tm-notification-settings-page',
  imports: [TmNotificationSettings],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'tm-notification-settings-page' },
  template: `<tm-notification-settings />`,
  styles: `
    :host {
      display: block;
    }
  `,
})
export class TmNotificationSettingsPage {
  /** The app's localization. */
  readonly #l10n = inject(TmLocalization);

  /** The section the page holds. */
  protected readonly section = viewChild(TmNotificationSettings);

  /** The section's body, once its chunk loads. */
  readonly #body: Signal<TmNotificationSettingsBody | null> = computed(() => this.section()?.ɵbody() ?? null);

  /** Creates the page, which publishes its page context while it stays. */
  constructor() {
    const model = computed(() => this.#body()?.ɵmodel() ?? null);
    const failure = computed(() => this.section()?.ɵfailure() ?? null);
    const dirty = computed(() => model()?.dirty() ?? false);
    inject(TmPageContexts).register(
      {
        kind: 'settings',
        routeTemplate: '/:t/me/notifications',
        title: this.#l10n.ɵtext('tm.nav.me.notifications'),
        status: computed(() => (failure() !== null ? 'error' : (model()?.status() ?? 'loading'))),
        state: computed(() => ({ category: 'notifications', mode: 'edit', dirty: dirty() })),
        errors: computed((): readonly TmContextError[] => {
          // The body's chunk did not load: the inline view's message says why.
          const kind = failure();
          if (kind !== null) {
            return [{ source: 'request', code: kind, message: this.#l10n.ɵtext(ɵTM_ROUTE_ERROR_VIEWS[kind].message)() }];
          }
          return this.#body()?.ɵerrors() ?? [];
        }),
        actions: computed(() => this.#body()?.ɵactions ?? []),
      },
      inject(DestroyRef),
    );
  }
}
