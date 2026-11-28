// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** A paragraph that is still loading: three lines, the last one shorter. */
export const Lines = {
  template: `<tm-skeleton [lines]="3" width="320px" />`,
};

/** A person's avatar and name. */
export const AvatarAndName = {
  template: `
    <tm-skeleton shape="circle" width="24px" />
    <tm-skeleton width="160px" />
  `,
};

/** A list's place: a header row, then rows at the grid's row height. */
export const Table = {
  template: `<tm-skeleton shape="table" [lines]="8" [columns]="5" />`,
};

/** A chart or pivot frame: a block of the frame's size. */
export const Block = {
  template: `<tm-skeleton shape="block" width="100%" height="240px" />`,
};
