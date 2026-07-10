/**
 * tm/no-hardcoded-sizing — bans hardcoded px/rem/em lengths on sizing,
 * spacing, and typography properties in library CSS (spec 0002 §1): every
 * size must come from a token variable so the deferred density/typography
 * runtime axes stay addable by swapping token sets alone. Covers the
 * size-carrying shorthands too (font, flex, border-width, translate,
 * transform function arguments). Allowed literals: 0, entries in the
 * `allow` option (hairline "1px" by default at config level), and
 * non-sizing properties (e.g. the `border` shorthand's 1px).
 */
import stylelint from 'stylelint';
import valueParser from 'postcss-value-parser';

const ruleName = 'tm/no-hardcoded-sizing';

const messages = stylelint.utils.ruleMessages(ruleName, {
  rejected: (prop, value) =>
    `Hardcoded sizing "${value}" on "${prop}" — use a token variable (var(--field-*, --space-*, …)); ` +
    `allowed literals are 0 and the configured allowlist.`,
});

const SIZING_PROPS =
  /^(height|min-height|max-height|width|min-width|max-width|inline-size|min-inline-size|max-inline-size|block-size|min-block-size|max-block-size|padding|padding-[a-z-]+|margin|margin-[a-z-]+|gap|row-gap|column-gap|font|font-size|line-height|border-radius|border-(start|end)-(start|end)-radius|inset|inset-[a-z-]+|top|right|bottom|left|flex|flex-basis|border-width|border-(top|right|bottom|left|block|inline)(-(start|end))?-width|translate|transform)$/;

const UNITS = new Set(['px', 'rem', 'em']);

const ruleFunction = (primary, secondaryOptions) => {
  return (root, result) => {
    if (!primary) {
      return;
    }
    const allow = new Set(secondaryOptions?.allow ?? []);
    root.walkDecls((decl) => {
      if (!SIZING_PROPS.test(decl.prop)) {
        return;
      }
      const parsed = valueParser(decl.value);
      parsed.walk((node) => {
        if (node.type !== 'word') {
          return;
        }
        const dim = valueParser.unit(node.value);
        if (!dim || !UNITS.has(dim.unit)) {
          return;
        }
        if (parseFloat(dim.number) === 0) {
          return;
        }
        if (allow.has(node.value)) {
          return;
        }
        stylelint.utils.report({
          ruleName,
          result,
          node: decl,
          message: messages.rejected(decl.prop, node.value),
        });
      });
    });
  };
};

ruleFunction.ruleName = ruleName;
ruleFunction.messages = messages;

export default stylelint.createPlugin(ruleName, ruleFunction);
