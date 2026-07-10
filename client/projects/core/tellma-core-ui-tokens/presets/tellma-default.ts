import type { TmSchemeColors, TmTokens } from '../contract/tokens';

/**
 * The default preset, reproducing `tellma-brand/design-system` (§4): same
 * hexes, same `--field-*` / focus-ring / spacing / type tokens, same
 * `[data-theme=dark]` inversion. One departure fixed by the spec: the focus
 * ring color is teal-500 (clears the 3:1 focus-indicator ratio vs the white
 * field; the brand's older spacing.css still carried teal-400).
 */

const light: TmSchemeColors = {
  colorScheme: 'light',
  text: {
    strong: '{ink.900}',
    body: '{grey.700}',
    secondary: '{grey.500}',
    muted: '{grey.400}',
    onDark: '{white}',
    link: '{teal.600}',
  },
  surface: {
    page: '{white}',
    subtle: '{grey.25}',
    sunken: '{grey.50}',
    card: '{white}',
    inverse: '{ink.900}',
    hover: '{grey.25}',
    selected: '{teal.50}',
  },
  border: {
    subtle: '{grey.100}',
    default: '{grey.200}',
    strong: '{grey.300}',
    divider: '{grey.100}',
  },
  // Light teal selection highlight; ink text keeps 14:1.
  selection: {
    bg: '{teal.100}',
    text: '{ink.900}',
  },
  action: {
    // Teal that CARRIES TEXT is teal-600 (5.67:1 with white); the canonical
    // logo teal-400 is decorative only (2.97:1 — see contrastExceptions).
    primary: '{teal.600}',
    primaryHover: '{teal.700}',
    primaryActive: '{teal.800}',
    onPrimary: '{white}',
    accent: '{teal.400}',
  },
  status: {
    success: { fg: '#2E7D5B', bg: '#E5F2EC', border: '#B6DDC9' },
    warning: { fg: '#B7791F', bg: '#FBF1DF', border: '#ECD5A6' },
    error: { fg: '#C0392B', bg: '#FAE8E6', border: '#EEC2BC' },
    info: { fg: '{teal.500}', bg: '{teal.50}', border: '{teal.100}' },
  },
  field: {
    bg: '{white}',
    bgDisabled: '{grey.50}',
    bgFilled: '{grey.25}',
    border: '{grey.200}',
    borderHover: '{grey.300}',
    borderFocus: '{teal.500}',
    borderInvalid: '{status.error.fg}',
    text: '{ink.900}',
    textDisabled: '{grey.400}',
    placeholder: '{grey.400}',
    icon: '{grey.400}',
  },
};

const dark: TmSchemeColors = {
  colorScheme: 'dark',
  // Inverted cool neutral ramp: light end = dark surfaces, dark end = light
  // text. Components built on the grey scale flip automatically.
  primitiveOverrides: {
    white: '#16252D',
    grey: {
      25: '#1C2C34',
      50: '#21323A',
      100: '#2A3C44',
      200: '#374B53',
      300: '#475B63',
      400: '#74888E',
      500: '#94A5AB',
      600: '#B1C1C5',
      700: '#CDD8DC',
      800: '#E3EBED',
      900: '#F2F7F8',
    },
    teal: {
      50: 'rgba(76, 160, 182, 0.16)',
      100: 'rgba(76, 160, 182, 0.34)',
      700: '#8FD0DE',
    },
  },
  text: {
    strong: '{grey.900}',
    body: '{grey.700}',
    secondary: '{grey.500}',
    muted: '{grey.400}',
    onDark: '{grey.900}',
    link: '{teal.300}',
  },
  surface: {
    page: '#0D181E',
    subtle: '#0D181E',
    sunken: '#0A1418',
    card: '{white}',
    inverse: '#1C2C34',
    hover: '#20313A',
    selected: 'rgba(76, 160, 182, 0.18)',
  },
  // White at low alpha reads cleaner than solid greys on dark.
  border: {
    subtle: 'rgba(255, 255, 255, 0.07)',
    default: 'rgba(255, 255, 255, 0.12)',
    strong: 'rgba(255, 255, 255, 0.20)',
    divider: 'rgba(255, 255, 255, 0.07)',
  },
  // Deep teal selection; the inverted light text keeps 10.5:1. teal-800 is
  // NOT overridden in this scheme, so the ref hits the base ramp.
  selection: {
    bg: '{teal.800}',
    text: '{grey.900}',
  },
  action: {
    // Lighter teal on dark carries INK text (ink on teal-400 = 6.12:1).
    primary: '{teal.400}',
    primaryHover: '{teal.300}',
    primaryActive: '{teal.200}',
    onPrimary: '{ink.900}',
    accent: '{teal.400}',
  },
  status: {
    success: { fg: '#5FC79A', bg: 'rgba(46, 125, 91, 0.18)', border: 'rgba(46, 125, 91, 0.42)' },
    warning: { fg: '#E0A93E', bg: 'rgba(183, 121, 31, 0.20)', border: 'rgba(183, 121, 31, 0.44)' },
    error: { fg: '#E06A5C', bg: 'rgba(192, 57, 43, 0.20)', border: 'rgba(192, 57, 43, 0.44)' },
    info: { fg: '{teal.300}', bg: 'rgba(76, 160, 182, 0.16)', border: 'rgba(76, 160, 182, 0.34)' },
  },
  field: {
    bg: '{white}',
    bgDisabled: '#1A2930',
    bgFilled: '{grey.25}',
    border: 'rgba(255, 255, 255, 0.14)',
    borderHover: 'rgba(255, 255, 255, 0.24)',
    borderFocus: '{teal.300}',
    borderInvalid: '{status.error.fg}',
    text: '{grey.900}',
    textDisabled: '{grey.400}',
    placeholder: '{grey.400}',
    icon: '{grey.400}',
  },
};

export const tmTokensDefault: TmTokens = {
  primitive: {
    color: {
      ink: { 700: '#163542', 800: '#0A2530', 900: '#001722' },
      teal: {
        50: '#EAF4F7',
        100: '#CBE6EC',
        200: '#A3D3DD',
        300: '#74BACA',
        400: '#4CA0B6',
        500: '#3E899D',
        600: '#316E80',
        700: '#265767',
        800: '#1B3F4B',
        900: '#0E2832',
      },
      grey: {
        25: '#F7FAFB',
        50: '#EEF3F4',
        100: '#E1E8EA',
        200: '#CBD6D9',
        300: '#A8B7BC',
        400: '#7C8E94',
        500: '#56686F',
        600: '#3C4E55',
        700: '#283A41',
        800: '#14262E',
        900: '{ink.900}',
      },
      white: '#FEFEFE',
    },
    radius: { xs: '4px', sm: '6px', md: '10px', lg: '16px', xl: '24px', full: '999px' },
    space: {
      0: '0',
      1: '4px',
      2: '8px',
      3: '12px',
      4: '16px',
      5: '20px',
      6: '24px',
      8: '32px',
      10: '40px',
      12: '48px',
      16: '64px',
      20: '80px',
      24: '96px',
    },
    font: {
      sans: "'Noto Sans', system-ui, -apple-system, 'Segoe UI', sans-serif",
      arabic: "'Noto Sans Arabic', 'Noto Sans', sans-serif",
      mono: "'Noto Sans Mono', ui-monospace, 'SF Mono', Menlo, monospace",
      // Brand faces per script first (core-ui vendors the Latin faces; the
      // Arabic faces arrive with @tellma/locale-ar), generics last.
      ui: ["'Noto Sans'", "'Noto Sans Arabic'", 'system-ui', '-apple-system', "'Segoe UI'", 'sans-serif'],
      size: { xs: '12px', sm: '14px', base: '16px', lg: '18px' },
      weight: { regular: '400', medium: '500', semibold: '600', bold: '700' },
      leading: { tight: '1.2', snug: '1.35', body: '1.6', arabic: '1.9' },
    },
    border: { width: '1px' },
    shadow: {
      xs: '0 1px 2px rgba(0, 23, 34, 0.05)',
      sm: '0 1px 3px rgba(0, 23, 34, 0.07), 0 1px 2px rgba(0, 23, 34, 0.04)',
      md: '0 4px 12px rgba(0, 23, 34, 0.08), 0 1px 3px rgba(0, 23, 34, 0.05)',
      lg: '0 12px 32px rgba(0, 23, 34, 0.12), 0 2px 6px rgba(0, 23, 34, 0.06)',
    },
    motion: {
      durationFast: '120ms',
      durationNormal: '180ms',
      durationSlow: '280ms',
      easeStandard: 'cubic-bezier(0.2, 0, 0, 1)',
      easeOut: 'cubic-bezier(0, 0, 0, 1)',
      easeInOut: 'cubic-bezier(0.45, 0, 0.2, 1)',
    },
  },
  semantic: {
    colorScheme: { light, dark },
    focusRing: { width: '2px', color: '{teal.500}', offset: '2px' },
    formField: {
      radius: '{radius.sm}',
      height: '38px',
      heightSm: '30px',
      heightLg: '46px',
      paddingX: '12px',
      paddingY: '8px',
      fontSize: '{font.size.sm}',
    },
    leadingByLang: {
      ar: '{font.leading.arabic}',
      en: '{font.leading.body}',
    },
  },
  component: {
    // tm-checkbox (§3.3): the visible box renders at the brand 18px while
    // the hit target is padded past the 24px minimum.
    checkbox: { boxSize: '18px' },
    // tm-select (§3.4): panel + option-row geometry (touch-comfortable rows).
    select: { panelMaxHeight: '280px', optionHeight: '36px' },
  },
  contrastPairs: [
    // Text on its surface.
    { fg: '--text-strong', bg: '--surface-page', kind: 'text' },
    { fg: '--text-body', bg: '--surface-card', kind: 'text' },
    { fg: '--text-secondary', bg: '--surface-page', kind: 'text' },
    { fg: '--text-muted', bg: '--surface-page', kind: 'text' },
    { fg: '--text-link', bg: '--surface-page', kind: 'text' },
    { fg: '--text-on-dark', bg: '--surface-inverse', kind: 'text' },
    { fg: '--color-on-primary', bg: '--color-primary', kind: 'text' },
    { fg: '--selection-text', bg: '--selection-bg', kind: 'text' },
    // Field group.
    { fg: '--field-text', bg: '--field-bg', kind: 'text' },
    { fg: '--field-text-disabled', bg: '--field-bg-disabled', kind: 'text' },
    { fg: '--field-placeholder', bg: '--field-bg', kind: 'text' },
    { fg: '--field-icon', bg: '--field-bg', kind: 'uiComponent' },
    { fg: '--field-border', bg: '--field-bg', kind: 'uiComponent' },
    { fg: '--field-border-hover', bg: '--field-bg', kind: 'uiComponent' },
    { fg: '--field-border-focus', bg: '--field-bg', kind: 'uiComponent' },
    { fg: '--field-border-invalid', bg: '--field-bg', kind: 'uiComponent' },
    // Focus indicator + brand accent.
    { fg: '--focus-ring-color', bg: '--surface-page', kind: 'uiComponent' },
    { fg: '--accent', bg: '--surface-page', kind: 'uiComponent' },
    // Status fg on the page and on its own tint.
    { fg: '--success', bg: '--surface-page', kind: 'text' },
    { fg: '--success', bg: '--success-bg', kind: 'text' },
    { fg: '--warning', bg: '--surface-page', kind: 'text' },
    { fg: '--warning', bg: '--warning-bg', kind: 'text' },
    { fg: '--error', bg: '--surface-page', kind: 'text' },
    { fg: '--error', bg: '--error-bg', kind: 'text' },
    { fg: '--info', bg: '--surface-page', kind: 'text' },
    { fg: '--info', bg: '--info-bg', kind: 'text' },
  ],
  contrastExceptions: [
    {
      // The canonical first entry named by the spec (§4).
      fg: '--accent',
      bg: '--surface-page',
      reason:
        'brand-identity surface only; never carries text — text uses --color-primary (teal-600). ' +
        'Canonical logo teal-400 reads 2.97:1 on the white page by design.',
    },
    {
      fg: '--text-muted',
      bg: '--surface-page',
      reason:
        'muted/disabled-adjacent text (captions, placeholders elsewhere); disabled text is ' +
        'exempt under WCAG 1.4.3 and muted text is never the sole carrier of information.',
    },
    {
      fg: '--field-placeholder',
      bg: '--field-bg',
      reason:
        'placeholder is supplementary — the visible label (tm-form-field) carries the accessible ' +
        'name and every required datum; brand grey-400 placeholder is a deliberate de-emphasis.',
    },
    {
      fg: '--field-text-disabled',
      bg: '--field-bg-disabled',
      reason: 'disabled controls are exempt from contrast minimums (WCAG 1.4.3/1.4.11).',
    },
    {
      fg: '--field-border',
      bg: '--field-bg',
      reason:
        'resting hairline border is decorative de-emphasis; the field is identified by its label ' +
        'and placeholder, and the hover/focus/invalid borders carry the 3:1 state indication.',
    },
    {
      fg: '--field-border-hover',
      bg: '--field-bg',
      reason:
        'hover is a transient pointer affordance, not the sole state indicator; focus/invalid ' +
        'borders clear 3:1.',
    },
    {
      fg: '--warning',
      bg: '--surface-page',
      reason:
        'brand warning amber (#B7791F, 3.4:1) — always paired with an icon and/or text label; ' +
        'body-size warning copy uses --text-body with a warning icon, not bare amber text.',
    },
    {
      fg: '--warning',
      bg: '--warning-bg',
      reason: 'warning ink on its own tint — same rationale as --warning on the page.',
    },
    {
      fg: '--success',
      bg: '--success-bg',
      reason:
        'brand success green on its own tint measures 4.34:1 — used in badges/alerts always ' +
        'paired with an icon; body-size success copy uses --text-body on the tint.',
    },
    {
      fg: '--info',
      bg: '--surface-page',
      reason:
        'teal-500 info accent (4.0:1) — informational glyph/badge color paired with body text; ' +
        'clears the 3:1 non-text ratio, used at large/bold sizes for text.',
    },
    {
      fg: '--info',
      bg: '--info-bg',
      reason: 'info ink on its own tint — same rationale as --info on the page.',
    },
  ],
};
