// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  inject,
  type Signal,
  type TemplateRef,
  ViewEncapsulation,
  type WritableSignal,
} from '@angular/core';
import { TmIcon } from '@tellma/core-ui/icon';
import { TM_MODAL_DATA } from '@tellma/core-ui/modal';
import Search from 'lucide/dist/esm/icons/search.mjs';
import X from 'lucide/dist/esm/icons/x.mjs';

import type { TmLauncher } from './tm-launcher';

// The launcher below md (spec 0027 §9.1): a TmModal of size lg labelled
// "Menu", full screen below sm, with a 44 px field beside Close above the
// panel's listbox. A deferred chunk of its own, which the panel loads once
// the viewport is below md, so a wide screen never loads it. The field is
// the panel's combobox input; its keys and text are the panel's. The panel
// opens the modal and matches its keys, so this chunk imports neither the
// modal service nor the shortcut grammar: an app's bundler then keeps those
// modules where the first frame and the panel already place them.

/** What the modal's content reads from the panel. */
export interface TmLauncherSheetHost {
  /** The launcher. */
  readonly launcher: TmLauncher;
  /** The listbox, as the panel renders it. */
  readonly body: Signal<TemplateRef<unknown>>;
  /** The listbox's id. */
  readonly listboxId: string;
  /** The hidden instructions' id. */
  readonly hintId: string;
  /** The active option's id, or `null`. */
  readonly activeId: Signal<string | null>;
  /** The field's name and placeholder. */
  readonly searchLabel: Signal<string>;
  /** The input's `aria-keyshortcuts`. */
  readonly keyShortcuts: string;
  /** What takes focus as the modal opens: Close after the Menu button, else the field. */
  readonly sheetFocus: Signal<'close' | 'field'>;
  /** Whether an IME composition is in progress in the input. */
  readonly composing: WritableSignal<boolean>;
  /**
   * A string of the launcher's.
   *
   * @param key - The key under `tm.launcher`.
   * @returns The text.
   */
  text(key: string): string;
  /**
   * The field's text, handed to the launcher.
   *
   * @param event - The input event.
   */
  typed(event: Event): void;
  /**
   * The combobox keys.
   *
   * @param event - The key.
   */
  keydown(event: KeyboardEvent): void;
  /** The release of a held key. */
  keyup(): void;
  /**
   * A key anywhere in the modal: Mod+K selects the field's text, as the
   * shortcut registry stands aside while a modal is open.
   *
   * @param event - The key.
   */
  sheetKeydown(event: KeyboardEvent): void;
  /** The end of a composition. */
  compositionEnd(): void;
}

/** The Menu modal's content: the field beside Close, then the panel's listbox. */
@Component({
  selector: 'tm-launcher-sheet',
  imports: [NgTemplateOutlet, TmIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // The content restyles the modal's own header and body, so the rules are
  // unencapsulated; every selector is scoped by a tm-launcher class.
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="tm-launcher__sheet" (keydown)="host.sheetKeydown($event)">
      <!-- The field beside Close. -->
      <div class="tm-launcher__sheet-head">
        <label class="tm-launcher__field">
          <tm-icon [svg]="searchIcon" />
          <input type="text" class="tm-launcher__sheet-input" autocomplete="off" spellcheck="false" dir="auto" [placeholder]="host.searchLabel()" [attr.aria-label]="host.searchLabel()" [value]="host.launcher.query()" [attr.aria-activedescendant]="host.activeId()" [attr.aria-controls]="host.listboxId" [attr.aria-describedby]="host.hintId" aria-autocomplete="list" aria-expanded="true" role="combobox" [attr.aria-keyshortcuts]="host.keyShortcuts" (input)="host.typed($event)" (keydown)="host.keydown($event)" (keyup)="host.keyup()" (compositionstart)="host.composing.set(true)" (compositionend)="host.compositionEnd()" />
        </label>
        <button type="button" class="tm-launcher__close" [attr.cdkFocusInitial]="host.sheetFocus() === 'close' ? '' : null" [attr.aria-label]="host.text('close')" (click)="host.launcher.close('escape')">
          <tm-icon [svg]="closeIcon" />
        </button>
      </div>
      <!-- The panel's listbox. -->
      <div class="tm-launcher__scroll">
        <ng-container [ngTemplateOutlet]="host.body()" />
      </div>
    </div>
  `,
  styles: `
    /* The modal's own header hidden, still naming the dialog. */
    .tm-launcher-sheet .tm-modal__header {
      position: absolute;
      clip-path: inset(50%);
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
    }

    .tm-launcher-sheet .tm-modal__body {
      display: flex;
      flex-direction: column;
      padding: 0;
      overflow: hidden;
    }

    .tm-launcher__sheet {
      flex: 1 1 auto;
      min-block-size: 0;
    }

    .tm-launcher__sheet-head {
      display: flex;
      flex: none;
      align-items: center;
      border-block-end: var(--border-width) solid var(--divider);
    }

    .tm-launcher__sheet-head .tm-launcher__field {
      block-size: 44px;
      border: 0;
    }

    .tm-launcher__close {
      display: inline-flex;
      flex: none;
      align-items: center;
      justify-content: center;
      inline-size: 44px;
      block-size: 44px;
      padding: 0;
      border: 0;
      background: transparent;
      color: var(--text-secondary);
      cursor: pointer;
      --icon-size: var(--modal-icon-size);
    }

    .tm-launcher__close:focus-visible {
      outline: var(--focus-ring-width) solid transparent;
      box-shadow: var(--focus-ring);
    }

    /* Full screen below sm, its entries 44 px tall. */
    @media (width < 576px) {
      .tm-modal-panel.tm-launcher-sheet {
        inline-size: 100vw;
        block-size: 100dvh;
        max-inline-size: 100vw;
        max-block-size: 100dvh;
      }

      .tm-modal-panel.tm-launcher-sheet > cdk-dialog-container {
        border-radius: 0;
      }

      .tm-launcher__sheet .tm-launcher__option {
        min-block-size: 44px;
      }
    }
  `,
})
export class TmLauncherSheet {
  /** The panel whose listbox and input this content shows. */
  protected readonly host = inject<TmLauncherSheetHost>(TM_MODAL_DATA);

  /** The search glyph. */
  protected readonly searchIcon = Search;

  /** The close glyph. */
  protected readonly closeIcon = X;
}
