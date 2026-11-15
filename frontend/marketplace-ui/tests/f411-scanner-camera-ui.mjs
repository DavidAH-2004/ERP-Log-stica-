/**
 * F411/F422: keyboard-wedge, denied camera and optical video-canvas camera in React UI.
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
  const s={posts:[],failNext:false,failGateway:false,successful:0,external:0,lookupDelayMs:0,lookupFailure:null};
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
      if(s.failGateway){s.failGateway=false;return fulfill(route,{mensaje:'Gateway response unknown'},503);}
      s.successful++;
      return fulfill(route,{resultado:'Coincide',mensaje:'Lectura QA registrada',
        sku:'F411-TEST',nombre:'Producto de prueba',ubicacionCodigo:'A1',opciones:null});
    }
    if(path===('/api/productos/por-codigo/'+valid)&&method==='GET'){
      // Capture each lookup's outcome BEFORE awaiting, so two overlapping
      // requests can resolve in opposite order deterministically.
      const failure=s.lookupFailure;s.lookupFailure=null;
      const wait=s.lookupDelayMs;s.lookupDelayMs=0;
      if(wait>0)await delay(wait);
      if(failure){
        if(failure==='network')return route.abort('failed');
        return fulfill(route,{mensaje:'Catalog temporarily unavailable'},503);
      }
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
  assert.equal(await qty.isDisabled(),true,'F432: quantity must be frozen after an uncertain write');
  assert.equal(await page.getByRole('button',{name:'Descartar'}).isDisabled(),true,
    'F432: an uncertain write must not be discarded and rescanned with a new UUID');
  assert.equal(await qty.inputValue(),'6');
  await page.getByRole('button',{name:'Reintentar guardar (Enter)'}).click();
  await scanner.waitFor({state:'visible'});
  assert.equal(state.posts.length,3);
  assert.equal(state.posts[1].cantidad,6);
  assert.deepEqual(state.posts[2],state.posts[1],'Retry must preserve UUID and amount');
  assert.equal(state.successful,2);
  assert.equal(state.external,0);

  // F428: scanning a valid EAN does NOT authorize saving before
  // the lookup responds, and failed network/API lookups close the form.
  state.lookupDelayMs=900;
  await scanner.fill(valid);
  await scanner.press('Enter');
  const save=page.getByRole('button',{name:'Guardar (Enter)'});
  await save.waitFor({state:'visible',timeout:6000});
  assert.equal(await save.isDisabled(),true,'Save must be disabled during product lookup');
  assert.equal(await qty.isDisabled(),true,'Quantity must be disabled during lookup');
  assert.equal(state.posts.length,3,'Lookup must not write any counts');
  await page.waitForFunction(()=>{
    const button=[...document.querySelectorAll('button')].find(b=>b.textContent?.includes('Guardar (Enter)'));
    return button && !button.disabled;
  },null,{timeout:9000});
  await page.getByRole('button',{name:'Descartar'}).click();
  await scanner.waitFor({state:'visible'});
  assert.equal(state.posts.length,3);

  state.lookupFailure='server';
  await scanner.fill(valid);
  await scanner.press('Enter');
  await page.getByText('No se pudo verificar el producto. Vuelve a intentarlo.').waitFor({timeout:8000});
  await scanner.waitFor({state:'visible'});
  assert.equal(await qty.count(),0,'5xx lookup must clear quantity form');
  assert.equal(state.posts.length,3,'5xx lookup must not write counts');

  state.lookupFailure='network';
  await scanner.fill(valid);
  await scanner.press('Enter');
  await page.getByText('Sin conexión: no se pudo verificar el producto. Vuelve a escanear cuando se recupere.').waitFor({timeout:8000});
  await scanner.waitFor({state:'visible'});
  assert.equal(await qty.count(),0,'Network failure must clear quantity form');
  assert.equal(state.posts.length,3,'Network failure must not write counts');

  // F429: the first lookup fails *after* the operator has discarded it
  // and the new lookup of the SAME EAN has succeeded. Its stale error
  // must not cancel the newer validated scan.
  state.lookupDelayMs=1100;
  state.lookupFailure='server';
  await scanner.fill(valid);
  await scanner.press('Enter');
  await page.getByRole('button',{name:'Descartar'}).click();
  await scanner.waitFor({state:'visible'});
  await scanner.fill(valid);
  await scanner.press('Enter');
  const newerSave=page.getByRole('button',{name:'Guardar (Enter)'});
  await newerSave.waitFor({state:'visible'});
  await page.waitForFunction(()=>{
    const button=[...document.querySelectorAll('button')].find(b=>b.textContent?.includes('Guardar (Enter)'));
    return button && !button.disabled;
  },null,{timeout:8000});
  await delay(1500);
  assert.equal(await newerSave.isVisible(),true,'Stale 503 must not close the newer scan');
  assert.equal(await newerSave.isEnabled(),true,'Stale 503 must not disable newer scan');
  assert.equal(state.posts.length,3,'No count writes from stale lookups');
  await page.getByRole('button',{name:'Descartar'}).click();

  // F433: HTTP 503 can be returned after an upstream write. Treat it as
  // uncertain, freeze the payload, and retry using the exact same UUID/amount.
  state.failGateway=true;
  await scanner.fill(valid);
  await scanner.press('Enter');
  await qty.waitFor({state:'visible'});
  await qty.fill('7');
  await qty.press('Enter');
  await page.getByText('El servidor no confirmó la lectura: reintenta guardar la misma lectura. No vuelvas a escanear.').waitFor({timeout:8000});
  assert.equal(await qty.isDisabled(),true,'F433: HTTP 503 must freeze the quantity');
  assert.equal(await page.getByRole('button',{name:'Descartar'}).isDisabled(),true,
    'F433: HTTP 503 must not let the operator generate a duplicate count');
  await page.getByRole('button',{name:'Reintentar guardar (Enter)'}).click();
  await scanner.waitFor({state:'visible'});
  assert.equal(state.posts.length,5,'HTTP 503 must result in exactly one retry');
  assert.deepEqual(state.posts[4],state.posts[3],
    'F433 retry must preserve UUID, EAN, amount and location');
  assert.equal(state.successful,3,'F433 must not count a gateway error twice');
  await ctx.close();

  // F422: exercitar la ruta REAL de @zxing/browser a través de
  // getUserMedia -> video -> canvas -> ZXing -> React -> lookup.
  // La "cámara" es una pista virtual hecha con canvas; no se enciende
  // ninguna cámara conectada ni se visita la API cloud.
  const cameraCtx=await browser.newContext({permissions:[]});
  await cameraCtx.addInitScript(()=>{
    const left=[
      '0001101','0011001','0010011','0111101','0100011',
      '0110001','0101111','0111011','0110111','0001011'
    ];
    const middle=[
      '0100111','0110011','0011011','0100001','0011101',
      '0111001','0000101','0010001','0001001','0010111'
    ];
    const right=[
      '1110010','1100110','1101100','1000010','1011100',
      '1001110','1010000','1000100','1001000','1110100'
    ];
    const parity=['LLLLLL','LLGLGG','LLGGLG','LLGGGL','LGLLGG',
      'LGGLLG','LGGGLL','LGLGLG','LGLGGL','LGGLGL'];
    const code='4006381333931';
    let bits='101';
    for(let n=1;n<=6;n++){
      const digit=Number(code[n]);
      bits+=parity[Number(code[0])][n-1]==='L'?left[digit]:middle[digit];
    }
    bits+='01010';
    for(let n=7;n<=12;n++)bits+=right[Number(code[n])];
    bits+='101';
    if(bits.length!==95)throw Error('F422 synthetic EAN width');

    Object.defineProperty(navigator,'mediaDevices',{
      configurable:true,
      value:{
        getUserMedia:async()=>{
          const canvas=document.createElement('canvas');
          canvas.width=1280;
          canvas.height=720;
          const ctx=canvas.getContext('2d',{alpha:false});
          const render=()=>{
            ctx.fillStyle='#fff';
            ctx.fillRect(0,0,1280,720);
            const x0=Math.floor((1280-95*7)/2);
            ctx.fillStyle='#000';
            for(let n=0;n<bits.length;n++){
              if(bits[n]==='1')ctx.fillRect(x0+n*7,165,7,380);
            }
          };
          render();
          const stream=canvas.captureStream(12);
          // Mantiene fotogramas disponibles para el decodificador.
          const timer=setInterval(render,100);
          stream.getVideoTracks()[0]?.addEventListener('ended',()=>clearInterval(timer));
          return stream;
        },
        enumerateDevices:async()=>[{deviceId:'f422-virtual',kind:'videoinput',
          label:'F422 synthetic canvas stream',groupId:'f422'}]
      }
    });
  });
  const cameraPage=await cameraCtx.newPage(),cameraState=await mockApi(cameraPage);
  await login(cameraPage,'admin-camera-f422@example.invalid');
  await cameraPage.goto(origin+'/conteos/escaner/'+sessionId);
  await cameraPage.getByRole('textbox',{name:'Código de barras'}).waitFor({timeout:15000});
  await cameraPage.getByRole('button',{name:'Escanear con cámara'}).click();
  await cameraPage.getByText('¿Cuántas unidades hay?').waitFor({timeout:20000});
  assert.equal(cameraState.posts.length,0,'Camera must not post inventory before quantity');
  assert.equal(cameraState.external,0,'Camera fixture cannot contact remote API');
  await cameraCtx.close();

  const buyer=await browser.newPage();
  await mockApi(buyer);
  await login(buyer,'buyer-f411@example.invalid');
  await buyer.goto(origin+'/conteos/escaner/'+sessionId);
  await buyer.getByRole('heading',{name:'Acceso no autorizado'}).waitFor({timeout:12000});
  await buyer.close();

  console.log('F411_SCANNER_CAMERA_UI_PASS '+JSON.stringify({
    syntheticUsbKeyboard:'13 digits + Enter',invalidLocalRejection:true,
    noInventoryPostBeforeQuantity:true,blockedCameraHandled:true,
    offlineRetrySameUuid:true,f432FrozenRetryPayload:true,
    f433Server503RetrySamePayload:true,
    buyerDenied:true,remoteApiCalls:0,
    f428LookupFailClosed:true,f428UnverifiedCannotSave:true,
    f429StaleLookupCannotClearNewScan:true,
    cameraSyntheticDecode:true,cameraHardwareTested:false,usbHardwareTested:false,productionTouched:false
  }));
}finally{
  if(browser)await browser.close();
  vite.kill('SIGTERM');
}
