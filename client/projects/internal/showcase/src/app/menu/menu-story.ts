// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, signal, viewChild, type TemplateRef } from '@angular/core';

import { TmContextMenuTrigger, TmMenu, type TmMenuEntry } from '@tellma/core-ui/menu';

/** tm-menu demo host — Playwright battery + visual verification. */
@Component({
  imports: [TmMenu, TmContextMenuTrigger],
  template: `
    <h2>Menu</h2>

    <div class="stack">
      <button
        type="button"
        #trigger
        data-testid="menu-trigger-button"
        (click)="menu.open(trigger, { restoreFocus: trigger })"
      >
        Options
      </button>

      <div
        class="context-area"
        tabindex="0"
        data-testid="menu-context-area"
        [tmContextMenuTrigger]="menu"
      >
        Right-click, long-press, or press Shift+F10 here for the context menu.
      </div>

      <p>Actions run: <span data-testid="menu-action-count">{{ actionCount() }}</span></p>
    </div>

    <ng-template #plusIcon>
      <svg viewBox="0 0 16 16" fill="none" aria-hidden="true">
        <path d="M8 3v10M3 8h10" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" />
      </svg>
    </ng-template>

    <tm-menu #menu [items]="items()" aria-label="Demo actions" />
  `,
  styles: `
    .stack {
      display: grid;
      gap: 16px;
      max-inline-size: 420px;
      justify-items: start;
    }
    .context-area {
      inline-size: 100%;
      padding: 32px 16px;
      border: 1px dashed #a8b7bc;
      border-radius: 4px;
      user-select: none;
    }
  `,
})
export class MenuStory {
  private readonly plusIcon = viewChild<TemplateRef<void>>('plusIcon');

  readonly actionCount = signal(0);

  readonly items = computed<readonly TmMenuEntry[]>(() => [
    {
      id: 'increment',
      label: 'Increment',
      icon: this.plusIcon(),
      action: () => this.actionCount.update((n) => n + 1),
    },
    { id: 'duplicate', label: 'Duplicate', action: () => this.actionCount.update((n) => n + 1) },
    { separator: true },
    {
      id: 'unavailable',
      label: 'Unavailable',
      disabled: true,
      action: () => this.actionCount.update((n) => n + 1),
    },
    // Resolved through the library's i18n seam -> 'Select an option' (EN).
    { id: 'localized', labelKey: 'select.placeholder', action: () => this.actionCount.update((n) => n + 1) },
  ]);
}
