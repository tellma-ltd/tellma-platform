// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  inject,
  Injectable,
  InjectionToken,
  Injector,
  isDevMode,
  TemplateRef,
  type Type,
} from '@angular/core';
import { Directionality } from '@angular/cdk/bidi';
import { Dialog } from '@angular/cdk/dialog';
import { createGlobalPositionStrategy } from '@angular/cdk/overlay';

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
 * The semantic size buckets of a modal. Centered, `sm` and `md` are fixed
 * token widths (`--modal-width-sm`/`--modal-width-md`) with body scroll past
 * the viewport-capped max height, and `lg` fills the viewport minus a
 * constant margin (`--modal-lg-margin`) on all sides; below the `sm`
 * breakpoint all three converge to near-full-screen. As a drawer
 * (`placement: 'end'`) the bucket picks the drawer's width
 * (`--modal-drawer-width-sm|md|lg`), and below the `sm` breakpoint every
 * drawer takes the full width.
 */
export type TmModalSize = 'sm' | 'md' | 'lg';

/** Configuration for {@link TmModal.open}. */
export interface TmModalConfig {
  /** The size bucket. Default `'md'`. */
  size?: TmModalSize;
  /**
   * Where the modal sits. `'center'` (the default) is the centered dialog;
   * `'end'` is the drawer: a modal side sheet docked to the inline-end edge
   * (the right in a left-to-right page, the left in a right-to-left one) at
   * the full viewport height, with the same header, scrolling body, footer,
   * focus trap, dismissal paths and scrim as the centered dialog. The
   * drawer appears at once, with no entrance motion, and never resizes
   * while open.
   *
   * @example
   * ```ts
   * this.modal.open(TrayPanel, { placement: 'end', size: 'sm', title: 'Notifications' });
   * ```
   */
  placement?: 'center' | 'end';
  /**
   * The dialog's ARIA role. Default `'dialog'`. Use `'alertdialog'` for a
   * dialog that interrupts with a message the user must answer (a confirm,
   * an error), and name that message with {@link TmModalConfig.describedBy}
   * so a screen reader reads it on open; an alert dialog without one is a
   * dev-mode warning.
   */
  role?: 'dialog' | 'alertdialog';
  /**
   * The id of an element inside the content that describes the dialog —
   * an alert dialog's message. It becomes the dialog's `aria-describedby`.
   */
  describedBy?: string;
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
 * Tellma modal reads alike. The same shell serves the centered dialog, the
 * drawer (`placement: 'end'`, a side sheet at the inline-end edge) and the
 * alert dialog (`role: 'alertdialog'`).
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
  /** The CDK dialog service every modal opens through. */
  private readonly dialog = inject(Dialog);
  /** The injector the drawer's position strategy is created with. */
  private readonly injector = inject(Injector);
  /** The live root direction: an open modal mirrors when the document's direction flips. */
  private readonly direction = inject(Directionality);

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
    const role = config.role ?? 'dialog';
    const describedBy = config.describedBy === '' ? undefined : config.describedBy;
    if (isDevMode()) {
      if (title === undefined) {
        console.warn(
          '[tellma-ui] TmModal.open called without a title — the dialog has no accessible ' +
            'name. Pass `title`, or have the content label the dialog itself.',
        );
      }
      if (role === 'alertdialog' && describedBy === undefined) {
        console.warn(
          '[tellma-ui] TmModal.open with role "alertdialog" but no `describedBy` — a screen ' +
            'reader will not read the alert message on open. Pass the id of the message element.',
        );
      }
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
      placement: config.placement ?? 'center',
      showClose: config.showClose ?? true,
      data: config.data,
      ref: ref as TmModalRef<unknown>,
    };
    const size = config.size ?? 'md';
    const drawer = payload.placement === 'end';
    const panelClass = ['tm-modal-panel', `tm-modal-panel--${size}`];
    const backdropClass = ['tm-modal-backdrop'];
    if (drawer) {
      panelClass.push('tm-modal-panel--end');
      backdropClass.push('tm-modal-backdrop--end');
    }
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
        role,
        ariaLabelledBy: title !== undefined ? titleId : null,
        ariaDescribedBy: describedBy ?? null,
        panelClass,
        backdropClass,
        // The drawer docks to the inline-end edge at the top. `end` is
        // resolved against the overlay's own direction on every apply, so
        // the live flip below moves an open drawer to the other edge.
        ...(drawer ? { positionStrategy: createGlobalPositionStrategy(this.injector).end('0').top('0') } : {}),
        providers: [
          { provide: TM_MODAL_DATA, useValue: config.data ?? null },
          { provide: TmModalRef, useValue: ref },
        ],
      },
    );
    ref.ɵattach(dialogRef);
    // No `direction` is passed to the CDK, so the overlay and the content
    // both take the live root direction at creation; while open, a flip of
    // the document's direction re-stamps the pane so the open modal mirrors
    // in place, and the re-applied position moves an open drawer's edge.
    const overlayRef = dialogRef.overlayRef;
    const directionChange = this.direction.change.subscribe((direction) => {
      overlayRef.setDirection(direction);
      overlayRef.updatePosition();
    });
    dialogRef.closed.subscribe(() => directionChange.unsubscribe());
    return ref;
  }
}
