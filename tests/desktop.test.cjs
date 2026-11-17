const {test}=require('node:test'),assert=require('node:assert/strict');
const {allowedExternal,localPage,validConnection}=require('../src/Desksync.Desktop/policy.cjs');
test('external sign-in and help links stay on the permitted HTTPS paths',()=>{
 assert(allowedExternal('https://afrosoftware.com/desksync/authorize?code=123'));
 for(const value of ['http://afrosoftware.com/desksync/authorize','https://evil.example','https://afrosoftware.com.evil.example/desksync/authorize','file:///etc/passwd','https://user:pass@afrosoftware.com/desksync/authorize'])assert(!allowedExternal(value),value);
});
test('only the desktop dashboard receives native IPC access',()=>{
 const origin='http://127.0.0.1:5392';assert(localPage(origin+'/',origin));assert(localPage(origin+'/index.html',origin));
 for(const value of ['http://127.0.0.1:5391/','http://localhost:5392/','https://evil.example/','file:///etc/passwd',origin+'/evil.html'])assert(!localPage(value,origin),value);
});
test('native connection-file writes accept only bounded valid pairing files',()=>{
 const code='desksync://pair?v=1&device='+Array(8).fill('AAAAAAA').join('-')+'&folder=desksync-test';assert(validConnection(code));
 for(const value of ['file:///etc/passwd',code.replace('v=1','v=2'),code.replace('desksync-test','../private'),code+'x'.repeat(1500),{},null])assert(!validConnection(value));
});
