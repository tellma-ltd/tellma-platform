// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Canonical modal-content usage — dependency-free template objects consumed
 * by the docs extractor and compiled against the live API by the examples
 * spec. These are CONTENT templates: the modal itself is opened by the
 * `TmModal` service (`modal.open(Content, { title: '…' })`).
 */

/** The action row of modal content, pinned to the bottom by the shell. */
export const ContentWithFooter = {
  template: `
    <p>Discard the draft? Unsaved changes will be lost.</p>
    <div tmModalFooter>
      <button tmButton variant="ghost">Cancel</button>
      <button tmButton variant="danger">Discard</button>
    </div>
  `,
};
