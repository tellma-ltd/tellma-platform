// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  computed,
  DestroyRef,
  DOCUMENT,
  inject,
  Injectable,
  InjectionToken,
  signal,
  untracked,
  type Signal,
  type WritableSignal,
} from '@angular/core';

/**
 * The user's theme choice. `system` follows the operating system's
 * light/dark preference live; `light` and `dark` pin one scheme.
 */
export type TmThemeSetting = 'system' | 'light' | 'dark';

/** Options for the theme, passed as `provideTellmaUi({ theme })`. */
export interface TmThemeOptions {
  /**
   * The setting the theme starts from — a value, or a function read once
   * when the theme is constructed (typically reading the app's own stored
   * choice). Default `'system'`.
   */
  readonly initial?: TmThemeSetting | (() => TmThemeSetting);
}

/**
 * The theme options `provideTellmaUi({ theme })` registered, read by
 * {@link TmTheme} at construction. Internal to the library.
 */
export const ɵTM_THEME_OPTIONS = new InjectionToken<TmThemeOptions>('ɵTM_THEME_OPTIONS');

/** The OS preference query the `system` setting follows. */
const DARK_QUERY = '(prefers-color-scheme: dark)';

/**
 * Resolves the System, Light or Dark choice to a scheme and paints it on
 * the page: `<html data-theme>` always holds `light` or `dark`, never
 * `system`, so the emitted token stylesheet stays two-state. The tokens'
 * `color-scheme` declarations ride the same attribute, so native scrollbars,
 * form controls and autofill follow every switch.
 *
 * The library never persists the choice. The app seeds it (through
 * `provideTellmaUi({ theme: { initial } })`) and stores changes itself, by
 * reading {@link TmTheme.setting} in an effect.
 *
 * Under `system`, one `matchMedia('(prefers-color-scheme: dark)')` listener
 * follows the OS preference; under `light` or `dark` the listener is
 * detached. Forced-colors mode is untouched.
 *
 * Created on first inject with `system` when the app passed no theme
 * options. With `provideTellmaUi({ theme })`, an environment initializer
 * creates it at boot, so OS changes are followed from the first frame.
 *
 * @example
 * ```ts
 * // app.config.ts — the seed equals what the boot script painted, so nothing flashes.
 * provideTellmaUi({ theme: { initial: () => storedTheme() ?? 'system' } });
 *
 * // a settings toggle — repaints in the click's own frame.
 * inject(TmTheme).set('dark');
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmTheme {
  /** The user's choice, as last passed to {@link TmTheme.set} or seeded at startup. */
  readonly setting: Signal<TmThemeSetting>;

  /** The scheme on screen: the setting itself, or the OS preference under `system`. */
  readonly effective: Signal<'light' | 'dark'>;

  private readonly state: WritableSignal<TmThemeSetting>;
  /** Re-applies the current setting: the OS listener, then the attribute. */
  private readonly apply: () => void;

  constructor() {
    const document = inject(DOCUMENT);
    const root = document.documentElement;
    const view = document.defaultView;
    // The OS preference list; null without a window to ask.
    const query = typeof view?.matchMedia === 'function' ? view.matchMedia(DARK_QUERY) : null;
    const initial = inject(ɵTM_THEME_OPTIONS, { optional: true })?.initial ?? 'system';
    const state = signal<TmThemeSetting>(typeof initial === 'function' ? initial() : initial);
    // The OS preference, refreshed only while the setting is `system`.
    const osDark = signal(false);
    const effective = computed(() => {
      const setting = state();
      return setting === 'system' ? (osDark() ? 'dark' : 'light') : setting;
    });

    // Writes the effective scheme to <html data-theme>. An unchanged value is
    // never rewritten, so a seed that equals what the boot script painted
    // costs no style recalculation.
    const paint = (): void => {
      const scheme = effective();
      if (root.getAttribute('data-theme') !== scheme) {
        root.setAttribute('data-theme', scheme);
      }
    };
    const onOsChange = (event: MediaQueryListEvent): void => {
      osDark.set(event.matches);
      paint();
    };
    // Attaches or detaches the one OS listener; never stacks a second.
    let listening = false;
    const listen = (on: boolean): void => {
      if (query !== null && on !== listening) {
        listening = on;
        if (on) {
          query.addEventListener('change', onOsChange);
        } else {
          query.removeEventListener('change', onOsChange);
        }
      }
    };

    this.state = state;
    this.setting = state.asReadonly();
    this.effective = effective;
    // Untracked: an app effect that calls set() (seeding from a URL or a
    // store) must not come to depend on the theme's own state, or any later
    // set() elsewhere would re-run that effect and undo itself.
    this.apply = () =>
      untracked(() => {
        const system = state() === 'system';
        // Switching to system reads the preference afresh: it may have
        // changed while no listener was attached.
        if (system && query !== null) {
          osDark.set(query.matches);
        }
        listen(system);
        paint();
      });
    inject(DestroyRef).onDestroy(() => listen(false));
    this.apply();
  }

  /**
   * Switches the theme and repaints synchronously: `<html data-theme>` is
   * written before this returns, so a theme click repaints in its own frame.
   */
  set(setting: TmThemeSetting): void {
    this.state.set(setting);
    this.apply();
  }
}
