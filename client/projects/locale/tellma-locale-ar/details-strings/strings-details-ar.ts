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
 * Discard changes question, the notice of a refresh that failed under an edit, Edit's reason for a record outside the
 * user's permission, and the layout kit's read-only lock, error names, audit card, aside and failed editor load, and
 * its child grids' totals and failed load and its child files' list; under `detailsSave`, the save's overlay labels and
 * error summary, and under `detailsDialogs`, the concurrency, deletion and leave dialogs. The same shape as its English.
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
    uploadsFailed: 'لم يُحفظ شيء: تعذّر رفع ملف.',
    refreshFailed: 'تعذّر تحديث هذا السجل. تغييراتك محفوظة.',
    retry: 'إعادة المحاولة',
    discard: {
      title: 'تجاهل التغييرات؟',
      message: 'ستُفقد تغييراتك على هذا السجل.',
      confirm: 'تجاهل التغييرات',
      keep: 'متابعة التعديل',
    },
    outsideFilter: 'لا تشمل صلاحية «{permission}» الممنوحة لك هذا السجل.',
    readOnly: 'للقراءة فقط',
    hasErrors: '{label}، يحتوي على أخطاء',
    audit: 'معلومات التدقيق',
    aside: 'معلومات ذات صلة',
    editorFailed: 'تعذّر تحميل هذا الحقل للتعديل.',
    listEditorFailed: 'تعذّر تحميل هذه القائمة للتعديل.',
    total: 'إجمالي {label}',
    totals: 'الإجماليات',
    tableFailed: 'تعذّر تحميل هذا الجدول.',
    noFiles: 'لا توجد ملفات',
    filesFailed: 'تعذّر تحميل هذه الملفات.',
    fileRefused: '{name}: {message}',
    uploading: 'جارٍ رفع {name}',
  },
  detailsSave: {
    finishing: 'جارٍ إكمال الرفع…',
    saving: 'جارٍ الحفظ…',
    heading: 'أصلح {count, plural, one {مشكلة واحدة} two {مشكلتين} few {# مشكلات} many {# مشكلة} other {# مشكلة}} للحفظ',
    show: 'عرض المشكلات',
    more: 'و{count} أخرى',
    fixed: 'تم الإصلاح',
    field: '{label}: {message}',
    cell: '{collection} · الصف {row} · {column}: {message}',
    row: '{collection} · الصف {row}: {message}',
    uploadFailed: 'تعذّر رفع الملف. أعد المحاولة أو أزِله.',
  },
  detailsDialogs: {
    conflict: {
      title: 'تغيّر هذا السجل أثناء تعديلك',
      other: 'حفظ {name} نسخة أحدث {time}. أعد التحميل لتأخذ نسخته، أو استبدلها بنسختك.',
      self: 'حفظتَ هذا السجل في علامة تبويب أو نافذة أخرى {time}. أعد تحميل تلك النسخة، أو استبدلها بهذه.',
      unknown: 'أعد التحميل لتأخذ النسخة المحفوظة، أو استبدلها بنسختك.',
      overwrite: 'استبدال',
      reloadTheirs: 'تحميل نسخته',
      reloadSaved: 'تحميل النسخة المحفوظة',
      reload: 'إعادة التحميل',
    },
    deleted: {
      title: 'حُذف هذا السجل',
      message: 'حذفه أحدهم أثناء تعديلك، لذا لا يمكن حفظ تغييراتك.',
      discard: 'تجاهل التغييرات',
    },
    leave: {
      title: 'المغادرة دون حفظ؟',
      message: 'في هذا السجل تغييرات غير محفوظة، وستُفقد إذا غادرت الآن.',
      discard: 'تجاهل التغييرات',
      stay: 'البقاء',
    },
  },
};
