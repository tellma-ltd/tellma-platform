import tmNoHardcodedSizing from './tools/stylelint/tm-no-hardcoded-sizing.mjs';

/**
 * Library CSS discipline (spec 0002 §1/§6/DoD 10):
 * - every library class is tm- prefixed (no collisions with distribution CSS)
 * - no hardcoded sizing (density/typography stay token-switchable)
 * - no bare `outline: none` (focus-visibility is never removed without a
 *   substitute; the substitute case is an explicit, justified disable)
 * The internal sandbox app is exempt (not shipped).
 */
export default {
  plugins: [tmNoHardcodedSizing],
  rules: {},
  overrides: [
    {
      files: [
        'projects/core/tellma-core-ui/**/*.css',
        'projects/core/tellma-core-ui-tokens/**/*.css',
        'projects/locale/**/*.css',
      ],
      rules: {
        'selector-class-pattern': [
          '^tm-',
          {
            message: 'Library CSS classes must be tm- prefixed (spec 0002 §1).',
          },
        ],
        'declaration-property-value-disallowed-list': [
          { outline: ['none', '0'] },
          {
            message:
              'Never remove the focus outline without an equally-visible substitute (spec 0002 §6).',
          },
        ],
        'tm/no-hardcoded-sizing': [true, { allow: ['1px'] }],
      },
    },
  ],
};
