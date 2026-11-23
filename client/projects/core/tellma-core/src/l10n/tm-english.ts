// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_UI_STRINGS_EN } from '@tellma/core-ui';

import type { TmLanguageDefinition, TmStringTree } from '../app/tm-app-config';

// English, the platform's default language (spec 0024 §7.3): the platform's
// own `tm` table and the component library's English. Every other language
// falls back to these strings key by key. The route error views are worded
// as spec 0024 §11's table words them.

/** The platform's English strings, under `tm`. */
export const TM_STRINGS_EN = {
  home: { title: 'Home' },
  admin: {
    settings: { title: 'Settings' },
    me: { title: 'My settings' },
  },
  errors: {
    clientOutdated: {
      title: '{displayName} was updated',
      message: 'Reload to continue. Your unsaved changes come back after the reload.',
    },
    offline: {
      title: "You're offline",
      message: "Check your connection. We'll keep trying.",
    },
    forbidden: {
      title: "You don't have permission for this",
      message: 'It needs “{permission}”. Ask your administrator to grant it.',
    },
    notFound: {
      title: 'Page not found',
      message: 'Check the address, or go back to the start.',
    },
    reload: 'Reload',
    retry: 'Try again',
  },
  shell: {
    tenantPicker: { title: 'Choose a company' },
    signedOut: { title: "You're signed out", signIn: 'Sign in' },
  },
  entity: {
    permission: '{resource}: {action}',
    tenantReadOnly: '{tenant} is read-only.',
    tenantFrozen: '{tenant} is under maintenance.',
    permissionNeeded: 'Needs “{permission}”. Ask your administrator to grant it.',
  },
  value: {
    none: '—',
    yes: 'Yes',
    no: 'No',
    file: 'File',
    chooseFile: 'Choose a file',
    fileTooLarge: 'This file is too large. The limit is {max}.',
    fileType: "This type of file isn't accepted.",
    uploadFailed: 'Upload failed',
  },
} as const satisfies TmStringTree;

/**
 * English, with the platform's and the component library's strings.
 *
 * @returns The language's definition, for `TmAppConfig.languages`.
 *
 * @example
 * ```ts
 * provideTellma({ metadata: tellmaMetadata, languages: [tmEnglish(), tmArabic()], features: [] });
 * ```
 */
export function tmEnglish(): TmLanguageDefinition {
  return { code: 'en', dir: 'ltr', strings: async () => ({ tm: TM_STRINGS_EN, tmUi: TM_UI_STRINGS_EN }) };
}
