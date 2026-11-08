// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, DOCUMENT, inject } from '@angular/core';

import { TmTheme, type TmThemeSetting } from '@tellma/core-ui';

/**
 * Theming demo/verification page (spec §4, DoD 9):
 * - swatches painted purely from token variables,
 * - the System / Light / Dark choice through `TmTheme` (the shell's
 *   light/dark toggle drives the same service),
 * - a runtime override input (documentElement.style.setProperty on
 *   --color-primary — the no-rebuild theming path).
 * The Playwright suite drives all three plus the @layer precedence check.
 */
@Component({
  template: `
    <h2>Tokens & theming</h2>

    <!-- The theme choice, through the library service a distribution's
         settings page calls. The readout shows what TmTheme resolved. -->
    <div class="theme-choice" role="group" aria-label="Theme">
      @for (option of options; track option.setting) {
        <button
          type="button"
          [attr.data-testid]="'theme-' + option.setting"
          [attr.aria-pressed]="theme.setting() === option.setting"
          (click)="theme.set(option.setting)"
        >
          {{ option.label }}
        </button>
      }
      <span data-testid="theme-readout"> {{ theme.setting() }} → {{ theme.effective() }} </span>
    </div>

    <div class="swatch-row">
      <div class="swatch primary" data-testid="swatch-primary">Primary action</div>
      <div class="swatch field" data-testid="swatch-field">Field surface</div>
      <div class="swatch page" data-testid="swatch-page">Page surface</div>
    </div>

    <p>
      <label>
        Primary color override:
        <input
          type="color"
          data-testid="primary-picker"
          value="#316e80"
          (input)="setPrimary($event)"
        />
      </label>
    </p>
  `,
  styles: `
    .theme-choice {
      display: flex;
      align-items: center;
      gap: var(--space-2);
    }
    .swatch-row {
      display: flex;
      gap: var(--space-3);
      margin-block: var(--space-4);
    }
    .swatch {
      display: grid;
      place-items: center;
      inline-size: 160px;
      block-size: 72px;
      border-radius: var(--radius-sm);
      font-size: var(--text-sm);
    }
    .swatch.primary {
      background: var(--color-primary);
      color: var(--color-on-primary);
    }
    .swatch.field {
      background: var(--field-bg);
      color: var(--field-text);
      border: 1px solid var(--field-border);
    }
    .swatch.page {
      background: var(--surface-page);
      color: var(--text-body);
      border: 1px solid var(--border-default);
    }
  `,
})
export class ThemingStory {
  private readonly document = inject(DOCUMENT);
  /** The library theme the choice drives. */
  protected readonly theme = inject(TmTheme);
  /** The three choices, labelled the way a settings page would. */
  protected readonly options: ReadonlyArray<{ setting: TmThemeSetting; label: string }> = [
    { setting: 'system', label: 'System' },
    { setting: 'light', label: 'Light' },
    { setting: 'dark', label: 'Dark' },
  ];

  protected setPrimary(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.document.documentElement.style.setProperty('--color-primary', value);
  }
}
