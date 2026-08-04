// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Directive } from '@angular/core';

/**
 * Marks the action-button row of modal content as the shell's footer: the
 * marked element is pinned to the bottom of the modal (it stays visible
 * while the body scrolls), stretched edge to edge, separated by the
 * standard divider, and lays its buttons out end-aligned.
 *
 * Place it as the last element of the content:
 *
 * ```html
 * <p>…content…</p>
 * <div tmModalFooter>
 *   <button tmButton variant="ghost" (click)="ref.close()">Cancel</button>
 *   <button tmButton (click)="save()">Save</button>
 * </div>
 * ```
 *
 * @tmGroup overlay
 * @tmA11yNotes Purely presentational — the footer stays part of the
 *   content's natural DOM and reading order.
 */
@Directive({
  selector: '[tmModalFooter]',
  host: { class: 'tm-modal__footer' },
})
export class TmModalFooter {}
