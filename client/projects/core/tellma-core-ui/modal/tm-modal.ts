// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  inject,
  Injectable,
  InjectionToken,
  isDevMode,
  TemplateRef,
  type Type,
} from '@angular/core';
import { Dialog } from '@angular/cdk/dialog';

import { TmModalRef, type TmModalResult, type ɵTmModalShellPayload } from './tm-modal-ref';
import { ɵTmModalShell } from './internal/tm-modal-shell';

/**
 * The open config's `data`, injectable by modal content components. Resolves
 * to `null` when the modal was opened without `data`.
 *
 * Template-based content cannot inject; it receives the same value through
 * the template context's `data` member instead.
 */
export const TM_MODAL_DATA = new InjectionToken<unknown>('TM_MODAL_DATA');

/**
 * The semantic size buckets of a modal. `sm` and `md` are fixed token
 * widths (`--modal-width-sm`/`--modal-width-md`) with body scroll past the
 * viewport-capped max height; `lg` fills the viewport minus a constant
 * margin (`--modal-lg-margin`) on all sides. On small viewports all three
 * converge to near-full-screen.
 */
export type TmModalSize = 'sm' | 'md' | 'lg';

/** Configuration for {@link TmModal.open}. */
export interface TmModalConfig {
  /** The size bucket. Default `'md'`. */
  size?: TmModalSize;
  /**
   * Extra CSS class applied to the overlay panel — the escape hatch for
   * the rare modal the semantic buckets genuinely don't fit (dimensions
   * included) without forking the shell. The class must be reachable from
   * a global stylesheet (the panel lives in the overlay container).
   */
  panelClass?: string;
  /**
   * The header title; also the dialog's accessible name
   * (`aria-labelledby`). Omitting it renders no title — the content then
   * owns labelling, and a dev-mode warning flags the unnamed dialog.
   */
  title?: string;
  /** Arbitrary input for the content, injected via {@link TM_MODAL_DATA}. */
  data?: unknown;
  /** Whether the header shows the X close button. Default `true`. */
  showClose?: boolean;
  /** Whether a click on the backdrop dismisses. Default `true`. */
  backdropDismiss?: boolean;
  /** Whether the Escape key dismisses. Default `true`. */
  escapeDismiss?: boolean;
  /**
   * Guard consulted before every user-initiated dismissal (X button,
   * backdrop, Escape); returning or resolving `false` keeps the modal
   * open. The unsaved-changes pattern: open a confirm modal from the guard
   * and resolve with the user's answer. Programmatic
   * {@link TmModalRef.close} is not guarded.
   */
  canDismiss?: () => boolean | Promise<boolean>;
}

let nextTitleId = 0;

/**
 * Service-opened modal dialogs on the CDK dialog (real focus trap,
 * initial-focus/restore-focus, backdrop, dialog stacking), wrapped in the
 * standard Tellma shell: header (title + X), scrollable body, and a footer
 * region content marks with the `TmModalFooter` directive — so every
 * Tellma modal reads alike.
 *
 * Content components inject {@link TmModalRef} to close themselves with a
 * result and {@link TM_MODAL_DATA} for input; the opener awaits
 * `ref.closed` for the typed, dismissal-path-discriminating outcome.
 *
 * A modal may open another (each layer gets its own backdrop and focus
 * trap; Escape and backdrop-click dismiss the topmost layer only; closing
 * restores focus into the layer beneath, ultimately the original opener).
 * More than two levels is discouraged. Dropdown overlays inside a modal
 * (`tm-select`, `tm-menu`, the date-picker popup) render in the native
 * top layer, above the modal, with no z-index management — and consume
 * their own Escape first, so Escape closes innermost-first.
 *
 * @example
 * ```ts
 * const ref = this.modal.open<boolean>(ConfirmDiscard, {
 *   size: 'sm',
 *   title: 'Discard draft?',
 *   data: { name: draft.name },
 * });
 * const result = await ref.closed;
 * if (result.via === 'api' && result.value) {
 *   this.discard();
 * }
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmModal {
  private readonly dialog = inject(Dialog);

  /**
   * Opens a modal over `content` — a component type (instantiated with
   * {@link TmModalRef} and {@link TM_MODAL_DATA} injectable) or an
   * `ng-template` (receiving the ref as `$implicit` and the data as
   * `data` in its context).
   */
  open<R = void>(
    content: Type<unknown> | TemplateRef<unknown>,
    config: TmModalConfig = {},
  ): TmModalRef<R> {
    // An empty title is "no title": a present-but-empty aria-labelledby
    // target is worse for AT heuristics than an unnamed dialog.
    const title = config.title === '' ? undefined : config.title;
    if (title === undefined && isDevMode()) {
      console.warn(
        '[tellma-ui] TmModal.open called without a title — the dialog has no accessible ' +
          'name. Pass `title`, or have the content label the dialog itself.',
      );
    }
    const titleId = `tm-modal-title-${nextTitleId++}`;
    const ref = new TmModalRef<R>({
      escapeDismiss: config.escapeDismiss ?? true,
      backdropDismiss: config.backdropDismiss ?? true,
      canDismiss: config.canDismiss,
    });
    const payload: ɵTmModalShellPayload = {
      component: content instanceof TemplateRef ? null : content,
      template: content instanceof TemplateRef ? content : null,
      title,
      titleId,
      showClose: config.showClose ?? true,
      data: config.data,
      ref: ref as TmModalRef<unknown>,
    };
    const size = config.size ?? 'md';
    const panelClass = ['tm-modal-panel', `tm-modal-panel--${size}`];
    if (config.panelClass !== undefined) {
      panelClass.push(config.panelClass);
    }
    const dialogRef = this.dialog.open<TmModalResult<R>, ɵTmModalShellPayload, ɵTmModalShell>(
      ɵTmModalShell,
      {
        data: payload,
        // The CDK never self-closes: every dismissal flows through the ref
        // so the guard and the typed result stay accurate.
        disableClose: true,
        autoFocus: 'first-tabbable',
        restoreFocus: true,
        role: 'dialog',
        ariaLabelledBy: title !== undefined ? titleId : null,
        panelClass,
        backdropClass: 'tm-modal-backdrop',
        providers: [
          { provide: TM_MODAL_DATA, useValue: config.data ?? null },
          { provide: TmModalRef, useValue: ref },
        ],
      },
    );
    ref.ɵattach(dialogRef);
    return ref;
  }
}
