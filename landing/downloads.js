(async()=>{
 const repo='https://api.github.com/repos/afonasev/spacewars';
 const configure=(release,prefix='')=>{
  if(release.draft)return false;
  const version=release.tag_name.replace(/^v/,'');if(!/^\d+\.\d+\.\d+$/.test(version))return false;
  for(const [suffix,id] of [['Windows-x64.exe','windows'],['macOS-Universal.dmg','mac']]){
   const name=`Spacewars-${version}-${suffix}`,a=release.assets.find(x=>x.name===name);
   if(!a||a.state!=='uploaded'||a.size<=0||a.browser_download_url!==`https://github.com/afonasev/spacewars/releases/download/${release.tag_name}/${name}`)return false;
  }
  for(const [suffix,id] of [['Windows-x64.exe','windows'],['macOS-Universal.dmg','mac']]){
   const a=release.assets.find(x=>x.name===`Spacewars-${version}-${suffix}`),b=document.getElementById(prefix+id);if(!b)continue;
   b.href=a.browser_download_url;b.classList.remove('disabled');b.removeAttribute('aria-disabled');b.removeAttribute('download');
   document.getElementById(prefix+id+'-meta').textContent=`v${version} · ${Math.round(a.size/1024/1024)} МБ · ${id==='windows'?'64 bit':'Apple Silicon / Intel'}`;
  }
  if(!prefix)document.getElementById('release').textContent=`Версия ${version} · Unity-прототип`;
  return true;
 };
 try{
  const response=await fetch(repo+'/releases?per_page=100',{cache:'no-store',headers:{Accept:'application/vnd.github+json'}});if(!response.ok)throw Error('GitHub unavailable');const releases=await response.json();
  const stable=releases.find(r=>!r.prerelease&&!r.draft&&configure(r));if(!stable)throw Error('stable release unavailable');
  releases.find(r=>r.prerelease&&!r.draft&&configure(r,'test-'));
 }catch(e){document.getElementById('download-error').textContent='GitHub временно недоступен. Все выпуски: github.com/afonasev/spacewars/releases';}
})();
