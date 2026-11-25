// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The Arabic of the platform's shell, which the shell frame reads under
// `tm.shell`, the tenantless pages under `tm.tenantless`, the tenant shell's
// app bar under `tm.tenantShell`, its session menu, markers and dialogs'
// frames under `tm.shellOverlays`, its installed-mode Back and Forward under
// `tm.shellHistory`, and its About and Shortcuts dialogs under
// `tm.aboutDialog` and `tm.shortcutsDialog`. It is a table of
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
 * no-match, failure and notice), the signed-out page, the tenant app bar,
 * the session menu, the markers' popovers, the installed-mode Back and
 * Forward, and the About and Shortcuts dialogs. The same shape as their
 * English.
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
  tenantShell: {
    bar: {
      home: 'الصفحة الرئيسية في {name}',
      menu: 'القائمة',
      search: 'بحث',
      slotCount: '{label}، {count}',
      account: 'الحساب: {user}، {tenant}',
      accountUser: 'الحساب: {user}',
      accountSandbox: '، بيئة تجريبية',
      accountReadOnly: '، للقراءة فقط',
      accountMaintenance: '، قيد الصيانة',
      markers: {
        sandbox: 'بيئة تجريبية',
        readOnly: 'للقراءة فقط',
        maintenance: 'صيانة',
      },
    },
    failed: 'تعذّر تحميل هذه الشركة.',
  },
  shellHistory: {
    back: 'رجوع',
    forward: 'تقدّم',
  },
  shellOverlays: {
    menu: {
      label: 'الحساب',
      tenant: 'الشركة {id}',
      tenantSandbox: ' · بيئة تجريبية',
      tenantReadOnly: ' · للقراءة فقط',
      tenantMaintenance: ' · قيد الصيانة',
      switch: 'تبديل',
      switchTenant: 'تبديل الشركة',
      mySettings: 'إعداداتي',
      language: 'اللغة',
      calendar: 'التقويم',
      theme: 'المظهر',
      themeSystem: 'النظام',
      themeLight: 'فاتح',
      themeDark: 'داكن',
      signOut: 'تسجيل الخروج',
    },
    markers: {
      sandbox: `${imperative('أنت', 'أنتِ')} في بيئة تجريبية. التغييرات هنا لا تمسّ البيانات الفعلية.`,
      openLive: 'فتح {name}',
      readOnly: `{tenant} للقراءة فقط. ${imperative('يمكنك', 'يمكنكِ')} عرض البيانات دون تغييرها. ${imperative('تواصل', 'تواصلي')} مع المسؤول.`,
      maintenance: `{tenant} قيد الصيانة. ${imperative('يمكنك', 'يمكنكِ')} عرض البيانات دون تغييرها حتى تنتهي.`,
    },
    dialogs: {
      about: 'حول',
      shortcuts: 'اختصارات لوحة المفاتيح',
      loading: 'جارٍ التحميل…',
      failed: `تعذّر فتح هذا. ${imperative('تحقّق', 'تحقّقي')} من اتصالك ثم ${imperative('حاول', 'حاولي')} مجددًا.`,
    },
  },
  aboutDialog: {
    copy: 'نسخ الإصدار',
    server: 'الخادم',
    platform: 'المنصة',
    unknown: '—',
    serverNewer: `يعمل الخادم بالإصدار {server}. ${imperative('أعد', 'أعيدي')} التحميل للتحديث.`,
    updating: 'جارٍ جلب التحديث…',
    updateFailed: `تعذّر تنزيل التحديث. ${imperative('تحقّق', 'تحقّقي')} من اتصالك ثم ${imperative('حاول', 'حاولي')} مجددًا.`,
    reloadAnyway: 'إعادة التحميل على أي حال',
    terms: 'شروط الخدمة',
    privacy: 'الخصوصية',
  },
  shortcutsDialog: {
    filter: 'تصفية الاختصارات',
    action: 'الإجراء',
    keys: 'المفاتيح',
    general: 'عام',
    page: 'هذه الصفحة',
    grids: 'القوائم والجداول',
    launcher: 'المشغّل',
    launcherMove: 'التنقّل بين النتائج',
    launcherOpen: 'فتح النتيجة',
    launcherNewTab: 'فتح النتيجة في علامة تبويب جديدة',
    launcherClose: 'إغلاق المشغّل',
    noMatch: 'لا يطابق أي اختصار.',
  },
};
