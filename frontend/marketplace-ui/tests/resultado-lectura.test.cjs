// Estilo (tono y vibración) por resultado de lectura de conteo. Mismo mecanismo que ean13.test.cjs.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const ts = require('typescript');

const src = fs.readFileSync(path.join(__dirname, '../src/utils/resultadoLectura.ts'), 'utf8');
const compiled = ts.transpileModule(src, {
  fileName: 'resultadoLectura.ts',
  reportDiagnostics: true,
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020, strict: true },
});
const faults = compiled.diagnostics?.filter(d => d.category === ts.DiagnosticCategory.Error) ?? [];
assert.equal(faults.length, 0, 'El módulo TypeScript debe ser válido');

const m = {};
vm.runInNewContext(compiled.outputText, { exports: m });
// Copia al contexto de la prueba: los objetos creados en vm tienen otro prototipo.
const e = r => ({ ...m.estiloResultado(r) });

assert.deepEqual(e('Coincide'), { tono: 'exito', vibrar: false });
assert.deepEqual(e('YaContado'), { tono: 'exito', vibrar: false });
assert.deepEqual(e('Diferencia'), { tono: 'aviso', vibrar: true });
assert.deepEqual(e('StockCambio'), { tono: 'aviso', vibrar: true });
assert.deepEqual(e('ElegirUbicacion'), { tono: 'aviso', vibrar: true });
assert.deepEqual(e('AjusteCreado'), { tono: 'info', vibrar: false });
assert.deepEqual(e('FueraDeLugar'), { tono: 'neutro', vibrar: false });
assert.deepEqual(e('CodigoInvalido'), { tono: 'error', vibrar: true });
assert.deepEqual(e('ProductoNoEncontrado'), { tono: 'error', vibrar: true });
assert.deepEqual(e('BloqueadoPorAjuste'), { tono: 'error', vibrar: true });
assert.deepEqual(e('SesionNoAbierta'), { tono: 'error', vibrar: true });
assert.deepEqual(e('Rechazado'), { tono: 'error', vibrar: true });

console.log('Resultados de lectura: 12 verificaciones aprobadas.');
