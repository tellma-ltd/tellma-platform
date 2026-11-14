// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, signal, viewChild, type TemplateRef } from '@angular/core';
import { Languages } from 'lucide';

import { TmButton } from '@tellma/core-ui/button';
import {
  TmMenu,
  TmMenuHeaderDef,
  TmMenuTrigger,
  type TmMenuEntry,
  type TmMenuItem,
} from '@tellma/core-ui/menu';

/** One language the demo session menu offers. */
interface DemoLanguage {
  /** The BCP 47 tag, set as `lang` on the row. */
  readonly code: string;
  /** The language's own name for itself. */
  readonly nativeName: string;
}

const LANGUAGES: readonly DemoLanguage[] = [
  { code: 'en', nativeName: 'English' },
  { code: 'ar', nativeName: 'العربية' },
  { code: 'fr', nativeName: 'Français' },
];

/**
 * The rich data-driven menu, shaped like a session menu: a trigger button,
 * a header block, a submenu of radio items, a labelled group, checkbox
 * items that keep the menu open, links (routed in place, plain, and opening
 * a new tab), entries disabled with a reason, all three icon forms, key-cap
 * shortcuts, details and descriptions. Playwright battery + visual
 * verification.
 */
@Component({
  imports: [TmButton, TmMenu, TmMenuHeaderDef, TmMenuTrigger],
  template: `
    <h2>Menu (rich entries)</h2>

    <div class="stack">
      <!-- The menu button: click, Enter, Space, ArrowDown and ArrowUp open it. -->
      <button
        tmButton
        variant="secondary"
        data-testid="menu-rich-trigger"
        [tmMenuTriggerFor]="menu"
      >
        Rana Haddad
      </button>

      <!-- What the menu did, for the battery to read. -->
      <dl class="state">
        <dt>Language</dt>
        <dd data-testid="menu-rich-language">{{ language() }}</dd>
        <dt>Density</dt>
        <dd data-testid="menu-rich-density">{{ density() }}</dd>
        <dt>Line numbers</dt>
        <dd data-testid="menu-rich-line-numbers">{{ lineNumbers() }}</dd>
        <dt>Last action</dt>
        <dd data-testid="menu-rich-last">{{ last() }}</dd>
        <dt>Items selected</dt>
        <dd data-testid="menu-rich-selected">{{ selected() }}</dd>
      </dl>

      <p id="menu-rich-anchor" data-testid="menu-rich-anchor">The plain link scrolls here.</p>
    </div>

    <!-- The template icon form: any markup, drawn inside the row's icon box. -->
    <ng-template #dotIcon>
      <svg viewBox="0 0 16 16" aria-hidden="true">
        <circle cx="8" cy="8" r="4" fill="currentColor" />
      </svg>
    </ng-template>

    <tm-menu #menu [items]="items()" aria-label="Session" (itemSelected)="countSelected()">
      <ng-template tmMenuHeader>
        <div class="who" data-testid="menu-rich-header">
          <strong>Rana Haddad</strong>
          <span>rana&#64;example.com</span>
        </div>
      </ng-template>
    </tm-menu>
  `,
  styles: `
    .stack {
      display: grid;
      gap: 16px;
      max-inline-size: 420px;
      justify-items: start;
    }
    .state {
      display: grid;
      grid-template-columns: auto 1fr;
      gap: 4px 16px;
      margin: 0;
    }
    .state dd {
      margin: 0;
    }
    .who {
      display: grid;
      gap: 2px;
    }
  `,
})
export class MenuRichStory {
  private readonly dotIcon = viewChild<TemplateRef<void>>('dotIcon');

  /**
   * This page's own address. A bare "#fragment" resolves against the
   * document's base address, which is the site root, so the links that stay
   * on this page spell out its path.
   */
  private readonly here = `${location.pathname}${location.search}`;

  /** The chosen language code. */
  readonly language = signal('en');
  /** The chosen density. */
  readonly density = signal<'compact' | 'comfortable'>('compact');
  /** Whether line numbers show. */
  readonly lineNumbers = signal(false);
  /** The id of the last entry whose action ran. */
  readonly last = signal('none');
  /** How many times the menu reported an item (links and parents never count). */
  readonly selected = signal(0);

  readonly items = computed<readonly TmMenuEntry[]>(() => [
    // A submenu whose detail shows the current choice; its rows are radio
    // items in a group, each labelled in its own language.
    {
      id: 'language',
      label: 'Language',
      icon: Languages, // icon data, bypassing the registry
      detail: LANGUAGES.find((l) => l.code === this.language())?.nativeName,
      items: [
        {
          group: true,
          items: LANGUAGES.map((l): TmMenuItem => ({
            id: `language-${l.code}`,
            label: l.nativeName,
            lang: l.code,
            role: 'radio',
            checked: l.code === this.language(),
            action: () => this.run(`language-${l.code}`, () => this.language.set(l.code)),
          })),
        },
        { separator: true },
        { id: 'language-more', label: 'More languages', href: `${this.here}#menu-rich-anchor` },
      ],
    },
    // A labelled group of radio items that keep the menu open.
    {
      group: true,
      label: 'Density',
      items: (['compact', 'comfortable'] as const).map((density): TmMenuItem => ({
        id: `density-${density}`,
        label: density === 'compact' ? 'Compact' : 'Comfortable',
        role: 'radio',
        checked: this.density() === density,
        keepOpen: true,
        action: () => this.run(`density-${density}`, () => this.density.set(density)),
      })),
    },
    { separator: true },
    // A checkbox item with a shortcut hint.
    {
      id: 'line-numbers',
      label: 'Line numbers',
      role: 'checkbox',
      checked: this.lineNumbers(),
      keepOpen: true,
      shortcut: 'Mod+Shift+L',
      action: () => this.run('line-numbers', () => this.lineNumbers.update((on) => !on)),
    },
    { separator: true },
    // Links: routed in place, followed natively, and opening a new tab.
    {
      id: 'settings',
      label: 'My settings',
      icon: this.dotIcon(), // a template
      href: `${this.here}#my-settings`,
      action: () => this.run('settings'),
    },
    { id: 'jump', label: 'Jump to the note', href: `${this.here}#menu-rich-anchor` },
    {
      id: 'docs',
      label: 'Documentation',
      href: 'https://example.com/docs',
      target: '_blank',
    },
    { separator: true },
    // Disabled with a reason: an item, and a submenu that never opens.
    {
      id: 'close-period',
      label: 'Close period',
      description: 'Replaced by the reason below',
      disabledReason: 'Only an administrator can close a period',
      action: () => this.run('close-period'),
    },
    {
      id: 'export',
      label: 'Export',
      disabledReason: 'There is nothing to export yet',
      items: [{ id: 'export-csv', label: 'CSV', action: () => this.run('export-csv') }],
    },
    { separator: true },
    {
      id: 'about',
      label: 'About',
      detail: 'v2.14.0',
      description: 'Version and licences',
      action: () => this.run('about'),
    },
    {
      id: 'sign-out',
      label: 'Sign out',
      icon: 'log-out', // a registry name
      shortcut: 'Mod+Shift+Q',
      action: () => this.run('sign-out'),
    },
  ]);

  /** Counts the items the menu reports. */
  protected countSelected(): void {
    this.selected.update((count) => count + 1);
  }

  /** Records which entry ran, then runs its effect. */
  private run(id: string, effect?: () => void): void {
    this.last.set(id);
    effect?.();
  }
}
