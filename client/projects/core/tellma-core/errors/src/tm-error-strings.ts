// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The error presenter's English strings (spec 0024 §8.3, §11), worded as
// the spec's table words them. They join the platform's `tm.errors` table
// when the presenter loads; the rows the route's own error views already
// show (offline, forbidden, client-outdated, Reload and Try again) stay
// there, in the primary table, and the presenter reuses them. Reload's
// download flow is this entry point's alone, so its words are here.

/** The error presenter's English strings, merged under `tm.errors`. */
export const TM_ERROR_STRINGS_EN = {
  notSent: { message: "Your changes are kept. Save again when you're back online." },
  outcomeUnknown: {
    title: "We couldn't confirm this",
    message: 'The connection dropped before {displayName} answered. Check whether it went through before you try again.',
  },
  outsideFilter: {
    title: "Your access doesn't cover this record",
    message: 'Your “{permission}” permission covers only some records. Ask your administrator if you need this one.',
  },
  restrictedPath: {
    title: "You can't see {field} here",
    message: 'It needs full “{resource}: Read” access. Ask your administrator to grant it.',
  },
  missing: { title: "This {entity} doesn't exist or you can't see it", record: "This record doesn't exist or you can't see it" },
  readOnly: { title: '{tenant} is read-only', message: 'You can view data but not change it. Contact your administrator.' },
  frozen: { title: '{tenant} is under maintenance', message: 'You can view data but not change it until it ends.' },
  unavailable: { title: '{displayName} is busy', message: 'Try again in a moment.' },
  serverOutdated: { title: '{displayName} is being updated', message: 'Try again in a minute.' },
  server: {
    title: 'Something went wrong',
    message: "{displayName} couldn't finish this. Try again. If it keeps happening, send this reference to support.",
  },
  badRequest: { title: "Couldn't complete this" },
  suspended: { title: '{tenant} is suspended', message: 'Contact your administrator.', switch: 'Switch tenant' },
  ended: { title: 'Your session ended', message: 'Sign in to continue. Your work is kept.' },
  stepUp: {
    title: "Confirm it's you",
    message: "This needs you to sign in again. You'll come back to this page with your unsaved changes.",
    continue: 'Continue',
  },
  updating: 'Getting the update…',
  updateFailed: "Couldn't download the update. Check your connection and try again.",
  reloadAnyway: 'Reload anyway',
  details: { status: 'Status', code: 'Code', time: 'Time', route: 'Page', version: 'Version', permission: 'Permission' },
} as const satisfies TmStringTree;
