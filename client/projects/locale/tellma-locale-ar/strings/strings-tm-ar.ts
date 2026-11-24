// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

/**
 * An imperative that conjugates for the addressee's gender, through the
 * ambient `{gender}` message parameter (`other` is the base form).
 *
 * @param male - The base form.
 * @param female - The form addressed to a woman.
 * @returns The ICU select.
 */
function imperative(male: string, female: string): string {
  return `{gender, select, female {${female}} other {${male}}}`;
}

/**
 * The Arabic translations of the platform's own strings, the `tm` table:
 * the same shape as English's. The server's packs word the same ideas the
 * same way: "ليست لديك صلاحية لذلك", "حاول مرة أخرى".
 */
export const TM_STRINGS_AR: TmStringTree = {
  home: { title: 'الرئيسية' },
  admin: {
    settings: { title: 'الإعدادات' },
    me: { title: 'إعداداتي' },
  },
  errors: {
    clientOutdated: {
      title: 'تم تحديث {displayName}',
      message: `${imperative('أعد', 'أعيدي')} التحميل للمتابعة. ستعود تغييراتك غير المحفوظة بعد إعادة التحميل.`,
    },
    offline: {
      title: imperative('أنت غير متصل', 'أنتِ غير متصلة'),
      message: `${imperative('تحقق', 'تحققي')} من اتصالك. سنواصل المحاولة.`,
    },
    forbidden: {
      title: 'ليست لديك صلاحية لذلك',
      message: `يتطلب ذلك صلاحية «{permission}». ${imperative('اطلب', 'اطلبي')} من المسؤول منحها لك.`,
    },
    notFound: {
      title: 'الصفحة غير موجودة',
      message: `${imperative('تحقق', 'تحققي')} من العنوان، أو ${imperative('عد', 'عودي')} إلى البداية.`,
    },
    reload: 'إعادة التحميل',
    retry: `${imperative('حاول', 'حاولي')} مرة أخرى`,
  },
  nav: {
    analysis: 'التحليل',
    administration: 'الإدارة',
    me: {
      profile: 'ملفي الشخصي',
      region: 'اللغة والمنطقة',
      display: 'العرض',
      notifications: 'الإشعارات',
    },
  },
  launcher: {
    synonyms: {
      meProfile: 'الصورة، التوقيع، بيانات الاتصال',
      meRegion: 'المنطقة الزمنية، التقويم، الأرقام، التنسيقات',
      meDisplay: 'السمة، الشريط المثبت',
    },
  },
  shell: {
    tenantPicker: { title: `${imperative('اختر', 'اختاري')} شركة` },
    signedOut: { title: imperative('لقد سجّلت الخروج', 'لقد سجّلتِ الخروج'), signIn: 'تسجيل الدخول' },
  },
  entity: {
    permission: '{resource}: {action}',
    tenantReadOnly: '{tenant} للقراءة فقط.',
    tenantFrozen: '{tenant} قيد الصيانة.',
    permissionNeeded: `يتطلب ذلك صلاحية «{permission}». ${imperative('اطلب', 'اطلبي')} من المسؤول منحها لك.`,
  },
  attention: {
    banners: { offline: 'انقطع الاتصال. جارٍ إعادة الاتصال…' },
  },
  value: {
    none: '—',
    yes: 'نعم',
    no: 'لا',
    file: 'ملف',
    chooseFile: `${imperative('اختر', 'اختاري')} ملفًا`,
    fileTooLarge: 'هذا الملف كبير جدًا. الحد الأقصى هو {max}.',
    fileType: 'هذا النوع من الملفات غير مقبول.',
    uploadFailed: 'تعذّر الرفع',
  },
  picker: {
    newLabel: '{label}…',
    unavailable: 'هذه الصفحة غير متاحة بعد.',
    loadFailed: 'تعذّر تحميل هذه الصفحة.',
  },
  filter: {
    operator: 'المعامل',
    apply: 'تطبيق',
    clear: 'مسح',
    from: 'من',
    to: 'إلى',
    true: 'صحيح',
    false: 'خطأ',
    op: {
      contains: 'يحتوي على',
      'starts-with': 'يبدأ بـ',
      'ends-with': 'ينتهي بـ',
      eq: 'يساوي',
      ne: 'لا يساوي',
      lt: 'أقل من',
      le: 'لا يزيد على',
      gt: 'أكبر من',
      ge: 'لا يقل عن',
      between: 'بين',
      before: 'قبل',
      after: 'بعد',
      in: 'أيٌّ من',
      is: 'هو',
    },
    describe: {
      contains: '{label} يحتوي على {value}',
      'starts-with': '{label} يبدأ بـ {value}',
      'ends-with': '{label} ينتهي بـ {value}',
      eq: '{label} هو {value}',
      ne: '{label} ليس {value}',
      lt: '{label} أقل من {value}',
      le: '{label} لا يزيد على {value}',
      gt: '{label} أكبر من {value}',
      ge: '{label} لا يقل عن {value}',
      between: '{label} بين {from} و{to}',
      before: '{label} قبل {value}',
      after: '{label} بعد {value}',
      in: '{label}: {values}',
      is: '{label}: {value, select, true {نعم} other {لا}}',
    },
  },
};
