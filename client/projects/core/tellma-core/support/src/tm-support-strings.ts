// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The support entry point's English strings (spec 0026 §8.4), merged under
// `tm.support` when the Diagnostics panel is first created. A language pack
// carries the same tree; the Arabic one is in @tellma/locale-ar.

/** The support entry point's English strings, under `tm.support`; a language pack carries the same tree. */
export const TM_SUPPORT_STRINGS_EN = {
  header: {
    label: 'This app',
    ui: 'UI version',
    server: 'Server version',
    worker: 'Offline version',
    tenant: 'Tenant',
    tab: 'Tab',
    connection: 'Connection',
    leader: 'Leader',
    hub: 'Live updates',
  },
  value: {
    unknown: 'Unknown',
    none: 'None',
    online: 'Online',
    offline: 'Offline',
    thisTab: 'This tab',
    otherTab: 'Another tab',
  },
  hub: {
    connecting: 'Connecting',
    connected: 'Connected',
    reconnecting: 'Reconnecting',
    offline: 'Offline',
    disabled: 'Off',
  },
  requests: {
    label: 'Recent requests',
    slowFailed: 'Slow and failed only',
    time: 'Time',
    duration: 'Duration',
    outcome: 'Outcome',
    request: 'Request',
    reference: 'Reference',
    empty: 'No requests yet.',
    emptyFiltered: 'No slow or failed requests.',
    copied: 'Request copied.',
    copyFailed: "Couldn't copy the request.",
  },
  outcome: {
    pending: 'Pending',
    ok: 'OK',
    slow: 'Slow',
    cancelled: 'Cancelled',
    error: 'Failed',
  },
  seconds: '{value} s',
  copy: 'Copy diagnostics',
  download: 'Download diagnostic file',
  report: 'Report a problem',
} as const satisfies TmStringTree;
