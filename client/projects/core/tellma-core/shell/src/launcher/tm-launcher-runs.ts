// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type Signal, untracked, type WritableSignal } from '@angular/core';
import type { TmLauncherItem, TmLauncherProvider, TmLocalization, TmUrls } from '@tellma/core';
import { tmSearchNormalize } from '@tellma/core/search-normalize';

// The launcher's provider runs (spec 0027 §9.6), a deferred chunk of their
// own that the panel loads only when the app registers a provider. The
// providers run on the edges of a burst of typing, as TmSearch's search
// output times them: at once on the first change after a pause, then once
// the burst ends. The next keystroke aborts the run in flight at once, and
// an IME composition or a held key holds them until it ends. Each answer becomes a bucket, the
// buckets kept in the providers' order.

/** How long a burst of typing waits for its next key before the providers run again. */
const BURST_MS = 50;

/** A provider's answer, as a bucket shows it. */
export interface TmLauncherBucket {
  /** The provider. */
  readonly provider: TmLauncherProvider;
  /** Its items, at most the context's limit. */
  readonly items: readonly TmLauncherItem[];
}

/** What the runs read from the panel and write to it. */
export interface TmLauncherRunsHost {
  /** The launcher's text. */
  readonly query: Signal<string>;
  /** Whether an IME composition is in progress in the input. */
  readonly composing: Signal<boolean>;
  /** The buckets the typed state shows. */
  readonly buckets: WritableSignal<readonly TmLauncherBucket[]>;
  /** The localization service, for the language and the buckets' headings. */
  readonly l10n: TmLocalization;
  /** The URLs service, for the routed tenant. */
  readonly urls: TmUrls;
  /**
   * Hears of a bucket that arrived, to announce it once the results were.
   *
   * @param bucket - The bucket.
   */
  answered(bucket: TmLauncherBucket): void;
}

/** The launcher's providers, run on the edges of each burst of typing while it is open. */
export class TmLauncherRuns {
  readonly #providers: readonly TmLauncherProvider[];
  readonly #host: TmLauncherRunsHost;
  #abort: AbortController | null = null;
  // The text the providers last ran on, or `null` when the next change runs them whatever the text.
  #lastRun: string | null = null;
  #trailing: ReturnType<typeof setTimeout> | undefined;
  #holding = false;

  /**
   * Creates the runs.
   *
   * @param providers - The providers, in their order.
   * @param host - The panel.
   */
  constructor(providers: readonly TmLauncherProvider[], host: TmLauncherRunsHost) {
    this.#providers = providers;
    this.#host = host;
  }

  /** Answers a change of the text: the leading edge at once, the trailing one when the burst ends. */
  changed(): void {
    if (untracked(this.#host.composing) || this.#holding) {
      return;
    }
    // The keystroke supersedes the run in flight, and its answers are dropped (spec 0027 §9.6).
    if (untracked(this.#host.query).trim() !== this.#lastRun) {
      this.#abort?.abort();
      this.#abort = null;
      this.#lastRun = null;
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

  /** Holds the runs while a key repeats. */
  hold(): void {
    this.#holding = true;
  }

  /** The release of a held key runs the providers on what the hold typed. */
  release(): void {
    if (this.#holding) {
      this.#holding = false;
      this.#flush();
    }
  }

  /** Forgets the last run, so the next change runs on the text even when it is the same. */
  restart(): void {
    this.#lastRun = null;
  }

  /** Stops what runs: the run in flight, the pending trailing edge and the hold. */
  stop(): void {
    this.#abort?.abort();
    this.#abort = null;
    clearTimeout(this.#trailing);
    this.#trailing = undefined;
    this.#holding = false;
  }

  /**
   * A bucket's announcement: "{bucket}: {n} results".
   *
   * @param bucket - The bucket.
   * @returns The text.
   */
  text(bucket: TmLauncherBucket): string {
    const l10n = this.#host.l10n;
    return l10n.ɵtext('tm.launcher.bucketCount', { bucket: l10n.ɵtext(bucket.provider.labelKey)(), count: bucket.items.length })();
  }

  /** Runs the providers on the text, unless they last ran on the same text, or a composition or a held key holds them. */
  #flush(): void {
    // The composition's end and the key's release flush in their turn.
    if (untracked(this.#host.composing) || this.#holding) {
      return;
    }
    const query = untracked(this.#host.query).trim();
    if (query === this.#lastRun) {
      return;
    }
    this.#lastRun = query;
    this.#abort?.abort();
    this.#host.buckets.set([]);
    const tenantId = untracked(this.#host.urls.tenantId);
    const folded = tmSearchNormalize(query).length;
    if (tenantId === null || query === '') {
      this.#abort = null;
      return;
    }
    const abort = new AbortController();
    this.#abort = abort;
    const context = { tenantId, language: untracked(this.#host.l10n.language), limit: 5, signal: abort.signal };
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
            this.#host.buckets.update((buckets) =>
              [...buckets, bucket].sort((a, b) => this.#providers.indexOf(a.provider) - this.#providers.indexOf(b.provider)),
            );
            this.#host.answered(bucket);
          },
          () => undefined,
        );
    }
  }
}
