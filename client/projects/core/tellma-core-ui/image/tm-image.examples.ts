// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Canonical `tm-image` usage — dependency-free template objects consumed
 * by the docs extractor and compiled against the live API by the examples
 * spec.
 */

/** A record avatar: fixed box, circle clip, version-stamped caching. */
export const ViewMode = {
  template: `
    <tm-image
      src="/api/tenants/101/agents/42/image"
      etag="v3"
      alt="Lina Hakim"
      shape="circle"
      [width]="96"
      [height]="96"
    />
  `,
};

/** An empty `src` shows the placeholder — custom via the marked template. */
export const CustomPlaceholder = {
  template: `
    <tm-image src="" alt="" [width]="96" [height]="96">
      <ng-template tmImagePlaceholder>LH</ng-template>
    </tm-image>
  `,
};

/** Edit mode: replace / adjust (via the stored original) / remove. */
export const EditMode = {
  template: `
    <tm-image
      mode="edit"
      src="/api/tenants/101/agents/42/image"
      editSrc="/api/tenants/101/agents/42/image/original"
      etag="v3"
      alt="Lina Hakim"
      [width]="160"
      [height]="160"
      (imageChange)="onImageChange($event)"
    />
  `,
};
