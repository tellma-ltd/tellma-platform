// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, signal } from '@angular/core';

import { TmBannerBar, type TmBannerTone } from '@tellma/core-ui/banner';

/** The messages the paged banner cycles through, the way a banner host would. */
const MESSAGES: ReadonlyArray<{ readonly tone: TmBannerTone; readonly message: string }> = [
  { tone: 'info', message: 'A new version is available.' },
  { tone: 'warning', message: 'The server restarts for maintenance at 22:00.' },
  { tone: 'error', message: 'You are offline. Changes are kept until you reconnect.' },
];

/**
 * tm-banner-bar in each tone, with an action, dismissible, with a message
 * too long for its line, and paged through three messages (previous and
 * next wrap around, as the banner host does).
 */
@Component({
  imports: [TmBannerBar],
  template: `
    <h2>Banner bar</h2>

    <!-- Each tone, plain. -->
    <div class="stack" data-testid="tones">
      <tm-banner-bar tone="info" message="Your trial ends in 5 days." data-testid="banner-info" />
      <tm-banner-bar tone="warning" message="Exchange rates are 2 days old." data-testid="banner-warning" />
      <tm-banner-bar tone="error" message="Sync failed." data-testid="banner-error" />
    </div>

    <!-- Every control on, and a message too long for the line. -->
    <div class="stack">
      <tm-banner-bar
        tone="warning"
        message="The server restarts for maintenance at 22:00 tonight; save your work before then, or it may be lost when the session ends."
        actionLabel="Learn more"
        [dismissible]="true"
        data-testid="banner-long"
        (action)="log('action')"
        (dismiss)="log('dismiss')"
      />
      <tm-banner-bar
        [tone]="current().tone"
        [message]="current().message"
        actionLabel="Reload"
        [dismissible]="true"
        [position]="{ index: index() + 1, count: messages.length }"
        data-testid="banner-paged"
        (previous)="step(-1)"
        (next)="step(1)"
        (action)="log('reload')"
        (dismiss)="log('dismiss paged')"
      />
      <tm-banner-bar
        tone="info"
        message="تتوفر نسخة جديدة."
        lang="ar"
        actionLabel="إعادة التحميل"
        data-testid="banner-arabic"
      />
    </div>
    <p class="log" data-testid="banner-log">{{ events().join(', ') }}</p>
  `,
  styles: `
    .stack {
      display: flex;
      flex-direction: column;
      gap: 8px;
      margin-block-end: 16px;
    }
    .log {
      min-block-size: 1.5em;
    }
  `,
})
export class BannerStory {
  /** The paged banner's messages. */
  protected readonly messages = MESSAGES;

  /** The paged banner's current message index (0-based). */
  protected readonly index = signal(0);

  /** The paged banner's current message. */
  protected readonly current = computed(() => MESSAGES[this.index()]);

  /** What the banners emitted, in order. */
  protected readonly events = signal<readonly string[]>([]);

  /** Moves the paged banner by `delta`, wrapping around. */
  protected step(delta: number): void {
    this.index.set((this.index() + delta + MESSAGES.length) % MESSAGES.length);
  }

  /** Records an event for the log line. */
  protected log(event: string): void {
    this.events.set([...this.events(), event]);
  }
}
