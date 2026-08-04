// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Canonical `tm-tab-group` usage — dependency-free template objects
 * consumed by the docs extractor and compiled against the live API by the
 * examples spec.
 */

/** Tabs are definitions; content is a template that lives only while active. */
export const Basic = {
  template: `
    <tm-tab-group>
      <tm-tab id="details" label="Details">
        <ng-template tmTabContent><p>Record details.</p></ng-template>
      </tm-tab>
      <tm-tab id="lines" label="Lines">
        <ng-template tmTabContent><p>Invoice lines.</p></ng-template>
      </tm-tab>
      <tm-tab id="audit" label="Audit" disabled>
        <ng-template tmTabContent><p>Audit trail.</p></ng-template>
      </tm-tab>
    </tm-tab-group>
  `,
};

/** Explicit activation: arrows move focus only; Enter/Space/click select. */
export const ExplicitSelection = {
  template: `
    <tm-tab-group selectionMode="explicit">
      <tm-tab id="cheap" label="Cheap">
        <ng-template tmTabContent><p>Instant.</p></ng-template>
      </tm-tab>
      <tm-tab id="expensive" label="Expensive" preserveContent>
        <ng-template tmTabContent><p>Kept alive (inert) once opened.</p></ng-template>
      </tm-tab>
    </tm-tab-group>
  `,
};

/** A rich label template replaces the plain string in the strip. */
export const RichLabel = {
  template: `
    <tm-tab-group>
      <tm-tab id="inbox">
        <ng-template tmTabLabel>Inbox <strong>(3)</strong></ng-template>
        <ng-template tmTabContent><p>Three unread.</p></ng-template>
      </tm-tab>
      <tm-tab id="archive" label="Archive">
        <ng-template tmTabContent><p>Archived items.</p></ng-template>
      </tm-tab>
    </tm-tab-group>
  `,
};
