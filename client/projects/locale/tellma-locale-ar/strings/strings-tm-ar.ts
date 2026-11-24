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
    confirmAction: `هل ${imperative('تريد', 'تريدين')} تنفيذ «{action}» على {n, plural, one {هذا العنصر} two {العنصرين} few {# عناصر} many {# عنصرًا} other {# عنصر}}؟`,
    confirmActionMessage: 'لا يمكن التراجع عن ذلك.',
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
  confirmations: {
    title: 'التأكيد للمتابعة',
    continue: 'متابعة',
    skip: '{n, plural, one {المتابعة دون هذا العنصر} two {المتابعة دون هذين العنصرين} few {المتابعة دون هذه العناصر الـ#} many {المتابعة دون هذه العناصر الـ#} other {المتابعة دون هذه العناصر الـ#}}',
    more: '{n, plural, one {وعنصر آخر} two {وعنصران آخران} few {و# عناصر أخرى} many {و# عنصرًا آخر} other {و# عنصر آخر}}',
  },
  actionDialog: {
    failed: 'تعذّر إتمام الإجراء.',
    item: '{label}: {message}',
    outcomeUnknown: {
      title: 'تعذّر علينا التأكد من ذلك',
      message: `انقطع الاتصال قبل أن ينتهي الإجراء. ${imperative('تحقق', 'تحققي')} مما إذا كان قد تم قبل أن ${imperative('تشغّله', 'تشغّليه')} مرة أخرى.`,
    },
  },
};
