// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Arabic translations for the library's built-in strings —
 * same shape as TM_UI_STRINGS_EN; plurals use ICU MessageFormat with the
 * Arabic plural categories.
 */
/**
 * The imperative verb conjugates for the addressee's gender, so it branches
 * on the ambient {gender} context param (TM_UI_MESSAGE_CONTEXT — 'other' is
 * the base form) while the counted noun stays in a single plural block.
 */
const enter = '{gender, select, female {أدخلي} other {أدخل}}';

/** Grid imperatives reused across several strings, same {gender} pattern. */
const copyVerb = '{gender, select, female {انسخي} other {انسخ}}';
const insertVerb = '{gender, select, female {أدرجي} other {أدرج}}';

/**
 * Counted rows with full Arabic plural categories. `rowsObject` is the
 * direct object of an imperative (accusative: صفا واحدا); `rowsCounted`
 * follows a «تم …» masdar frame (genitive: صف واحد).
 */
const rowsObject =
  '{count, plural, zero {# صف} one {صفا واحدا} two {صفين} few {# صفوف} many {# صفا} other {# صف}}';
const rowsCounted =
  '{count, plural, zero {# صف} one {صف واحد} two {صفين} few {# صفوف} many {# صفا} other {# صف}}';

/**
 * The Arabic translations of the library's built-in strings — same shape as
 * `TM_UI_STRINGS_EN`; registered under the tmUi namespace of the 'ar'
 * language resources by `provideTellmaLocaleAr()`.
 */
export const TM_LOCALE_AR_STRINGS = {
  errors: {
    required: 'هذا الحقل مطلوب',
    email: `${enter} عنوان بريد إلكتروني صحيحا`,
    minLength: `${enter} {minLength, plural, one {حرفا واحدا على الأقل} two {حرفين على الأقل} few {# أحرف على الأقل} many {# حرفا على الأقل} other {# حرف على الأقل}}`,
    maxLength: `${enter} {maxLength, plural, one {حرفا واحدا كحد أقصى} two {حرفين كحد أقصى} few {# أحرف كحد أقصى} many {# حرفا كحد أقصى} other {# حرف كحد أقصى}}`,
    min: `${enter} قيمة لا تقل عن {min}`,
    max: `${enter} قيمة لا تزيد عن {max}`,
    pattern: 'القيمة لا تطابق التنسيق المطلوب',
    minDate: `${enter} تاريخا لا يسبق {minDate}`,
    maxDate: `${enter} تاريخا لا يتجاوز {maxDate}`,
  },
  select: {
    placeholder: '{gender, select, female {حددي خيارا} other {حدد خيارا}}',
  },
  grid: {
    loading: 'جار التحميل…',
    empty: 'لا توجد سجلات للعرض',
    newRow: 'صف جديد',
    rowHeader: 'الصف {index}',
    selectAll: 'تحديد كل الصفوف',
    selectRow: 'تحديد الصف',
    expand: 'توسيع الصف',
    collapse: 'طي الصف',
    menu: {
      cut: '{gender, select, female {قصي} other {قص}}',
      copy: copyVerb,
      copyWithHeaders: `${copyVerb} مع العناوين`,
      paste: '{gender, select, female {الصقي} other {الصق}}',
      pasteHint: '{gender, select, female {اضغطي} other {اضغط}} على {shortcut} للصق',
      insertAbove: `${insertVerb} ${rowsObject} أعلاه`,
      insertBelow: `${insertVerb} ${rowsObject} أدناه`,
      insertChild: `${insertVerb} صفا فرعيا`,
      deleteRows: `{gender, select, female {احذفي} other {احذف}} ${rowsObject}`,
    },
    op: {
      cellEdit: 'تحرير خلية',
      clear: 'المسح',
      paste: 'اللصق',
      fillDown: 'التعبئة لأسفل',
      cutMove: 'النقل',
      rowInsert: 'إدراج صفوف',
      rowDelete: 'حذف صفوف',
      rowMove: 'نقل صفوف',
      transaction: 'التغيير',
    },
    announce: {
      selection: 'تم تحديد {rows} × {cols}',
      selectionAll: 'تم تحديد كل الخلايا',
      copied:
        'تم نسخ {cells, plural, zero {# خلية} one {خلية واحدة} two {خليتين} few {# خلايا} many {# خلية} other {# خلية}}',
      copyRefused: 'يتعذر نسخ تحديد متعدد النطاقات بهذا الشكل',
      copyFailed: `فشل النسخ — {gender, select, female {حددي} other {حدد}} الخلايا و${copyVerb} مرة أخرى`,
      cutCancelled: 'تم إلغاء القص',
      pasted:
        '{cells, plural, =0 {لم يتم لصق أي شيء} one {تم لصق خلية واحدة} two {تم لصق خليتين} few {تم لصق # خلايا} many {تم لصق # خلية} other {تم لصق # خلية}}{errors, plural, =0 {} one {، خطأ واحد} two {، خطآن} few {، # أخطاء} many {، # خطأ} other {، # خطأ}}{pending, plural, =0 {} one {، خلية واحدة قيد المطابقة} two {، خليتان قيد المطابقة} few {، # خلايا قيد المطابقة} many {، # خلية قيد المطابقة} other {، # خلية قيد المطابقة}}',
      pasteRowsDropped: `تعذرت إضافة ${rowsCounted} — الجدول لا ينشئ صفوفا`,
      undone:
        '{skipped, plural, =0 {تم التراجع عن {action}} one {تم التراجع عن {action} — صف واحد لم يعد موجودا} two {تم التراجع عن {action} — صفان لم يعودا موجودين} few {تم التراجع عن {action} — # صفوف لم تعد موجودة} many {تم التراجع عن {action} — # صفا لم تعد موجودة} other {تم التراجع عن {action} — # صف لم تعد موجودة}}',
      redone:
        '{skipped, plural, =0 {تمت إعادة {action}} one {تمت إعادة {action} — صف واحد لم يعد موجودا} two {تمت إعادة {action} — صفان لم يعودا موجودين} few {تمت إعادة {action} — # صفوف لم تعد موجودة} many {تمت إعادة {action} — # صفا لم تعد موجودة} other {تمت إعادة {action} — # صف لم تعد موجودة}}',
      undoSkipped: 'تم تخطي التراجع — الصفوف المتأثرة لم تعد موجودة',
      redoSkipped: 'تم تخطي الإعادة — الصفوف المتأثرة لم تعد موجودة',
      rowsInserted: `تم إدراج ${rowsCounted}`,
      rowsDeleted: `تم حذف ${rowsCounted}`,
      rowsMoved: `تم نقل ${rowsCounted}`,
      moveRejected: 'يتعذر نقل الصف إلى داخل شجرته الفرعية',
      editorCancelledRowRemoved: 'تم إلغاء التحرير — تمت إزالة الصف',
      resolved:
        'تمت مطابقة {count, plural, zero {# تسمية} one {تسمية واحدة} two {تسميتين} few {# تسميات} many {# تسمية} other {# تسمية}}{errors, plural, =0 {} one {، واحدة لم تتطابق} two {، اثنتان لم تتطابقا} few {، # لم تتطابق} many {، # لم تتطابق} other {، # لم تتطابق}}',
      lazyLoadFailed: 'تعذر تحميل الصفوف الفرعية',
      errorJump: 'الخطأ {index} من {count}',
      checkedCount: 'تم تحديد {selected} من {total}',
      loaded:
        '{count, plural, =0 {لا توجد سجلات} one {تم تحميل سجل واحد} two {تم تحميل سجلين} few {تم تحميل # سجلات} many {تم تحميل # سجلا} other {تم تحميل # سجل}}',
      loading: 'جار التحميل',
    },
    cellErrors: {
      invalidInput: '«{text}» ليست قيمة {column} صالحة؛ يبقى الحقل فارغا حتى يتم التصحيح.',
      notFound: 'لا يوجد {collection} باسم «{label}»',
      ambiguous: '«{label}» يطابق أكثر من {collection}',
      tally:
        '{count, plural, zero {# خطأ} one {خطأ واحد} two {خطآن} few {# أخطاء} many {# خطأ} other {# خطأ}}',
      pending:
        '{count, plural, zero {# خلية} one {خلية واحدة} two {خليتان} few {# خلايا} many {# خلية} other {# خلية}} قيد المطابقة',
      next: 'الخطأ التالي',
      previous: 'الخطأ السابق',
    },
    find: {
      label: 'البحث في الجدول',
      counter: '{index} من {count}',
      noMatches: 'لا توجد تطابقات',
      next: 'التطابق التالي',
      previous: 'التطابق السابق',
      close: 'إغلاق البحث',
    },
  },
} as const;
