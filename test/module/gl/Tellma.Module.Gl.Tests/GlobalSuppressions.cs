// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;

// The suite's namespaces follow the package's, Tellma.Module.Gl; "Module" is a Visual Basic keyword.
[assembly: SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "The module package naming convention; the suite is C#, where Module is not a keyword.",
    Scope = "namespaceanddescendants",
    Target = "~N:Tellma.Module.Gl.Tests")]
