/**
 * F411: keyboard-wedge (USB simulation) and denied camera in actual React UI.
 * All requests are intercepted on local Vite. No hardware or persistent DB.
 */
import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import { spawn } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';

const origin='http://127.0.0.1:4183';
const sessionId='11111111-1111-4111-8111-111111111111';
const valid='4006381333931';
const invalid='2000000000014';
const vite=spawn(process.execPath,[
  './node_modules/vite/bin/vite.js','--host','127.0.0.1','--port','4183','--strictPort'
],{stdio:['ignore','pipe','pipe'],env:{...process.env,VITE_CHECKOUT_HABILITADO:'false',VITE_BFF_AUTH_MODE:''}});
let output='';
for(const stream of [vite.stdout,vite.stderr]) stream.on('data',chunk=>{
  output=(output+String(chunk)).slice(-2500);
});
async function waitVite(){
  for(let n=0;n<80;n++){
    if(vite.exitCode!==null) throw new Error('Vite exited: '+output);
    try{
      const r=await fetch(origin+'/login');
      if(r.ok&&(await r.text()).includes('LOGIMKT'))return;
    }catch{/*not ready*/}
    await delay(250);
  }
  throw new Error('Vite did not start: '+output);
}
function jwt(role){
  const expiresAt=new Date(Date.now()+900000).toISOString();
  const exp=Math.floor(Date.parse(expiresAt)/1000);
  const part=Buffer.from(JSON.stringify({role,exp})).toString('base64url');
  return {token:'e30.'+part+'.local-fixture-only',expiresAt,role,rol:role,usuario:role+' CI'};
}
const fulfill=(route,body,status=200)=>route.fulfill({
  status,contentType:'application/json',body:JSON.stringify(body)
});
async function mockApi(page){
  const s={posts:[],failNext:false,successful:0,external:0};
  await page.route(url=>url.pathname.startsWith('/api/'),async route=>{
    const req=route.request(),url=new URL(req.url()),path=url.pathname,method=req.method();
    if(url.hostname!=='127.0.0.1'){s.external++;return route.abort();}
    if(path==='/api/auth/login'&&method==='POST'){
      const email=req.postDataJSON()?.email||'';
      return fulfill(route,jwt(email.startsWith('buyer')?'Comprador':'Admin'));
    }
    if(path===('/api/inventario/conteos-ciclicos/sesiones/'+sessionId)&&method==='GET'){
      return fulfill(route,{
        sesion:{id:sessionId,numero:'SC-2026-F411',modo:'Planificado',
          pasillo:null,estado:'Abierta',creadaPor:'CI',fechaInicio:'2026-11-15T10:00:00Z',
          fechaCierre:null,esperados:1,contados:s.successful,hallazgos:0},
        faltan:[],contados:[],reconteo:[],fueraDeLugar:[]
      });
    }
    if(path===('/api/inventario/conteos-ciclicos/sesiones/'+sessionId+'/lecturas')&&method==='POST'){
      const body=req.postDataJSON();
      s.posts.push(body);
      if(s.failNext){s.failNext=false;return route.abort('failed');}
      s.successful++;
      return fulfill(route,{resultado:'Coincide',mensaje:'Lectura QA registrada',
        sku:'F411-TEST',nombre:'Producto de prueba',ubicacionCodigo:'A1',opciones:null});
    }
    if(path===('/api/productos/por-codigo/'+valid)&&method==='GET'){
      return fulfill(route,{id:'22222222-2222-4222-8222-222222222222',
        sku:'F411-TEST',nombre:'Producto de prueba',codigoEAN:valid});
    }
    return fulfill(route,{items:[],total:0,pagina:1,tamano:20,kpis:[],
      resumenEjecutivo:{totalOrdenes:0},meses:[],resumenAnual:{ventasTotalesAnuales:0}});
  });
  return s;
}
async function login(page,email){
  const errors=[];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto(origin+'/login');
  const emailBox=page.getByPlaceholder('nombre@ejemplo.com');
  try {
    await emailBox.waitFor({state:'visible',timeout:10000});
  } catch (error) {
    const body=await page.locator('body').innerText().catch(()=>'<unavailable>');
    throw new Error('F411 login view not ready; url='+page.url()+
      '; body='+body.slice(0,650)+'; browserErrors='+errors.slice(0,3).join(' | ')+
      '; Vite='+output.slice(-300),{cause:error});
  }
  await emailBox.fill(email);
  await page.locator('input[type=password]').fill('f411-local-fixture-only');
  await page.getByRole('button',{name:'Ingresar al sistema'}).click();
  await page.waitForFunction(()=>!!localStorage.getItem('erp_token'),null,{timeout:10000});
}
let browser;
try{
  await waitVite();
  browser=await chromium.launch({headless:true,args:['--no-sandbox']});
  const ctx=await browser.newContext({permissions:[]});
  // Always reject camera access; never use a physical camera in CI.
  await ctx.addInitScript(()=>{
    const blocked=()=>Promise.reject(new DOMException('QA denied','NotAllowedError'));
    Object.defineProperty(navigator,'mediaDevices',{configurable:true,
      value:{getUserMedia:blocked,enumerateDevices:async()=>[]}});
  });
  const page=await ctx.newPage(),state=await mockApi(page);
  await login(page,'admin-f411@example.invalid');
  await page.goto(origin+'/conteos/escaner/'+sessionId);
  const scanner=page.getByRole('textbox',{name:'Código de barras'});
  await scanner.waitFor({state:'visible',timeout:15000});

  await page.getByRole('button',{name:'Escanear con cámara'}).click();
  await page.getByText('Permiso de cámara denegado.').waitFor({timeout:10000});
  await page.getByRole('button',{name:'Cerrar',exact:true}).click();
  assert.equal(state.posts.length,0,'No reads from a blocked camera');

  await scanner.focus();
  await page.keyboard.type(invalid,{delay:2});
  await page.keyboard.press('Enter');
  await page.getByText('Código inválido: vuelve a escanear.').waitFor();
  assert.equal(state.posts.length,0);

  await scanner.focus();
  await page.keyboard.type(valid,{delay:2});
  await page.keyboard.press('Enter');
  await page.getByText('¿Cuántas unidades hay?').waitFor();
  assert.equal(state.posts.length,0,'Scanner cannot write inventory before quantity');
  const qty=page.locator('input[type=number]');
  await qty.fill('5');
  await qty.press('Enter');
  await scanner.waitFor({state:'visible'});
  assert.equal(state.posts.length,1);
  assert.equal(state.posts[0].codigo,valid);
  assert.equal(state.posts[0].cantidad,5);
  assert.match(state.posts[0].lecturaId,/^[0-9a-f]{8}-[0-9a-f-]{27}$/i);

  state.failNext=true;
  await scanner.focus();
  await page.keyboard.type(valid,{delay:2});
  await page.keyboard.press('Enter');
  await page.getByText('¿Cuántas unidades hay?').waitFor();
  await qty.fill('6');
  await qty.press('Enter');
  await page.getByText('Sin conexión: reintenta guardar la misma lectura. No vuelvas a escanear.').waitFor();
  await page.getByRole('button',{name:'Reintentar guardar (Enter)'}).click();
  await scanner.waitFor({state:'visible'});
  assert.equal(state.posts.length,3);
  assert.equal(state.posts[1].cantidad,6);
  assert.deepEqual(state.posts[2],state.posts[1],'Retry must preserve UUID and amount');
  assert.equal(state.successful,2);
  assert.equal(state.external,0);
  await ctx.close();

  const buyer=await browser.newPage();
  await mockApi(buyer);
  await login(buyer,'buyer-f411@example.invalid');
  await buyer.goto(origin+'/conteos/escaner/'+sessionId);
  await buyer.getByRole('heading',{name:'Acceso no autorizado'}).waitFor({timeout:12000});
  await buyer.close();

  console.log('F411_SCANNER_CAMERA_UI_PASS '+JSON.stringify({
    syntheticUsbKeyboard:'13 digits + Enter',invalidLocalRejection:true,
    noInventoryPostBeforeQuantity:true,blockedCameraHandled:true,
    offlineRetrySameUuid:true,buyerDenied:true,remoteApiCalls:0,
    cameraHardwareTested:false,usbHardwareTested:false,productionTouched:false
  }));
}finally{
  if(browser)await browser.close();
  vite.kill('SIGTERM');
}
