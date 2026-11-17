#!/usr/bin/env node
// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * @tellma/core-ui-mcp — scoped MCP server for the Tellma UI component
 * library: answers `list` / `describe` / `example` over
 * stdio from the generated components.json (the single source of truth
 * extracted from the component sources), so
 * `npx @tellma/core-ui-mcp@<pinned>` answers against the exact version a
 * distribution depends on. stdio transport — no port (worktree-parallel by
 * construction).
 */
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { z } from 'zod';

/** The fields of one components.json entry that the tools read by name. */
interface ComponentDoc {
  /** The selector of a component or directive, the class name of a service. */
  name: string;
  /** `component`, `directive` or `service`. */
  kind: string;
  /** The docs group the entry sorts under. */
  group: string;
  /** The element or attribute selector; `null` for a service. */
  selector: string | null;
  /** The import path that exports it. */
  entryPoint: string;
  /** The Signal Forms contract it implements, if any. */
  formControl: string | null;
  /** Its TSDoc description. */
  description: string;
  /** Its usage examples: templates (`html`) and service calls (`ts`). */
  examples: { title: string; code: string; language: 'html' | 'ts' }[];
}

/** The generated components.json, as far as the tools read it. */
interface ComponentsJson {
  /** The @tellma/core-ui version the record describes. */
  libraryVersion: string;
  /** Every documented component, directive and service. */
  components: ComponentDoc[];
}

const packageRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const doc: ComponentsJson = JSON.parse(
  readFileSync(join(packageRoot, 'generated', 'components.json'), 'utf8'),
);

const server = new McpServer({ name: 'tellma-core-ui-mcp', version: doc.libraryVersion });

/** Finds an entry by its name or selector, ignoring case. */
function findComponent(name: string): ComponentDoc | undefined {
  const needle = name.toLowerCase();
  return doc.components.find(
    (component) =>
      component.name.toLowerCase() === needle || component.selector?.toLowerCase() === needle,
  );
}

server.registerTool(
  'list',
  {
    description:
      'Lists every component, directive and service in @tellma/core-ui with its selector (null for a service), entry point, and one-line role.',
    inputSchema: {},
  },
  () => ({
    content: [
      {
        type: 'text',
        text: JSON.stringify(
          {
            libraryVersion: doc.libraryVersion,
            components: doc.components.map((component) => ({
              name: component.name,
              selector: component.selector,
              kind: component.kind,
              group: component.group,
              entryPoint: component.entryPoint,
              formControl: component.formControl,
              summary: component.description.split('\n')[0],
            })),
          },
          null,
          2,
        ),
      },
    ],
  }),
);

server.registerTool(
  'describe',
  {
    description:
      'Full reference for one component, directive or service: inputs/outputs, members, slots, tokens read, a11y model, harness.',
    inputSchema: {
      name: z.string().describe('Name or selector, e.g. "tm-select" or "TmToasts"'),
    },
  },
  ({ name }) => {
    const component = findComponent(name);
    if (!component) {
      return {
        content: [
          {
            type: 'text',
            text: `Unknown component "${name}". Known: ${doc.components.map((c) => c.name).join(', ')}`,
          },
        ],
        isError: true,
      };
    }
    return { content: [{ type: 'text', text: JSON.stringify(component, null, 2) }] };
  },
);

server.registerTool(
  'example',
  {
    description:
      'Canonical usage examples for one component, directive or service: templates from the co-located examples, and code from its documentation.',
    inputSchema: {
      name: z.string().describe('Name or selector, e.g. "tm-select" or "TmToasts"'),
    },
  },
  ({ name }) => {
    const component = findComponent(name);
    if (!component) {
      return {
        content: [
          {
            type: 'text',
            text: `Unknown component "${name}". Known: ${doc.components.map((c) => c.name).join(', ')}`,
          },
        ],
        isError: true,
      };
    }
    // Each example is titled in its own language's comment syntax.
    const examples =
      component.examples.length > 0
        ? component.examples
            .map((example) =>
              example.language === 'ts'
                ? `// ${example.title}\n${example.code}`
                : `<!-- ${example.title} -->\n${example.code}`,
            )
            .join('\n\n')
        : `No examples yet; import ${component.name} from '${component.entryPoint}'.`;
    return { content: [{ type: 'text', text: examples }] };
  },
);

const transport = new StdioServerTransport();
await server.connect(transport);
