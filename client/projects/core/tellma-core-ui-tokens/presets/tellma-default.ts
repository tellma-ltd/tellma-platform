// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmSchemeColors, TmTokens } from '../contract/tokens';

/**
 * The default preset, reproducing the Tellma Design System artifact
 * (https://claude.ai/artifact/UE2N6v8hntWxU9k8dLcQDo): same
 * hexes, same `--field-*` / focus-ring / spacing / type tokens; the dark
 * scheme restates the brand's inverted neutrals as semantic-role values
 * (primitives never change meaning). One deliberate departure: the focus
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
    // logo teal-400 is decorative only (2.97:1 on the white page by design).
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
    focusHalo: '{teal.50}',
    text: '{ink.900}',
    textDisabled: '{grey.400}',
    placeholder: '{grey.400}',
    icon: '{grey.400}',
  },
  // Eight hues that tell things apart (chart series, keyed avatars), in a
  // fixed slot order: teal, orange, violet, amber, magenta, green, blue,
  // red. Every onTint reads at 7:1 or more on its tint.
  categorical: {
    1: { mark: '#118AA6', tint: '#D3E9EE', onTint: '#084B5D' },
    2: { mark: '#EB6834', tint: '#FBE3DA', onTint: '#6A3B2A' },
    3: { mark: '#6250D6', tint: '#E2DFF7', onTint: '#2C3173' },
    4: { mark: '#C98500', tint: '#F4E8D0', onTint: '#5A4813' },
    5: { mark: '#D55181', tint: '#F7DFE8', onTint: '#60314D' },
    6: { mark: '#008300', tint: '#D0E8D0', onTint: '#004813' },
    7: { mark: '#2A78D6', tint: '#D8E6F7', onTint: '#134373' },
    8: { mark: '#E34948', tint: '#F9DDDD', onTint: '#662E33' },
  },
  // The magnitude ramp over the brand teal (re-branding teal re-brands the
  // steps), from near the white surface to the strongest teal.
  sequential: {
    1: '{teal.100}',
    2: '{teal.200}',
    3: '{teal.300}',
    4: '{teal.400}',
    5: '{teal.600}',
    6: '{teal.700}',
    7: '{teal.800}',
  },
};

// The dark neutral scale — the brand's inversion of the light greys, stated
// as literals: primitives are scheme-invariant (--white stays white), so
// dark expresses its whole appearance through the semantic roles below.
const darkNeutral = {
  surface: '#16252D', // card/field surfaces (the slot white fills in light)
  25: '#1C2C34',
  400: '#74888E',
  500: '#94A5AB',
  700: '#CDD8DC',
  900: '#F2F7F8',
} as const;

const dark: TmSchemeColors = {
  colorScheme: 'dark',
  text: {
    strong: darkNeutral[900],
    body: darkNeutral[700],
    secondary: darkNeutral[500],
    muted: darkNeutral[400],
    onDark: darkNeutral[900],
    link: '{teal.300}',
  },
  surface: {
    page: '#0D181E',
    // A visible step OFF the page (midway to the card surface): the role's
    // contract is "a subtle panel tint", and everything riding it — grid
    // zebra stripes, readonly-cell tints, header fills — vanishes if it
    // collapses onto the page color the way an earlier draft had it.
    subtle: '#121E25',
    sunken: '#0A1418',
    card: darkNeutral.surface,
    inverse: darkNeutral[25],
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
  // Deep teal selection; the light text keeps 10.5:1.
  selection: {
    bg: '{teal.800}',
    text: darkNeutral[900],
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
    bg: darkNeutral.surface,
    bgDisabled: '#1A2930',
    bgFilled: darkNeutral[25],
    border: 'rgba(255, 255, 255, 0.14)',
    borderHover: 'rgba(255, 255, 255, 0.24)',
    borderFocus: '{teal.300}',
    borderInvalid: '{status.error.fg}',
    // Translucent, not the light scheme's opaque teal-50: an opaque tint
    // this pale would read as a plate around the field on a dark surface.
    focusHalo: 'rgba(76, 160, 182, 0.22)',
    text: darkNeutral[900],
    textDisabled: darkNeutral[400],
    placeholder: darkNeutral[400],
    icon: darkNeutral[400],
  },
  // The same eight hues, re-toned for the dark surfaces: tints are deep, and
  // text on them is pale. Every onTint reads at 6.5:1 or more on its tint.
  categorical: {
    1: { mark: '#2AA5C2', tint: '#144554', onTint: '#AED6DF' },
    2: { mark: '#D95926', tint: '#5A3A2F', onTint: '#F0CCBD' },
    3: { mark: '#9085E9', tint: '#2E3363', onTint: '#C7C5EE' },
    4: { mark: '#C98500', tint: '#4F441F', onTint: '#E6D5AE' },
    5: { mark: '#D55181', tint: '#533348', onTint: '#E9C5D4' },
    6: { mark: '#008300', tint: '#0F431F', onTint: '#A9D4AE' },
    7: { mark: '#3987E5', tint: '#1C4063', onTint: '#B6D1EE' },
    8: { mark: '#E66767', tint: '#583136', onTint: '#EDC3C3' },
  },
  // The ramp reversed: step 1 sits near the dark surface, step 7 is the
  // brightest teal.
  sequential: {
    1: '{teal.900}',
    2: '{teal.800}',
    3: '{teal.700}',
    4: '{teal.600}',
    5: '{teal.400}',
    6: '{teal.300}',
    7: '{teal.200}',
  },
};

/** The Tellma brand default preset — the document the shipped stylesheet is emitted from. */
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
      sans: "'Noto Sans', 'Noto Sans Fallback', system-ui, -apple-system, 'Segoe UI', sans-serif",
      arabic: "'Noto Sans Arabic', 'Noto Sans Arabic Fallback', 'Noto Sans', sans-serif",
      mono: "'Noto Sans Mono', 'Noto Sans Mono Fallback', ui-monospace, 'SF Mono', Menlo, monospace",
      // Brand faces per script first (core-ui vendors the Latin faces; the
      // Arabic faces arrive with @tellma/locale-ar), then the metric-adjusted
      // local fallbacks (unicode-range keeps each on its own script),
      // generics last.
      ui: [
        "'Noto Sans'",
        "'Noto Sans Arabic'",
        "'Noto Sans Fallback'",
        "'Noto Sans Arabic Fallback'",
        'system-ui',
        '-apple-system',
        "'Segoe UI'",
        'sans-serif',
      ],
      size: { xs: '12px', sm: '14px', base: '16px', lg: '18px' },
      weight: { regular: '400', medium: '500', semibold: '600', bold: '700' },
      leading: { tight: '1.2', snug: '1.35', body: '1.6', arabic: '1.9' },
    },
    border: { width: '1px' },
    // Overlay elevation only — in-canvas surfaces stay flat with a hairline.
    // md is the anchored-panel shadow (menus, dropdowns, popovers, the date
    // popup); lg lifts a modal off its scrim. Both are negative-spread so
    // the panel edge stays crisp and the shadow reads as depth, not haze.
    shadow: {
      xs: '0 1px 2px rgba(0, 23, 34, 0.05)',
      sm: '0 4px 12px -4px rgba(0, 23, 34, 0.4)',
      md: '0 8px 24px -8px rgba(0, 23, 34, 0.28), 0 2px 6px -2px rgba(0, 23, 34, 0.12)',
      lg: '0 16px 40px -12px rgba(0, 23, 34, 0.4)',
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
    focusRing: { width: '2px', color: '{teal.500}', offset: '2px', haloWidth: '3px' },
    formField: {
      radius: '{radius.sm}',
      height: '38px',
      heightSm: '30px',
      heightLg: '46px',
      paddingX: '12px',
      paddingXsm: '10px',
      paddingXlg: '14px',
      paddingY: '8px',
      fontSize: '{font.size.sm}',
      fontSizeSm: '13px',
      fontSizeLg: '15px',
      labelGap: '6px',
      labelGapSm: '4px',
      labelGapLg: '7px',
      // Label and hint are chrome around the control, not part of it: they
      // hold their type through every size so a dense form still reads.
      labelFontSize: '13px',
      hintFontSize: '{font.size.xs}',
      errorIconSize: '13px',
      optionHeight: '30px',
      optionHeightSm: '26px',
      optionHeightLg: '34px',
    },
    leadingByLang: {
      ar: '{font.leading.arabic}',
      en: '{font.leading.body}',
    },
  },
  component: {
    // tm-alert: page/section status wrapper — geometry only; per-kind
    // colors ride the status semantic tokens in the component CSS. An alert
    // is a block of prose, not a control, so it keeps card geometry
    // (radius-md, roomy padding) while the controls around it go dense.
    alert: {
      gap: '10px',
      paddingX: '14px',
      paddingY: '12px',
      radius: '{radius.md}',
      iconSize: '18px',
      headingFontSize: '{font.size.sm}',
      bodyFontSize: '13px',
    },
    // tm-avatar: a person as a round photo or as initials on a tinted
    // circle. Five fixed diameters, each with its own initials size (two
    // capitals fit the smallest) and the glyph shown for an empty name. A
    // keyed avatar paints a `categorical` slot's tint and on-tint pair; an
    // unkeyed one the accent teal with ink initials (6.1:1 in both schemes,
    // because the accent and the ink primitive do not change with them).
    avatar: {
      sizeXs: '20px',
      sizeSm: '24px',
      sizeMd: '28px',
      sizeLg: '40px',
      sizeXl: '72px',
      fontSizeXs: '9px',
      fontSizeSm: '10px',
      fontSizeMd: '11px',
      fontSizeLg: '15px',
      fontSizeXl: '26px',
      iconSizeXs: '12px',
      iconSizeSm: '14px',
      iconSizeMd: '16px',
      iconSizeLg: '24px',
      iconSizeXl: '40px',
      bg: '{action.accent}',
      text: '{ink.900}',
    },
    // tm-badge: a status label in a 20px pill. The status tones tint the
    // pill with the status roles in the component CSS and keep body ink for
    // the text (a status ink on its own tint is below 4.5:1 for some
    // tones); the tone's ink draws the dot and the icon. Accent is the one
    // solid badge.
    badge: {
      height: '20px',
      paddingX: '8px',
      gap: '4px',
      radius: '{radius.full}',
      fontSize: '11px',
      iconSize: '12px',
      dotSize: '6px',
      text: '{text.body}',
      neutralBg: '{surface.sunken}',
      neutralBorder: '{border.default}',
      neutralInk: '{text.secondary}',
      accentBg: '{action.primary}',
      accentText: '{action.onPrimary}',
    },
    // tm-counter: a number in a 16px pill. Overlay counters sit on their
    // parent's block-start, inline-end corner inside a halo of the surface
    // they sit on (`--counter-halo` is the one variable a host sets, to its
    // bar colour); inline counters reserve two digits in the component CSS.
    // Every fill carries text at 4.5:1 or more in both schemes.
    counter: {
      height: '16px',
      minWidth: '16px',
      paddingX: '4px',
      fontSize: '11px',
      halo: '{surface.card}',
      haloWidth: '2px',
      overlayOffset: '-4px',
      neutralBg: '{text.secondary}',
      neutralText: '{surface.card}',
      accentBg: '{action.primary}',
      accentText: '{action.onPrimary}',
      attentionBg: '{status.error.fg}',
      attentionText: '{action.onPrimary}',
    },
    // tm-chip and tm-chip-set: a value, filter or link in a pill on the
    // size ladder's chip steps (20, 24, 28 px). The tones follow tm-badge:
    // the status tones tint with the status roles in the component CSS and
    // keep body ink for the text; accent is the one solid chip. Unlike the
    // rest of the component tier, this group is public to read: the
    // launcher's chip-shaped options draw on it.
    chip: {
      height: '24px',
      heightSm: '20px',
      heightLg: '28px',
      paddingX: '10px',
      paddingXsm: '8px',
      paddingXlg: '12px',
      fontSize: '12px',
      fontSizeSm: '11px',
      fontSizeLg: '13px',
      iconSize: '14px',
      iconSizeSm: '12px',
      iconSizeLg: '16px',
      removeIconSize: '12px',
      // The remove button is a circle inset this far inside the pill, with
      // a little more room at the pill's end.
      removeInset: '3px',
      removeEndGap: '2px',
      // Every chip control takes the pointer at least this square (WCAG 2.2
      // target minimum), whatever its drawn size.
      targetSize: '24px',
      gap: '4px',
      setGap: '4px',
      radius: '{radius.full}',
      text: '{text.body}',
      neutralBg: '{surface.sunken}',
      neutralBorder: '{border.default}',
      neutralInk: '{text.secondary}',
      accentBg: '{action.primary}',
      accentText: '{action.onPrimary}',
    },
    // TmToasts: short timed messages stacked in the block-start, inline-end
    // corner, above everything. `offsetBlockStart` is the one variable a
    // consumer sets (the shell, to clear its top bar). Below the sm
    // breakpoint a toast takes the full width less a gutter on each side.
    toast: {
      width: '360px',
      gutter: '8px',
      offsetBlockStart: '8px',
      gap: '8px',
      padding: '12px',
      radius: '{radius.md}',
      bg: '{surface.card}',
      border: '{border.default}',
      iconSize: '16px',
      titleFontSize: '13px',
      detailFontSize: '12px',
      closeSize: '28px',
      closeIconSize: '14px',
      pillFontSize: '12px',
    },
    // tm-banner-bar: one 32px row, the height of the shell's banner row. It
    // fills with the tone's status tint and draws the status border on its
    // block-end edge; the text keeps body ink.
    banner: {
      height: '32px',
      paddingX: '12px',
      gap: '8px',
      fontSize: '12px',
      iconSize: '16px',
      buttonSize: '24px',
      buttonIconSize: '14px',
      text: '{text.body}',
    },
    // TmBusyOverlay: a transparent blocker at once, then after `delay` a
    // scrim with a card holding a spinner and the label. `delay` is the one
    // variable a consumer may set; the scrim is a translucent ink veil that
    // reads the same over both schemes, lighter than the modal's because
    // nothing sits on it but the card.
    busyOverlay: {
      delay: '200ms',
      scrim: 'rgba(4, 18, 24, 0.4)',
      paddingX: '16px',
      paddingY: '12px',
      gap: '10px',
      radius: '{radius.md}',
      bg: '{surface.card}',
      fontSize: '13px',
      text: '{text.strong}',
      spinnerSize: '20px',
    },
    // tm-card: a framed block under an optional heading. Card fill, a
    // hairline, radius md, no shadow. The header is a 33px band (8px
    // padding around a 17px line of 13px semibold text), or 51px with the
    // 18px subtitle line, whatever the card holds. The error dot sits in a
    // box every card with `invalid` bound reserves.
    card: {
      bg: '{surface.card}',
      border: '{border.default}',
      radius: '{radius.md}',
      headerPaddingX: '11px',
      headerPaddingY: '8px',
      headerGap: '8px',
      fontSize: '13px',
      lineHeight: '17px',
      subtitleFontSize: '12px',
      subtitleLineHeight: '18px',
      bodyPadding: '11px',
      chevronSize: '14px',
      dotSize: '6px',
      dotGap: '6px',
    },
    // tm-page-header: a page's title row and toolbar row, each at least the
    // field height, then a band that wraps. 12x14px padding, or 10px below
    // the sm breakpoint.
    pageHeader: {
      paddingX: '14px',
      paddingY: '12px',
      paddingNarrow: '10px',
      rowGap: '8px',
      gap: '8px',
      titleFontSize: '16px',
      backFontSize: '13px',
      separatorSize: '14px',
    },
    // dl[tmDescriptionList]: label-value pairs. Labels are 12px medium
    // secondary text, values 13px strong text, pairs 10px apart down and
    // 18px across; an inline pair gives its label this share of its width.
    descriptionList: {
      labelFontSize: '12px',
      labelColor: '{text.secondary}',
      valueFontSize: '13px',
      valueColor: '{text.strong}',
      rowGap: '10px',
      columnGap: '18px',
      pairGap: '2px',
      inlineLabelWidth: '40%',
    },
    // tm-splitter: a 7px bar between two panes with a 3x28px grip, taking
    // the pointer across a 24px hit area. The bar is quiet at rest and
    // takes the accent while hovered or dragged.
    splitter: {
      size: '7px',
      hitSize: '24px',
      gripWidth: '3px',
      gripLength: '28px',
      bg: '{border.subtle}',
      grip: '{border.strong}',
      activeBg: '{action.accent}',
    },
    // tmButton: heights ride the shared field-height scale (buttons align
    // with form fields in toolbars); per-variant colors resolve per scheme
    // through the semantic action/field/status roles.
    //
    // Kebab hazard: two adjacent capitals do NOT split, so `paddingXSm`
    // emits `--button-padding-xsm`. The keys below are spelled the way they
    // emit; check any new one against the generated stylesheet.
    button: {
      radius: '{radius.sm}',
      gap: '8px',
      gapSm: '6px',
      gapLg: '8px',
      paddingXsm: '12px',
      paddingX: '16px',
      paddingXlg: '22px',
      fontSize: '{font.size.sm}',
      fontSizeSm: '13px',
      fontSizeLg: '15px',
      // Projected icons need an explicit size: an icon set's natural 24px
      // is taller than a small button's whole content box.
      iconSize: '14px',
      // Disabled is a solid flat plate, not a faded live button: a
      // translucent control shows whatever it sits on and stops reading as
      // one surface. Filled and outline variants land on different plates
      // so a disabled ghost does not grow a box it never had.
      //
      // The disabled INK is --text-muted for every variant. The brand sheet
      // draws white on the filled plate, which is 1.6:1 — legible only
      // because disabled text is exempt from contrast rules. Muted ink on
      // the same plate reads, and disabled-ness is already carried by the
      // flat fill plus the removed border.
      disabledBg: '{border.default}',
      disabledBgSubtle: '{surface.sunken}',
      disabledText: '{text.muted}',
      primaryBg: '{action.primary}',
      primaryText: '{action.onPrimary}',
      primaryHoverBg: '{action.primaryHover}',
      primaryActiveBg: '{action.primaryActive}',
      secondaryBg: '{surface.card}',
      secondaryText: '{text.strong}',
      secondaryBorder: '{border.default}',
      secondaryHoverBg: '{surface.hover}',
      secondaryHoverBorder: '{border.strong}',
      secondaryActiveBg: '{surface.sunken}',
      ghostText: '{text.strong}',
      ghostHoverBg: '{surface.hover}',
      ghostActiveBg: '{border.subtle}',
      dangerBg: '{status.error.fg}',
      dangerText: '{action.onPrimary}',
    },
    // tm-checkbox (§3.3): the visible box renders at the brand 18px while
    // the hit target is padded past the 24px minimum. The box does NOT
    // follow the size ladder — a smaller tick would drop under the touch
    // minimum and is the one control whose glyph must stay legible.
    checkbox: {
      boxSize: '18px',
      // The mark sits INSIDE the box: the glyph spans nearly its whole
      // viewBox, so drawn edge to edge it would touch the box's border.
      glyphSize: '14px',
      gap: '9px',
      labelFontSize: '13px',
    },
    // tm-radio-group: the drawn circle and its dot. Rows ride the size
    // ladder's option-row heights (formField.optionHeight*), so these are the
    // only fixed sizes; a horizontal group spaces its rows by inlineGap.
    radio: {
      circleSize: '16px',
      dotSize: '8px',
      gap: '8px',
      inlineGap: '16px',
    },
    // tm-segmented: a sunken track at the field height whose segments sit
    // inset from its edge; the track follows the size ladder (formField),
    // so these are the inset and the icon geometry only.
    segmented: {
      inset: '2px',
      iconGap: '4px',
      iconSize: '16px',
    },
    // tm-switch: a 28×16 track with a 12px knob. The hit target is the
    // whole label row, padded past the 24px minimum.
    switch: {
      trackWidth: '28px',
      trackHeight: '16px',
      knobSize: '12px',
      gap: '8px',
      labelFontSize: '13px',
    },
    // tm-time-field and tm-date-time-field: the time input's width, which
    // holds the widest locale's time text ("12:30 AM", "١٢:٣٠ م"), and the
    // gap between the date, the time and the zone label.
    dateTime: {
      timeWidth: '112px',
      gap: '8px',
    },
    // tm-date-picker: popup geometry. Cells exceed the 24px touch minimum;
    // the popup width holds seven cells plus gaps at every view. Cells are
    // wider than they are tall — a calendar reads as columns of weekdays,
    // and the extra width is where two-digit days breathe.
    datePicker: {
      cellHeight: '30px',
      cellGap: '1px',
      slotHeight: '32px',
      popupWidth: '256px',
      popupPadding: '8px',
      headerFontSize: '13px',
      weekdayHeight: '22px',
      weekdayFontSize: '10.5px',
      cellFontSize: '12.5px',
      navGlyphSize: '15px',
    },
    // tm-select (§3.4): panel + option-row geometry. Rows follow the size
    // ladder because a picker used dozens of times a minute is read as a
    // list, not tapped as a button.
    select: {
      panelMaxHeight: '280px',
      panelPadding: '4px',
      optionRadius: '{radius.xs}',
      optionPaddingX: '8px',
      optionGap: '8px',
      checkSize: '15px',
    },
    // tm-entity-picker (spec 0006 §10): dropdown geometry — rows share the
    // select's sizing; the min-width floor keeps the panel readable when a
    // narrow grid cell would make matched width unusable.
    entityPicker: {
      panelMaxHeight: '280px',
      panelMinWidth: '200px',
      panelPadding: '4px',
      optionRadius: '{radius.xs}',
      optionPaddingX: '8px',
      optionGap: '8px',
      // A row with a description line under its label (spec 0010 §E.1): a
      // 14px label and a 12px description at the snug leading (35px), with
      // air above and below. Every row of a two-line list takes it, so the
      // panel never re-measures between result sets.
      optionHeight2line: '42px',
      iconSize: '15px',
      noticeFontSize: '11.5px',
    },
    // tm-empty-state: every "nothing to show" and inline-failure state in
    // one centred anatomy — a 22px icon, a 14px heading, a 12px detail held
    // to a readable measure, then the actions. `page` centres in the space
    // left; `panel` is the compact form for a popover or a widget.
    emptyState: {
      iconSize: '22px',
      gap: '8px',
      headingFontSize: '{font.size.sm}',
      detailFontSize: '{font.size.xs}',
      detailMaxWidth: '42ch',
      actionsGap: '8px',
      paddingPage: '24px',
      paddingPanel: '16px',
      iconColor: '{text.muted}',
      headingColor: '{text.strong}',
      detailColor: '{text.secondary}',
    },
    // tm-grid / tm-tree-grid: row density mirrors the field-height scale
    // one notch tighter (data rows, not form fields); selection fill is a
    // translucent brand teal so gridlines and text stay readable under it.
    grid: {
      // The brand sheet's ladder is 26/30/34, but 26 reads as cramped once
      // there is real data in the cells rather than specimen text — the
      // glyphs touch the rules. The whole ladder moves up one step so the
      // dense default lands on 30.
      rowHeight: '34px',
      rowHeightSm: '30px',
      rowHeightLg: '38px',
      headerBg: '{surface.sunken}',
      headerText: '{text.secondary}',
      line: '{border.subtle}',
      // The header's lower edge is the one rule drawn at full strength, in
      // both the ruled and the plain look: it separates chrome from data.
      headerLine: '{border.default}',
      selectionBg: 'rgba(76, 160, 182, 0.14)',
      selectionBorder: '{action.accent}',
      errorBg: '{status.error.bg}',
      errorBorder: '{status.error.border}',
      readonlyBg: '{field.bgFilled}',
      zebraBg: '{surface.subtle}',
      // The active row of a list that opens rows: the selected-surface tint
      // under a 2px accent bar, distinct from the zebra stripe it replaces.
      activeRowBg: '{surface.selected}',
      cutBorder: '{action.primary}',
      findMatchBg: '{status.warning.bg}',
      findActiveOutline: '{status.warning.fg}',
      // The pulse a row gets when code scrolls it into view: the info tint
      // (distinct from the find match's warning tint), long enough to be
      // seen landing, short enough not to linger.
      flashBg: '{status.info.bg}',
      flashDuration: '1200ms',
      // One indent step per level, sized to sit under the twisty.
      indent: '16px',
      rowHeaderWidth: '34px',
      checkColWidth: '30px',
      minColWidth: '48px',
      handleSize: '24px',
      // A header action's square target (a column's filter button): the
      // 24px minimum target, inside every density's header height.
      headerActionSize: '24px',
      // The row-end column (a list's column editor, a per-row menu): one
      // header action and 6px on each side.
      rowEndWidth: '36px',
      cellPaddingX: '8px',
      cellFontSize: '12px',
      // The plain (readonly) look drops the vertical rules, so its cells
      // need their own inline breathing room to stay separable.
      //
      // Named plain-FIRST, not `cellPaddingXPlain`: two adjacent capitals do
      // not kebab-split, so that spelling emits `--grid-cell-padding-xplain`
      // and every rule reading `-x-plain` silently resolves to nothing.
      plainCellPaddingX: '11px',
      plainCellFontSize: '12.5px',
      headerFontSize: '11.5px',
      rowHeaderFontSize: '10.5px',
      statusHeight: '30px',
      statusFontSize: '11.5px',
      statusIconSize: '14px',
      twistySize: '15px',
    },
    // tm-dropzone: drop-region geometry; colors ride the field/surface
    // semantics in the component CSS.
    files: {
      dropzoneMinHeight: '108px',
      dropzonePadding: '14px',
      dropzoneRadius: '{radius.md}',
      dropzoneGap: '5px',
      dropzoneIconSize: '22px',
      dropzoneHintFontSize: '12.5px',
      dropzoneMetaFontSize: '11px',
    },
    // tm-icon: the square glyph box. `size` is the default every unsized
    // icon reads; a container re-points `--icon-size` at its own glyph token
    // (a button at its icon size, a menu row at its), and the five steps are
    // what the `size` input picks from. The stroke is a unitless SVG width
    // in the 24-unit viewBox, so it scales with the box; a host that needs a
    // heavier glyph sets its own `--icon-stroke-width`.
    icon: {
      size: '16px',
      sizeXs: '12px',
      sizeSm: '14px',
      sizeMd: '16px',
      sizeLg: '20px',
      sizeXl: '24px',
      strokeWidth: '1.75',
    },
    // tm-kbd: one key cap per key. The cap is 18px tall so a shortcut hint
    // fits a 30px menu row and a line of 13px prose without growing either;
    // the min width keeps a one-character cap at least square. The lower edge is drawn heavier (in
    // the component CSS, from `border`) so a cap reads as a key, not a tag.
    kbd: {
      height: '18px',
      minWidth: '18px',
      paddingX: '4px',
      gap: '4px',
      radius: '{radius.xs}',
      fontSize: '11px',
      bg: '{surface.subtle}',
      border: '{border.strong}',
      text: '{text.body}',
    },
    // tm-image: fixed-box chrome. The chrome fill is a static ink veil
    // that reads over any image on both schemes.
    image: {
      radius: '{radius.sm}',
      glyphSize: '20px',
      chromeBg: 'rgba(0, 23, 34, 0.72)',
      chromeText: '{white}',
      chromeButtonSize: '20px',
      cropBarPaddingX: '8px',
      cropBarPaddingY: '5px',
      cropBarShadow: '0 4px 14px -4px rgba(0, 23, 34, 0.55)',
      cropSliderTrack: '4px',
      cropSliderKnob: '12px',
      cropDoneHeight: '22px',
      cropDoneGlyphSize: '12px',
      chromeIconSize: '14px',
      chromeRadius: '{radius.xs}',
    },
    // tm-menu: panel + item-row geometry; colors ride the field/surface
    // semantic tokens in the component CSS. A menu is a pointer surface
    // read in one pass, so it holds one dense height rather than following
    // the size ladder; below the sm breakpoint, where rows are pressed
    // with a finger, they take the touch height instead.
    menu: {
      minWidth: '180px',
      itemHeight: '30px',
      itemHeightTouch: '44px',
      iconSize: '15px',
      itemGap: '9px',
      itemPaddingX: '8px',
      itemRadius: '{radius.xs}',
      itemFontSize: '12.5px',
      panelPadding: '4px',
    },
    // tm-modal: panel buckets + shell geometry. The scrim is a static ink
    // veil that reads correctly over both schemes.
    modal: {
      widthSm: '420px',
      widthMd: '640px',
      margin: '16px',
      lgMargin: '48px',
      // The drawer (placement 'end') widths: a side sheet narrow enough to
      // leave the page beside it readable at sm, wide enough for a form at
      // lg. Below the sm breakpoint every drawer takes the full width.
      drawerWidthSm: '360px',
      drawerWidthMd: '480px',
      drawerWidthLg: '720px',
      radius: '{radius.md}',
      paddingX: '16px',
      headerPaddingY: '11px',
      footerPaddingX: '12px',
      footerPaddingY: '10px',
      footerBg: '{surface.subtle}',
      titleFontSize: '{font.size.sm}',
      closeSize: '28px',
      iconSize: '15px',
      scrim: 'rgba(4, 18, 24, 0.55)',
      // The alert dialogs (TmConfirm, TmErrorDialog): a 26px tinted disc
      // beside the message, and the gaps between the body's blocks.
      alertIconSize: '26px',
      alertGlyphSize: '16px',
      alertGap: '12px',
      messageFontSize: '13px',
    },
    // tm-spinner: a decorative ring — a quiet full track with one quarter
    // arc turning on it. The arc rides `color` so a host re-points it by
    // setting text color alone; only the track needs a token, because it
    // has to stay a groove against whatever ink the arc inherited.
    spinner: {
      size: '14px',
      track: '{border.default}',
      duration: '700ms',
    },
    // tm-file-preview: viewer-region geometry inside the lg modal.
    preview: {
      minHeight: '320px',
      cardIconSize: '20px',
      audioMaxWidth: '480px',
      footerPaddingX: '10px',
      footerPaddingY: '8px',
      footerFontSize: '11px',
      footerBg: '{surface.subtle}',
    },
    // tm-popover: panel geometry; colors ride the surface/border semantics
    // in the component CSS.
    popover: {
      padding: '12px',
      radius: '{radius.md}',
      // The 'auto' size: the content's width, up to this whole-panel cap
      // (a 320px content box plus the padding and border).
      maxInlineSize: '346px',
      // The fixed sizes: panels of one kind read alike and never resize
      // while their content loads.
      widthSm: '320px',
      widthMd: '440px',
      widthLg: '640px',
      // The gap every popover keeps from the viewport edges, so a lg panel
      // on a phone is full width less this on each side.
      viewportMargin: '8px',
    },
    // tm-progress-bar and tm-progress-ring. The bar is a 4px track that
    // consumers overlay on an edge, so it takes no layout space; once shown
    // it stays at least `minVisible`, so a fast answer never flashes it.
    // The indeterminate sweep and the ring's spin are the only motion here,
    // and both stop under reduced motion. Every fill reads at 3:1 or more
    // against the track in both schemes: the accent is the action teal, not
    // the lighter accent teal, and the warning fill is the warning role
    // pulled a quarter of the way to the strong text colour (darker in
    // light, lighter in dark), since the warning role alone is 2.45:1
    // against the light track.
    progress: {
      height: '4px',
      radius: '{radius.full}',
      track: '{border.default}',
      accent: '{action.primary}',
      success: '{status.success.fg}',
      warning: 'color-mix(in srgb, var(--warning) 75%, var(--text-strong))',
      error: '{status.error.fg}',
      minVisible: '250ms',
      sweepDuration: '1.4s',
      sweepWidth: '40%',
      ringStroke: '1.5px',
      ringDuration: '1s',
    },
    // tm-skeleton: static placeholders with the final geometry of what
    // replaces them. A text line is an 8px bar on a 20px pitch (a line of
    // body text); the last of several lines stops at 60%. The table shape
    // rides the grid's row heights so a list's placeholder and its rows
    // match. Nothing animates.
    skeleton: {
      bg: '{border.subtle}',
      radius: '{radius.xs}',
      barHeight: '8px',
      lineGap: '12px',
      lastLineWidth: '60%',
      cellPaddingX: '8px',
    },
    // tm-tab-group: strip geometry; the active indicator draws inside the
    // tab box so activation never reflows. A vertical strip is a nav list
    // rather than a header, so it runs at its own tighter row height.
    tabs: {
      height: '34px',
      verticalHeight: '30px',
      indicatorThickness: '2px',
      gap: '2px',
      labelPaddingX: '11px',
      fontSize: '13px',
      verticalFontSize: '12.5px',
      verticalPaddingX: '10px',
      verticalRadius: '{radius.xs}',
    },
    // tmTooltip: inverse-surface text bubble. The directive reads `delay`
    // at show time; `offset` is the visual gap from the host.
    tooltip: {
      delay: '500ms',
      offset: '6px',
      paddingX: '9px',
      paddingY: '5px',
      radius: '{radius.sm}',
      maxInlineSize: '200px',
      fontSize: '11.5px',
    },
    // The validation-message bubble: one look shared by tm-form-field and
    // the grid's active-cell error, so a field error and a cell error read
    // as the same thing. It is a card that has gone red at the edge, not a
    // filled error banner — the field it points at already carries the red.
    errorPopover: {
      paddingX: '9px',
      paddingY: '5px',
      gap: '5px',
      radius: '{radius.sm}',
      fontSize: '{font.size.xs}',
      iconSize: '13px',
      // The visual gap between the field's border box and the bubble; the
      // arrow overlaps back into it.
      offset: '7px',
      arrowSize: '6px',
      // How far the arrow tip sits from the bubble's leading edge, so it
      // lands under the field's inline padding rather than its corner.
      arrowInset: '13px',
      maxInlineSize: '320px',
      shadow: '0 6px 16px -8px rgba(0, 23, 34, 0.28)',
    },
  },
};
