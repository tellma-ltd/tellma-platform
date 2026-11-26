// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, inject, Injectable, type Signal } from '@angular/core';
import { TmLaunchCache, TmLocalization, TmSession, TmUrls } from '@tellma/core';

// Who and where the user is, as the tenant shell shows it (spec 0027 §7.4,
// §10): the user's name and email, the tenant's name, the avatar's photo and
// tone key, and the markers. The app bar reads it in the first frame and the
// session menu, the marker popovers and the dialogs read the same values
// once their chunks load, so the two never disagree. Everything derives from
// the launch documents, with the profile cookie standing in while a cold
// launch's sync runs.

/** A tenant marker: a sandbox, and the tenant's state when it is not active. */
export type TmShellMarker = 'sandbox' | 'readOnly' | 'maintenance';

/**
 * Who and where the user is in the tenant shell: the names the session
 * button and the session menu show, the avatar's face, and the markers.
 * Provided by the tenant shell.
 */
@Injectable()
export class TmShellIdentity {
  readonly #cache = inject(TmLaunchCache);
  readonly #l10n = inject(TmLocalization);
  readonly #session = inject(TmSession);
  readonly #urls = inject(TmUrls);

  /** The routed tenant's id, or `null` off a tenant route. */
  readonly tenantId: Signal<number | null> = this.#urls.tenantId;

  /** The user's record in the tenant, once the `me` document is stored. */
  readonly #user = computed(() => this.#cache.docs.me()?.user);

  /**
   * The user's name in the active content slot, else the record's first
   * name; while a cold launch's sync runs, the profile cookie's name.
   */
  readonly userName: Signal<string> = computed(() => {
    const user = this.#user();
    if (user === undefined) {
      return this.#session.profile()?.name ?? '';
    }
    const slot = this.#l10n.contentSlot();
    const name = slot === 3 ? user.name3 : slot === 2 ? user.name2 : user.name;
    return name?.trim() || user.name;
  });

  /** The user's email: the record's, else the profile cookie's. */
  readonly email: Signal<string> = computed(() => this.#user()?.email ?? this.#session.profile()?.email ?? '');

  /**
   * The tenant's name in the active content slot, else its first name, or
   * `null` while the settings document is missing (a cold launch's sync).
   */
  readonly tenantName: Signal<string | null> = computed(() => {
    const names = this.#cache.docs.settings()?.names;
    if (names === undefined) {
      return null;
    }
    const wanted = names[this.#l10n.contentSlot() - 1]?.trim();
    return wanted || (names.find((name) => (name ?? '').trim() !== '') ?? '').trim();
  });

  /** The avatar's photo, the user's thumbnail, or `null` without one or once the route leaves the tenant. */
  readonly avatarSrc: Signal<string | null> = computed(() => {
    const imageId = this.#user()?.imageId;
    return imageId === undefined || this.tenantId() === null ? null : this.#urls.blob('user-image', imageId, { variant: 'thumb' });
  });

  /**
   * The key the avatar's tone derives from: the user's record key, so the
   * initials take the same tone as in every list of the tenant; `null`
   * before the record is known.
   */
  readonly colorKey: Signal<string | null> = computed(() => {
    const user = this.#user();
    return user === undefined ? null : `core.User:${user.id}`;
  });

  /** The markers that show, in order: the sandbox, then the tenant's state; none before the tenant's facts are stored. */
  readonly markers: Signal<readonly TmShellMarker[]> = computed(() => {
    const facts = this.#cache.docs.tenant();
    if (facts === undefined) {
      return [];
    }
    const markers: TmShellMarker[] = facts.category === 'Sandbox' ? ['sandbox'] : [];
    if (facts.state === 'ReadOnly') {
      markers.push('readOnly');
    } else if (facts.state === 'Frozen') {
      markers.push('maintenance');
    }
    return markers;
  });

  /** A sandbox's live tenant, or `null` for a live tenant or before the facts are stored. */
  readonly liveTenantId: Signal<number | null> = computed(() => {
    const facts = this.#cache.docs.tenant();
    return facts?.category === 'Sandbox' ? (facts.liveTenantId ?? null) : null;
  });
}
