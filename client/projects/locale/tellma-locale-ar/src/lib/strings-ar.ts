/**
 * Arabic translations for the library's built-in strings (spec §7/DoD 13) —
 * same shape as TM_UI_STRINGS_EN; plurals use ICU MessageFormat with the
 * Arabic plural categories.
 */
export const TM_LOCALE_AR_STRINGS = {
  errors: {
    required: 'هذا الحقل مطلوب',
    email: 'أدخل عنوان بريد إلكتروني صحيحًا',
    minLength:
      '{minLength, plural, one {أدخل حرفًا واحدًا على الأقل} two {أدخل حرفين على الأقل} few {أدخل # أحرف على الأقل} other {أدخل # حرفًا على الأقل}}',
    maxLength:
      '{maxLength, plural, one {أدخل حرفًا واحدًا كحد أقصى} two {أدخل حرفين كحد أقصى} few {أدخل # أحرف كحد أقصى} other {أدخل # حرفًا كحد أقصى}}',
    min: 'أدخل قيمة لا تقل عن {min}',
    max: 'أدخل قيمة لا تزيد عن {max}',
    pattern: 'القيمة لا تطابق التنسيق المطلوب',
    minDate: 'أدخل تاريخًا لا يسبق {minDate}',
    maxDate: 'أدخل تاريخًا لا يتجاوز {maxDate}',
  },
  formField: {
    required: 'مطلوب',
  },
  select: {
    placeholder: 'اختر خيارًا',
  },
} as const;
