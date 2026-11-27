// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The Arabic of the pinned bar: the bar itself, which the tenant shell reads
// under `tm.pins` in its first frame, the bar's menus and popover, under
// `tm.pinOverlays`, and the Manage pins dialog, under `tm.managePins`. It is
// a table of its own beside the shell's, whose ceiling it would pass;
// `tmArabic()` merges it under `tm` on the first switch to Arabic. Labels
// and button names are verbal nouns, as Arabic interfaces write them; the
// lines addressed to the user conjugate for the addressee.

/**
 * A word that conjugates for the addressee's gender, through the ambient
 * `{gender}` message parameter (`other` is the base form).
 *
 * @param male - The base form.
 * @param female - The form addressed to a woman.
 * @returns The ICU select.
 */
function gendered(male: string, female: string): string {
  return `{gender, select, female {${female}} other {${male}}}`;
}

/** A count of pages, in the number's form. */
const PAGES = '{count, plural, zero {لا صفحات} one {صفحة واحدة} two {صفحتين} few {# صفحات} many {# صفحة} other {# صفحة}}';

/** The refusals the popover and the dialog share. */
const NAMES = {
  blank: `${gendered('أدخل', 'أدخلي')} اسمًا.`,
  long: `${gendered('استخدم', 'استخدمي')} 40 حرفًا أو أقل.`,
  duplicate: 'يوجد مجلد بهذا الاسم',
};

/**
 * The Arabic of the pinned bar, under `tm`: the bar's chips, controls and
 * announcements, its menus and popover, and the Manage pins dialog. The
 * same shape as their English.
 */
export const TM_PINS_STRINGS_AR: TmStringTree = {
  pins: {
    bar: 'الصفحات المثبّتة',
    more: 'المزيد',
    pin: 'تثبيت هذه الصفحة',
    edit: 'تعديل التثبيت',
    manage: 'إدارة الصفحات المثبّتة',
    manageCommand: 'إدارة الصفحات المثبّتة…',
    toggle: 'تثبيت هذه الصفحة أو إلغاء تثبيتها',
    empty: `لا صفحات مثبّتة بعد. ${gendered('استخدم', 'استخدمي')} زر التثبيت لتثبيت هذه الصفحة.`,
    emptyShort: 'لا صفحات مثبّتة بعد.',
    tooLong: 'رابط هذه الصفحة أطول من أن يُثبَّت',
    full: `${gendered('ثبّتَّ', 'ثبّتِّ')} ${PAGES}. ${gendered('ألغِ', 'ألغي')} تثبيت إحداها لإضافة غيرها.`,
    pinnedToast: 'تم تثبيت {label}.',
    pinned: 'تم تثبيت {label}',
    unpinned: 'تم إلغاء تثبيت {label}',
    noAccessChip: '{label}، لا وصول',
    noAccess: 'لم يعد لديك وصول إلى هذه الصفحة.',
    unavailableChip: '{label}، غير متاحة',
    unavailable: 'هذه الصفحة غير متاحة.',
  },
  pinOverlays: {
    pinned: 'تم التثبيت',
    edit: 'تعديل التثبيت',
    name: 'الاسم',
    folder: 'المجلد',
    noFolder: 'بلا مجلد',
    newFolder: 'مجلد جديد…',
    folderName: 'اسم المجلد',
    remove: 'إزالة',
    done: 'تم',
    ...NAMES,
    emptyFolder: 'لا صفحات في هذا المجلد',
    unpin: 'إلغاء التثبيت',
    movedTo: 'تم نقل {label} إلى {folder}',
    moved: 'تم نقل {label}، الموضع {index} من {count}',
  },
  managePins: {
    empty: 'لا صفحات مثبّتة بعد.',
    emptyFolder: 'لا صفحات في هذا المجلد',
    pages: 'صفحات {folder}',
    rename: 'إعادة تسمية {label}',
    moveTo: 'نقل {label} إلى',
    remove: 'إزالة {label}',
    name: 'الاسم',
    noFolder: 'بلا مجلد',
    noAccess: 'لا وصول',
    unavailable: 'غير متاحة',
    newFolder: 'مجلد جديد',
    newFolderNamed: 'مجلد جديد {index}',
    foldersFull: 'لديك {count, plural, zero {لا مجلدات} one {مجلد واحد} two {مجلدان} few {# مجلدات} many {# مجلدًا} other {# مجلد}}، وهو الحد الأقصى.',
    showBar: 'إظهار شريط الصفحات المثبّتة',
    cancel: 'إلغاء',
    save: 'حفظ',
    ...NAMES,
    movedTo: 'تم نقل {label} إلى {folder}',
    movedOut: 'تم نقل {label} خارج مجلده',
  },
};
