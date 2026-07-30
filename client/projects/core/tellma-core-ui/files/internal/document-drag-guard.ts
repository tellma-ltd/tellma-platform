// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * The missed-drop guard: a file dropped outside a designated target
 * navigates the browser away from the SPA, so while at least one
 * `tm-dropzone` is connected, document-level handlers cancel the default
 * for `dragover`/`drop` everywhere else (and show a not-allowed cursor).
 *
 * Designated targets are `tm-dropzone` hosts and any element carrying the
 * `data-tm-drop-target` attribute (the escape hatch for consumer-built
 * drop surfaces).
 */

let refCount = 0;

function insideDropTarget(target: EventTarget | null): boolean {
  return (
    target instanceof Element && target.closest('tm-dropzone, [data-tm-drop-target]') !== null
  );
}

/**
 * Whether the guard applies: FILE drags only. Text dragged between inputs
 * is ordinary editing — cancelling it app-wide would break native
 * drag-and-drop wherever a dropzone happens to be mounted.
 */
function guarded(event: DragEvent): boolean {
  return (
    event.dataTransfer?.types.includes('Files') === true && !insideDropTarget(event.target)
  );
}

function onDragOver(event: DragEvent): void {
  if (!guarded(event)) {
    return;
  }
  event.preventDefault();
  if (event.dataTransfer !== null) {
    event.dataTransfer.dropEffect = 'none';
  }
}

function onDrop(event: DragEvent): void {
  if (!guarded(event)) {
    return;
  }
  event.preventDefault();
}

/**
 * Acquires the guard (installing it with the first holder) and returns
 * the idempotent release; the last release uninstalls the listeners.
 */
export function tmAcquireDocumentDragGuard(): () => void {
  refCount += 1;
  if (refCount === 1) {
    document.addEventListener('dragover', onDragOver);
    document.addEventListener('drop', onDrop);
  }
  let released = false;
  return () => {
    if (released) {
      return;
    }
    released = true;
    refCount -= 1;
    if (refCount === 0) {
      document.removeEventListener('dragover', onDragOver);
      document.removeEventListener('drop', onDrop);
    }
  };
}
