// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The Arabic of the platform's Queryex tools, which the expression editor
// and the path picker read under `tm.queryex` and `tm.pathPicker`. It is a
// table of its own beside the platform's main one, so that table's ceiling
// holds; `tmArabic()` merges the two under `tm` on the first switch to
// Arabic. Code stays left to right; only the words around it are Arabic.

/**
 * The Arabic of the expression editor and the path picker, under `tm`: the
 * editor's status line, problems list, parameter chips and type names, and
 * the picker's search, count and date parts. The same shape as their
 * English.
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
};
