/**
 * A typed layer over native HTML5 drag and drop: `[tmDragSource]` makes an
 * element draggable with a payload, `[tmDropTarget]` accepts payloads by
 * kind, and the root `TmDragDrop` service holds the drag in flight. A drag
 * writes its text as `text/plain`, so text fields take it at the drop
 * caret.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { TmDragDrop } from './tm-drag-drop';
export type { TmDragPayload, TmDrop } from './tm-drag-drop';
export { TmDragSource } from './tm-drag-source';
export { TmDropTarget } from './tm-drop-target';
