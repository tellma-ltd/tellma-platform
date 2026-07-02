/**
 * components.json extractor (spec 0002 §11, plan D5): derives every field
 * from typed source — signal input()/model()/output() call-sites, JSDoc
 * (incl. the @tmGroup, @tmA11yNotes and @tmStatus tags), inline-template ng-content
 * scan, component-CSS var(--…) scan, and the co-located *.stories.ts —
 * so docs cannot drift from code. Nothing is hand-authored.
 *
 * (API Extractor's .api.json stays goldens-only: it cannot see templates,
 * slots, CSS tokens, or stories — recorded deviation from §11's framing.)
 */
import { readFileSync, existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  Node,
  Project,
  type ClassDeclaration,
  type ObjectLiteralExpression,
} from 'ts-morph';

import type { ComponentDoc, ComponentsJson } from './components-schema.mjs';
import { COMPONENTS_JSON_SCHEMA_VERSION } from './components-schema.mjs';

const clientDir = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const uiDir = join(clientDir, 'projects', 'core', 'tellma-core-ui');

/** The documented surfaces and their entry points/harnesses (§11/§12). */
const SOURCES: {
  file: string;
  entryPoint: string;
  harness: string;
  /** Directive-owned GLOBAL stylesheet (a directive carries no styleUrl). */
  styles?: string;
}[] = [
  {
    file: 'input/tm-input.ts',
    entryPoint: '@tellma/core-ui/input',
    harness: 'TmInputHarness',
    styles: 'styles/tm-input.css',
  },
  {
    file: 'checkbox/tm-checkbox.ts',
    entryPoint: '@tellma/core-ui/checkbox',
    harness: 'TmCheckboxHarness',
  },
  {
    file: 'form-field/tm-form-field.ts',
    entryPoint: '@tellma/core-ui/form-field',
    harness: 'TmFormFieldHarness',
  },
  { file: 'select/tm-select.ts', entryPoint: '@tellma/core-ui/select', harness: 'TmSelectHarness' },
  { file: 'select/tm-option.ts', entryPoint: '@tellma/core-ui/select', harness: 'TmOptionHarness' },
];

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

function extractProps(cls: ClassDeclaration) {
  const inputs: ComponentDoc['inputs'] = [];
  const outputs: ComponentDoc['outputs'] = [];
  let formControl: ComponentDoc['formControl'] = null;

  for (const prop of cls.getProperties()) {
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
        type: typeArg ?? prop.getType().getText().replace(/^InputSignal(WithTransform)?</, ''),
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

function extractExamples(sourceFile: string): ComponentDoc['examples'] {
  const storiesPath = sourceFile.replace(/\.ts$/, '.stories.ts');
  if (!existsSync(storiesPath)) {
    return [];
  }
  const text = readFileSync(storiesPath, 'utf8');
  const examples: ComponentDoc['examples'] = [];
  for (const match of text.matchAll(
    /export const (\w+): Story = \{[\s\S]*?template: `([\s\S]*?)`,?\s*\}\)/g,
  )) {
    examples.push({ title: match[1], code: match[2].trim() });
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
    const filePath = join(uiDir, source.file);
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
          source.styles ? join(uiDir, source.styles) : undefined,
        ),
        a11y: extractA11y(cls, template),
        examples: extractExamples(filePath),
        harness: source.harness,
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
