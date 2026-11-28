// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The Arabic of the home page and the dashboard grid, which they read under
// `tm.home` and `tm.dashboard`. It is a table of its own beside the
// platform's main one, so each one's ceiling holds; `tmArabic()` merges them
// under `tm` on the first switch to Arabic. The merge is shallow, so the
// `home` scope holds the home page's title too, which the primary reads
// before the home page's chunk loads. Every string reads the same to either
// gender: the empty state's line is passive and the grid's label a noun.

/**
 * The Arabic of the home page and the dashboard grid, under `tm`: the home
 * page's title, its three greetings and its grid's label, and the grid's
 * tile states, header and status line with the built-in widgets' titles,
 * empty and offline states. The same shape as their English.
 */
export const TM_DASHBOARDS_STRINGS_AR: TmStringTree = {
  home: {
    title: 'الرئيسية',
    greeting: {
      morning: 'صباح الخير، {name}',
      afternoon: 'مساء الخير، {name}',
      evening: 'مساء الخير، {name}',
    },
    grid: 'عملك',
  },
  dashboard: {
    unavailable: 'هذه الأداة غير متاحة.',
    loadFailed: 'تعذّر تحميل هذه الأداة.',
    options: 'خيارات الأداة {title}',
    refresh: 'تحديث',
    viewAll: 'عرض الكل',
    viewAllOf: 'عرض الكل: {title}',
    updated: 'آخر تحديث {time}',
    stale: 'تعذّر التحديث. البيانات المعروضة من {time}.',
    totalsOmitted: 'المجاميع غير معروضة',
    totalsOmittedHint: 'لا يظهر صف المجاميع لأن الصفوف لا تتسع كلها في الصفحة الأولى.',
    assignmentsOffline: 'أنت غير متصل. تظهر مهامك عند عودة الاتصال.',
    toMe: {
      title: 'مهامي',
      empty: 'لا شيء بانتظارك',
    },
    byMe: {
      title: 'ما أسندتُه',
      empty: 'لا مهام أسندتها ما زالت مفتوحة',
    },
    recents: {
      title: 'الصفحات الأخيرة',
      empty: 'تظهر هنا الصفحات التي تمت زيارتها',
    },
  },
};
