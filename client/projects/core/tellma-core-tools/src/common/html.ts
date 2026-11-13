// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { parse, parseFragment, type DefaultTreeAdapterTypes } from 'parse5';

// HTML and SVG as the browser parses them, through parse5's implementation of
// the HTML standard's parser, so the checks see the elements and attributes a
// browser would, not what a pattern match guesses.

/** An element of a parsed tree. */
export type TmHtmlElement = DefaultTreeAdapterTypes.Element;

/** A parsed node that holds children. */
type Parent = DefaultTreeAdapterTypes.ParentNode;

/** Parses a whole document, as a browser parses `index.html`. */
export function tmParseDocument(html: string): DefaultTreeAdapterTypes.Document {
  return parse(html);
}

/** Parses a fragment, as a browser parses markup inside `<body>`; an SVG file parses as foreign content. */
export function tmParseFragment(html: string): DefaultTreeAdapterTypes.DocumentFragment {
  return parseFragment(html);
}

/**
 * Lists every element under a node in document order, the contents of
 * `<template>` elements included.
 *
 * @param root - The node to search.
 * @returns The elements.
 */
export function tmElements(root: Parent): TmHtmlElement[] {
  const found: TmHtmlElement[] = [];
  const walk = (node: Parent): void => {
    for (const child of node.childNodes) {
      if ('tagName' in child) {
        found.push(child);
        walk(child);
        if (child.tagName === 'template' && 'content' in child) {
          walk((child as DefaultTreeAdapterTypes.Template).content);
        }
      }
    }
  };
  walk(root);
  return found;
}

/** The value of an element's attribute, or null when it has none. */
export function tmAttribute(element: TmHtmlElement, name: string): string | null {
  return element.attrs.find((attribute) => attribute.name === name)?.value ?? null;
}

/** The first element with the tag name under a node, or null. */
export function tmFirstElement(root: Parent, tagName: string): TmHtmlElement | null {
  return tmElements(root).find((element) => element.tagName === tagName) ?? null;
}
