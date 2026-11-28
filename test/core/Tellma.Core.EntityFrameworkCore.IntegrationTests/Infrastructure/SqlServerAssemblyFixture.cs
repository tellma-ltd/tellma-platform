// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// One SQL Server for the whole assembly: the shared fixture, which every test reaches through its
// constructor and asks for a database of its own (CreateDatabaseAsync).
[assembly: AssemblyFixture(typeof(SqlServerFixture))]
