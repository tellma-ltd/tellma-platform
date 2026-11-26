// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The grid's deferred chunk: the parts a first grid frame never shows, the
// context menu and the coarse-pointer selection handles. The view loads it
// with import() once the browser is idle after the first render, or at once
// when one of them is wanted sooner. Neither part imports the grid's own code
// other than its types, so the chunk shares nothing with the entry point.

export { ɵTmGridContextMenu } from './grid-context-menu';
export { ɵTmGridTouchHandles } from './touch-handles';
