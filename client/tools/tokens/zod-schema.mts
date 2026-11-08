// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * zod mirror of the TmTokens contract — the build-time schema layer (§4).
 * Lives in workspace tooling (not shipped) so the published package stays
 * dependency-free; `satisfies z.ZodType<TmTokens>` keeps it in compile-time
 * sync with the canonical TS contract.
 */
import { z } from 'zod';

import type { TmTokens } from '@tellma/core-ui-tokens';

const value = z.string().min(1);
/** An object with one required member per stop, each matching `member` (a value by default). */
const ramp = <S extends number, M extends z.ZodType = typeof value>(
  stops: readonly S[],
  member?: M,
) => z.object(Object.fromEntries(stops.map((s) => [s, member ?? value])) as { [K in S]: M });

const statusColors = z.object({ fg: value, bg: value, border: value });
const categoricalTone = z.object({ mark: value, tint: value, onTint: value });

const schemeColors = z.object({
  colorScheme: z.enum(['light', 'dark']),
  text: z.object({
    strong: value,
    body: value,
    secondary: value,
    muted: value,
    onDark: value,
    link: value,
  }),
  surface: z.object({
    page: value,
    subtle: value,
    sunken: value,
    card: value,
    inverse: value,
    hover: value,
    selected: value,
  }),
  border: z.object({ subtle: value, default: value, strong: value, divider: value }),
  selection: z.object({ bg: value, text: value }),
  action: z.object({
    primary: value,
    primaryHover: value,
    primaryActive: value,
    onPrimary: value,
    accent: value,
  }),
  status: z.object({
    success: statusColors,
    warning: statusColors,
    error: statusColors,
    info: statusColors,
  }),
  field: z.object({
    bg: value,
    bgDisabled: value,
    bgFilled: value,
    border: value,
    borderHover: value,
    borderFocus: value,
    borderInvalid: value,
    focusHalo: value,
    text: value,
    textDisabled: value,
    placeholder: value,
    icon: value,
  }),
  categorical: ramp([1, 2, 3, 4, 5, 6, 7, 8] as const, categoricalTone),
  sequential: ramp([1, 2, 3, 4, 5, 6, 7] as const),
});

export const tmTokensZodSchema = z.object({
  primitive: z.object({
    color: z.object({
      ink: ramp([700, 800, 900]),
      teal: ramp([50, 100, 200, 300, 400, 500, 600, 700, 800, 900]),
      grey: ramp([25, 50, 100, 200, 300, 400, 500, 600, 700, 800, 900]),
      white: value,
    }),
    radius: z.object({ xs: value, sm: value, md: value, lg: value, xl: value, full: value }),
    space: ramp([0, 1, 2, 3, 4, 5, 6, 8, 10, 12, 16, 20, 24] as const),
    font: z.object({
      sans: value,
      arabic: value,
      mono: value,
      ui: z.array(value).min(1),
      size: z.object({ xs: value, sm: value, base: value, lg: value }),
      weight: z.object({ regular: value, medium: value, semibold: value, bold: value }),
      leading: z.object({ tight: value, snug: value, body: value, arabic: value }),
    }),
    border: z.object({ width: value }),
    shadow: z.object({ xs: value, sm: value, md: value, lg: value }),
    motion: z.object({
      durationFast: value,
      durationNormal: value,
      durationSlow: value,
      easeStandard: value,
      easeOut: value,
      easeInOut: value,
    }),
  }),
  semantic: z.object({
    colorScheme: z.object({ light: schemeColors, dark: schemeColors }),
    focusRing: z.object({ width: value, color: value, offset: value, haloWidth: value }),
    formField: z.object({
      radius: value,
      height: value,
      heightSm: value,
      heightLg: value,
      paddingX: value,
      paddingXsm: value,
      paddingXlg: value,
      paddingY: value,
      fontSize: value,
      fontSizeSm: value,
      fontSizeLg: value,
      labelGap: value,
      labelGapSm: value,
      labelGapLg: value,
      labelFontSize: value,
      hintFontSize: value,
      errorIconSize: value,
      optionHeight: value,
      optionHeightSm: value,
      optionHeightLg: value,
    }),
    leadingByLang: z.record(z.string(), value),
  }),
  component: z.record(z.string(), z.record(z.string(), value)),
});

// Compile-time sync with the canonical TS contract: if the zod mirror drifts,
// this assignment stops compiling.
const _sync: TmTokens = undefined as unknown as z.infer<typeof tmTokensZodSchema>;
void _sync;
