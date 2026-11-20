// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;

// Every module package is named Tellma.Module.<M>, and its namespace follows the package name.
// "Module" is a Visual Basic keyword; the platform's packages are consumed from C#.
[assembly: SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "The module package naming convention; modules are consumed from C#, where Module is not a keyword.",
    Scope = "namespace",
    Target = "~N:Tellma.Module.Gl")]
