#!/usr/bin/env node
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

interface ComponentDoc {
  name: string;
  kind: string;
  group: string;
  selector: string;
  entryPoint: string;
  formControl: string | null;
  description: string;
  examples: { title: string; code: string }[];
}

interface ComponentsJson {
  libraryVersion: string;
  components: ComponentDoc[];
}

const packageRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const doc: ComponentsJson = JSON.parse(
  readFileSync(join(packageRoot, 'generated', 'components.json'), 'utf8'),
);

const server = new McpServer({ name: 'tellma-core-ui-mcp', version: doc.libraryVersion });

function findComponent(name: string): ComponentDoc | undefined {
  const needle = name.toLowerCase();
  return doc.components.find(
    (component) =>
      component.name.toLowerCase() === needle || component.selector.toLowerCase() === needle,
  );
}

server.registerTool(
  'list',
  {
    description:
      'Lists every component in @tellma/core-ui with its selector, entry point, and one-line role.',
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
      'Full reference for one component: inputs/outputs/slots, tokens read, a11y model, harness.',
    inputSchema: { name: z.string().describe('Component name or selector, e.g. "tm-select"') },
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
    description: 'Canonical usage examples (from the co-located stories) for one component.',
    inputSchema: { name: z.string().describe('Component name or selector, e.g. "tm-select"') },
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
    const examples =
      component.examples.length > 0
        ? component.examples
            .map((example) => `<!-- ${example.title} -->\n${example.code}`)
            .join('\n\n')
        : `No story examples yet; import ${component.name} from '${component.entryPoint}'.`;
    return { content: [{ type: 'text', text: examples }] };
  },
);

const transport = new StdioServerTransport();
await server.connect(transport);
