'use strict';
const {app,BrowserWindow,Menu,Tray,nativeImage,dialog,ipcMain,shell,session}=require('electron');
const fs=require('node:fs'),path=require('node:path'),os=require('node:os'),http=require('node:http'),crypto=require('node:crypto'),{spawn}=require('node:child_process');
const {allowedExternal,localPage,validConnection}=require('./policy.cjs');
app.setName('Desksync');
let win,tray,backend,origin,token,closing=false,starting=false,pollTimer,logTail='',lastState,backendExited=false;
const background=process.argv.includes('--background'),smoke=process.argv.includes('--smoke-test');
const smokeOutput=process.env.DESKSYNC_SMOKE_OUTPUT;
const data=process.env.DESKSYNC_TEST_STATE|| (process.platform==='darwin'?path.join(os.homedir(),'Library','Application Support','Afrosoftware','Desksync'):path.join(process.env.XDG_STATE_HOME||path.join(os.homedir(),'.local','state'),'desksync'));
const startup=path.join(__dirname,'startup.html'),icon=path.join(__dirname,'icon.png');
if(!app.requestSingleInstanceLock())app.quit();else{
 app.on('second-instance',()=>show());
 app.whenReady().then(async()=>{
  await session.defaultSession.setProxy({mode:'direct'});
  session.defaultSession.setPermissionRequestHandler((_,__,cb)=>cb(false));
  session.defaultSession.setPermissionCheckHandler(()=>false);
  win=new BrowserWindow({width:1120,height:800,minWidth:900,minHeight:650,title:'Desksync',icon,show:!background||smoke,webPreferences:{preload:path.join(__dirname,'preload.cjs'),nodeIntegration:false,contextIsolation:true,sandbox:true,webviewTag:false}});
  win.on('close',e=>{if(!closing&&tray&&!smoke){e.preventDefault();win.hide();}});
  win.webContents.on('will-navigate',(e,url)=>{if(!origin||!localPage(url,origin))e.preventDefault();});
  win.webContents.setWindowOpenHandler(({url})=>{if(allowedExternal(url))shell.openExternal(url);return {action:'deny'};});
  menu();createTray();await win.loadFile(startup);await startBackend();
 }).catch(e=>{console.error(e.message);app.exit(1);});
 app.on('activate',()=>show());
 app.on('window-all-closed',()=>{if(!tray)quit();});
 app.on('before-quit',e=>{if(!closing){e.preventDefault();quit();}});
}
function show(page){if(!win)return;win.show();win.focus();if(page)win.webContents.send('navigate',page);}
function status(message,error=false){if(!win?.isDestroyed())win.webContents.send('startup-status',{message,error});}
function request(route,body){return new Promise((resolve,reject)=>{
 if(!origin)return reject(Error('The desktop engine has not started.'));
 const u=new URL(route,origin);if(u.origin!==origin)return reject(Error('Invalid local request.'));
 const bytes=body?Buffer.from(JSON.stringify(body)):null;
 const req=http.request(u,{method:bytes?'POST':'GET',headers:{'X-Desksync-Token':token,Origin:origin,...(bytes?{'Content-Type':'application/json','Content-Length':bytes.length}:{})},timeout:20000},res=>{let value='';res.on('data',x=>{value+=x;if(value.length>2e6)req.destroy(Error('Local response too large.'));});res.on('end',()=>{try{if(res.statusCode!==200)throw Error('Local engine returned HTTP '+res.statusCode);resolve(JSON.parse(value));}catch(e){reject(e);}});});req.on('error',reject);req.on('timeout',()=>req.destroy(Error('Local engine timed out.')));if(bytes)req.write(bytes);req.end();
});}
async function startBackend(){
 if(starting)return;starting=true;backendExited=false;logTail='';token=crypto.randomBytes(32).toString('hex');
 try{
  if(backend&&!backend.killed)backend.kill();
  await win.loadFile(startup);status('Starting your private sync engine…');
  const net=require('node:net');const port=await new Promise((resolve,reject)=>{const s=net.createServer();s.on('error',reject);s.listen(0,'127.0.0.1',()=>{const p=s.address().port;s.close(()=>resolve(p));});});
  origin='http://127.0.0.1:'+port;
  const resources=app.isPackaged?process.resourcesPath:process.env.DESKSYNC_DEV_RESOURCES||path.resolve(__dirname,'../../artifacts/desktop-dev');
  const binary=path.join(resources,'backend',process.platform==='win32'?'desksync.exe':'desksync');
  const args=['--data',data,'--port',String(port),'--no-browser'];
  if(smoke){args.push('--engine-port','15391','--folder',path.join(data,'test-files'));}
  backend=spawn(binary,args,{env:{...process.env,DESKSYNC_DESKTOP_TOKEN:token},stdio:['ignore','pipe','pipe'],windowsHide:true});
  backend.stdout.on('data',x=>{logTail=(logTail+x.toString()).slice(-6000);});backend.stderr.on('data',x=>{logTail=(logTail+x.toString()).slice(-6000);});
  backend.on('error',e=>{backendExited=true;logTail=e.message;});
  backend.on('exit',()=>{backendExited=true;if(!closing&&win&&!win.isDestroyed()&&!starting){win.loadFile(startup).then(()=>status('The background sync process stopped. '+logTail,true));}});
  let ready=false;for(let i=0;i<150;i++){if(backendExited)throw Error(logTail||'The desktop engine stopped during startup.');try{const value=await request('/api/desktop');if(value.app==='desksync'){ready=true;break;}}catch{}await new Promise(r=>setTimeout(r,400));}
  if(!ready)throw Error('The private desktop engine did not become ready. '+logTail);
  await win.loadURL(origin+'/');
  clearInterval(pollTimer);pollTimer=setInterval(refreshTray,5000);await refreshTray();
  if(smoke){await new Promise(r=>setTimeout(r,1500));const result={version:app.getVersion(),platform:process.platform,arch:process.arch,windowTitle:win.getTitle(),stateType:lastState?.type,setupCompleted:lastState?.sync?.setupCompleted,renderer:await win.webContents.executeJavaScript('({title:document.title,text:document.body.innerText,bridge:!!window.desksyncDesktop,bodyWidth:document.body.scrollWidth,viewport:innerWidth})')};
   if(smokeOutput){fs.mkdirSync(path.dirname(smokeOutput),{recursive:true});fs.writeFileSync(smokeOutput,JSON.stringify(result,null,2));fs.writeFileSync(smokeOutput+'.png',(await win.webContents.capturePage()).toPNG());}
   if(result.stateType!=='state'||!result.renderer.text.includes('Where are you setting up Desksync?')||result.renderer.bodyWidth>result.renderer.viewport+2)throw Error('Desktop renderer smoke check failed: '+JSON.stringify(result));
   console.log('DESKSYNC_DESKTOP_SMOKE_PASS');await quit();
  }
 }catch(e){status(e.message,true);if(smoke){console.error(e);await quit(1);}}finally{starting=false;}
}
async function command(action,extra={}){try{const state=await request('/api/command',{action,...extra});if(state.type==='failure')throw Error(state.error);await refreshTray();}catch(e){dialog.showErrorBox('Desksync needs attention',e.message);}}
async function refreshTray(){try{lastState=await request('/api/state');if(!tray)return;const s=lastState.sync;tray.setToolTip('Desksync · '+(!s?'Needs attention':s.paused?'Paused':s.errors?'Needs attention':s.presentation==='syncing'?'Syncing files':s.presentation==='offline'?'Waiting for other device':'Up to date'));tray.setContextMenu(Menu.buildFromTemplate([{label:'Open Desksync',click:()=>show()},{label:'Open sync folder',click:()=>command('open-folder')},{label:s?.paused?'Resume sync':'Pause sync',enabled:!!s,click:()=>command('pause',{paused:!s.paused})},{label:'Scan now',enabled:!!s,click:()=>command('rescan')},{type:'separator'},{label:'Devices',click:()=>show('devices')},{label:'Settings',click:()=>show('settings')},{type:'separator'},{label:'Quit Desksync',click:()=>quit()}]));}catch{}}
function createTray(){try{const image=nativeImage.createFromPath(icon).resize({width:24,height:24});tray=new Tray(image);tray.on('click',()=>show());}catch(e){console.warn('Tray unavailable:',e.message);}}
function menu(){Menu.setApplicationMenu(Menu.buildFromTemplate([
 ...(process.platform==='darwin'?[{label:'Desksync',submenu:[{role:'about'},{type:'separator'},{label:'Quit Desksync',click:()=>quit()}]}]:[]),
 {label:'File',submenu:[{label:'Open sync folder',accelerator:'CmdOrCtrl+O',click:()=>command('open-folder')},{label:'Server connection',click:()=>show('connections')},{label:'Settings',click:()=>show('settings')},{type:'separator'},{label:'Quit Desksync',click:()=>quit()}]},
 {label:'Edit',submenu:[{role:'undo'},{role:'redo'},{type:'separator'},{role:'cut'},{role:'copy'},{role:'paste'},{role:'selectAll'}]},
 {label:'View',submenu:[{label:'Home',click:()=>show('home')},{label:'Sync activity',click:()=>show('activity')},{label:'Devices',click:()=>show('devices')},{label:'Notifications',click:()=>show('notifications')},{type:'separator'},{role:'resetZoom'},{role:'zoomIn'},{role:'zoomOut'}]},
 {label:'Help',submenu:[{label:'Setup guide',click:()=>shell.openExternal('https://github.com/PeteZDj/desksync/blob/main/docs/desktop.md')},{label:'About Desksync',click:()=>dialog.showMessageBox(win,{title:'Desksync',message:'Desksync '+app.getVersion(),detail:'Afrosoftware · Encrypted synchronization by Syncthing 2.1.7\nDesktop preview. Updates are manual.'})}]}
 ]));}
function trusted(event){if(event.sender!==win?.webContents||!localPage(event.senderFrame.url,origin))throw Error('Untrusted desktop request.');}
ipcMain.handle('pick-folder',async event=>{trusted(event);const r=await dialog.showOpenDialog(win,{title:'Choose your Desksync folder',properties:['openDirectory','createDirectory']});return r.canceled?null:r.filePaths[0];});
ipcMain.handle('load-connection',async event=>{trusted(event);const r=await dialog.showOpenDialog(win,{title:'Open Desksync connection',filters:[{name:'Desksync connection',extensions:['desksync','txt']}],properties:['openFile']});if(r.canceled)return null;const f=r.filePaths[0];if(fs.statSync(f).size>1500)throw Error('Connection file too large.');const code=fs.readFileSync(f,'utf8');if(!validConnection(code))throw Error('Invalid connection file.');return code;});
ipcMain.handle('save-connection',async(event,code)=>{trusted(event);if(!validConnection(code))throw Error('Invalid connection code.');const r=await dialog.showSaveDialog(win,{defaultPath:'My Desksync computer.desksync',filters:[{name:'Desksync connection',extensions:['desksync']}]});if(!r.canceled)fs.writeFileSync(r.filePath,code,{mode:0o600});return !r.canceled;});
ipcMain.handle('retry-startup',async event=>{if(event.sender!==win?.webContents||event.senderFrame.url!==require('node:url').pathToFileURL(startup).href)throw Error('Untrusted retry.');await startBackend();});
ipcMain.handle('enable-startup',async event=>{trusted(event);if(process.platform==='darwin')app.setLoginItemSettings({openAtLogin:true,args:['--background']});else{if(/["\\\n%]/.test(process.execPath))throw Error('Unsupported application path.');const dir=path.join(process.env.XDG_CONFIG_HOME||path.join(os.homedir(),'.config'),'autostart');fs.mkdirSync(dir,{recursive:true});fs.writeFileSync(path.join(dir,'desksync.desktop'),'[Desktop Entry]\nType=Application\nName=Desksync\nExec="'+process.execPath+'" --background\nTerminal=false\n',{mode:0o600});}await dialog.showMessageBox(win,{message:'Desksync will start in the background at login.'});});
async function quit(code=0){if(closing)return;closing=true;clearInterval(pollTimer);try{await request('/api/command',{action:'shutdown'});}catch{}if(backend&&!backend.killed)backend.kill();tray?.destroy();app.exit(code);}
