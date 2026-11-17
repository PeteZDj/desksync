'use strict';
// Keep the Windows renderer contract; only the transport changes on Linux.
(()=>{
 const listeners=[],token=document.querySelector('meta[name="desksync-token"]').content;
 const emit=data=>listeners.forEach(fn=>fn({data}));
 const request=async(path,body)=>{
  const response=await fetch(path,{method:body?'POST':'GET',headers:{'X-Desksync-Token':token,...(body?{'Content-Type':'application/json'}:{})},body:body?JSON.stringify(body):undefined,cache:'no-store'});
  if(!response.ok)throw Error('The local dashboard connection failed. Reload this page.');
  return response.json();
 };
 let polling=false;
 async function poll(){if(polling)return;polling=true;try{emit(await request('/api/state'));}catch(e){emit({type:'failure',error:e.message});}finally{polling=false;}}
 window.chrome={webview:{addEventListener:(_,fn)=>listeners.push(fn),postMessage:async message=>{
  if(message.action==='ready'){poll();return;}
  if(message.action==='ui-ready')return;
  if(['disconnect','remove-device'].includes(message.action)&&!confirm(message.action==='disconnect'?'Disconnect and pause sync? Local files remain.':'Remove this device? Files already downloaded remain on that device.'))return;
  if(message.action==='configure'&&!confirm('Use this folder for two-way sync? Changes and deletions synchronize between approved devices. Changing an existing location copies files and keeps the originals.'))return;
  if(message.action==='load-connection'){
   if(window.desksyncDesktop){try{const code=await window.desksyncDesktop.loadConnection();if(code)emit(await request('/api/command',{action:'advanced-pair',code}));}catch(e){emit({type:'failure',error:e.message});}return;}
   const picker=document.createElement('input');picker.type='file';picker.accept='.desksync,.txt';picker.onchange=async()=>{try{const f=picker.files[0];if(!f)return;if(f.size>1500)throw Error('This connection file is too large.');emit(await request('/api/command',{action:'advanced-pair',code:await f.text()}));}catch(e){emit({type:'failure',error:e.message});}};picker.click();return;
  }
  if(message.action==='browse'){const path=window.desksyncDesktop?await window.desksyncDesktop.pickFolder():prompt('Enter the absolute folder path:');if(path)emit({type:'folder-picked',path});return;}
  try{
   const value=await request('/api/command',message);
   if(value.type==='connection'){
    if(window.desksyncDesktop){await window.desksyncDesktop.saveConnection(value.code);return;}
    const a=document.createElement('a'),url=URL.createObjectURL(new Blob([value.code],{type:'text/plain'}));a.href=url;a.download='My Linux computer.desksync';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
   }else if(value.type==='desktop-startup'){if(window.desksyncDesktop)await window.desksyncDesktop.enableStartup();else emit({type:'failure',error:'Use the installed desktop app to enable startup.'});}
   else if(value.type==='open-url'){const w=window.open(value.url,'_blank','noopener');if(!w)emit({type:'failure',error:'Open this address in your browser: '+value.url});}
   else emit(value);
  }catch(e){emit({type:'failure',error:e.message});}
 }}};
 window.desksyncDesktop?.onNavigate(page=>emit({type:'navigate',page}));
 setInterval(poll,2500);
})();
