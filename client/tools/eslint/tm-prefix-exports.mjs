/**
 * tm/prefix-exports — every exported symbol of a library package carries the
 * library prefix (spec 0002 §1, D3): classes/interfaces/types/enums start
 * with `Tm`, SCREAMING_CASE constants with `TM_`, functions/values with `tm`
 * or `provideTellma`. The spec's own deliberately-unprefixed exports
 * (SignalLike, Ref, fontPreloadLinks, …) are named in the reviewed `allow`
 * option — additions are an explicit, reviewed act.
 */
const SCREAMING = /^[A-Z][A-Z0-9_]*$/;
const TYPE_LIKE = /^Tm[A-Z0-9]/;
const VALUE_LIKE = /^(tm[A-Z0-9]|provideTellma[A-Z0-9])/;

export default {
  meta: {
    type: 'suggestion',
    docs: {
      description: 'Exported symbols must carry the tm/Tm/TM_ library prefix.',
    },
    schema: [
      {
        type: 'object',
        properties: {
          allow: { type: 'array', items: { type: 'string' } },
        },
        additionalProperties: false,
      },
    ],
    messages: {
      badName:
        'Exported symbol "{{name}}" must carry the library prefix ' +
        '(Tm… for types/classes, TM_… for constants, tm…/provideTellma… for functions) ' +
        'or be added to the reviewed allowlist.',
    },
  },
  create(context) {
    const allow = new Set(context.options[0]?.allow ?? []);

    function check(name, node) {
      if (!name || allow.has(name)) {
        return;
      }
      if (SCREAMING.test(name)) {
        if (!name.startsWith('TM_')) {
          context.report({ node, messageId: 'badName', data: { name } });
        }
        return;
      }
      if (TYPE_LIKE.test(name) || VALUE_LIKE.test(name)) {
        return;
      }
      context.report({ node, messageId: 'badName', data: { name } });
    }

    return {
      ExportNamedDeclaration(node) {
        const d = node.declaration;
        if (d) {
          if (d.id && d.id.type === 'Identifier') {
            check(d.id.name, d.id);
          }
          if (d.type === 'VariableDeclaration') {
            for (const declarator of d.declarations) {
              if (declarator.id.type === 'Identifier') {
                check(declarator.id.name, declarator.id);
              }
            }
          }
        }
        for (const spec of node.specifiers ?? []) {
          if (spec.exported.type === 'Identifier') {
            check(spec.exported.name, spec.exported);
          }
        }
      },
    };
  },
};
