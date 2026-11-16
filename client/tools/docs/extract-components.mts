// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// components.json extractor (spec 0002 §11): derives every field from typed
// source — signal input()/model()/output() call-sites, public members,
// JSDoc (incl. the @tmGroup, @tmA11yNotes, @tmHarness and @tmStatus tags),
// inline-template ng-content scan, CSS var(--…) scan, the co-located
// *.examples.ts and the TSDoc's fenced code — so docs cannot drift from
// code. Nothing is hand-authored.
//
// Components and directives are documented by their decorator. A service
// (an exported `@Injectable`) is documented when its TSDoc carries a
// `@tmGroup` tag: the toast, busy-overlay, confirm, error-dialog, modal and
// drag-drop entry points expose their API through one.
//
// (API Extractor's .api.json stays goldens-only: it cannot see templates,
// slots, CSS tokens, or examples — recorded deviation from §11's framing.)
import { readFileSync, readdirSync, existsSync, statSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  Node,
  Project,
  Scope,
  TypeFormatFlags,
  type ClassDeclaration,
  type GetAccessorDeclaration,
  type MethodDeclaration,
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
 * component, directive or service source (tm-*.ts) of every @tellma/core-ui
 * entry point. Entry points without documented classes (contracts, the
 * primary) simply contribute nothing; harnesses pair by the
 * `<ClassName>Harness` convention unless a `@tmHarness` tag names one, and a
 * directive's global stylesheet (it has no styleUrl) is declared in the
 * package's "tellma".docs.globalStyles.
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
const SOURCES: { file: string; entryPoint: string; entryDir: string; styles?: string }[] =
  coreUi.entryPoints.flatMap((entryPoint: { id: string; dir: string; importPath: string }) =>
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
        entryDir: entryPoint.dir,
        styles: GLOBAL_STYLES[entryPoint.id]
          ? join(coreUi.dir, GLOBAL_STYLES[entryPoint.id])
          : undefined,
      })),
  );

/** Anything that carries JSDoc: a class or one of its members. */
type Documented = { getJsDocs(): ReturnType<ClassDeclaration['getJsDocs']> };

/** The description of the node's last JSDoc block, trimmed; empty without one. */
function jsDocText(node: Documented): string {
  const docs = node.getJsDocs();
  return docs.length === 0 ? '' : docs[docs.length - 1].getDescription().trim();
}

/** The text of the node's first `@<tag>`, or `undefined` without one. */
function jsDocTag(node: Documented, tag: string): string | undefined {
  for (const doc of node.getJsDocs()) {
    for (const docTag of doc.getTags()) {
      if (docTag.getTagName() === tag) {
        return docTag.getCommentText()?.trim();
      }
    }
  }
  return undefined;
}

/** Whether the node's JSDoc carries `tag` at all (a flag tag such as `@internal` has no text). */
function hasJsDocTag(node: Documented, tag: string): boolean {
  return node.getJsDocs().some((doc) => doc.getTags().some((t) => t.getTagName() === tag));
}

/**
 * What a documented class is, and the decorator metadata a component or
 * directive declares. A service has no metadata: it is documented when it
 * is an `@Injectable` whose TSDoc opts in with `@tmGroup`.
 */
function decoratorMeta(
  cls: ClassDeclaration,
):
  | { kind: 'component' | 'directive'; meta: ObjectLiteralExpression }
  | { kind: 'service'; meta: null }
  | null {
  for (const name of ['Component', 'Directive'] as const) {
    const decorator = cls.getDecorator(name);
    const arg = decorator?.getArguments()[0];
    if (arg && Node.isObjectLiteralExpression(arg)) {
      return { kind: name === 'Component' ? 'component' : 'directive', meta: arg };
    }
  }
  if (cls.getDecorator('Injectable') && jsDocTag(cls, 'tmGroup') !== undefined) {
    return { kind: 'service', meta: null };
  }
  return null;
}

/** Angular lifecycle hooks: public by necessity, never API. */
const LIFECYCLE_HOOKS = new Set([
  'ngOnChanges',
  'ngOnInit',
  'ngDoCheck',
  'ngAfterContentInit',
  'ngAfterContentChecked',
  'ngAfterViewInit',
  'ngAfterViewChecked',
  'ngOnDestroy',
]);

/** Strips the machine-local `import("…").` qualifiers a type printer can emit. */
function stripImports(text: string): string {
  return text.replace(/import\("[^"]*"\)\./g, '');
}

/** Whether a class member is public API: public, not `ɵ`-prefixed, not `@internal`, not a hook. */
function isPublicMember(
  member: MethodDeclaration | GetAccessorDeclaration | PropertyDeclaration,
): boolean {
  const name = member.getName();
  return (
    member.getScope() === Scope.Public &&
    !member.isStatic() &&
    !name.startsWith('ɵ') &&
    !name.startsWith('#') &&
    !LIFECYCLE_HOOKS.has(name) &&
    !hasJsDocTag(member, 'internal')
  );
}

/** The signature text of a method declaration, without its body. */
function methodSignature(method: MethodDeclaration): string {
  const typeParams = method.getTypeParameters().map((param) => param.getText());
  // A parameter with a default reads as optional; the default itself is
  // an implementation detail the description states when it matters.
  const params = method.getParameters().map((param) => {
    const rest = param.isRestParameter() ? '...' : '';
    const optional = param.hasQuestionToken() || param.hasInitializer() ? '?' : '';
    const type =
      param.getTypeNode()?.getText() ??
      stripImports(
        param.getType().getText(param, TypeFormatFlags.UseAliasDefinedOutsideCurrentScope),
      );
    return `${rest}${param.getName()}${optional}: ${type}`.replace(/\s+/g, ' ');
  });
  const returnType =
    method.getReturnTypeNode()?.getText() ??
    stripImports(
      method.getReturnType().getText(method, TypeFormatFlags.UseAliasDefinedOutsideCurrentScope),
    );
  const generics = typeParams.length > 0 ? `<${typeParams.join(', ')}>` : '';
  return `${method.getName()}${generics}(${params.join(', ')}): ${returnType}`;
}

/**
 * The public members beyond the signal inputs, models and outputs: methods
 * (each overload, when it has overloads), getters and other public
 * properties, the way a caller holding the instance — a `viewChild`, an
 * injected service — reaches them.
 */
function extractMembers(cls: ClassDeclaration): ComponentDoc['members'] {
  const members: ComponentDoc['members'] = [];
  // Members an implemented interface declares (the form-field control
  // contract, a driver target) belong to that interface's documentation:
  // the library calls them, an app author does not.
  const contract = new Set(
    cls.getImplements().flatMap((clause) =>
      clause
        .getType()
        .getProperties()
        .map((symbol) => symbol.getName()),
    ),
  );
  /** Whether a member is public API that this class itself adds. */
  const documented = (
    member: MethodDeclaration | GetAccessorDeclaration | PropertyDeclaration,
  ): boolean => isPublicMember(member) && !contract.has(member.getName());
  for (const prop of cls.getProperties()) {
    const init = prop.getInitializer();
    const callee = init && Node.isCallExpression(init) ? init.getExpression().getText() : undefined;
    // Signal inputs, models and outputs are documented as inputs/outputs;
    // signal queries and injected dependencies are wiring, not API.
    if (
      callee !== undefined &&
      /^(input|model|output|contentChild|contentChildren|viewChild|viewChildren|inject)(\.required)?$/.test(
        callee,
      )
    ) {
      continue;
    }
    if (!documented(prop)) {
      continue;
    }
    const readonly = prop.isReadonly() ? 'readonly ' : '';
    members.push({
      name: prop.getName(),
      signature: `${readonly}${prop.getName()}: ${prop.getTypeNode()?.getText() ?? propTypeText(prop)}`,
      description: jsDocText(prop),
    });
  }
  for (const accessor of cls.getGetAccessors()) {
    if (!documented(accessor)) {
      continue;
    }
    const type =
      accessor.getReturnTypeNode()?.getText() ??
      stripImports(
        accessor
          .getReturnType()
          .getText(accessor, TypeFormatFlags.UseAliasDefinedOutsideCurrentScope),
      );
    members.push({
      name: accessor.getName(),
      signature: `get ${accessor.getName()}(): ${type}`,
      description: jsDocText(accessor),
    });
  }
  for (const method of cls.getMethods()) {
    if (!documented(method)) {
      continue;
    }
    // An overloaded method documents each overload; its implementation
    // signature is not callable from outside.
    const overloads = method.getOverloads();
    for (const declaration of overloads.length > 0 ? overloads : [method]) {
      members.push({
        name: declaration.getName(),
        signature: methodSignature(declaration),
        description: jsDocText(declaration) || jsDocText(method),
      });
    }
  }
  return members;
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

/** Every `var(--…)` a stylesheet reads, sorted and deduplicated. */
function tokensOf(cssPaths: readonly string[]): string[] {
  const names = cssPaths.flatMap((cssPath) =>
    [...readFileSync(cssPath, 'utf8').matchAll(/var\((--[a-z0-9-]+)/g)].map((m) => m[1]),
  );
  return [...new Set(names)].sort();
}

/** Every stylesheet under `dir`, its internal folders included, in a stable order. */
function stylesheetsUnder(dir: string): string[] {
  return readdirSync(dir)
    .sort()
    .flatMap((name) => {
      const path = join(dir, name);
      if (statSync(path).isDirectory()) {
        return stylesheetsUnder(path);
      }
      return name.endsWith('.css') ? [path] : [];
    });
}

/**
 * The tokens a documented class reads. A component reads its own
 * stylesheet (`styleUrl`), a directive its global one; a service draws
 * through its entry point's internal components, so it reads every
 * stylesheet of the entry point.
 */
function extractTokens(
  componentDir: string,
  meta: ObjectLiteralExpression | null,
  entryDir: string,
  globalStyles?: string,
): string[] {
  if (meta === null) {
    return tokensOf(stylesheetsUnder(entryDir));
  }
  const styleUrl = metaString(meta, 'styleUrl');
  const cssPath = styleUrl ? join(componentDir, styleUrl) : globalStyles;
  if (!cssPath || !existsSync(cssPath)) {
    return [];
  }
  return tokensOf([cssPath]);
}

/**
 * Reads the co-located *.examples.ts through the AST, not a regex: each
 * exported const object literal contributes its `template` string. A
 * template-less export is skipped cleanly instead of corrupting the scan,
 * and extra properties beside `template` don't confuse it.
 */
export function extractExamples(project: Project, sourceFile: string): ComponentDoc['examples'] {
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
      examples.push({
        title: decl.getName(),
        code: value.getLiteralText().trim(),
        language: 'html',
      });
    }
  }
  return examples;
}

/**
 * The fenced TypeScript in a class's TSDoc — its description and its
 * `@example` tags — titled by the class: how a service is called, which no
 * template shows. JSDoc's leading ` * ` is already stripped from the text.
 */
export function extractCodeExamples(cls: ClassDeclaration): ComponentDoc['examples'] {
  const text = cls
    .getJsDocs()
    .map((doc) => doc.getInnerText())
    .join('\n');
  return [...text.matchAll(/```ts\r?\n([\s\S]*?)```/g)].map((match, index, all) => ({
    title: all.length === 1 ? cls.getName()! : `${cls.getName()!} (${index + 1})`,
    code: match[1].trim(),
    language: 'ts' as const,
  }));
}

/**
 * The harness a class pairs with: the one its `@tmHarness` tag names, else
 * `<ClassName>Harness`, and only when that class exists in
 * @tellma/core-ui-testing. A tag that names a missing harness throws, so
 * docs:build never advertises an import that does not exist.
 */
function harnessOf(cls: ClassDeclaration): string | null {
  const tagged = jsDocTag(cls, 'tmHarness');
  if (tagged !== undefined) {
    if (!TESTING_SOURCE.includes(`class ${tagged} `)) {
      throw new Error(
        `${cls.getName()} names @tmHarness ${tagged}, which @tellma/core-ui-testing does not export.`,
      );
    }
    return tagged;
  }
  return TESTING_SOURCE.includes(`class ${cls.getName()}Harness`)
    ? `${cls.getName()}Harness`
    : null;
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
      const selector = meta === null ? null : (metaString(meta, 'selector') ?? '');
      const template = meta === null ? '' : (metaString(meta, 'template') ?? '');
      const { inputs, outputs, formControl } = extractProps(cls);

      // A service is named by its class. Multi-selectors ('input[tmInput],
      // textarea[tmInput]') derive the display name from their FIRST part —
      // the whole-string strip would mangle it into 'tmInput], textarea[tmInput'.
      const primarySelector = selector?.split(',')[0].trim() ?? '';
      const name =
        selector === null
          ? cls.getName()!
          : primarySelector.startsWith('input[')
            ? primarySelector.replace(/^input\[|\]$/g, '')
            : selector;
      components.push({
        name,
        kind,
        // Unchecked here: docs:build validates the closed set against the
        // schema and names the offending component.
        group: (jsDocTag(cls, 'tmGroup') ?? 'form-control') as ComponentDoc['group'],
        selector,
        entryPoint: source.entryPoint,
        formControl,
        description: jsDocText(cls),
        inputs,
        outputs,
        members: extractMembers(cls),
        slots: extractSlots(template, cls.getText()),
        tokens: extractTokens(dirname(filePath), meta, source.entryDir, source.styles),
        a11y: extractA11y(cls, template),
        // Templates first: they are what a page author copies; a service's
        // call shows in its TSDoc's code.
        examples: [...extractExamples(project, filePath), ...extractCodeExamples(cls)],
        harness: harnessOf(cls),
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
