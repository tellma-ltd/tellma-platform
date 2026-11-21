// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 *
 * The names below resolve through TmIconRegistry, so each must be
 * registered first, e.g. `provideTellmaIcons({ plus: Plus, 'chevron-right':
 * { node: ChevronRight, mirrorInRtl: true } })` with the data from `lucide`.
 */

/** A decorative icon by registry name, sized by its container (16px by default). */
export const ByName = {
  template: `<tm-icon name="plus" />`,
};

/** A container sizes every icon inside it by setting --icon-size. */
export const SizedByContainer = {
  template: `<span style="--icon-size: 20px"><tm-icon name="plus" /> Add a line</span>`,
};

/** An icon that carries meaning on its own gets a label (role="img"). */
export const Labelled = {
  template: `<tm-icon name="circle-alert" label="Has errors" />`,
};

/** A fixed size from the ladder: xs 12, sm 14, md 16, lg 20, xl 24 (px). */
export const Sizes = {
  template: `
    <tm-icon name="plus" size="xs" />
    <tm-icon name="plus" size="sm" />
    <tm-icon name="plus" size="md" />
    <tm-icon name="plus" size="lg" />
    <tm-icon name="plus" size="xl" />
  `,
};

/** A directional glyph registered with mirrorInRtl flips in right-to-left text. */
export const Directional = {
  template: `
    <span dir="ltr"><tm-icon name="chevron-right" /></span>
    <span dir="rtl"><tm-icon name="chevron-right" /></span>
  `,
};

/** Library code passes the data directly, bypassing the registry. */
export const DirectData = {
  template: `<tm-icon [svg]="checkIcon" />`,
};
