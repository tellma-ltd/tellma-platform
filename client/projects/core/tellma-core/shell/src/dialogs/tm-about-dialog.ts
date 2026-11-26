// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet, NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, InjectionToken, Injector, input, signal, type Type } from '@angular/core';
import { TmAppVersion, TmLaunchCache, TmLocalization, TmServerText, TmTelemetry, ɵTmClientMetadataStore } from '@tellma/core';
import { TmTheme } from '@tellma/core-ui';
import { TmAlert } from '@tellma/core-ui/alert';
import { TmButton } from '@tellma/core-ui/button';
import { TmCopyButton } from '@tellma/core-ui/copy';
import { TmDescriptionList } from '@tellma/core-ui/description-list';
import { TmTab, TmTabContent, TmTabGroup } from '@tellma/core-ui/tabs';

import type { TmShellSupport } from '../tm-shell-support';

// The About dialog (spec 0027 §11.1), a deferred chunk the session menu's
// About row opens. Top to bottom: the wordmark for the theme; the
// distribution's own description when its server text has one; the versions
// of the UI (copyable), the server, the platform and each feature but core,
// as mono values in left-to-right isolates; an alert with Reload when the
// server runs a newer build; then the copyright and the legal links. It
// reads only local data, so it works offline. That content is the About
// tab of a two-tab group; the Diagnostics tab imports the Diagnostics panel
// from @tellma/core/support on its first selection (spec 0026 §8.4), and when
// that entry point cannot load it shows the offline line that names Copy
// diagnostics' shortcut, from the support host's chunk, which the startup
// preloads at idle; a later selection tries the panel again. Until the panel
// is in, its place keeps the panel's height, so the first selection grows
// the dialog only once. The tabs' code
// is a static import of this chunk, not a deferred block of its own: a
// dynamic entry reaching only part of the CDK would split the CDK's code in
// the first frame into smaller chunks, each paying its own compression
// overhead. The overlays' host, which opens the dialog, hands it the support
// tools as an input, so this chunk shares no module with the overlays'.

/** The scope of the About dialog's strings under `tm`. */
const SCOPE = 'aboutDialog';

/** The About dialog's English strings, under `tm.aboutDialog`. */
export const TM_ABOUT_DIALOG_STRINGS_EN = {
  /** The copy button's name. */
  copy: 'Copy version',
  server: 'Server',
  platform: 'Platform',
  /** The second tab's label. */
  diagnostics: 'Diagnostics',
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

/** The Diagnostics tab's offline line's inputs. */
const OFFLINE_INPUTS: Readonly<Record<string, unknown>> = { kind: 'diagnostics' };

/**
 * The About dialog: an About tab with the wordmark, the description, the
 * versions, Reload when the server is newer, and the legal lines, and a
 * Diagnostics tab with the Diagnostics panel.
 */
@Component({
  selector: 'tm-about-dialog',
  imports: [NgComponentOutlet, NgTemplateOutlet, TmAlert, TmButton, TmCopyButton, TmDescriptionList, TmTab, TmTabContent, TmTabGroup],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <tm-tab-group (selectedIdChange)="selected($event)">
      <tm-tab id="about" [label]="aboutLabel()">
        <ng-template tmTabContent><ng-container [ngTemplateOutlet]="about" /></ng-template>
      </tm-tab>
      <tm-tab id="diagnostics" [label]="diagnosticsLabel()">
        <ng-template tmTabContent>
          <!-- The panel once loaded; else, as tall as the panel, the offline line once it failed, or the loading status. -->
          @if (diagnostics(); as diagnostics) {
            <ng-container [ngComponentOutlet]="diagnostics" />
          } @else {
            <div class="tm-about-dialog__pending">
              @if (offline(); as offline) {
                <ng-container [ngComponentOutlet]="offline" [ngComponentOutletInputs]="offlineInputs" />
              } @else if (failed()) {
                <p class="tm-about-dialog__status" role="alert">{{ failedLabel() }}</p>
              } @else {
                <p class="tm-about-dialog__status" role="status">{{ loadingLabel() }}</p>
              }
            </div>
          }
        </ng-template>
      </tm-tab>
    </tm-tab-group>
    <ng-template #about>
      <!-- The About tab's content. -->
      <div class="tm-about-dialog__about">
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
                <button
                  tmButton
                  size="sm"
                  type="button"
                  variant="ghost"
                  [pending]="anywayPending()"
                  [attr.aria-label]="anywayPending() ? updatingLabel() : null"
                  (click)="reloadAnyway()"
                >
                  {{ reloadAnywayLabel() }}
                </button>
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
      </div>
    </ng-template>
  `,
  styles: `
    :host {
      display: block;
    }

    .tm-about-dialog__about {
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

    .tm-about-dialog__status {
      margin: 0;
      color: var(--text-muted);
    }

    /* The Diagnostics tab before its panel: the panel's height, which the
       opener sets for the width and the language, so the first selection
       grows the dialog only once. */
    .tm-about-dialog__pending {
      min-block-size: var(--tm-about-dialog-diagnostics-height, auto);
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
  readonly #anywayPending = signal(false);
  readonly #failures = signal(0);
  readonly #telemetry = inject(TmTelemetry);
  readonly #diagnostics = signal<Type<unknown> | null>(null);
  readonly #offline = signal<Type<unknown> | null>(null);
  readonly #failed = signal(false);
  #loading = false;

  /** The support tools, which the Diagnostics tab loads its content through. */
  readonly support = input.required<TmShellSupport>();

  /** The Diagnostics panel, once loaded. */
  protected readonly diagnostics = this.#diagnostics.asReadonly();

  /** The offline line, once the panel failed to load. */
  protected readonly offline = this.#offline.asReadonly();

  /** Whether neither the panel nor the offline line could load. */
  protected readonly failed = this.#failed.asReadonly();

  /** The offline line's inputs: the Diagnostics tab's variant. */
  protected readonly offlineInputs = OFFLINE_INPUTS;

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

  /** Whether Reload anyway is loading the error views that run it. */
  protected readonly anywayPending = this.#anywayPending.asReadonly();

  /** How many times Reload, or Reload anyway, failed to get the update. */
  protected readonly failures = this.#failures.asReadonly();

  /** The About tab's label: the dialog's title. */
  protected readonly aboutLabel = this.#l10n.ɵtext('tm.shellOverlays.dialogs.about');

  /** The Diagnostics tab's label. */
  protected readonly diagnosticsLabel = this.#l10n.ɵtext('tm.aboutDialog.diagnostics');

  /** The Diagnostics tab's status while its content loads. */
  protected readonly loadingLabel = this.#l10n.ɵtext('tm.shellOverlays.dialogs.loading');

  /** The Diagnostics tab's line when neither its panel nor the offline line could load. */
  protected readonly failedLabel = this.#l10n.ɵtext('tm.shellOverlays.dialogs.failed');

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
   * Follows the tab selection: a selection of Diagnostics while its panel
   * is not in loads it. When the panel cannot load, the offline line shows
   * in its place, and the next selection tries the panel again.
   *
   * @param id - The selected tab's id.
   */
  protected selected(id: string | undefined): void {
    if (id !== 'diagnostics' || this.#diagnostics() !== null || this.#loading) {
      return;
    }
    this.#loading = true;
    const support = this.support();
    support
      .diagnostics()
      .then(
        (panel) => this.#diagnostics.set(panel),
        (error: unknown) => {
          // Offline, or after a deploy removed the chunk: the shortcut still copies the diagnostics.
          this.#telemetry.error(error, { where: 'tm.about.diagnostics' });
          if (this.#offline() !== null) {
            return undefined;
          }
          return support.offline().then(
            (line) => this.#offline.set(line),
            () => this.#failed.set(true),
          );
        },
      )
      .finally(() => (this.#loading = false));
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

  /**
   * Reloads as the browser would: a degraded worker sends the load to the
   * network. The button turns busy while the error views load; when they
   * cannot, it counts as one more failure, and the alert keeps saying the
   * update could not be downloaded, so the next press tries the import again.
   */
  protected async reloadAnyway(): Promise<void> {
    this.#anywayPending.set(true);
    try {
      const { ɵTmErrorUpdates } = await this.#errorViews();
      this.#injector.get(ɵTmErrorUpdates).reload();
    } catch {
      // The error views did not load (offline, or a deploy removed their chunk).
      this.#anywayPending.set(false);
      this.#failures.update((failures) => failures + 1);
    }
  }

  /** A string of the dialog. */
  #text(key: string, params?: Readonly<Record<string, unknown>>): string {
    return this.#l10n.ɵtext(`tm.aboutDialog.${key}`, params)();
  }
}
