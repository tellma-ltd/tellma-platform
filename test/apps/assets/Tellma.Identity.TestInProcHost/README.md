# Tellma.Identity.TestInProcHost

A **test asset** (not an xunit project): a minimal distribution-shaped web host that mounts the
[`Tellma.Identity`](../../../../src/apps/Tellma.Identity/README.md) engine in-proc at the reserved
`/id` path base, exactly the way a distribution's web host would. The integration suite boots it via
`WebApplicationFactory` to prove the in-proc composition serves the same authority as the standalone
host (composition parity), and to run the critical flow suites against both shapes.
