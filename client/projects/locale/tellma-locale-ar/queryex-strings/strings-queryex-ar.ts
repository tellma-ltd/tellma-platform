// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The Arabic of the platform's Queryex tools and of the search page they
// serve, which the expression editor, the path picker and the search page
// read under `tm.queryex`, `tm.pathPicker` and `tm.search`. It is a table of
// its own beside the platform's main one, so that table's ceiling holds;
// `tmArabic()` merges the two under `tm` on the first switch to Arabic.
// Code stays left to right; only the words around it are Arabic.

/**
 * The Arabic of the expression editor, the path picker and the search page,
 * under `tm`: the editor's status line, problems list, parameter chips and
 * type names; the picker's search, count and date parts; and the search
 * page's toolbar, result count and the states of its results. The same
 * shape as their English.
 */
export const TM_QUERYEX_STRINGS_AR: TmStringTree = {
  queryex: {
    noProblems: 'لا توجد مشكلات',
    checking: 'جارٍ التحقق…',
    offline: 'يتعذّر التحقق دون اتصال',
    failed: 'تعذّر التحقق من التعبير',
    retry: 'إعادة المحاولة',
    problemAt: 'السطر {line}: {message}',
    more: '+{count} أخرى',
    problems: '{count, plural, one {مشكلة واحدة} two {مشكلتان} few {# مشكلات} other {# مشكلة}}',
    parameters: 'المعلمات',
    untyped: 'دون نوع',
    conflicting: 'أنواع متعارضة',
    type: {
      Bool: 'نعم أو لا',
      Numeric: 'رقم',
      String: 'نص',
      Guid: 'معرّف',
      Date: 'تاريخ',
      DateTime: 'تاريخ ووقت',
      DateTimeOffset: 'لحظة',
      HierarchyId: 'تسلسل هرمي',
      Geography: 'موقع',
    },
  },
  pathPicker: {
    fields: 'الحقول',
    search: 'البحث في الحقول',
    count: '{count, plural, one {حقل واحد} two {حقلان} few {# حقول} many {# حقلًا} other {# حقل}}',
    none: 'لا توجد حقول مطابقة',
    gregorian: '{part} (ميلادي)',
    part: {
      year: 'السنة',
      quarter: 'الربع',
      month: 'الشهر',
      week: 'الأسبوع',
      day: 'اليوم',
      quarterOfYear: 'ربع السنة',
      monthOfYear: 'شهر السنة',
      dayOfWeek: 'يوم الأسبوع',
    },
  },
  search: {
    expressionColumn: 'تعبير',
    refresh: 'تحديث',
    more: 'المزيد',
    moreActions: 'إجراءات أخرى',
    chooseColumns: 'اختيار الأعمدة',
    loading: 'جارٍ تحميل النتائج',
    search: 'بحث',
    searchHint: 'تنقّل بين النتائج بمفاتيح الأسهم، واضغط Enter لفتح إحداها.',
    openInNewTab: 'فتح في علامة تبويب جديدة',
    copyLink: 'نسخ الرابط',
    linkCopied: 'تم نسخ الرابط',
    linkCopyFailed: 'تعذّر نسخ الرابط',
    results: '{count, plural, one {نتيجة واحدة} two {نتيجتان} few {# نتائج} other {# نتيجة}}',
    resultsAtLeast: '{count}+ نتيجة',
    offline: 'أنت غير متصل',
    noData: 'لا يوجد شيء هنا بعد',
    noDataMessage: 'أضف واحدًا للبدء.',
    noMatches: 'لا توجد نتائج مطابقة',
    noMatchesMessage: 'امسح عوامل التصفية أو وسّع البحث.',
    clearFilters: 'مسح عوامل التصفية',
    clearSearch: 'مسح البحث',
    invalidFilter: 'تعبير التصفية هذا غير صالح',
    invalidLink: 'عامل تصفية هذا الرابط غير صالح',
    calculation: 'يتعذّر حساب عمود أو عامل تصفية لبعض الصفوف',
    divideByZero: 'فيه قسمة على صفر.',
    overflow: 'إحدى النتائج كبيرة جدًا.',
    failed: 'تعذّر تحميل النتائج.',
    query: 'بحث',
    open: 'فتح',
  },
};
