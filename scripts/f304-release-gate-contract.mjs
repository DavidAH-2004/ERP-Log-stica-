// F304: Execute the exact Vercel release-gate Bash logic against mocked gh/curl.
// No production API traffic, GitHub commit-status writes, or migrations.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const workflow = fs.readFileSync(path.join(root, '.github/workflows/erp-production-release-gate.yml'), 'utf8');
assert.ok(workflow.includes('status@30f760c6640485cd92f8c785ef361382555fb712'));
assert.ok(workflow.includes("name: 'Vercel - erp-log-stica: ERP-Production-Release-Gate'"));
assert.ok(workflow.includes('types: [vercel.deployment.ready]'));
assert.ok(workflow.includes("github.event.client_payload.environment == 'production'"));
assert.ok(workflow.includes("github.event.client_payload.project.id == 'prj_zmOkd2DQ4NtTqAFi9Bv7RFbGQp82'"));
assert.ok(workflow.includes('GH_TOKEN: $' + '{{ github.token }}'));
const lines = workflow.split(/\r?\n/);
function step(name) {
  const a = lines.indexOf('      - name: ' + name);
  assert.ok(a >= 0, 'step missing ' + name);
  const b = lines.findIndex((x, i) => i > a && x === '        run: |');
  assert.ok(b > a, 'run block missing ' + name);
  const out = [];
  for (let i = b + 1; i < lines.length && lines[i].startsWith('          '); i++) out.push(lines[i].slice(10));
  assert.ok(out.length >= 3, 'empty code ' + name);
  return out.join('\n') + '\n';
}
const bash = step('Fail-closed release authorization and identity') +
  step('Confirm Production API readiness (additional smoke, not schema proof)');
assert.ok(bash.includes('https://marketplaceerp-api.onrender.com/health/ready'));
assert.ok(bash.includes('current_main_sha='));
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'erp-f304-'));
const sha = 'a'.repeat(40);
let count = 0;
try {
  fs.writeFileSync(path.join(tmp, 'gh'),
    '#!/bin/bash\n[[ "$1" == "api" && "$2" == "repos/Marzza01/ERP-Log-stica-/git/ref/heads/main" ]] || exit 77\n[[ "$MOCK_GH_FAIL" == "0" ]] || exit 77\nprintf "%s\\n" "$MOCK_MAIN_SHA"\n', {mode: 0o700});
  fs.writeFileSync(path.join(tmp, 'curl'),
    '#!/bin/bash\n[[ " $* " == *"https://marketplaceerp-api.onrender.com/health/ready"* ]] || exit 78\nprintf "%s" "$MOCK_HTTP_CODE"\n', {mode: 0o700});
  const base = {
    GITHUB_REPOSITORY: 'Marzza01/ERP-Log-stica-', GH_TOKEN: 'fake-no-access',
    TARGET_PROJECT: 'prj_zmOkd2DQ4NtTqAFi9Bv7RFbGQp82',
    TARGET_ID: 'dpl_ABC123abc', TARGET_BRANCH: 'main',
    TARGET_ENVIRONMENT: 'production', TARGET_SHA: sha,
    APPROVED_SHA: sha, BACKEND_VERIFIED_SHA: sha, SCHEMA_VERIFIED_SHA: sha,
    RECOVERY_VERIFIED_SHA: sha, MOCK_MAIN_SHA: sha, MOCK_GH_FAIL: '0', MOCK_HTTP_CODE: '200'
  };
  function check(label, updates, exit, fragment) {
    const env = {...process.env, ...base, ...updates, PATH: tmp + path.delimiter + process.env.PATH};
    const p = spawnSync('bash', ['-c', bash], {env, encoding: 'utf8', timeout: 10000});
    if (p.error) throw p.error;
    const output = (p.stdout || '') + (p.stderr || '');
    assert.equal(p.status, exit, label + ': wrong exit ' + output.slice(0, 300));
    if (fragment) assert.ok(output.includes(fragment), label + ': wrong reason ' + output.slice(0, 300));
    count++;
    console.log('PASS ' + label);
  }
  check('approved release', {}, 0, 'four release attestations validated');
  check('wrong repository', {GITHUB_REPOSITORY:'Wrong/repo'}, 1, 'Unexpected GitHub repository');
  check('wrong project', {TARGET_PROJECT:'prj_other'}, 1, 'Unexpected Vercel project');
  check('preview denied', {TARGET_ENVIRONMENT:'preview'}, 1, 'Not a production deployment');
  check('wrong branch', {TARGET_BRANCH:'staging'}, 1, 'Deployment is not from main');
  check('bad deployment id', {TARGET_ID:'wrong'}, 1, 'Invalid deployment ID');
  check('bad SHA', {TARGET_SHA:'invalid'}, 1, 'Invalid deployment SHA');
  check('obsolete SHA', {MOCK_MAIN_SHA:'b'.repeat(40)}, 1, 'Deployment commit is not current main HEAD');
  check('GitHub check unavailable', {MOCK_GH_FAIL:'1'}, 77, '');
  check('missing authorization', {APPROVED_SHA:''}, 1, 'Missing explicit approval');
  check('wrong authorization', {APPROVED_SHA:'b'.repeat(40)}, 1, 'Missing explicit approval');
  check('missing Render', {BACKEND_VERIFIED_SHA:''}, 1, 'Render production SHA not certified');
  check('wrong Render', {BACKEND_VERIFIED_SHA:'b'.repeat(40)}, 1, 'Render production SHA not certified');
  check('missing Neon', {SCHEMA_VERIFIED_SHA:''}, 1, 'Neon migration/schema compatibility not certified');
  check('wrong Neon', {SCHEMA_VERIFIED_SHA:'b'.repeat(40)}, 1, 'Neon migration/schema compatibility not certified');
  check('missing recovery', {RECOVERY_VERIFIED_SHA:''}, 1, 'Configuration/data recovery gate not certified');
  check('wrong recovery', {RECOVERY_VERIFIED_SHA:'b'.repeat(40)}, 1, 'Configuration/data recovery gate not certified');
  check('not ready', {MOCK_HTTP_CODE:'503'}, 1, 'Production API readiness returned HTTP 503');
  console.log('F304_PASS=' + count + '_OFFLINE_SCENARIOS');
} finally {
  fs.rmSync(tmp, {recursive: true, force: true});
}
