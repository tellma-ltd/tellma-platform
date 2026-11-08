// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * tm/csp-only-in-csp-specs — a unit spec that mentions
 * `Content-Security-Policy` must be a `*.csp.spec.ts` file. The unit runner
 * shares one browser page between the spec files of a run, and a policy
 * installed through a `<meta>` can never be lifted, so a CSP spec in the
 * shared run breaks every later file that needs Angular's JIT — on whichever
 * machine happens to order it first. The `*.csp.spec.ts` suffix moves the
 * file into the `test-csp` target, a run of its own.
 *
 * Written as a rule rather than a `no-restricted-syntax` entry because flat
 * config replaces that rule's options per block, and the entry-point
 * boundary blocks already set it for their own spec files.
 */
const CSP = /content-security-policy/i;

export default /** @type {import('eslint').Rule.RuleModule} */ ({
  meta: {
    type: 'problem',
    docs: {
      description: 'Keep Content-Security-Policy unit specs in *.csp.spec.ts files.',
    },
    schema: [],
    messages: {
      cspOutsideCspSpec:
        'A spec that installs a Content-Security-Policy must be named *.csp.spec.ts: the ' +
        'policy cannot be lifted, and only that suffix runs in a test run of its own.',
    },
  },
  create(context) {
    if (context.filename.endsWith('.csp.spec.ts')) {
      return {};
    }
    const check = (node, text) => {
      if (CSP.test(text)) {
        context.report({ node, messageId: 'cspOutsideCspSpec' });
      }
    };
    return {
      Literal(node) {
        if (typeof node.value === 'string') {
          check(node, node.value);
        }
      },
      TemplateElement(node) {
        check(node, node.value.raw);
      },
    };
  },
});
