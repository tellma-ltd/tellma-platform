// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, inject, InjectionToken, Injector, signal } from '@angular/core';
import { TmAppVersion, TmLaunchCache, TmLocalization, TmServerText, ɵTmClientMetadataStore } from '@tellma/core';
import { TmTheme } from '@tellma/core-ui';
import { TmAlert } from '@tellma/core-ui/alert';
import { TmButton } from '@tellma/core-ui/button';
import { TmCopyButton } from '@tellma/core-ui/copy';
import { TmDescriptionList } from '@tellma/core-ui/description-list';

// The About dialog (spec 0027 §11.1), a deferred chunk the session menu's
// About row opens. Top to bottom: the wordmark for the theme; the
// distribution's own description when its server text has one; the versions
// of the UI (copyable), the server, the platform and each feature but core,
// as mono values in left-to-right isolates; an alert with Reload when the
// server runs a newer build; then the copyright and the legal links. It
// reads only local data, so it works offline. The Diagnostics tab joins it
// once the support entry point lands.

/** The scope of the About dialog's strings under `tm`. */
const SCOPE = 'aboutDialog';

/** The About dialog's English strings, under `tm.aboutDialog`. */
export const TM_ABOUT_DIALOG_STRINGS_EN = {
  /** The copy button's name. */
  copy: 'Copy version',
  server: 'Server',
  platform: 'Platform',
  /** A version that is not known. */
  unknown: '—',
  /** The alert while the server runs a newer build. */
  serverNewer: 'The server runs {server}. Reload to update.',
  /** Reload's accessible name while it gets the update, its failure, and the way past a worker that keeps failing. */
  updating: 'Getting the update…',
  updateFailed: "Couldn't download the update. Check your connection and try again.",
  reloadAnyway: 'Reload anyway',
  terms: 'Terms of service',
  privacy: 'Privacy',
} as const;

/** Loads the error views, whose Reload the alert shares. Internal: a test replaces it. */
export const TM_ABOUT_ERROR_VIEWS = new InjectionToken<() => Promise<typeof import('@tellma/core/errors')>>('TM_ABOUT_ERROR_VIEWS', {
  providedIn: 'root',
  factory: () => () => import('@tellma/core/errors'),
});

/** The About dialog: the wordmark, the description, the versions, Reload when the server is newer, and the legal lines. */
@Component({
  selector: 'tm-about-dialog',
  imports: [TmAlert, TmButton, TmCopyButton, TmDescriptionList],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The wordmark, for the theme. -->
    <img class="tm-about-dialog__wordmark" [src]="wordmark()" [alt]="displayName()" />
    <!-- The distribution's own description, when its server text has one. -->
    @if (description(); as description) {
      <p class="tm-about-dialog__description">{{ description }}</p>
    }
    <!-- The versions. -->
    <dl tmDescriptionList class="tm-about-dialog__versions">
      <div>
        <dt>{{ displayName() }}</dt>
        <dd>
          <span class="tm-about-dialog__mono" dir="ltr">{{ label }}</span>
          <tm-copy-button size="sm" [text]="ui" [label]="copyLabel()" />
        </dd>
      </div>
      <div>
        <dt>{{ serverLabel() }}</dt>
        <dd><span class="tm-about-dialog__mono" dir="ltr">{{ server() }}</span></dd>
      </div>
      <div>
        <dt>{{ platformLabel() }}</dt>
        <dd><span class="tm-about-dialog__mono" dir="ltr">{{ platform }}</span></dd>
      </div>
      @for (feature of features; track feature.key) {
        <div>
          <dt><span dir="ltr">{{ feature.key }}</span></dt>
          <dd><span class="tm-about-dialog__mono" dir="ltr">{{ feature.version }}</span></dd>
        </div>
      }
    </dl>
    <!-- The server runs a newer build: Reload, which turns busy, says when it fails, and offers Reload anyway. -->
    @if (serverNewer()) {
      <tm-alert kind="info" class="tm-about-dialog__newer">
        <p class="tm-about-dialog__alert-text">{{ newerMessage() }}</p>
        <span class="tm-about-dialog__actions">
          <button tmButton size="sm" type="button" [pending]="pending()" [attr.aria-label]="pending() ? updatingLabel() : null" (click)="reload()">
            {{ reloadLabel() }}
          </button>
          @if (failures() > 1) {
            <button tmButton size="sm" type="button" variant="ghost" (click)="reloadAnyway()">{{ reloadAnywayLabel() }}</button>
          }
        </span>
      </tm-alert>
    }
    <!-- The copyright and the legal links, each omitted when absent. -->
    @if (copyright(); as copyright) {
      <p class="tm-about-dialog__legal">{{ copyright }}</p>
    }
    @if (links(); as links) {
      <p class="tm-about-dialog__legal tm-about-dialog__links">
        @if (links.terms; as terms) {
          <a [href]="terms" target="_blank" rel="noopener">{{ termsLabel() }}</a>
        }
        @if (links.privacy; as privacy) {
          <a [href]="privacy" target="_blank" rel="noopener">{{ privacyLabel() }}</a>
        }
      </p>
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-4);
    }

    .tm-about-dialog__wordmark {
      display: block;
      block-size: 20px;
      inline-size: auto;
    }

    .tm-about-dialog__description,
    .tm-about-dialog__alert-text,
    .tm-about-dialog__legal {
      margin: 0;
    }

    .tm-about-dialog__versions dd {
      display: flex;
      align-items: center;
      gap: var(--space-2);
    }

    .tm-about-dialog__mono {
      font-family: var(--font-mono);
      unicode-bidi: isolate;
    }

    .tm-about-dialog__actions {
      display: flex;
      gap: var(--space-2);
      margin-block-start: var(--space-2);
    }

    .tm-about-dialog__legal {
      color: var(--text-muted);
      font-size: var(--text-xs);
    }

    .tm-about-dialog__links {
      display: flex;
      gap: var(--space-4);
    }
  `,
})
export class TmAboutDialog {
  readonly #l10n = inject(TmLocalization);
  readonly #version = inject(TmAppVersion);
  readonly #cache = inject(TmLaunchCache);
  readonly #serverText = inject(TmServerText);
  readonly #metadata = inject(ɵTmClientMetadataStore);
  readonly #theme = inject(TmTheme);
  readonly #injector = inject(Injector);
  readonly #errorViews = inject(TM_ABOUT_ERROR_VIEWS);
  readonly #pending = signal(false);
  readonly #failures = signal(0);

  /** The distribution's display name. */
  protected readonly displayName = this.#l10n.displayName;

  /** The UI version with its abbreviated commit, such as `1.4.0 (3f9c2ab)`. */
  protected readonly label = this.#version.label;

  /** The full UI version, which the copy button copies. */
  protected readonly ui = this.#version.ui;

  /** The platform's version. */
  protected readonly platform = this.#version.platform;

  /** Each composed feature's version but core's. */
  protected readonly features = this.#version.features.filter((feature) => feature.key !== 'core');

  /** The wordmark's file for the effective theme. */
  protected readonly wordmark = computed(() => `brand/wordmark-on-${this.#theme.effective() === 'dark' ? 'dark' : 'light'}.svg`);

  /** The distribution's own description, or `null` without its server key. */
  protected readonly description = computed(() =>
    this.#serverText.has('Distribution_About') ? this.#serverText.message('Distribution_About')() : null,
  );

  /** The server's version: the last build a response reported, else the cached distribution version, else a dash. */
  protected readonly server = computed(
    () => this.#version.server() ?? this.#cache.distribution()?.distributionVersion ?? this.#text('unknown'),
  );

  /** Whether the server runs a newer build than this UI. */
  protected readonly serverNewer = this.#version.serverNewer;

  /** The alert's message: the server's build, or the failed download. */
  protected readonly newerMessage = computed(() =>
    this.#failures() > 0 ? this.#text('updateFailed') : this.#text('serverNewer', { server: this.#version.server() ?? '' }),
  );

  /** The copyright: the distribution's own localized line, else the Core chunk's. */
  protected readonly copyright = computed(() =>
    this.#serverText.has('Distribution_Copyright')
      ? this.#serverText.message('Distribution_Copyright')()
      : (this.#metadata.current()?.distribution.copyright ?? null),
  );

  /** The legal links, or `null` while the distribution document has none. */
  protected readonly links = computed(() => {
    const links = this.#cache.distribution()?.links;
    return links?.terms || links?.privacy ? links : null;
  });

  /** Whether Reload is getting the update. */
  protected readonly pending = this.#pending.asReadonly();

  /** How many times Reload failed to download the update. */
  protected readonly failures = this.#failures.asReadonly();

  /** The copy button's name. */
  protected readonly copyLabel = this.#l10n.ɵtext('tm.aboutDialog.copy');

  /** The Server term. */
  protected readonly serverLabel = this.#l10n.ɵtext('tm.aboutDialog.server');

  /** The Platform term. */
  protected readonly platformLabel = this.#l10n.ɵtext('tm.aboutDialog.platform');

  /** Reload's label. */
  protected readonly reloadLabel = this.#l10n.ɵtext('tm.errors.reload');

  /** Reload's accessible name while it gets the update. */
  protected readonly updatingLabel = this.#l10n.ɵtext('tm.aboutDialog.updating');

  /** Reload anyway's label. */
  protected readonly reloadAnywayLabel = this.#l10n.ɵtext('tm.aboutDialog.reloadAnyway');

  /** The terms link's text. */
  protected readonly termsLabel = this.#l10n.ɵtext('tm.aboutDialog.terms');

  /** The privacy link's text. */
  protected readonly privacyLabel = this.#l10n.ɵtext('tm.aboutDialog.privacy');

  /** Creates the dialog, which registers its strings. */
  constructor() {
    this.#l10n.ɵregisterStrings(SCOPE, TM_ABOUT_DIALOG_STRINGS_EN);
  }

  /**
   * Reload, as the error views run it: the button turns busy while the new
   * version downloads, and a failed download keeps the alert and says so in
   * its message; from the second failure the alert offers Reload anyway.
   */
  protected async reload(): Promise<void> {
    this.#pending.set(true);
    let result: string;
    try {
      const { ɵTmErrorUpdates } = await this.#errorViews();
      result = await this.#injector.get(ɵTmErrorUpdates).update();
    } catch {
      // The error views did not load, so the update cannot be asked for.
      result = 'unreachable';
    }
    // On `ready` and `disabled` the page is reloading already.
    if (result !== 'ready' && result !== 'disabled') {
      this.#pending.set(false);
      this.#failures.update((failures) => failures + 1);
    }
  }

  /** Reloads as the browser would: a degraded worker sends the load to the network. */
  protected async reloadAnyway(): Promise<void> {
    const { ɵTmErrorUpdates } = await this.#errorViews();
    this.#injector.get(ɵTmErrorUpdates).reload();
  }

  /** A string of the dialog. */
  #text(key: string, params?: Readonly<Record<string, unknown>>): string {
    return this.#l10n.ɵtext(`tm.aboutDialog.${key}`, params)();
  }
}
