// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  Injector,
  input,
  type InputSignal,
  runInInjectionContext,
  type Signal,
  untracked,
} from '@angular/core';
import { type TmEntityDescriptor, TmEntities, type TmId, type TmJsonObject, type TmLazyComponent } from '@tellma/core';

import { TmDetailsController } from './tm-details-controller';
import { TmDetailsFrame } from './tm-details-frame';
import { tmDetailsRouteId } from './tm-details-model';
import { type TmDetailsSource, tmEntitySource } from './tm-details-source';

// The routed details page (spec 0031 §3.1): the route host renders it on
// `/{t}/{segment}/{id}` and `/{t}/{segment}/new`, with the entity, the id
// and a create's `clone` query as inputs. A step to another id changes the
// input in place, which re-keys the record and keeps the header; the
// layout comes from the entity's `details` declaration.

/**
 * The layout an entity's `details` declaration names for a mode: the
 * layout itself, or the hatch's `page`, or its `create` in create mode.
 *
 * @param descriptor - The entity's descriptor.
 * @param create - Whether the record is new.
 * @returns The layout's loader.
 */
function layoutOf(descriptor: TmEntityDescriptor, create: boolean): TmLazyComponent {
  const details = descriptor.declaration['details'] as TmLazyComponent | { readonly page: TmLazyComponent; readonly create?: TmLazyComponent } | undefined;
  if (typeof details === 'function') {
    return details;
  }
  const layout = details === undefined ? undefined : create ? (details.create ?? details.page) : details.page;
  // The route table opens no details page without a layout; a load that cannot succeed shows the error state.
  return layout ?? (() => Promise.reject(new Error(`The entity '${descriptor.name}' declares no details page.`)));
}

/**
 * The routed details page of an entity's record, in view, edit and create
 * mode. Internal: the route table renders it; a distribution names the
 * layout with the entity's `details` declaration.
 */
@Component({
  selector: 'tm-details-host',
  imports: [TmDetailsFrame],
  providers: [TmDetailsController],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<tm-details-frame />`,
  styles: `
    /* The page fills the shell's main: the frame splits it into the header and the band. */
    :host {
      display: block;
      block-size: 100%;
    }
  `,
})
export class TmDetailsHost {
  /** The routed entity's name, such as `acme.Customer`. */
  readonly entity: InputSignal<string> = input.required<string>();

  /** The route's record id; absent on the create route. */
  readonly id: InputSignal<string | undefined> = input<string>();

  /** The create route's `clone` query: the id of the record a new one copies. */
  readonly clone: InputSignal<string | undefined> = input<string>();

  /** Sets the page up over the routed entity's source and layout. */
  constructor() {
    const injector = inject(Injector);
    const entities = inject(TmEntities);
    const descriptor = computed(() => entities.get(this.entity()));
    const keyType = computed(() => descriptor().key.type);
    const id = computed(() => tmDetailsRouteId(this.id(), keyType()));
    const cloneId = computed(() => (this.id() === undefined ? tmDetailsRouteId(this.clone(), keyType()) : null));

    // --- One source per entity, whose id follows the route; the copied record's has a fixed id ---
    const sourceOf = (of: TmEntityDescriptor, recordId: Signal<TmId | null>): TmDetailsSource<TmJsonObject> =>
      untracked(() => runInInjectionContext(injector, () => tmEntitySource<TmJsonObject>(of, recordId)));
    const source = computed(() => sourceOf(descriptor(), id));
    const cloneSource = computed(() => {
      const copied = cloneId();
      return copied === null ? null : sourceOf(descriptor(), computed(() => copied));
    });

    inject<TmDetailsController<TmJsonObject>>(TmDetailsController).ɵsetup({
      source,
      cloneSource,
      layout: computed(() => layoutOf(descriptor(), id() === null)),
      routed: true,
    });
  }
}
