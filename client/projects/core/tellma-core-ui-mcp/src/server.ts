#!/usr/bin/env node
/**
 * @tellma/core-ui-mcp — scoped MCP server for the Tellma UI component library.
 *
 * Serves list/describe/example tools over stdio, answering from the generated
 * components.json (the single source of truth extracted from the component
 * sources). The tools are registered in a later stage, once components.json
 * generation lands; until then the server starts and reports no tools.
 */
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';

const server = new McpServer({ name: 'tellma-core-ui-mcp', version: '0.1.0' });

const transport = new StdioServerTransport();
await server.connect(transport);
