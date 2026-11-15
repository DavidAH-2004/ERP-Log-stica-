import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import {
  RGBLuminanceSource, BinaryBitmap, HybridBinarizer,
  MultiFormatReader, BarcodeFormat, DecodeHintType
} from '@zxing/library';

/** F406: real JsBarcode SVG -> browser raster -> actual ZXing decode.
 * Purely synthetic EAN fixtures, no camera, printer, USB reader or network.
 * Verifies software optical compatibility, NOT physical hardware acceptance.
 */
const component = readFileSync(new URL('../src/components/ui/CodigoBarrasSvg.tsx', import.meta.url), 'utf8');
assert.match(component, /format:\s*'EAN13'/);
assert.match(component, /marginLeft:\s*ZONA_IZQUIERDA/);
assert.match(component, /marginRight:\s*ZONA_DERECHA/);
assert.match(component, /width:\s*ANCHO_MODULO/);
assert.match(component, /displayValue:\s*true/);

const bundle = fileURLToPath(new URL('../node_modules/jsbarcode/dist/JsBarcode.all.min.js', import.meta.url));
const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage({ viewport: { width: 880, height: 320 } });
  await page.setContent('<!doctype html><html><body style="margin:0;background:white"><svg id="barcode" xmlns="http://www.w3.org/2000/svg"></svg></body></html>');
  await page.addScriptTag({ path: bundle });

  const fixtures = ['2000000000015', '4006381333931', '5901234123457'];
  for (const code of fixtures) {
    const encoded = await page.evaluate(async code => {
      const svg = document.querySelector('#barcode');
      svg.innerHTML = '';
      window.JsBarcode(svg, code, {
        format: 'EAN13',
        displayValue: true,
        height: 70,
        width: 2,
        margin: 0,
        marginLeft: 22,
        marginRight: 14,
        fontSize: 16,
        textMargin: 2,
        background: '#ffffff',
        lineColor: '#000000',
      });
      const width = Math.ceil(parseFloat(svg.getAttribute('width')) * 3);
      const height = Math.ceil(parseFloat(svg.getAttribute('height')) * 3);
      const canvas = document.createElement('canvas');
      canvas.width = width;
      canvas.height = height;
      const ctx = canvas.getContext('2d', {willReadFrequently: true});
      ctx.fillStyle = '#fff';
      ctx.fillRect(0, 0, width, height);
      const xml = new XMLSerializer().serializeToString(svg);
      const image = new Image();
      const uri = URL.createObjectURL(new Blob([xml], {type: 'image/svg+xml'}));
      try {
        await new Promise((resolve, reject) => {
          image.onload = resolve;
          image.onerror = reject;
          image.src = uri;
        });
        ctx.drawImage(image, 0, 0, width, height);
      } finally {
        URL.revokeObjectURL(uri);
      }
      const rgba = ctx.getImageData(0, 0, width, height).data;
      const pixels = new Uint8ClampedArray(width * height);
      for (let i = 0; i < pixels.length; i++) {
        const offset = i * 4;
        pixels[i] = Math.round((rgba[offset] + rgba[offset + 1] * 2 + rgba[offset + 2]) / 4);
      }
      return {width, height, pixels: Array.from(pixels)};
    }, code);
    const hints = new Map([[DecodeHintType.POSSIBLE_FORMATS, [BarcodeFormat.EAN_13]]]);
    const scanner = new MultiFormatReader();
    scanner.setHints(hints);
    const source = new RGBLuminanceSource(
      Uint8ClampedArray.from(encoded.pixels), encoded.width, encoded.height);
    const decoded = scanner.decodeWithState(
      new BinaryBitmap(new HybridBinarizer(source)));
    assert.equal(decoded.getText(), code,
      'ZXing must recover the same digits JsBarcode printed');
    assert.equal(decoded.getBarcodeFormat(), BarcodeFormat.EAN_13);
  }
  console.log('F406_OPTICAL_SOFTWARE_ROUNDTRIP_PASS ' + JSON.stringify({
    fixtureCount:3, generator:'jsbarcode', decoder:'zxing/library',
    renderer:'chromium SVG and canvas', cameraHardwareTested:false,
    printerHardwareTested:false, payments:'Disabled', productionTouched:false
  }));
} finally {
  await browser.close();
}
