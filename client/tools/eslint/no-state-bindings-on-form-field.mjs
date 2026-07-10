/**
 * tm/no-state-bindings-on-form-field — template rule forbidding
 * disabled/readonly/required bindings on a [formField]-bound control
 * (spec 0002 §5): when bound, the field is authoritative for those states and
 * the framework's write ordering against a conflicting template binding is
 * NOT a public contract. The conflict is forbidden instead of relied on.
 */
const FORBIDDEN = new Set(['disabled', 'readonly', 'required']);

export default {
  meta: {
    type: 'problem',
    docs: {
      description:
        'Forbid template-binding disabled/readonly/required on [formField]-bound controls.',
    },
    schema: [],
    messages: {
      forbidden:
        'Do not bind "{{name}}" on a [formField]-bound control — the bound field is ' +
        'authoritative for disabled/readonly/required; set it in the form schema.',
    },
  },
  create(context) {
    function checkElement(node) {
      const bound = [...(node.inputs ?? []), ...(node.attributes ?? [])];
      const hasFormField = bound.some((attr) => attr.name === 'formField');
      if (!hasFormField) {
        return;
      }
      for (const attr of bound) {
        if (FORBIDDEN.has(attr.name)) {
          context.report({
            loc: attr.sourceSpan
              ? {
                  start: {
                    line: attr.sourceSpan.start.line + 1,
                    column: attr.sourceSpan.start.col,
                  },
                  end: { line: attr.sourceSpan.end.line + 1, column: attr.sourceSpan.end.col },
                }
              : node.loc,
            messageId: 'forbidden',
            data: { name: attr.name },
          });
        }
      }
    }
    return {
      Element: checkElement,
      Element$1: checkElement, // older template-parser AST node name
    };
  },
};
