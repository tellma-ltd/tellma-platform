import { execFileSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { beforeAll, describe, expect, it } from 'vitest';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';

const clientDir = resolve(import.meta.dirname, '..', '..');
const mcpDir = join(clientDir, 'projects', 'core', 'tellma-core-ui-mcp');

/**
 * DoD 14: the scoped MCP server answers list/describe/example over stdio,
 * exercised through the real SDK client against the real built server.
 */
describe('@tellma/core-ui-mcp server', () => {
  let client: Client;

  beforeAll(async () => {
    // Build the inputs if this spec runs standalone.
    if (!existsSync(join(mcpDir, 'generated', 'components.json'))) {
      execFileSync(process.execPath, ['node_modules/tsx/dist/cli.mjs', 'tools/docs/build-docs.mts'], {
        cwd: clientDir,
      });
    }
    if (!existsSync(join(mcpDir, 'dist', 'server.js'))) {
      execFileSync(process.execPath, ['node_modules/typescript/bin/tsc', '-b', mcpDir], {
        cwd: clientDir,
      });
    }
    client = new Client({ name: 'mcp-smoke-test', version: '0.0.0' });
    await client.connect(
      new StdioClientTransport({
        command: process.execPath,
        args: [join(mcpDir, 'dist', 'server.js')],
      }),
    );
  }, 60_000);

  it('lists the Phase-1 components', async () => {
    const result = await client.callTool({ name: 'list', arguments: {} });
    const text = (result.content as { type: string; text: string }[])[0].text;
    const parsed = JSON.parse(text) as { components: { name: string }[] };
    const names = parsed.components.map((component) => component.name);
    expect(names).toContain('tmInput');
    expect(names).toContain('tm-checkbox');
    expect(names).toContain('tm-form-field');
    expect(names).toContain('tm-select');
    expect(names).toContain('tm-option');
  });

  it('describes tm-select with its full reference', async () => {
    const result = await client.callTool({ name: 'describe', arguments: { name: 'tm-select' } });
    const component = JSON.parse(
      (result.content as { type: string; text: string }[])[0].text,
    ) as {
      selector: string;
      formControl: string;
      inputs: { name: string }[];
      harness: string;
    };
    expect(component.selector).toBe('tm-select');
    expect(component.formControl).toBe('FormValueControl');
    expect(component.inputs.some((input) => input.name === 'valueKey')).toBe(true);
    expect(component.harness).toBe('TmSelectHarness');
  });

  it('returns story-derived examples', async () => {
    const result = await client.callTool({ name: 'example', arguments: { name: 'tm-select' } });
    const text = (result.content as { type: string; text: string }[])[0].text;
    expect(text).toContain('<tm-select');
    expect(text).toContain('tm-option');
  });

  it('flags unknown components as errors', async () => {
    const result = await client.callTool({ name: 'describe', arguments: { name: 'tm-nope' } });
    expect(result.isError).toBe(true);
  });
});
