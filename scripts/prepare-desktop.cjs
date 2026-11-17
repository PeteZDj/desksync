const fs=require('node:fs'),path=require('node:path'),os=require('node:os'),crypto=require('node:crypto'),{spawnSync}=require('node:child_process');
const root=path.resolve(__dirname,'..'),platform=process.platform==='darwin'?'mac':process.platform,arch=process.arch;
if(!['linux','mac'].includes(platform)||!['x64','arm64'].includes(arch))throw Error('Build on a Linux or macOS x64/ARM64 host.');
const rid=(platform==='mac'?'osx':'linux')+'-'+arch,stage=path.join(root,'artifacts','desktop-engine'),output=path.join(root,'artifacts','desktop-dev','backend');
const names={linux:{x64:['syncthing-linux-amd64-v2.1.7.tar.gz','2edc416e7ed136b7c858ed5f3f02a3b8316589605e54c380228ceea7acbf0142'],arm64:['syncthing-linux-arm64-v2.1.7.tar.gz','48e456690cd2b97e7d26b02b5ac35f62a3b36172267b144cfeaf02c993ab955c']},mac:{x64:['syncthing-macos-amd64-v2.1.7.zip','b8be90176d0f24daebae7b5aaddac52ebfae395df6d99f5e0e54656db00868cb'],arm64:['syncthing-macos-arm64-v2.1.7.zip','2df075b463231a307c91da657157beaed06383a941ba7ea04d33d536e3c4af38']}};
function run(exe,args){const result=spawnSync(exe,args,{cwd:root,stdio:'inherit'});if(result.status!==0)throw Error(exe+' failed');}
(async()=>{
 fs.mkdirSync(stage,{recursive:true});fs.mkdirSync(output,{recursive:true});
 const [name,hash]=names[platform][arch],archive=path.join(stage,name);
 const response=await fetch('https://github.com/syncthing/syncthing/releases/download/v2.1.7/'+name);if(!response.ok)throw Error('Engine download failed');const bytes=Buffer.from(await response.arrayBuffer());if(crypto.createHash('sha256').update(bytes).digest('hex')!==hash)throw Error('Engine checksum mismatch');fs.writeFileSync(archive,bytes);
 if(name.endsWith('.zip'))run('unzip',['-o',archive,'-d',stage]);else run('tar',['-xzf',archive,'-C',stage]);
 run('dotnet',['publish','src/Desksync.Linux/Desksync.Linux.csproj','-c','Release','-r',rid,'--self-contained','true','-o',output]);
 const engine=path.join(stage,name.replace(/\.tar\.gz$|\.zip$/,''));fs.copyFileSync(path.join(engine,'syncthing'),path.join(output,'syncthing'));fs.chmodSync(path.join(output,'syncthing'),0o755);fs.chmodSync(path.join(output,'desksync'),0o755);
 fs.copyFileSync(path.join(engine,'LICENSE.txt'),path.join(output,'SYNCTHING-LICENSE.txt'));fs.copyFileSync(path.join(root,'LICENSE'),path.join(output,'LICENSE.txt'));
 const deps=JSON.parse(fs.readFileSync(path.join(root,'src','Desksync.Linux','bin','Release','net10.0',rid,'desksync.deps.json')));
 for(const library of Object.keys(deps.libraries).filter(x=>x.startsWith('runtimepack.'))){const [name,version]=library.split('/'),pkg=name.slice(12).toLowerCase(),home=path.join(process.env.NUGET_PACKAGES||path.join(os.homedir(),'.nuget','packages'),pkg,version);for(const license of ['LICENSE.TXT','THIRD-PARTY-NOTICES.TXT']){const file=fs.readdirSync(home).find(x=>x.toUpperCase()===license);fs.copyFileSync(path.join(home,file),path.join(output,pkg+'-'+license));}}
 console.log('Prepared desktop backend for '+rid);
})().catch(e=>{console.error(e);process.exitCode=1;});
