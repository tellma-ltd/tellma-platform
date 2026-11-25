// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The Arabic of the details page, which the page reads under `tm.details`.
// It is a table of its own beside the platform's main one and the shell's,
// so each one's ceiling holds; `tmArabic()` merges them under `tm` on the
// first switch to Arabic. Button labels are verbal nouns, as Arabic
// toolbars write them, so they need no gendered forms.

/**
 * The Arabic of the details page, under `tm`: the toolbar's buttons, the
 * Active, Inactive and Unsaved changes badges, the status lines, the
 * Discard changes question, and Edit's reason for a record outside the
 * user's permission. The same shape as its English.
 */
export const TM_DETAILS_STRINGS_AR: TmStringTree = {
  details: {
    edit: 'تعديل',
    create: 'جديد',
    save: 'حفظ',
    cancel: 'إلغاء',
    refresh: 'تحديث',
    actions: 'الإجراءات',
    active: 'نشط',
    inactive: 'غير نشط',
    unsaved: 'تغييرات غير محفوظة',
    loading: 'جارٍ التحميل',
    editing: 'تعديل {name}',
    saved: 'تم الحفظ',
    discard: {
      title: 'تجاهل التغييرات؟',
      message: 'ستُفقد تغييراتك على هذا السجل.',
      confirm: 'تجاهل التغييرات',
      keep: 'متابعة التعديل',
    },
    outsideFilter: 'لا تشمل صلاحية «{permission}» الممنوحة لك هذا السجل.',
  },
};
