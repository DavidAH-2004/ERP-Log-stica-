import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {evaluateReleaseEvidence} from './f484-production-evidence-gate.mjs';
const sha='a'.repeat(40);
const pass={
  mainSha:sha, renderLiveSha:sha, vercelProductionSha:sha,
  renderBranch:'main',vercelProductionBranch:'main',
  gitMainProtected:true,prodNeonTargetIndependentlyVerified:true,
  productionBackupRestoredToIsolatedDatabase:true,
  renderSecretsRestorable:true,vercelSecretsRestorable:true,
  activeWritersQuiescedOrFenced:true,rollbackRunbookValidated:true,
  paymentsRemainDisabled:true,stagingDatabaseIndependent:true,
  explicitOwnerProductionAuthorization:true,
  artifactId:'F484-SYNTHETIC-ONLY',evidenceCapturedAtUtc:'2026-11-16T00:00:00Z',
  productionBackendReachable:true,productionFrontendReachable:true
};
assert.equal(evaluateReleaseEvidence(pass).status,'READY_FOR_MANUAL_REVIEW');
for(const [key,value] of [
  ['renderLiveSha','b'.repeat(40)],
  ['vercelProductionSha','c'.repeat(40)],
  ['renderBranch','integracion/main-reconciliacion-20261102'],
  ['vercelProductionBranch','development'],
  ['prodNeonTargetIndependentlyVerified',false],
  ['productionBackupRestoredToIsolatedDatabase',false],
  ['renderSecretsRestorable',false],
  ['vercelSecretsRestorable',false],
  ['activeWritersQuiescedOrFenced',false],
  ['rollbackRunbookValidated',false],
  ['paymentsRemainDisabled',false],
  ['stagingDatabaseIndependent',false],
  ['gitMainProtected',false],
  ['explicitOwnerProductionAuthorization',false],
  ['artifactId',''],
  ['evidenceCapturedAtUtc',''],
  ['productionBackendReachable',false]
]) {
  const invalid={...pass,[key]:value};
  const result=evaluateReleaseEvidence(invalid);
  assert.equal(result.status,'NO_GO',key);
  assert.ok(result.missing.length>0,key);
}
// Known *partial* public inventory of actual infrastructure: mismatched Render
// commit and historical branch MUST be NO_GO even when other fields are true.
const observed={
  ...pass, mainSha:'8325ccec0bee1eaf30511f4c190804fcb073c769',
  renderLiveSha:'77e9a16ea2b9d7d7cdd3c83c65bf9bb1b4e81d1d',
  renderBranch:'integracion/main-reconciliacion-20261102',
  vercelProductionSha:'b'.repeat(40),vercelProductionBranch:'unknown'
};
assert.equal(evaluateReleaseEvidence(observed).status,'NO_GO');
assert.equal(evaluateReleaseEvidence({}).status,'NO_GO');

const observedFromConnectedProviders = JSON.parse(
  readFileSync(new URL('../docs/F486_PRODUCTION_NO_GO_PUBLIC_EVIDENCE.json',import.meta.url),'utf8'));
const verified = evaluateReleaseEvidence(observedFromConnectedProviders);
assert.equal(verified.status,'NO_GO');
assert.ok(verified.missing.some(x=>x.includes('Render SHA')));
assert.ok(verified.missing.some(x=>x.includes('Vercel public alias SHA')));
assert.ok(verified.missing.some(x=>x.includes('Render production branch')));
console.log('F484_PRODUCTION_EVIDENCE_GATE_PASS readOnly=true deploy=false');
