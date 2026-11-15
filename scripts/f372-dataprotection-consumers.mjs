// F372: fail closed if a .NET DataProtection consumer is introduced without a
// durable, cross-instance key-ring design. This is a static guard, not a
// substitute for a deployed restart or authenticated browser test.
import { readdirSync, readFileSync } from 'node:fs';
import { resolve, join, dirname, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..', 'src');
const consumers = [
  /\bAddCookie\s*(?:<[^>]+>)?\s*\(/,
  /\bAddIdentity\s*(?:<[^>]+>)?\s*\(/,
  /\bAddDefaultIdentity\s*(?:<[^>]+>)?\s*\(/,
  /\bAddAntiforgery\s*\(/,
  /\bAddSession\s*\(/,
  /\bUseSession\s*\(/,
  /\bIDataProtector\b/,
  /\bIDataProtectionProvider\b/,
  /\bITempDataDictionary\b/,
  /\bCookieTempDataProvider\b/,
  /\bSignInAsync\s*\(/,
];
const whitelist = new Set(['obj', 'bin', '.git']);
function findFiles(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap(item => {
    const path = join(dir, item.name);
    if (item.isDirectory()) return whitelist.has(item.name) ? [] : findFiles(path);
    return item.isFile() && item.name.endsWith('.cs') ? [path] : [];
  });
}
function violations(source) {
  // Ignore ordinary line comments for reliable documentation additions.
  const code = source.split(/\r?\n/).map(line => {
    const index = line.indexOf('//');
    return index < 0 ? line : line.slice(0, index);
  }).join('\n');
  return consumers.filter(pattern => pattern.test(code)).map(pattern => pattern.source);
}
if (process.argv.includes('--self-test')) {
  if (!violations('services.AddCookie();').length ||
      !violations('services.AddAntiforgery();').length ||
      !violations('IDataProtector protector;').length ||
      violations('// AddCookie();').length ||
      violations('services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();').length) {
    throw new Error('F372 static detection contract failed');
  }
}
const findings = findFiles(root).flatMap(file =>
  violations(readFileSync(file, 'utf8')).map(rule => relative(root, file) + ' (' + rule + ')'));
if (findings.length) {
  process.stderr.write('F372 NO-GO: DataProtection consumer needs a persistent shared key ring:\n' +
    findings.join('\n') + '\n');
  process.exitCode = 1;
} else {
  process.stdout.write('F372_PASS: no explicit ASP.NET DataProtection consumer in tracked source modules; JWT bearer is independent of this key ring. Runtime/indirect uses not certified.\n');
}
