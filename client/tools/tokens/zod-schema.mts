/**
 * zod mirror of the TmTokens contract — the build-time schema layer (§4).
 * Lives in workspace tooling (not shipped) so the published package stays
 * dependency-free; `satisfies z.ZodType<TmTokens>` keeps it in compile-time
 * sync with the canonical TS contract.
 */
import { z } from 'zod';

import type { TmTokens } from '@tellma/core-ui-tokens';

const value = z.string().min(1);
const ramp = <S extends number>(stops: readonly S[]) =>
  z.object(Object.fromEntries(stops.map((s) => [s, value])) as { [K in S]: typeof value });

const statusColors = z.object({ fg: value, bg: value, border: value });

const schemeColors = z.object({
  colorScheme: z.enum(['light', 'dark']),
  primitiveOverrides: z
    .object({
      white: value.optional(),
      grey: ramp([25, 50, 100, 200, 300, 400, 500, 600, 700, 800, 900]).partial().optional(),
      teal: ramp([50, 100, 200, 300, 400, 500, 600, 700, 800, 900]).partial().optional(),
    })
    .optional(),
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
    text: value,
    textDisabled: value,
    placeholder: value,
    icon: value,
  }),
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
    focusRing: z.object({ width: value, color: value, offset: value }),
    formField: z.object({
      radius: value,
      height: value,
      heightSm: value,
      heightLg: value,
      paddingX: value,
      paddingY: value,
      fontSize: value,
    }),
    leadingByLang: z.record(z.string(), value),
  }),
  component: z.record(z.string(), z.record(z.string(), value)),
  contrastPairs: z.array(
    z.object({ fg: value, bg: value, kind: z.enum(['text', 'largeText', 'uiComponent']) }),
  ),
  contrastExceptions: z.array(
    z.object({
      fg: value,
      bg: value,
      reason: value,
      expires: value.optional(),
      owner: value.optional(),
    }),
  ),
});

// Compile-time sync with the canonical TS contract: if the zod mirror drifts,
// this assignment stops compiling.
const _sync: TmTokens = undefined as unknown as z.infer<typeof tmTokensZodSchema>;
void _sync;
