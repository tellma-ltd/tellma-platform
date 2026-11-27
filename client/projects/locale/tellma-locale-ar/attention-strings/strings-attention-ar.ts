// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The Arabic of the attention panels, rows and pages: the bell's and the
// tray's panels, under `tm.attentionPanels`, the attention rows (an
// assignment's caption title and the assignment list's lines), under
// `tm.attentionRows`, and the Notifications section of My settings, under
// `tm.attentionPages`. It is a table of its own beside the platform's main
// one, so each one's ceiling holds; `tmArabic()` merges it under `tm` on the
// first switch to Arabic. Labels and button names are verbal nouns, as
// Arabic interfaces write them; the questions conjugate for the addressee.

/**
 * A verb that conjugates for the addressee's gender, through the ambient
 * `{gender}` message parameter (`other` is the base form).
 *
 * @param male - The base form.
 * @param female - The form addressed to a woman.
 * @returns The ICU select.
 */
function addressed(male: string, female: string): string {
  return `{gender, select, female {${female}} other {${male}}}`;
}

/**
 * The Arabic of the attention panels, rows and pages, under `tm`: the
 * bell's panel, its In progress section and the tray's panel, the
 * assignment captions and list lines, and the Notifications section of My
 * settings. The same shape as their English.
 */
export const TM_ATTENTION_STRINGS_AR: TmStringTree = {
  attentionPanels: {
    // The bell's panel.
    bell: {
      markAllRead: 'تعليم الكل كمقروء',
      more: 'خيارات إضافية',
      settings: 'إعدادات الإشعارات',
      clearRead: 'مسح الإشعارات المقروءة',
      clearReadTitle: `هل ${addressed('تريد', 'تريدين')} مسح الإشعارات المقروءة؟`,
      clearReadMessage: 'تُحذف من قائمتك.',
      download: 'تنزيل',
      clear: 'مسح',
      unread: 'غير مقروء',
      cleared: 'مُسح الإشعار',
      fresh: '{count, number} جديدة',
      arrived:
        '{count, plural, one {إشعار جديد واحد} two {إشعاران جديدان} few {# إشعارات جديدة} many {# إشعارًا جديدًا} other {# إشعار جديد}}',
      paused: 'التحديثات المباشرة متوقفة · آخر تحديث {time}',
      refresh: 'تحديث',
      empty: 'لا جديد لديك',
      seeAll: 'عرض كل الإشعارات',
    },
    // The bell's In progress section.
    jobs: {
      heading: 'قيد التشغيل',
      more: '+{count, number} أخرى في «مهامي في الخلفية»',
      waiting: 'في انتظار البدء',
      working: 'جارٍ العمل…',
      stopping: 'جارٍ الإيقاف…',
      held: 'معلّقة',
      heldLine: 'توقفت مؤقتًا بعد انقطاع في الخدمة',
      awaiting: 'بانتظار التأكيد',
      awaitingLine: 'بانتظار تأكيدك',
      review: 'مراجعة',
      // Stop and its confirmation, by the job's kind: `import` for an import, else any other.
      stop: '{kind, select, import {إيقاف الاستيراد} other {إيقاف}}',
      stopTitle: `{kind, select, import {هل ${addressed('تريد', 'تريدين')} إيقاف هذا الاستيراد؟} other {هل ${addressed('تريد', 'تريدين')} إيقاف هذه المهمة؟}}`,
      stopMessage: '{kind, select, import {تبقى الصفوف المحفوظة محفوظة، ولن يُستورد باقي الملف.} other {يبقى ما حُفظ من العمل محفوظًا.}}',
      keepRunning: 'متابعة التشغيل',
    },
    // The tray's panel.
    tray: {
      empty: 'لا شيء بانتظارك',
      seeAll: 'عرض كل المهام المسندة',
    },
    // The panels' page-context actions.
    actions: { openItem: 'فتح' },
  },
  attentionRows: {
    // An assignment's caption: the entity's singular label, then the target's code.
    assignment: { title: '{entity} {code}' },
    // The assignment list's second lines, its mark and its loading state.
    list: {
      comment: '{assigner}: {comment}',
      returned: 'أعاده {assigner}: {comment}',
      assignedBy: 'أسنده {assigner}',
      automatic: 'أُسند تلقائيًا',
      with: 'لدى {assignee}',
      quote: '«{comment}»',
      joiner: ' · ',
      new: 'جديد',
      loading: 'جارٍ تحميل المهام المسندة',
    },
    separator: '، ',
  },
  attentionPages: {
    // The Notifications section of My settings: the pop-up card, the grid and the save status.
    settings: {
      popups: 'الرسائل المنبثقة',
      popupsLabel: 'إظهار رسالة منبثقة عند وصول إشعار أو مهمة مسندة',
      type: 'الإشعار',
      cell: '{type}، {channel}',
      alwaysOn: 'مفعّل دائمًا',
      noInbox: 'لا يظهر داخل التطبيق',
      saving: 'جارٍ الحفظ…',
      saved: 'تم حفظ كل التغييرات',
      failed: 'تعذّر حفظ التغييرات',
      retry: 'إعادة المحاولة',
      loadFailed: 'تعذّر تحميل إعدادات الإشعارات.',
    },
    // The test delivery card; its hints ask the addressee to act.
    test: {
      heading: 'رسائل تجريبية',
      send: 'إرسال رسالة تجريبية',
      noAddress: 'لا يوجد عنوان',
      sent: 'تم الإرسال',
      noEmail: `${addressed('أضف', 'أضيفي')} عنوان بريد إلكتروني في ملفك الشخصي أولًا.`,
      noContact: `${addressed('أضف', 'أضيفي')} عنوان تواصل في ملفك الشخصي أولًا.`,
      notConfigured: '{channel} غير مُعدّ على هذا الخادم.',
    },
    // The section's page actions.
    actions: {
      set: 'تشغيل قناة إشعار أو إيقافها',
      test: 'إرسال رسالة تجريبية',
    },
  },
};
