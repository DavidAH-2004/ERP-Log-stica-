// Validación EAN-13 del cliente. Mismo mecanismo que checkout-state-check.cjs:
// transpila el módulo TypeScript y lo ejecuta aislado en un contexto vm.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const ts = require('typescript');

const src = fs.readFileSync(path.join(__dirname, '../src/utils/ean13.ts'), 'utf8');
const compiled = ts.transpileModule(src, {
  fileName: 'ean13.ts',
  reportDiagnostics: true,
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020, strict: true },
});
const faults = compiled.diagnostics?.filter(d => d.category === ts.DiagnosticCategory.Error) ?? [];
assert.equal(faults.length, 0, 'El módulo TypeScript debe ser válido');

const m = {};
vm.runInNewContext(compiled.outputText, { exports: m });

assert.equal(m.esEan13Valido('2000000000015'), true);
assert.equal(m.esEan13Valido('4006381333931'), true);
assert.equal(m.esEan13Valido('7410000000191'), false);
assert.equal(m.esEan13Valido('200000000001'), false);
assert.equal(m.esEan13Valido('20000000000A5'), false);
assert.equal(m.esEan13Valido(''), false);
assert.equal(m.normalizarLectura(' 2000000000015\r\n'), '2000000000015');
assert.equal(m.normalizarLectura('2000 0000 00015'), '2000000000015');
assert.equal(m.normalizarLectura('\t2000000000015\t'), '2000000000015');

console.log('Código de barras: 9 verificaciones EAN-13 aprobadas.');
