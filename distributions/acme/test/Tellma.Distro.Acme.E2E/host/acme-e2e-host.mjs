// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * The Acme e2e host, which Playwright's webServer starts: it brings up a SQL
 * Server, migrates the deployment onto it, builds the client app, and then
 * runs the Acme web host in Development, with the in-process identity engine,
 * serving the built app (its service worker, content security policy and
 * hashed names) on https://localhost:<port>. Sign-in codes go to the
 * development mail drop, a directory of one JSON file per message.
 *
 * Inputs, all environment variables:
 *
 *   ACME_E2E_PORT              the host's port (set by scripts/e2e.mjs)
 *   TELLMA_E2E_MAIL_DROP       the mail drop's directory (set by the config)
 *   TELLMA_E2E_STATE_DIR       the signed-in states' directory (set by the config)
 *   TELLMA_E2E_SQL             `localdb` (the Windows default), `container`
 *                              (the default elsewhere: a throwaway SQL Server
 *                              container) or a connection string to a server;
 *                              unset, TELLMA_TEST_SQL is used when it is set
 *   TELLMA_TEST_SQL_IMAGE      the container's image
 *   TELLMA_E2E_CONFIGURATION   the .NET build configuration, Debug by default
 *   TELLMA_E2E_WEBROOT         an app already built (a published wwwroot, say),
 *                              served as it is with no client build
 *
 * On a server that outlives the run (LocalDB or a named one) the databases
 * are kept between runs, named Tellma.e2e.<DB_SUFFIX from .dev-ports.local>.acme.*,
 * so a rerun migrates nothing and keeps its signed-in states. A container is
 * fresh every run, and so are the states. The host exits when Playwright
 * stops it, and takes the web host and the container with it.
 *
 * Usage: node host/acme-e2e-host.mjs   (Playwright's webServer runs it)
 */
import { spawn, spawnSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { createWriteStream, existsSync, mkdirSync, readFileSync, rmSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseEnv } from 'node:util';

const packageDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const repoRoot = resolve(packageDir, '..', '..', '..', '..');
const artifacts = join(packageDir, '.artifacts', 'host');
const acmeSrc = join(repoRoot, 'distributions', 'acme', 'src');
const windows = process.platform === 'win32';

/** The children to stop when the host stops, newest first. */
const cleanups = [];
let stopping = false;

// --- Inputs ---
const port = Number(process.env['ACME_E2E_PORT']);
const mailDrop = process.env['TELLMA_E2E_MAIL_DROP'];
const stateDir = process.env['TELLMA_E2E_STATE_DIR'];
if (!Number.isInteger(port) || port <= 0 || !mailDrop || !stateDir) {
  fail(
    'ACME_E2E_PORT, TELLMA_E2E_MAIL_DROP and TELLMA_E2E_STATE_DIR must be set. Run the suite through `pnpm run e2e`.',
  );
}
const configuration = process.env['TELLMA_E2E_CONFIGURATION'] || 'Debug';
const origin = `https://localhost:${port}`;

process.on('SIGINT', () => stop(130));
process.on('SIGTERM', () => stop(143));
process.on('exit', () => runCleanups());

try {
  mkdirSync(artifacts, { recursive: true });
  const sql = await bringUpSql();
  if (sql.fresh) {
    // A fresh catalog knows none of the kept identity cookies.
    rmSync(stateDir, { recursive: true, force: true });
  }
  mkdirSync(stateDir, { recursive: true });

  const webroot = process.env['TELLMA_E2E_WEBROOT']
    ? resolve(process.env['TELLMA_E2E_WEBROOT'])
    : buildClient();
  if (!existsSync(join(webroot, 'index.html'))) {
    fail(`The app at ${webroot} has no index.html.`);
  }

  // --- The deployment: the catalog, the identity store and the seeded tenants ---
  const settings = {
    ASPNETCORE_ENVIRONMENT: 'Development',
    DOTNET_ENVIRONMENT: 'Development',
    Tellma__PublicOrigin: origin,
    Tellma__Catalog__ConnectionString: sql.catalog,
    Tellma__Sql__Profiles__default: sql.profile,
    Tellma__Sql__DatabasePrefix: sql.prefix,
  };
  log(`migrating ${sql.catalogName}`);
  await run(
    'dotnet',
    [
      'run',
      '--project',
      join(acmeSrc, 'Tellma.Distro.Acme.Migrator'),
      '-c',
      configuration,
      '--no-launch-profile',
      '--',
      'migrate',
    ],
    settings,
  );

  // --- The web host, serving the built app with the mail drop as its email transport ---
  // Kestrel serves HTTPS in Development with the ASP.NET development certificate, which a fresh
  // machine (a CI runner) lacks; creating it needs no trust, since the browsers ignore its issuer.
  spawnSync('dotnet', ['dev-certs', 'https'], { stdio: 'ignore' });
  rmSync(mailDrop, { recursive: true, force: true });
  mkdirSync(mailDrop, { recursive: true });
  log(`starting the web host on ${origin} (its log: ${join(artifacts, 'host.log')})`);
  const web = start(
    'dotnet',
    [
      'run',
      '--project',
      join(acmeSrc, 'Tellma.Distro.Acme.Web'),
      '-c',
      configuration,
      '--no-launch-profile',
      '--',
      '--urls',
      origin,
      '--webroot',
      webroot,
    ],
    {
      ...settings,
      Email__Provider: 'development-drop',
      Email__DevelopmentDrop__Directory: mailDrop,
      Tellma__Blobs__FileSystem__RootPath: join(artifacts, 'blobs'),
      // Every worker's browser reaches the host from one loopback address, and
      // parallel tests share the principals, so the per-address and per-user
      // request rates stand for a whole office, not one person: the defaults
      // (60 anonymous requests a minute per address) would refuse a run.
      Tellma__Api__AnonymousRequestsPerMinutePerIp: '100000',
      Tellma__Api__RequestsPerMinutePerUser: '100000',
      Tellma__Api__RequestsPerMinutePerUserAcrossTenants: '100000',
    },
    join(artifacts, 'host.log'),
  );
  web.on('exit', (code, signal) => {
    if (!stopping) {
      fail(`The web host exited (${signal ?? code}); its log is ${join(artifacts, 'host.log')}.`);
    }
  });
} catch (error) {
  fail(error instanceof Error ? error.message : String(error));
}

/**
 * The SQL Server the run uses, and the names and settings of its databases.
 *
 * @returns {Promise<{ catalog: string, catalogName: string, profile: string, prefix: string, fresh: boolean }>}
 */
async function bringUpSql() {
  const choice =
    process.env['TELLMA_E2E_SQL'] ||
    process.env['TELLMA_TEST_SQL'] ||
    (windows ? 'localdb' : 'container');
  const suffix = devPorts()['DB_SUFFIX'];
  const base = suffix ? `Tellma.e2e.${suffix}.acme` : 'Tellma.e2e.acme';

  // --- A throwaway container, removed when the host stops ---
  if (choice === 'container') {
    const image =
      process.env['TELLMA_TEST_SQL_IMAGE'] || 'mcr.microsoft.com/mssql/server:2022-latest';
    const name = `tellma-e2e-acme-${port}`;
    const password = `Tm-${randomBytes(12).toString('hex')}-A1`;
    // Every container of this worktree's runs carries its label. A run that was
    // killed before its cleanup left one behind, under another run's port; it
    // is this run's to remove, as is one that holds this run's name.
    const label = `tellma-e2e=${suffix || 'default'}`;
    const leftovers = spawnSync('docker', ['ps', '-aq', '--filter', `label=${label}`], {
      encoding: 'utf8',
    })
      .stdout?.split(/\s+/)
      .filter((id) => id !== '');
    spawnSync('docker', ['rm', '-f', name, ...(leftovers ?? [])], { stdio: 'ignore' });
    log(`starting SQL Server (${image})`);
    const started = spawnSync(
      'docker',
      [
        'run',
        '-d',
        '--name',
        name,
        '--label',
        label,
        '-e',
        'ACCEPT_EULA=Y',
        '-e',
        `MSSQL_SA_PASSWORD=${password}`,
        '-p',
        '127.0.0.1::1433',
        image,
      ],
      { encoding: 'utf8' },
    );
    if (started.status !== 0) {
      fail(
        `Docker could not start SQL Server: ${started.stderr || started.error?.message}. Start Docker, or set TELLMA_E2E_SQL to a connection string.`,
      );
    }
    cleanups.push(() => spawnSync('docker', ['rm', '-f', name], { stdio: 'ignore' }));
    await waitForContainer(name, password);
    const mapped = /:(\d+)\s*$/m.exec(
      spawnSync('docker', ['port', name, '1433/tcp'], { encoding: 'utf8' }).stdout ?? '',
    );
    if (!mapped) {
      fail("Docker did not report the SQL Server container's port.");
    }
    const server = `Server=127.0.0.1,${mapped[1]}`;
    const credential = `User ID=sa;Password=${password};TrustServerCertificate=True`;
    return databases(server, credential, base, true);
  }

  // --- LocalDB, started if it is not running ---
  if (choice === 'localdb') {
    if (!windows) {
      fail(
        'LocalDB runs only on Windows. Set TELLMA_E2E_SQL to `container` or to a connection string.',
      );
    }
    spawnSync('sqllocaldb', ['start', 'MSSQLLocalDB'], { stdio: 'ignore' });
    return databases(
      'Server=(localdb)\\MSSQLLocalDB',
      'Integrated Security=True;TrustServerCertificate=True',
      base,
      false,
    );
  }

  // --- A server named by its connection string: its server keys, and its credential as the profile ---
  const pairs = choice
    .split(';')
    .map((pair) => pair.trim())
    .filter((pair) => pair.includes('='));
  const isServerKey = (pair) =>
    /^(server|data source|address|addr|network address)\s*=/i.test(pair);
  const isDatabaseKey = (pair) => /^(database|initial catalog|max pool size)\s*=/i.test(pair);
  const server = pairs.filter(isServerKey).join(';');
  if (!server) {
    fail(
      `TELLMA_E2E_SQL is neither \`localdb\`, \`container\` nor a connection string that names a server.`,
    );
  }
  const credential = pairs.filter((pair) => !isServerKey(pair) && !isDatabaseKey(pair)).join(';');
  return databases(server, credential, base, false);
}

/**
 * The deployment's databases on one server.
 *
 * @param {string} server The server's connection-string keys.
 * @param {string} credential The credential's connection-string keys.
 * @param {string} base The databases' common name.
 * @param {boolean} fresh Whether the server is new to this run.
 */
function databases(server, credential, base, fresh) {
  const catalogName = `${base}.catalog`;
  return {
    catalog: [server, `Database=${catalogName}`, credential].filter(Boolean).join(';'),
    catalogName,
    // The platform's startup check wants every credential profile to cap its pool.
    profile: [credential, 'Max Pool Size=20'].filter(Boolean).join(';'),
    prefix: base,
    fresh,
  };
}

/**
 * Waits until the container's SQL Server answers a query, or fails after three minutes.
 *
 * @param {string} name The container's name.
 * @param {string} password The sa password.
 */
async function waitForContainer(name, password) {
  const deadline = Date.now() + 180_000;
  for (;;) {
    const probe = spawnSync(
      'docker',
      [
        'exec',
        name,
        '/opt/mssql-tools18/bin/sqlcmd',
        '-S',
        'localhost',
        '-U',
        'sa',
        '-P',
        password,
        '-C',
        '-l',
        '5',
        '-Q',
        'SELECT 1',
      ],
      { stdio: 'ignore' },
    );
    if (probe.status === 0) {
      return;
    }
    const state = spawnSync('docker', ['inspect', '-f', '{{.State.Running}}', name], {
      encoding: 'utf8',
    }).stdout?.trim();
    if (state !== 'true') {
      fail(
        `The SQL Server container stopped. If the image exits at once, set TELLMA_TEST_SQL_IMAGE=mcr.microsoft.com/mssql/server:2025-latest.`,
      );
    }
    if (Date.now() > deadline) {
      fail('The SQL Server container did not answer within three minutes.');
    }
    // A probe that failed returns at once; the next one waits for the server's own start-up.
    await new Promise((settle) => setTimeout(settle, 1000));
  }
}

/**
 * Builds the client app with `tellma-client build` and returns its output.
 *
 * @returns {string} The built app's directory.
 */
function buildClient() {
  const client = join(acmeSrc, 'Tellma.Distro.Acme.Client');
  log('building the client app (tellma-client build)');
  const built = spawnSync('pnpm', ['--dir', client, 'run', 'build'], {
    stdio: 'inherit',
    shell: windows,
  });
  if (built.status !== 0) {
    fail(`The client build failed (exit ${built.status ?? built.signal}).`);
  }
  return join(client, 'dist', 'acme', 'browser');
}

/**
 * Runs a command to its end, failing the host when it fails.
 *
 * @param {string} command The command.
 * @param {string[]} args Its arguments.
 * @param {Record<string, string>} env Variables over the host's own.
 */
function run(command, args, env) {
  return new Promise((settle) => {
    const child = spawn(command, args, {
      cwd: repoRoot,
      env: { ...process.env, ...env },
      stdio: 'inherit',
    });
    child.on('error', (error) => fail(`${command} did not start: ${error.message}`));
    child.on('exit', (code, signal) => {
      if (code !== 0) {
        fail(`${command} ${args.at(-1)} failed (${signal ?? code}).`);
      }
      settle();
    });
  });
}

/**
 * Starts a long-running command whose output goes to a log file, stopped with the host.
 *
 * @param {string} command The command.
 * @param {string[]} args Its arguments.
 * @param {Record<string, string>} env Variables over the host's own.
 * @param {string} logFile The log file.
 */
function start(command, args, env, logFile) {
  const output = createWriteStream(logFile);
  const child = spawn(command, args, {
    cwd: repoRoot,
    env: { ...process.env, ...env },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  child.stdout.pipe(output);
  child.stderr.pipe(output);
  child.on('error', (error) => fail(`${command} did not start: ${error.message}`));
  cleanups.push(() => stopTree(child));
  return child;
}

/**
 * Stops a child and every process it started: dotnet run starts the host as a child of its own.
 *
 * @param {import('node:child_process').ChildProcess} child The child.
 */
function stopTree(child) {
  if (child.exitCode !== null || child.pid === undefined) {
    return;
  }
  if (windows) {
    spawnSync('taskkill', ['/pid', String(child.pid), '/T', '/F'], { stdio: 'ignore' });
  } else {
    child.kill('SIGTERM');
  }
}

/** The worktree's .dev-ports.local, or nothing when there is none. */
function devPorts() {
  const file = join(repoRoot, '.dev-ports.local');
  return existsSync(file) ? parseEnv(readFileSync(file, 'utf8')) : {};
}

/** Runs every cleanup once, newest first. */
function runCleanups() {
  while (cleanups.length > 0) {
    try {
      cleanups.pop()();
    } catch {
      // A cleanup that fails leaves the others to run.
    }
  }
}

/**
 * Stops the host and everything it started.
 *
 * @param {number} code The exit code.
 */
function stop(code) {
  stopping = true;
  runCleanups();
  process.exit(code);
}

/**
 * Reports a failure and stops the host.
 *
 * @param {string} message What failed.
 * @returns {never}
 */
function fail(message) {
  console.error(`[acme-e2e-host] ${message}`);
  stop(1);
}

/**
 * Reports progress.
 *
 * @param {string} message The progress.
 */
function log(message) {
  console.log(`[acme-e2e-host] ${message}`);
}
