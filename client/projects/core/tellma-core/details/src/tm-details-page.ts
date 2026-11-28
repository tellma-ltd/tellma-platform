// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, inject, input, type InputSignal, signal, type Type } from '@angular/core';
import type { TmJsonObject, TmLazyComponent } from '@tellma/core';

import { TmDetailsController } from './tm-details-controller';
import { TmDetailsFrame } from './tm-details-frame';
import type { TmDetailsSource } from './tm-details-source';

// The source page (spec 0031 §3.1): the details host over a source and a
// layout the page gives, such as a settings page over its own endpoints.
// It has the host's lifecycle, header and band, but no crumb, New, pager or
// clone: it is not an entity's routed page.

/**
 * A details page over a given source and layout: the record's header,
 * modes, loading and errors, with the layout in the band.
 *
 * @example
 * ```html
 * <tm-details-page [source]="source" [layout]="layout" />
 * ```
 */
@Component({
  selector: 'tm-details-page',
  imports: [TmDetailsFrame],
  providers: [TmDetailsController],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<tm-details-frame />`,
  styles: `
    /* The page fills its container: the frame splits it into the header and the band. */
    :host {
      display: block;
      block-size: 100%;
    }
  `,
})
export class TmDetailsPage {
  /** Where the page reads and writes its record. */
  readonly source: InputSignal<TmDetailsSource<unknown>> = input.required<TmDetailsSource<unknown>>();

  /** The layout the band renders: a component, or its loader. */
  readonly layout: InputSignal<TmLazyComponent | Type<unknown>> = input.required<TmLazyComponent | Type<unknown>>();

  /** Sets the page up over its inputs. */
  constructor() {
    inject<TmDetailsController<TmJsonObject>>(TmDetailsController).ɵsetup({
      source: computed(() => this.source() as TmDetailsSource<TmJsonObject>),
      cloneSource: signal(null).asReadonly(),
      layout: this.layout,
      routed: false,
    });
  }
}
