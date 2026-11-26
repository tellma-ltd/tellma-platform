// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The Arabic of the support tools: the support dialog's host, which the
// platform reads under `tm.supportHost` before the support tools load, and
// the Diagnostics panel, under `tm.support`. It is a table of its own beside
// the platform's main one, so each one's ceiling holds; `tmArabic()` merges
// it under `tm` on the first switch to Arabic. Labels and button names are
// verbal nouns, as Arabic interfaces write them; the two offline lines'
// imperatives conjugate for the addressee.

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
 * The Arabic of the support tools, under `tm`: the support dialog's title,
 * loading label, Close and offline lines, and the Diagnostics panel's
 * header, request grid, outcomes and buttons. The same shape as their
 * English.
 */
export const TM_SUPPORT_STRINGS_AR: TmStringTree = {
  supportHost: {
    title: 'الإبلاغ عن مشكلة',
    loading: 'جارٍ التحميل',
    close: 'إغلاق',
    dialog: `هذا غير متاح دون اتصال. ${imperative('اضغط', 'اضغطي')} {keys} لنسخ بيانات التشخيص.`,
    diagnostics: `بيانات التشخيص غير متاحة دون اتصال. ${imperative('اضغط', 'اضغطي')} {keys} لنسخها.`,
  },
  support: {
    header: {
      label: 'هذا التطبيق',
      ui: 'إصدار الواجهة',
      server: 'إصدار الخادم',
      worker: 'الإصدار دون اتصال',
      tenant: 'الشركة',
      tab: 'علامة التبويب',
      connection: 'الاتصال',
      leader: 'العلامة الرئيسية',
      hub: 'التحديثات المباشرة',
    },
    value: {
      unknown: 'غير معروف',
      none: 'لا يوجد',
      online: 'متصل',
      offline: 'غير متصل',
      thisTab: 'هذه العلامة',
      otherTab: 'علامة أخرى',
    },
    hub: {
      connecting: 'جارٍ الاتصال',
      connected: 'متصل',
      reconnecting: 'جارٍ إعادة الاتصال',
      offline: 'غير متصل',
      disabled: 'متوقف',
    },
    requests: {
      label: 'الطلبات الأخيرة',
      slowFailed: 'البطيئة والفاشلة فقط',
      time: 'الوقت',
      duration: 'المدة',
      outcome: 'النتيجة',
      request: 'الطلب',
      reference: 'المرجع',
      empty: 'لا توجد طلبات بعد.',
      emptyFiltered: 'لا توجد طلبات بطيئة أو فاشلة.',
      copied: 'تم نسخ الطلب.',
      copyFailed: 'تعذّر نسخ الطلب.',
    },
    outcome: {
      pending: 'قيد الانتظار',
      ok: 'تم',
      slow: 'بطيء',
      cancelled: 'أُلغي',
      error: 'فشل',
    },
    seconds: '{value} ث',
    copy: 'نسخ بيانات التشخيص',
    download: 'تنزيل ملف التشخيص',
    report: 'الإبلاغ عن مشكلة',
  },
};
