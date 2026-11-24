// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The Arabic of the platform's shell, which the shell frame reads under
// `tm.shell` and the tenantless pages under `tm.tenantless`. It is a table of
// its own beside the platform's main one, so that table's ceiling holds;
// `tmArabic()` merges the two under `tm` on the first switch to Arabic.

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
 * The Arabic of the shell, under `tm`: the frame's skip link and the tenant
 * list's title, the shared Sign in, the tenantless app bar's menus, the tenant
 * picker (search, loading, Connect, badges, unavailable reasons, empty,
 * no-match, failure and notice) and the signed-out page. The same shape as
 * their English.
 */
export const TM_SHELL_STRINGS_AR: TmStringTree = {
  shell: {
    signedOut: { signIn: 'تسجيل الدخول' },
    skip: 'الانتقال إلى المحتوى',
    tenants: 'الشركات',
  },
  tenantless: {
    bar: {
      language: 'اللغة',
      account: 'الحساب',
      myAccount: 'حسابي',
      signOut: 'تسجيل الخروج',
    },
    picker: {
      title: `${imperative('اختر', 'اختاري')} شركة`,
      search: 'البحث في الشركات',
      loading: 'جارٍ تحميل الشركات…',
      tenant: 'الشركة {id}',
      connect: 'دخول',
      connectTo: 'دخول إلى {name}',
      badges: {
        lastUsed: 'آخر استخدام',
        sandbox: 'بيئة تجريبية',
        readOnly: 'للقراءة فقط',
        frozen: 'قيد الصيانة',
        suspended: 'معلّقة',
        provisioning: 'قيد الإعداد',
        inactive: 'معطّلة',
      },
      unavailable: {
        inactive: 'أُزيل وصولك إلى هذه الشركة.',
        suspended: `معلّقة. ${imperative('تواصل', 'تواصلي')} مع المسؤول.`,
        provisioning: 'يجري إعداد هذه الشركة.',
      },
      empty: {
        title: imperative('لست عضوًا في أي شركة بعد', 'لستِ عضوة في أي شركة بعد'),
        message: `${imperative('اطلب', 'اطلبي')} من المسؤول دعوتك. ${imperative('أنت مسجّل الدخول', 'أنتِ مسجّلة الدخول')} باسم {email}.`,
      },
      noMatch: 'لا توجد شركات تطابق «{query}».',
      failed: 'تعذّر تحميل شركاتك.',
      notice: {
        named: 'لم يعد بإمكانك فتح {name}.',
        unnamed: 'لم يعد بإمكانك فتح الشركة {id}.',
        dismiss: 'إغلاق',
      },
    },
    signedOut: {
      title: imperative('لقد سجّلت الخروج', 'لقد سجّلتِ الخروج'),
      message: `${imperative('لقد سجّلت', 'لقد سجّلتِ')} الخروج من {displayName} على هذا الجهاز.`,
      signedInAs: `${imperative('أنت مسجّل الدخول', 'أنتِ مسجّلة الدخول')} باسم {name}.`,
      continue: 'متابعة',
    },
  },
};
