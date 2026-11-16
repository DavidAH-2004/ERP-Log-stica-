/**
 * F484: release evidence evaluator. OFFLINE and read-only.
 * It does not fetch Render/Neon/Vercel, deploy, merge or approve production.
 * All attestation fields must come from independent, verified sources.
 * A "ready" result is NOT permission to operate Production.
 */
import {readFileSync} from 'node:fs';
import {fileURLToPath} from 'node:url';

const sha = /^[a-f0-9]{40}$/i;
const yes = value => value === true;
const nonempty = value => typeof value === 'string' && value.trim().length > 0;

export function evaluateReleaseEvidence(evidence) {
  const missing = [];
  if(!evidence || typeof evidence !== 'object' || Array.isArray(evidence))
    return {status:'NO_GO', missing:['evidence object absent']};
  for(const [key,valid] of [
    ['mainSha', sha.test(evidence.mainSha ?? '')],
    ['renderLiveSha', sha.test(evidence.renderLiveSha ?? '')],
    ['vercelProductionSha', sha.test(evidence.vercelProductionSha ?? '')],
    ['gitMainProtected', yes(evidence.gitMainProtected)],
    ['prodNeonTargetIndependentlyVerified', yes(evidence.prodNeonTargetIndependentlyVerified)],
    ['productionBackupRestoredToIsolatedDatabase', yes(evidence.productionBackupRestoredToIsolatedDatabase)],
    ['renderSecretsRestorable', yes(evidence.renderSecretsRestorable)],
    ['vercelSecretsRestorable', yes(evidence.vercelSecretsRestorable)],
    ['activeWritersQuiescedOrFenced', yes(evidence.activeWritersQuiescedOrFenced)],
    ['rollbackRunbookValidated', yes(evidence.rollbackRunbookValidated)],
    ['paymentsRemainDisabled', yes(evidence.paymentsRemainDisabled)],
    ['stagingDatabaseIndependent', yes(evidence.stagingDatabaseIndependent)],
    ['explicitOwnerProductionAuthorization', yes(evidence.explicitOwnerProductionAuthorization)],
    ['artifactId', nonempty(evidence.artifactId)],
    ['evidenceCapturedAtUtc', /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$/.test(evidence.evidenceCapturedAtUtc ?? '')]
  ]) if(!valid)missing.push(key);

  if(evidence.renderBranch !== 'main') missing.push('Render production branch must be main');
  if(evidence.vercelProductionBranch !== 'main') missing.push('Vercel public production alias must be main');
  if(sha.test(evidence.mainSha ?? '') &&
      evidence.mainSha !== evidence.renderLiveSha)
    missing.push('Render SHA does not equal main');
  if(sha.test(evidence.mainSha ?? '') &&
      evidence.mainSha !== evidence.vercelProductionSha)
    missing.push('Vercel public alias SHA does not equal main');
  if(evidence.productionBackendReachable !== true ||
     evidence.productionFrontendReachable !== true)
    missing.push('both production health checks required');
  return {status:missing.length===0?'READY_FOR_MANUAL_REVIEW':'NO_GO',missing};
}

if(process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const file = process.argv[2];
  if(!file){console.error('F484_NO_GO: provide a local verified evidence JSON file');process.exitCode=2;}
  else {
    try {
      const doc=JSON.parse(readFileSync(file,'utf8'));
      const result=evaluateReleaseEvidence(doc);
      console.log(JSON.stringify(result));
      if(result.status!=='READY_FOR_MANUAL_REVIEW')process.exitCode=2;
    } catch(e){
      console.error('F484_NO_GO: unreadable or invalid evidence');
      process.exitCode=2;
    }
  }
}
