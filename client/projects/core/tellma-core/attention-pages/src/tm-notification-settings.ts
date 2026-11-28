// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  type ComponentRef,
  computed,
  DestroyRef,
  effect,
  inject,
  InjectionToken,
  input,
  type Signal,
  signal,
  viewChild,
  ViewContainerRef,
} from '@angular/core';
import {
  ɵTM_API_UPDATES,
  ɵTM_ROUTE_ERROR_VIEWS,
  ɵTmRouteError,
  type ɵTmRouteErrorKind,
  TmTelemetry,
} from '@tellma/core';
import { TmCard, type TmCardHeadingLevel } from '@tellma/core-ui/card';
import { TmSkeleton } from '@tellma/core-ui/skeleton';

import type { TmNotificationSettingsUser } from './tm-notification-settings-model';
import type { TmNotificationSettingsBody } from './tm-notification-settings-body';

// The shell of the Notifications section of My settings (spec 0028 §10.3).
// The section's body (its model, its saving, its template and its strings)
// is a chunk of its own, so the other pages of this entry point (the
// notification, assignment and job pages and their cells) never download
// it. The shell loads the chunk when it renders, draws the body's first-read
// skeleton in the body's place meanwhile, and passes its inputs through. A
// chunk that fails to load (offline, or after a deploy removed it) follows
// spec 0024 §4.4's rule, as the route host does: it is reported, the worker
// checks for an update, and the section shows client-outdated (Reload) or
// offline (Try again, which loads the chunk again) inline, in the primary's
// strings, since the section's own ship in the chunk that failed.

/** What the section body's chunk exports. Internal. */
export interface TmNotificationSettingsChunk {
  /** The section's body, created in the shell's place with the `user` and `headingLevel` inputs. */
  readonly TmNotificationSettingsBody: typeof TmNotificationSettingsBody;
}

/** Loads the section body's chunk. Internal: a test delays it or makes it fail. */
export const TM_NOTIFICATION_SETTINGS_LOAD = new InjectionToken<() => Promise<TmNotificationSettingsChunk>>('TM_NOTIFICATION_SETTINGS_LOAD', {
  providedIn: 'root',
  factory: () => () => import('./tm-notification-settings-body'),
});

/**
 * The Notifications section of My settings: arrival pop-ups on or off, each
 * notification type's channels on or off, and test messages. Every change
 * applies at once and saves itself; the header says how the saving goes.
 * With another user's id it edits that user's settings through their routes
 * and has no test delivery.
 *
 * @example
 * ```html
 * <tm-notification-settings />
 * <tm-notification-settings [user]="42" [headingLevel]="3" />
 * ```
 */
@Component({
  selector: 'tm-notification-settings',
  imports: [TmCard, TmSkeleton, ɵTmRouteError],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'tm-notification-settings',
    '[attr.aria-busy]': 'busy() ? "true" : null',
  },
  template: `
    <!-- The body, once its chunk loads. -->
    <ng-container #slot />

    @if (failure(); as kind) {
      <!-- The chunk did not load: client-outdated or offline, inline, with its action. -->
      <tm-route-error [view]="views[kind]" (retry)="load()" />
    } @else if (body() === null) {
      <!-- While the chunk loads: the body's first-read layout, an empty status line over skeleton cards. -->
      <div class="tm-notification-settings__placeholder" aria-hidden="true">
        <div class="tm-notification-settings__header"></div>
        @for (index of [0, 1]; track index) {
          <tm-card>
            <tm-skeleton width="30%" />
            <tm-skeleton [lines]="3" />
          </tm-card>
        }
      </div>
    }
  `,
  styles: `
    :host,
    .tm-notification-settings__placeholder {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
    }

    /* The status line's box, as the body keeps it. */
    .tm-notification-settings__placeholder > .tm-notification-settings__header {
      min-block-size: var(--space-8);
    }
  `,
})
export class TmNotificationSettings {
  /** Loads the body's chunk. */
  readonly #load = inject(TM_NOTIFICATION_SETTINGS_LOAD);

  /** The app's telemetry, which hears of a chunk that failed to load. */
  readonly #telemetry = inject(TmTelemetry);

  /** The worker's update check, which tells an outdated client from an offline one. */
  readonly #updates = inject(ɵTM_API_UPDATES);

  /** Why the chunk did not load, or `null` while it loads or has loaded. */
  readonly #failure = signal<ɵTmRouteErrorKind | null>(null);

  /** Whether a load of the chunk is in flight. */
  #loading = false;

  /** The body, once its chunk loads. */
  readonly #body = signal<ComponentRef<TmNotificationSettingsBody> | null>(null);

  /** Whether the shell is torn down, so a late chunk renders nothing. */
  #ended = false;

  /** Whose settings to edit: `'me'`, the signed-in user, or another user's id. */
  readonly user = input<TmNotificationSettingsUser>('me');

  /** The cards' heading level: 2 on the hub's page, 3 inside a dialog. */
  readonly headingLevel = input<TmCardHeadingLevel>(2);

  /** Where the body is created. */
  protected readonly slot = viewChild.required('slot', { read: ViewContainerRef });

  /** The body's instance, once its chunk loads; `null` before. */
  protected readonly body: Signal<TmNotificationSettingsBody | null> = computed(() => this.#body()?.instance ?? null);

  /** Why the chunk did not load, or `null` while it loads or has loaded. */
  protected readonly failure: Signal<ɵTmRouteErrorKind | null> = this.#failure.asReadonly();

  /** The inline views of a chunk that did not load. */
  protected readonly views = ɵTM_ROUTE_ERROR_VIEWS;

  /** Whether the section is loading: its chunk, its first read, or a reload over the shown values. */
  protected readonly busy = computed(() => {
    if (this.#failure() !== null) {
      return false;
    }
    const model = this.body()?.ɵmodel();
    return model === undefined || model === null || model.status() === 'loading' || model.refreshing();
  });

  /** Creates the shell, which loads the body's chunk once it renders and passes its inputs on. */
  constructor() {
    inject(DestroyRef).onDestroy(() => (this.#ended = true));

    // --- The chunk: loaded once the shell has rendered its slot ---
    afterNextRender(() => this.load());

    // --- The inputs: passed to the body as they change ---
    effect(() => {
      const body = this.#body();
      body?.setInput('user', this.user());
      body?.setInput('headingLevel', this.headingLevel());
    });
  }

  /** The section's body, for the page that hosts it, whose page context reads it; not for apps. */
  get ɵbody(): Signal<TmNotificationSettingsBody | null> {
    return this.body;
  }

  /** Why the body's chunk did not load, for the page that hosts it, whose page context reads it; not for apps. */
  get ɵfailure(): Signal<ɵTmRouteErrorKind | null> {
    return this.failure;
  }

  /**
   * Loads the body's chunk and creates the body in the slot: first after the
   * shell renders, then on the offline view's Try again. A failure is
   * reported, and the worker's update check picks the view that says why.
   */
  protected load(): void {
    if (this.#loading || this.#body() !== null) {
      return;
    }
    this.#loading = true;
    this.#failure.set(null);
    this.#load().then(
      (chunk) => {
        this.#loading = false;
        if (!this.#ended) {
          this.#body.set(this.slot().createComponent(chunk.TmNotificationSettingsBody));
        }
      },
      (error: unknown) => {
        this.#telemetry.error(error, { source: 'tm-notification-settings' });
        // Spec 0024 §4.4: an update the worker has, or failed to get, means the client is outdated.
        void this.#updates.check().then((update) => {
          this.#loading = false;
          if (!this.#ended) {
            this.#failure.set(update === 'ready' || update === 'failed' ? 'client-outdated' : 'offline');
          }
        });
      },
    );
  }
}
