// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Extractor fixture: shapes that broke the old regex scan — a template-less
 * export, a template with sibling properties, and a plain string template.
 */
export const ArgsOnly = { args: { label: 'No template here' } };

export const WithSiblings = {
  template: `<tm-x label="A"></tm-x>`,
  args: { label: 'A' },
};

export const Plain = { template: '<tm-x label="B"></tm-x>' };

const notExported = { template: `<tm-x label="never"></tm-x>` };
void notExported;
