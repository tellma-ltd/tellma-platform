// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Injector } from '@angular/core';
import { TmL10n } from '@tellma/core-ui';
import { TmConfirm } from '@tellma/core-ui/confirm';
import { TM_NOW, TmLaunchCache, TmLocalization, type TmStringTree, TmTelemetry } from '@tellma/core';

import type { TmConflictInfo } from './tm-details-dialogs';

// The editor's dialogs' copy and logic (spec 0031 §9.1, §9.2, §11.3), loaded
// on first use: each is a `TmConfirm` alert dialog focused on its primary
// choice, so Escape answers the safe way. The concurrency dialog's copy
// names who saved the newer version: another user by name, the current
// user in another tab or window, or no one known; its choice is recorded
// with whether the copy named the current user.

/** The dialogs' English strings, merged under `tm.detailsDialogs` on first use; a language pack carries the same tree. */
export const TM_DETAILS_DIALOGS_STRINGS_EN = {
  conflict: {
    title: 'This record changed while you were editing',
    other: '{name} saved a newer version {time}. Reload to take theirs, or overwrite with yours.',
    self: 'You saved this record in another tab or window {time}. Reload that version, or overwrite it with this one.',
    unknown: 'Reload to take the saved version, or overwrite it with yours.',
    overwrite: 'Overwrite',
    reloadTheirs: 'Reload theirs',
    reloadSaved: 'Reload saved version',
    reload: 'Reload',
  },
  deleted: {
    title: 'This record was deleted',
    message: "Someone deleted it while you were editing, so your changes can't be saved.",
    discard: 'Discard changes',
  },
  leave: {
    title: 'Leave without saving?',
    message: "This record has unsaved changes. They're lost if you leave now.",
    discard: 'Discard changes',
    stay: 'Stay',
  },
} as const satisfies TmStringTree;

/**
 * The dialogs' strings by key under `tm.detailsDialogs`, registered on first use.
 *
 * @param injector - The app's injector.
 * @returns A function that reads one string with its arguments.
 */
function strings(injector: Injector): (key: string, args?: Readonly<Record<string, string>>) => string {
  const l10n = injector.get(TmLocalization);
  l10n.ɵregisterStrings('detailsDialogs', TM_DETAILS_DIALOGS_STRINGS_EN);
  return (key, args) => l10n.ɵtext(`tm.detailsDialogs.${key}`, args)();
}

/**
 * Asks what to do about a save that met a newer version. Internal: `TmDetailsDialogs.conflict` calls it.
 *
 * @param injector - The app's injector.
 * @param conflict - Who saved the newer version, and when; `null` when unknown.
 * @returns `reload`, `overwrite`, or `null` when dismissed.
 */
export async function ɵtmConflictDialog(injector: Injector, conflict: TmConflictInfo | null): Promise<'reload' | 'overwrite' | null> {
  const text = strings(injector);
  // --- Who saved: the current user elsewhere, another user by name, or no one known; each needs the time ---
  const at = conflict?.modifiedAt;
  const me = injector.get(TmLaunchCache).docs.me()?.user.id;
  const time = at === undefined ? '' : injector.get(TmL10n).formatRelativeTime(at, { now: injector.get(TM_NOW)(), style: 'long' });
  const self = time !== '' && conflict?.modifiedById !== undefined && conflict.modifiedById === me;
  const other = !self && time !== '' && conflict?.modifiedByName !== undefined && conflict.modifiedByName !== '';
  const [message, reload] = self
    ? [text('conflict.self', { time }), 'reloadSaved']
    : other
      ? [text('conflict.other', { name: conflict.modifiedByName!, time }), 'reloadTheirs']
      : [text('conflict.unknown'), 'reload'];
  const choice = await injector.get(TmConfirm).ask<'reload' | 'overwrite'>({
    title: text('conflict.title'),
    message,
    choices: [
      { key: 'overwrite', label: text('conflict.overwrite'), variant: 'ghost' },
      { key: 'reload', label: text(`conflict.${reload}`), variant: 'primary' },
    ],
    initialFocus: 'reload',
  });
  // --- The choice, with whether the copy said the current user saved the newer version ---
  injector.get(TmTelemetry).event('tm.details.conflict', { choice: choice ?? 'dismiss', self });
  return choice;
}

/**
 * Says that the record was deleted while it was edited. Internal: `TmDetailsDialogs.deleted` calls it.
 *
 * @param injector - The app's injector.
 * @returns `discard`, or `null` when the user kept editing.
 */
export function ɵtmDeletedDialog(injector: Injector): Promise<'discard' | null> {
  const text = strings(injector);
  return injector.get(TmConfirm).ask<'discard'>({
    title: text('deleted.title'),
    message: text('deleted.message'),
    choices: [{ key: 'discard', label: text('deleted.discard'), variant: 'primary' }],
    initialFocus: 'discard',
  });
}

/**
 * Asks whether to leave a page with unsaved changes. Internal: `TmDetailsDialogs.leave` calls it.
 *
 * @param injector - The app's injector.
 * @returns `discard`, or `stay`, which Escape also answers.
 */
export async function ɵtmLeaveDialog(injector: Injector): Promise<'discard' | 'stay'> {
  const text = strings(injector);
  const answer = await injector.get(TmConfirm).ask<'discard' | 'stay'>({
    title: text('leave.title'),
    message: text('leave.message'),
    choices: [
      { key: 'discard', label: text('leave.discard'), variant: 'ghost' },
      { key: 'stay', label: text('leave.stay'), variant: 'primary' },
    ],
    initialFocus: 'stay',
  });
  return answer ?? 'stay';
}
