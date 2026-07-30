// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject, ViewEncapsulation } from '@angular/core';
import { NgComponentOutlet, NgTemplateOutlet } from '@angular/common';
import { DIALOG_DATA } from '@angular/cdk/dialog';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';

import type { ɵTmModalShellPayload } from '../tm-modal-ref';

/**
 * The standard modal shell `TmModal.open` wraps every content in: header
 * (title + optional X), scrollable body hosting the consumer's component
 * or template, and the CSS that pins `[tmModalFooter]`-marked content to
 * the bottom.
 *
 * Encapsulation is off because most of this stylesheet must reach nodes
 * outside this component's template: the overlay pane (`.tm-modal-panel`,
 * carrying the size buckets), the backdrop scrim, the CDK dialog
 * container, and the footer element living inside the CONSUMER's content.
 * Every selector is namespaced `tm-modal`.
 *
 * @internal Opened exclusively by `TmModal`; never use directly.
 */
@Component({
  selector: 'tm-modal-shell',
  imports: [NgComponentOutlet, NgTemplateOutlet],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './tm-modal-shell.css',
  host: { class: 'tm-modal' },
  template: `
    @if (payload.title !== undefined || payload.showClose) {
      <div class="tm-modal__header">
        @if (payload.title !== undefined) {
          <h2 class="tm-modal__title" [id]="payload.titleId">{{ payload.title }}</h2>
        }
        @if (payload.showClose) {
          <button
            type="button"
            class="tm-modal__close"
            [attr.aria-label]="closeLabel()"
            (click)="dismissFromCloseButton()"
          >
            <svg viewBox="0 0 16 16" fill="none" aria-hidden="true">
              <path
                d="m4 4 8 8m0-8-8 8"
                stroke="currentColor"
                stroke-width="1.5"
                stroke-linecap="round"
              />
            </svg>
          </button>
        }
      </div>
    }
    <div class="tm-modal__body">
      @if (payload.template; as contentTemplate) {
        <ng-container
          [ngTemplateOutlet]="contentTemplate"
          [ngTemplateOutletContext]="templateContext"
        />
      } @else {
        <ng-container [ngComponentOutlet]="payload.component" />
      }
    </div>
  `,
})
export class ɵTmModalShell {
  private readonly translate = inject(TM_UI_TRANSLATE);

  protected readonly payload = inject<ɵTmModalShellPayload>(DIALOG_DATA);

  /** Localized aria-label of the X button. */
  protected readonly closeLabel = this.translate('modal.close');

  /** Template-content context: the ref as `$implicit`, the open data as `data`. */
  protected readonly templateContext = {
    $implicit: this.payload.ref,
    data: this.payload.data,
  };

  protected dismissFromCloseButton(): void {
    this.payload.ref.ɵdismiss('close-button');
  }
}
