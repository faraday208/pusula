// Starts a published pusula from a folder of its own, the way someone who downloaded it would, and checks what that
// person gets: the default address (from the settings built into the program), the page, a file of the page and the API.
// The release workflow and CI run it on every platform the downloads are made for.
//
// Usage: node tools/smoke-test.mjs <path of the pusula binary>

import { spawn } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const expectedAddress = 'http://localhost:5190';
const startTimeoutMs = 60_000;

const binary = process.argv[2];
if (!binary) {
  console.error('usage: node tools/smoke-test.mjs <path of the pusula binary>');
  process.exit(2);
}

const folder = mkdtempSync(join(tmpdir(), 'pusula-smoke-'));
const notes = join(folder, 'notes');
mkdirSync(notes);
writeFileSync(join(notes, 'Note.md'), '# Note\n');

const startedAt = Date.now();
const server = spawn(resolve(binary), [notes], { cwd: folder, stdio: ['ignore', 'pipe', 'pipe'] });
const exited = new Promise(done => server.once('exit', done));
let output = '';
server.stdout.on('data', chunk => { output += chunk; });
server.stderr.on('data', chunk => { output += chunk; });

function listening() {
  return new Promise((done, failed) => {
    const timer = setTimeout(() => failed(new Error(`no "Now listening on" within ${startTimeoutMs / 1000} s`)), startTimeoutMs);
    exited.then(code => {
      clearTimeout(timer);
      failed(new Error(`pusula exited (${code}) before it listened`));
    });
    server.stdout.on('data', () => {
      const match = /Now listening on: (\S+)/.exec(output);
      if (match) {
        clearTimeout(timer);
        done(match[1]);
      }
    });
  });
}

async function check(url, what, test) {
  const response = await fetch(url);
  const body = await response.text();
  if (response.status !== 200 || !test(response, body)) {
    throw new Error(`${what}: ${url} answered ${response.status} (${response.headers.get('content-type')})`);
  }
  console.log(`ok: ${what}`);
}

try {
  const address = await listening();
  console.log(`listening on ${address} after ${Date.now() - startedAt} ms`);
  if (address !== expectedAddress) {
    throw new Error(`listens on ${address}, not on ${expectedAddress}: the built-in settings were not read`);
  }

  await check(`${address}/`, 'the page', (_, body) => body.includes('<title>pusula</title>'));
  await check(`${address}/js/app.js`, 'a file of the page', response => response.headers.get('content-type')?.includes('javascript'));
  await check(`${address}/api/sources`, 'the API', (_, body) => {
    const sources = JSON.parse(body).sources;
    return sources.length === 1 && sources[0].id === 'notes' && sources[0].available === true;
  });
  console.log('smoke test passed');
} catch (error) {
  console.error(`smoke test failed: ${error.message}\n--- what pusula wrote ---\n${output}`);
  process.exitCode = 1;
} finally {
  server.kill();
  await exited;
  rmSync(folder, { recursive: true, force: true, maxRetries: 5 });
}
