'use strict';
function allowedExternal(value){try{const u=new URL(value);return u.protocol==='https:'&&!u.username&&!u.password&&((u.hostname==='afrosoftware.com'&&u.pathname.startsWith('/desksync/authorize'))||(u.hostname==='github.com'&&u.pathname.startsWith('/PeteZDj/desksync')));}catch{return false;}}
function localPage(value,origin){try{const u=new URL(value);return u.origin===origin&&['/','/index.html'].includes(u.pathname)&&!u.username&&!u.password;}catch{return false;}}
function validConnection(value){try{const u=new URL(value);return typeof value==='string'&&value.length<=1500&&u.protocol==='desksync:'&&u.hostname==='pair'&&u.searchParams.get('v')==='1'&&/^[A-Z2-7]{7}(-[A-Z2-7]{7}){7}$/.test(u.searchParams.get('device'))&&/^[a-zA-Z0-9_-]{1,80}$/.test(u.searchParams.get('folder'));}catch{return false;}}
module.exports={allowedExternal,localPage,validConnection};
