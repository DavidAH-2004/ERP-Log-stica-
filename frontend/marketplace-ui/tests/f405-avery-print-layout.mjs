import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { chromium } from 'playwright';

/**
 * F405: validate actual Avery 5160 CSS on headless Chromium print media.
 * Pure client-side fixture: no API, DB, camera, printer or external URLs.
 * Physical paper/alignment still needs its own acceptance check (F398).
 */
const source = readFileSync(new URL('../src/pages/productos/EtiquetasPage.tsx', import.meta.url), 'utf8');
const css = readFileSync(new URL('../src/pages/productos/etiquetas.css', import.meta.url), 'utf8');

function prop(key) {
  const match = source.match(new RegExp('\\b' + key + ":\\s*'([^']+)'"));
  assert.ok(match, 'Missing Avery size in component: ' + key);
  return match[1];
}
const specs = Object.fromEntries([
  'paginaAncho', 'paginaAlto', 'margenSuperior', 'margenLateral',
  'etiquetaAncho', 'etiquetaAlto', 'pasoHorizontal', 'pasoVertical',
].map(key => [key, prop(key)]));
assert.match(source, /columnas:\s*3,\s*filas:\s*10/);
assert.match(source, /MAX_ETIQUETAS_POR_LOTE\s*=\s*1500/);
assert.match(source, /@page \{ size: letter; margin: 0; \}/);

const inches = value => parseFloat(value) * 96;
const fullWidth = inches(specs.paginaAncho);
const fullHeight = inches(specs.paginaAlto);
const labelWidth = inches(specs.etiquetaAncho);
const labelHeight = inches(specs.etiquetaAlto);
const stepX = inches(specs.pasoHorizontal);
const stepY = inches(specs.pasoVertical);
const marginX = inches(specs.margenLateral);
const marginY = inches(specs.margenSuperior);

assert.equal(fullWidth, 816);
assert.equal(fullHeight, 1056);
assert.equal(labelWidth, 252);
assert.equal(labelHeight, 96);
assert.equal(stepX, 264);
assert.equal(stepY, 96);
assert.equal(marginX + stepX * 2 + labelWidth + marginX, fullWidth);
assert.equal(marginY + stepY * 9 + labelHeight + marginY, fullHeight);

const variables = {
  '--pagina-ancho': specs.paginaAncho,
  '--pagina-alto': specs.paginaAlto,
  '--margen-superior': specs.margenSuperior,
  '--margen-lateral': specs.margenLateral,
  '--etiqueta-ancho': specs.etiquetaAncho,
  '--etiqueta-alto': specs.etiquetaAlto,
  '--separacion-columnas': (parseFloat(specs.pasoHorizontal) - parseFloat(specs.etiquetaAncho)) + 'in',
  '--columnas': '3',
  '--filas': '10',
};
const inline = Object.entries(variables).map(([k,v]) => k + ':' + v).join(';');
const labels = Array.from({ length: 30 }, (_, i) =>
  '<div class="etiqueta" data-slot="' + i + '">EAN-13 QA</div>').join('');

const html = '<!DOCTYPE html><html><head><style>' +
  '@page { size: letter; margin: 0; }html,body{margin:0;padding:0}' +
  css +
  '</style></head><body class="imprimiendo-etiquetas">' +
  '<div class="impresion-etiquetas"><div class="hoja-etiquetas" style="' + inline + '">' +
  labels + '</div></div></body></html>';

const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage({ viewport: { width: 1200, height: 1300 } });
  await page.emulateMedia({ media: 'print' });
  await page.setContent(html);
  const positions = await page.locator('.hoja-etiquetas > .etiqueta').evaluateAll(elements =>
    elements.map(element => {
      const r = element.getBoundingClientRect();
      return { left: r.left, top: r.top, width: r.width, height: r.height };
    }));
  assert.equal(positions.length, 30);
  const near = (actual, expected) => assert.ok(
    Math.abs(actual - expected) < 0.5, 'Expected ' + expected + 'px, got ' + actual + 'px');
  for (let i = 0; i < 30; i++) {
    const col = i % 3;
    const row = Math.floor(i / 3);
    near(positions[i].left, marginX + col * stepX);
    near(positions[i].top, marginY + row * stepY);
    near(positions[i].width, labelWidth);
    near(positions[i].height, labelHeight);
  }
  const pdf = await page.pdf({ preferCSSPageSize: true, printBackground: true });
  assert.ok(pdf.length > 1000 && pdf.subarray(0, 4).toString() === '%PDF',
    'Chromium did not produce printable PDF bytes');
  const pdfText = pdf.toString('latin1');
  const count = (pdfText.match(/\/Type\s*\/Page\b/g) || []).length;
  assert.equal(count, 1, '30 labels must print on exactly one letter page');
  console.log('F405_AVERY_5160_CHROMIUM_PASS ' + JSON.stringify({
    labels:30, columns:3, rows:10, pageInches:'8.5x11',
    widthInches:2.625, heightInches:1, pdfPages:count,
    printerHardwareTested:false, productionTouched:false
  }));
} finally {
  await browser.close();
}
