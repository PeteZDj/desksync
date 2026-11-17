'use strict';
const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('desksyncDesktop',{
 pickFolder:()=>ipcRenderer.invoke('pick-folder'),
 loadConnection:()=>ipcRenderer.invoke('load-connection'),
 saveConnection:code=>ipcRenderer.invoke('save-connection',code),
 enableStartup:()=>ipcRenderer.invoke('enable-startup'),
 retry:()=>ipcRenderer.invoke('retry-startup'),
 onStartup:callback=>ipcRenderer.on('startup-status',(_,message)=>callback(message)),
 onNavigate:callback=>ipcRenderer.on('navigate',(_,page)=>callback(page))
});
