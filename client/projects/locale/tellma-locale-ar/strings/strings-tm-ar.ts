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
    updating: 'جارٍ تنزيل التحديث…',
    updateFailed: `تعذّر تنزيل التحديث. ${imperative('تحقق', 'تحققي')} من اتصالك ${imperative('وحاول', 'وحاولي')} مرة أخرى.`,
    reloadAnyway: 'إعادة التحميل على أي حال',
    notSent: { message: `تغييراتك محفوظة. ${imperative('احفظ', 'احفظي')} مرة أخرى عند عودة الاتصال.` },
    outcomeUnknown: {
      title: 'تعذّر علينا التأكد من ذلك',
      message: `انقطع الاتصال قبل أن يستجيب {displayName}. ${imperative('تحقق', 'تحققي')} مما إذا تمت العملية قبل أن ${imperative('تحاول', 'تحاولي')} مرة أخرى.`,
    },
    outsideFilter: {
      title: 'صلاحياتك لا تشمل هذا السجل',
      message: `صلاحية «{permission}» لديك تشمل بعض السجلات فقط. ${imperative('اطلب', 'اطلبي')} من المسؤول إن كنت بحاجة إلى هذا السجل.`,
    },
    restrictedPath: {
      title: 'لا يمكنك رؤية {field} هنا',
      message: `يتطلب ذلك صلاحية «{resource}: قراءة» كاملة. ${imperative('اطلب', 'اطلبي')} من المسؤول منحها لك.`,
    },
    missing: { title: '{entity} هذا غير موجود أو لا يمكنك رؤيته', record: 'هذا السجل غير موجود أو لا يمكنك رؤيته' },
    readOnly: { title: '{tenant} للقراءة فقط', message: `يمكنك عرض البيانات دون تغييرها. ${imperative('تواصل', 'تواصلي')} مع المسؤول.` },
    frozen: { title: '{tenant} قيد الصيانة', message: 'يمكنك عرض البيانات دون تغييرها حتى تنتهي الصيانة.' },
    unavailable: { title: '{displayName} مشغول', message: `${imperative('حاول', 'حاولي')} مرة أخرى بعد قليل.` },
    serverOutdated: { title: 'جارٍ تحديث {displayName}', message: `${imperative('حاول', 'حاولي')} مرة أخرى بعد دقيقة.` },
    server: {
      title: 'حدث خطأ ما',
      message: `تعذّر على {displayName} إتمام ذلك. ${imperative('حاول', 'حاولي')} مرة أخرى. إن تكرر ذلك، ${imperative('أرسل', 'أرسلي')} هذا المرجع إلى الدعم.`,
    },
    badRequest: { title: 'تعذّر إتمام ذلك' },
    suspended: { title: '{tenant} معلّقة', message: `${imperative('تواصل', 'تواصلي')} مع المسؤول.`, switch: 'تبديل الشركة' },
    ended: { title: 'انتهت جلستك', message: `${imperative('سجّل', 'سجّلي')} الدخول للمتابعة. عملك محفوظ.` },
    stepUp: {
      title: `${imperative('أكّد', 'أكّدي')} هويتك`,
      message: `يتطلب ذلك أن ${imperative('تسجّل', 'تسجّلي')} الدخول مرة أخرى. ستعود إلى هذه الصفحة مع تغييراتك غير المحفوظة.`,
      continue: 'متابعة',
    },
    details: { status: 'الحالة', code: 'الرمز', time: 'الوقت', route: 'الصفحة', version: 'الإصدار', permission: 'الصلاحية' },
  },
  session: {
    signingOut: 'جارٍ تسجيل الخروج…',
    waiting: 'جارٍ انتظار انتهاء الحفظ…',
    signOut: 'تسجيل الخروج',
    dirty: {
      title: 'تسجيل الخروج وتجاهل التغييرات غير المحفوظة؟',
      message: 'لديك تغييرات غير محفوظة في {count, plural, one {علامة تبويب واحدة} two {علامتي تبويب} few {# علامات تبويب} many {# علامة تبويب} other {# علامة تبويب}}.',
    },
    busy: {
      title: 'تسجيل الخروج أثناء الحفظ؟',
      message: 'لا تزال عملية حفظ جارية. إن سجّلت الخروج الآن، فلن تعرف ما إذا اكتملت.',
    },
    failed: `تعذّر تسجيل الخروج. ${imperative('تحقق', 'تحققي')} من اتصالك ${imperative('وحاول', 'وحاولي')} مرة أخرى.`,
    ended: {
      title: 'انتهت جلستك',
      message: `${imperative('سجّل', 'سجّلي')} الدخول للمتابعة. عملك محفوظ.`,
    },
  },
  updates: { banner: 'يتوفر إصدار جديد.' },
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
};
