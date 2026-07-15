// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * components.json extractor (spec 0002 §11, plan D5): derives every field
 * from typed source — signal input()/model()/output() call-sites, JSDoc
 * (incl. the @tmGroup, @tmA11yNotes and @tmStatus tags), inline-template ng-content
 * scan, component-CSS var(--…) scan, and the co-located *.examples.ts —
 * so docs cannot drift from code. Nothing is hand-authored.
 *
 * (API Extractor's .api.json stays goldens-only: it cannot see templates,
 * slots, CSS tokens, or examples — recorded deviation from §11's framing.)
 */
import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  Node,
  Project,
  TypeFormatFlags,
  type ClassDeclaration,
  type ObjectLiteralExpression,
  type PropertyDeclaration,
} from 'ts-morph';

// eslint-disable-next-line @typescript-eslint/ban-ts-comment
// @ts-ignore untyped workspace helper (plain .mjs)
import { discoverLibraries } from '../workspace.mjs';
import type { ComponentDoc, ComponentsJson } from './components-schema.mjs';
import { COMPONENTS_JSON_SCHEMA_VERSION } from './components-schema.mjs';

const clientDir = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const uiDir = join(clientDir, 'projects', 'core', 'tellma-core-ui');

/**
 * The documented surfaces, DERIVED from the workspace: every root-level
 * component/directive source (tm-*.ts) of every @tellma/core-ui entry point.
 * Entry points without decorated classes (contracts, the primary) simply
 * contribute nothing; harnesses pair by the `<ClassName>Harness` convention,
 * and a directive's global stylesheet (it has no styleUrl) is declared in
 * the package's "tellma".docs.globalStyles.
 */
const coreUi = discoverLibraries(clientDir).find(
  (library: { name: string }) => library.name === '@tellma/core-ui',
)!;

/**
 * Harnesses pair by the `<ClassName>Harness` convention — but only when the
 * class actually EXISTS in @tellma/core-ui-testing; a component without one
 * (tm-spinner) documents `harness: null` rather than advertising a
 * nonexistent import.
 */
const testingDir = join(clientDir, 'projects', 'core', 'tellma-core-ui-testing');
const TESTING_SOURCE = readdirSync(testingDir)
  .filter((name) => name.endsWith('.ts'))
  .map((name) => readFileSync(join(testingDir, name), 'utf8'))
  .join('\n');
const GLOBAL_STYLES: Record<string, string> = coreUi.tellma?.docs?.globalStyles ?? {};
const SOURCES: { file: string; entryPoint: string; styles?: string }[] = coreUi.entryPoints.flatMap(
  (entryPoint: { id: string; dir: string; importPath: string }) =>
    readdirSync(entryPoint.dir)
      .filter(
        (name) =>
          /^tm-[\w-]+\.ts$/.test(name) &&
          !name.endsWith('.spec.ts') &&
          !name.endsWith('.examples.ts'),
      )
      .map((name) => ({
        file: join(entryPoint.dir, name),
        entryPoint: entryPoint.importPath,
        styles: GLOBAL_STYLES[entryPoint.id]
          ? join(coreUi.dir, GLOBAL_STYLES[entryPoint.id])
          : undefined,
      })),
);

function jsDocText(node: { getJsDocs(): { getDescription(): string }[] }): string {
  const docs = node.getJsDocs();
  return docs.length === 0 ? '' : docs[docs.length - 1].getDescription().trim();
}

function jsDocTag(node: ClassDeclaration, tag: string): string | undefined {
  for (const doc of node.getJsDocs()) {
    for (const docTag of doc.getTags()) {
      if (docTag.getTagName() === tag) {
        return docTag.getCommentText()?.trim();
      }
    }
  }
  return undefined;
}

function decoratorMeta(cls: ClassDeclaration): {
  kind: 'component' | 'directive';
  meta: ObjectLiteralExpression;
} | null {
  for (const name of ['Component', 'Directive'] as const) {
    const decorator = cls.getDecorator(name);
    const arg = decorator?.getArguments()[0];
    if (arg && Node.isObjectLiteralExpression(arg)) {
      return { kind: name === 'Component' ? 'component' : 'directive', meta: arg };
    }
  }
  return null;
}

function metaString(meta: ObjectLiteralExpression, key: string): string | undefined {
  const prop = meta.getProperty(key);
  if (prop && Node.isPropertyAssignment(prop)) {
    const init = prop.getInitializer();
    if (init && (Node.isStringLiteral(init) || Node.isNoSubstitutionTemplateLiteral(init))) {
      return init.getLiteralText();
    }
  }
  return undefined;
}

/**
 * Type text for the docs artifact. Bare getText() qualifies out-of-scope
 * symbols with machine-local `import("…/node_modules/…").` prefixes; the
 * generated files ship in the published package, so nothing machine-specific
 * may appear. The enclosing node + flag keep names scope-relative, and the
 * replace strips any qualifier the printer still emits (e.g. on nested
 * type arguments).
 */
function propTypeText(prop: PropertyDeclaration): string {
  return prop
    .getType()
    .getText(prop, TypeFormatFlags.UseAliasDefinedOutsideCurrentScope)
    .replace(/import\("[^"]*"\)\./g, '');
}

/** The class plus its base-class chain (inherited inputs are API too). */
function classChain(cls: ClassDeclaration): ClassDeclaration[] {
  const chain: ClassDeclaration[] = [];
  for (let current: ClassDeclaration | undefined = cls; current; current = current.getBaseClass()) {
    chain.push(current);
  }
  return chain;
}

function extractProps(cls: ClassDeclaration) {
  const inputs: ComponentDoc['inputs'] = [];
  const outputs: ComponentDoc['outputs'] = [];
  let formControl: ComponentDoc['formControl'] = null;

  // Own properties first, then up the base chain; an override shadows its
  // base declaration.
  const seen = new Set<string>();
  const props = classChain(cls)
    .flatMap((klass) => klass.getProperties())
    .filter((prop) => !seen.has(prop.getName()) && seen.add(prop.getName()));

  for (const prop of props) {
    const init = prop.getInitializer();
    if (!init || !Node.isCallExpression(init)) {
      continue;
    }
    const callee = init.getExpression().getText();
    const typeArg = init.getTypeArguments()[0]?.getText();
    const firstArg = init.getArguments()[0]?.getText();
    const optionsArg = init.getArguments().find((a) => Node.isObjectLiteralExpression(a));
    let alias: string | undefined;
    if (optionsArg && Node.isObjectLiteralExpression(optionsArg)) {
      alias = metaString(optionsArg, 'alias');
    }
    const name = alias ?? prop.getName();
    const description = jsDocText(prop);

    if (callee === 'input' || callee === 'input.required') {
      inputs.push({
        name,
        type: typeArg ?? propTypeText(prop),
        default: callee === 'input.required' ? undefined : firstArg,
        required: callee === 'input.required',
        description,
        signal: 'input',
      });
    } else if (callee === 'model' || callee === 'model.required') {
      inputs.push({
        name,
        type: typeArg ?? 'unknown',
        default: callee === 'model.required' ? undefined : firstArg,
        required: callee === 'model.required',
        description,
        signal: 'model',
      });
      if (name === 'value') {
        formControl = 'FormValueControl';
      }
      if (name === 'checked') {
        formControl = 'FormCheckboxControl';
      }
    } else if (callee === 'output') {
      outputs.push({
        name,
        type: typeArg ?? 'void',
        required: false,
        description,
        signal: 'output',
      });
    }
  }
  return { inputs, outputs, formControl };
}

function extractSlots(template: string, classText: string): ComponentDoc['slots'] {
  const slots: ComponentDoc['slots'] = [];
  for (const match of template.matchAll(/<ng-content(?:\s+select="([^"]+)")?\s*\/?>/g)) {
    const selector = match[1] ?? '*';
    slots.push({
      name: selector === '*' ? 'default' : selector.replace(/[[\]]/g, ''),
      selector,
      description:
        selector === '*'
          ? 'Default projected content.'
          : `Content marked with the ${selector} attribute.`,
    });
  }
  // Content queried (not DOM-projected) children are slots too — e.g.
  // tm-select renders its tm-option children inside the overlay listbox.
  for (const match of classText.matchAll(/contentChildren\((Tm\w+)/g)) {
    const selector = match[1].replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase();
    slots.push({
      name: selector,
      selector,
      description: `${selector} children (queried and rendered by the component).`,
    });
  }
  return slots;
}

function extractTokens(
  componentDir: string,
  meta: ObjectLiteralExpression,
  globalStyles?: string,
): string[] {
  const styleUrl = metaString(meta, 'styleUrl');
  const cssPath = styleUrl ? join(componentDir, styleUrl) : globalStyles;
  if (!cssPath || !existsSync(cssPath)) {
    return [];
  }
  const css = readFileSync(cssPath, 'utf8');
  return [...new Set([...css.matchAll(/var\((--[a-z0-9-]+)/g)].map((m) => m[1]))].sort();
}

/**
 * Reads the co-located *.examples.ts through the AST, not a regex: each
 * exported const object literal contributes its `template` string. A
 * template-less export is skipped cleanly instead of corrupting the scan,
 * and extra properties beside `template` don't confuse it.
 */
export function extractExamples(
  project: Project,
  sourceFile: string,
): ComponentDoc['examples'] {
  const examplesPath = sourceFile.replace(/\.ts$/, '.examples.ts');
  if (!existsSync(examplesPath)) {
    return [];
  }
  const file = project.addSourceFileAtPath(examplesPath);
  const examples: ComponentDoc['examples'] = [];
  for (const decl of file.getVariableDeclarations()) {
    const init = decl.getInitializer();
    if (!decl.isExported() || !init || !Node.isObjectLiteralExpression(init)) {
      continue;
    }
    const templateProp = init.getProperty('template');
    if (!templateProp || !Node.isPropertyAssignment(templateProp)) {
      continue;
    }
    const value = templateProp.getInitializer();
    if (value && (Node.isNoSubstitutionTemplateLiteral(value) || Node.isStringLiteral(value))) {
      examples.push({ title: decl.getName(), code: value.getLiteralText().trim() });
    }
  }
  return examples;
}

/** Roles/keyboard facts derivable from the template + host metadata. */
function extractA11y(cls: ClassDeclaration, template: string): ComponentDoc['a11y'] {
  const roles = [...new Set([...template.matchAll(/role="([\w-]+)"/g)].map((m) => m[1]))];
  if (/ngCombobox/.test(template)) {
    roles.push('combobox');
  }
  if (/ngListbox/.test(template)) {
    roles.push('listbox');
  }
  if (/ngOption/.test(template)) {
    roles.push('option');
  }
  if (/type="checkbox"/.test(template)) {
    roles.push('checkbox');
  }
  const keyboard: string[] = [];
  if (/ngCombobox/.test(template)) {
    keyboard.push(
      'ArrowDown/ArrowUp: move the active option',
      'Home/End: first/last option',
      'Enter/Space: commit the active option',
      'Escape: close the panel',
      'Printable characters: typeahead',
    );
  }
  if (/type="checkbox"/.test(template)) {
    keyboard.push('Space: toggle');
  }
  return {
    roles: [...new Set(roles)].filter((r) => r !== 'none'),
    keyboard,
    notes: jsDocTag(cls, 'tmA11yNotes') ?? '',
  };
}

export function extractComponents(): ComponentsJson {
  const project = new Project({
    tsConfigFilePath: join(clientDir, 'tsconfig.json'),
    skipAddingFilesFromTsConfig: true,
  });

  const components: ComponentDoc[] = [];

  for (const source of SOURCES) {
    const filePath = source.file;
    const sourceFile = project.addSourceFileAtPath(filePath);
    for (const cls of sourceFile.getClasses()) {
      const decorated = decoratorMeta(cls);
      if (!decorated || !cls.isExported()) {
        continue;
      }
      const { kind, meta } = decorated;
      const selector = metaString(meta, 'selector') ?? '';
      const template = metaString(meta, 'template') ?? '';
      const { inputs, outputs, formControl } = extractProps(cls);

      components.push({
        name: selector.startsWith('input[') ? selector.replace(/^input\[|\]$/g, '') : selector,
        kind,
        group: jsDocTag(cls, 'tmGroup') ?? 'form-control',
        selector,
        entryPoint: source.entryPoint,
        formControl,
        description: jsDocText(cls),
        inputs,
        outputs,
        slots: extractSlots(template, cls.getText()),
        tokens: extractTokens(
          dirname(filePath),
          meta,
          source.styles,
        ),
        a11y: extractA11y(cls, template),
        examples: extractExamples(project, filePath),
        harness: TESTING_SOURCE.includes(`class ${cls.getName()}Harness`)
          ? `${cls.getName()}Harness`
          : null,
        status: (jsDocTag(cls, 'tmStatus') as ComponentDoc['status'] | undefined) ?? 'stable',
      });
    }
  }

  const uiPackage = JSON.parse(readFileSync(join(uiDir, 'package.json'), 'utf8')) as {
    version: string;
  };

  return {
    schemaVersion: COMPONENTS_JSON_SCHEMA_VERSION,
    libraryVersion: uiPackage.version,
    components,
  };
}
