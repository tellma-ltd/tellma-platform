/**
 * Public API Surface of @tellma/locale-ar — the reference Arabic locale
 * pack: the library's and the platform's strings (in the ./strings entry
 * point, loaded on first use) + the self-hosted Noto Sans Arabic subset. A
 * Tellma app offers Arabic with tmArabic(); an app on
 * the component library alone wires it by provideTellmaLocaleAr(). The
 * template every later locale pack copies.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export * from './provide-tellma-locale-ar';
export * from './tm-arabic';
