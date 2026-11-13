// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The Acme web host: the three platform calls and nothing else. What the distribution adds goes in
// AcmeComposition (both hosts) or AcmeWebComposition (this host only), and its endpoints onto the
// groups MapTellma returns.
using Tellma.Core.AspNetCore;
using Tellma.Distro.Acme;
using Tellma.Distro.Acme.Web;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
_ = builder.AddTellma(AcmeComposition.Slug, AcmeComposition.Compose, AcmeWebComposition.Compose);
WebApplication app = builder.Build();
_ = app.UseTellma();
_ = app.MapTellma();
app.Run();
