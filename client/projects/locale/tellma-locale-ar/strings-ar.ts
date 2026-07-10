/**
 * Arabic translations for the library's built-in strings (spec §7/DoD 13) —
 * same shape as TM_UI_STRINGS_EN; plurals use ICU MessageFormat with the
 * Arabic plural categories.
 */
/**
 * The imperative verb conjugates for the addressee's gender, so it branches
 * on the ambient {gender} context param (TM_UI_MESSAGE_CONTEXT — 'other' is
 * the base form) while the counted noun stays in a single plural block.
 */
const enter = '{gender, select, female {أدخلي} other {أدخل}}';

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
} as const;
