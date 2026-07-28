// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The editor-session's view half: mounts exactly one live editor into the
// active cell's outlet (a consumer `*tmGridEditor` template or a built-in
// editor component), captures the hosted control's self-registration
// through a cell-scoped injector providing TM_CELL_EDITOR_HOST, and
// destroys the mounted view on commit/cancel. The engine's
// `TmGridEditState` owns the session STATE; this class owns its DOM.

import {
  Injector,
  isDevMode,
  type EmbeddedViewRef,
  type OutputRefSubscription,
  type TemplateRef,
  type ViewContainerRef,
} from '@angular/core';

import type { TmCellEditor, TmCellEditorHost } from '@tellma/core-ui/contracts';
import { TM_CELL_EDITOR_HOST } from '@tellma/core-ui';

import type { TmGridEditorContext } from '../tm-grid-templates';
import { ɵTmGridEnumEditor, ɵTmGridTextEditor } from './editors';

/** Which editor source a mount resolved to. */
export type ɵTmGridEditorKind = 'text' | 'enum' | 'template';

/** What the session mounts for one open editor. */
export type ɵTmGridEditorMountConfig =
  | {
      readonly kind: 'template';
      /** The consumer's `*tmGridEditor` template. */
      readonly template: TemplateRef<TmGridEditorContext<unknown, unknown>>;
      /** The template's context (value at open + row). */
      readonly context: TmGridEditorContext<unknown, unknown>;
    }
  | {
      readonly kind: 'text';
      /** The accessible name (column header). */
      readonly label: string;
    }
  | {
      readonly kind: 'enum';
      /** The accessible name (column header). */
      readonly label: string;
      /** The column's options. */
      readonly options: readonly unknown[];
      /** Maps an option to its display label. */
      readonly optionLabel: ((option: unknown) => string) | undefined;
      /** Maps an option to the value written to the model. */
      readonly optionValue: ((option: unknown) => unknown) | undefined;
      /** Called when the user activates an option (commit-and-close). */
      onActivation(): void;
    };

/** One mounted editor: the registered control plus its view's lifetime. */
export interface ɵTmGridMountedEditor {
  /** The editor source that was mounted. */
  readonly kind: ɵTmGridEditorKind;
  /** The control registered through TM_CELL_EDITOR_HOST. */
  readonly editor: TmCellEditor<unknown>;
  /** Whether the editor's dropdown panel is open (enum only, else false). */
  isDropdownOpen(): boolean;
  /** Opens the editor's dropdown panel (enum only, else a no-op). */
  openDropdown(): void;
}

/**
 * Mount/teardown of the single live editor view. The outlet is the
 * `ng-container` the shared view template renders inside the editing
 * cell's `[data-tm-editor]` container; the composition root forces that
 * outlet into existence synchronously (see its `openEditor`) and then asks
 * this class to mount into it.
 */
export class ɵTmGridEditorSession {
  private outlet: ViewContainerRef | null = null;
  private pending: ɵTmGridEditorMountConfig | null = null;
  private mounted: ɵTmGridMountedEditor | null = null;
  private destroyView: (() => void) | null = null;
  private activationSub: OutputRefSubscription | null = null;

  constructor(private readonly injector: Injector) {}

  /** The mounted editor, or `null` while nothing is live. */
  current(): ɵTmGridMountedEditor | null {
    return this.mounted;
  }

  /** The shared view hands the editing cell's outlet over (or `null`). */
  attachOutlet(outlet: ViewContainerRef | null): void {
    this.outlet = outlet;
  }

  /** Stages the next mount; `mountIfReady` performs it once the outlet exists. */
  stage(config: ɵTmGridEditorMountConfig): void {
    this.pending = config;
  }

  /**
   * Mounts the staged editor into the current outlet. Creation runs the
   * view's creation pass synchronously, so the hosted control constructs —
   * and registers through TM_CELL_EDITOR_HOST — before this returns; one
   * `detectChanges` flushes the initial bindings so the editor is
   * focusable and seeded within the same task (the IME requirement).
   * Returns the mounted editor, or `null` when nothing registered (a
   * dev-mode error) or no outlet exists.
   */
  mountIfReady(): ɵTmGridMountedEditor | null {
    const config = this.pending;
    const outlet = this.outlet;
    if (config === null || outlet === null) {
      return this.mounted;
    }
    this.pending = null;
    this.destroy();

    let registered: TmCellEditor<unknown> | null = null;
    const host: TmCellEditorHost = {
      register: (editor) => {
        registered = editor;
      },
    };
    const cellInjector = Injector.create({
      providers: [{ provide: TM_CELL_EDITOR_HOST, useValue: host }],
      parent: this.injector,
    });

    let isDropdownOpen: () => boolean = () => false;
    let openDropdown: () => void = () => undefined;

    if (config.kind === 'template') {
      const view: EmbeddedViewRef<TmGridEditorContext<unknown, unknown>> =
        outlet.createEmbeddedView(config.template, config.context, { injector: cellInjector });
      view.detectChanges();
      this.destroyView = () => view.destroy();
    } else if (config.kind === 'text') {
      const ref = outlet.createComponent(ɵTmGridTextEditor, { injector: cellInjector });
      ref.setInput('label', config.label);
      ref.changeDetectorRef.detectChanges();
      this.destroyView = () => ref.destroy();
    } else {
      const ref = outlet.createComponent(ɵTmGridEnumEditor, { injector: cellInjector });
      ref.setInput('label', config.label);
      ref.setInput('options', config.options);
      ref.setInput('optionLabel', config.optionLabel);
      ref.setInput('optionValue', config.optionValue);
      ref.changeDetectorRef.detectChanges();
      this.activationSub = ref.instance.activated.subscribe(() => config.onActivation());
      isDropdownOpen = () => ref.instance.panelOpen();
      openDropdown = () => ref.instance.openPanel();
      this.destroyView = () => ref.destroy();
    }

    if (registered === null) {
      this.destroy();
      if (isDevMode()) {
        throw new Error(
          'tm-grid: the cell editor mounted nothing that implements TmCellEditor — ' +
            'a *tmGridEditor template must host a control that injects ' +
            'TM_CELL_EDITOR_HOST and registers itself on construction ' +
            '(every tm-* form control does).',
        );
      }
      return null;
    }
    this.mounted = { kind: config.kind, editor: registered, isDropdownOpen, openDropdown };
    return this.mounted;
  }

  /** Destroys the mounted editor view (commit, cancel, mode flips, disposal). */
  destroy(): void {
    this.activationSub?.unsubscribe();
    this.activationSub = null;
    this.destroyView?.();
    this.destroyView = null;
    this.mounted = null;
  }
}
