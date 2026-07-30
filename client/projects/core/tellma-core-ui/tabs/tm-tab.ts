// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterNextRender,
  booleanAttribute,
  Component,
  contentChild,
  Directive,
  inject,
  input,
  isDevMode,
  TemplateRef,
} from '@angular/core';

/**
 * A rich tab label template: `<ng-template tmTabLabel>` inside a `tm-tab`
 * replaces the plain `label` string in the tab strip (icons, counters).
 */
@Directive({ selector: 'ng-template[tmTabLabel]' })
export class TmTabLabel {
  /** The label content template. */
  readonly template = inject(TemplateRef);
}

/**
 * A tab's panel content: `<ng-template tmTabContent>` inside a `tm-tab`.
 * A TEMPLATE by requirement, not a nicety — the group instantiates it on
 * first activation and destroys it on deactivation (plain projected
 * content belongs to the consumer's view and could only ever be detached,
 * never destroyed).
 */
@Directive({ selector: 'ng-template[tmTabContent]' })
export class TmTabContent {
  /** The panel content template. */
  readonly template = inject(TemplateRef);
}

/**
 * One tab DEFINITION: label + content template. Renders nothing itself —
 * the enclosing `tm-tab-group` renders the real tab strip and panels (the
 * aria directives cannot be content-projected across DI boundaries), and
 * the content template instantiates only while its tab is active.
 *
 * @tmGroup layout
 */
@Component({
  selector: 'tm-tab',
  template: ``,
})
export class TmTab {
  /** The tab's stable identity (and the `selectedId` value). */
  readonly id = input.required<string>();
  /** The plain-text label; a `tmTabLabel` template overrides it. */
  readonly label = input('');
  /** Whether the tab is disabled (kept in the strip, never activates). */
  readonly disabled = input(false, { transform: booleanAttribute });
  /**
   * Keep the panel's DOM alive (hidden + inert) after its first
   * activation, instead of destroying it on deactivation — for expensive
   * tabs whose transient state must survive switching. Default `false`.
   */
  readonly preserveContent = input(false, { transform: booleanAttribute });

  /** The optional rich label template. */
  readonly labelTemplate = contentChild(TmTabLabel);
  /** The panel content template (`<ng-template tmTabContent>`). */
  readonly content = contentChild(TmTabContent);

  constructor() {
    if (isDevMode()) {
      afterNextRender(() => {
        if (this.content() === undefined) {
          console.warn(
            `tm-tab '${this.id()}': no <ng-template tmTabContent> found — the panel will be ` +
              `empty. Tab content must be a template so it can instantiate on activation and ` +
              `be destroyed on deactivation.`,
          );
        }
      });
    }
  }
}
