// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, TemplateRef, viewChild, type Signal } from '@angular/core';

/** The named icon templates of the grid's built-in context-menu items. */
export interface ɵTmGridIconTemplates {
  /** Cut (scissors). */
  readonly cut: TemplateRef<void>;
  /** Copy (overlapping sheets). */
  readonly copy: TemplateRef<void>;
  /** Copy with headers (copy + plus). */
  readonly copyPlus: TemplateRef<void>;
  /** Paste (clipboard). */
  readonly clipboard: TemplateRef<void>;
  /** Insert rows (list + plus). */
  readonly listPlus: TemplateRef<void>;
  /** Delete rows (list − minus). */
  readonly listMinus: TemplateRef<void>;
}

/**
 * A renderless holder of the built-in context-menu icon templates:
 * Lucide-derived inline SVGs (spec-0002 static-glyph posture — no icon
 * font, no registry), each `aria-hidden` and stroked in `currentColor`.
 * The shared view instantiates it once and hands the template map to the
 * composition root's menu builder.
 */
@Component({
  selector: 'tm-grid-icons',
  template: `
    <ng-template #cut>
      <svg class="tm-grid__menu-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
        <circle cx="6" cy="6" r="3" />
        <path d="M8.12 8.12 12 12" />
        <path d="M20 4 8.12 15.88" />
        <circle cx="6" cy="18" r="3" />
        <path d="M14.8 14.8 20 20" />
      </svg>
    </ng-template>
    <ng-template #copy>
      <svg class="tm-grid__menu-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
        <rect width="14" height="14" x="8" y="8" rx="2" ry="2" />
        <path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2" />
      </svg>
    </ng-template>
    <ng-template #copyPlus>
      <svg class="tm-grid__menu-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
        <path d="M15 12v6" />
        <path d="M12 15h6" />
        <rect width="14" height="14" x="8" y="8" rx="2" ry="2" />
        <path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2" />
      </svg>
    </ng-template>
    <ng-template #clipboard>
      <svg class="tm-grid__menu-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
        <rect width="8" height="4" x="8" y="2" rx="1" ry="1" />
        <path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2" />
      </svg>
    </ng-template>
    <ng-template #listPlus>
      <svg class="tm-grid__menu-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
        <path d="M11 12H3" />
        <path d="M16 6H3" />
        <path d="M16 18H3" />
        <path d="M18 9v6" />
        <path d="M15 12h6" />
      </svg>
    </ng-template>
    <ng-template #listMinus>
      <svg class="tm-grid__menu-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
        <path d="M11 12H3" />
        <path d="M16 6H3" />
        <path d="M16 18H3" />
        <path d="M21 12h-6" />
      </svg>
    </ng-template>
  `,
  styles: `
    .tm-grid__menu-icon {
      display: block;
      inline-size: var(--menu-icon-size);
      block-size: var(--menu-icon-size);
    }
  `,
  host: { class: 'tm-grid-icons' },
})
export class ɵTmGridIcons {
  private readonly cut = viewChild.required('cut', { read: TemplateRef });
  private readonly copy = viewChild.required('copy', { read: TemplateRef });
  private readonly copyPlus = viewChild.required('copyPlus', { read: TemplateRef });
  private readonly clipboard = viewChild.required('clipboard', { read: TemplateRef });
  private readonly listPlus = viewChild.required('listPlus', { read: TemplateRef });
  private readonly listMinus = viewChild.required('listMinus', { read: TemplateRef });

  /** The named templates, resolvable once this component has rendered. */
  readonly templates: Signal<ɵTmGridIconTemplates> = computed(() => ({
    cut: this.cut() as TemplateRef<void>,
    copy: this.copy() as TemplateRef<void>,
    copyPlus: this.copyPlus() as TemplateRef<void>,
    clipboard: this.clipboard() as TemplateRef<void>,
    listPlus: this.listPlus() as TemplateRef<void>,
    listMinus: this.listMinus() as TemplateRef<void>,
  }));
}
