// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable } from '@angular/core';
import { DefaultUrlSerializer, type UrlTree } from '@angular/router';

// The canonical form of the app's paths (spec 0024 §9.1): no trailing slash,
// so `/17/` routes as `/17` and the address bar shows `/17`. Case is never
// folded, and the query and fragment are kept as they are.

/** The router's URL parser, which drops a path's trailing slashes. */
@Injectable()
export class TmUrlSerializer extends DefaultUrlSerializer {
  /**
   * Parses a URL after dropping its path's trailing slashes.
   *
   * @param url - The URL, such as `/17/?q=jed`.
   * @returns The URL tree of its canonical form, such as `/17?q=jed`.
   */
  override parse(url: string): UrlTree {
    const end = url.search(/[?#]/);
    const path = end < 0 ? url : url.slice(0, end);
    return super.parse((path.replace(/\/+$/, '') || '/') + (end < 0 ? '' : url.slice(end)));
  }
}
