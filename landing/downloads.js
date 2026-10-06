(async()=>{
 try{
  const response=await fetch('/native/v1/downloads.json',{cache:'no-store'});if(!response.ok)throw Error('catalog unavailable');const data=await response.json();if(data.schema!==1||!Array.isArray(data.artifacts))throw Error('invalid catalog');
  for(const [platform,id] of [['windows-x64','windows'],['macos-universal','mac']]){
   const a=data.artifacts.find(x=>x.platform===platform);if(!a)continue;
   if(!/^https:\/\/github\.com\/afonasev\/spacewars\/releases\/download\/v[0-9]+\.[0-9]+\.[0-9]+\/Spacewars-[A-Za-z0-9_.-]+$/.test(a.url)||!Number.isSafeInteger(a.size)||a.size<=0||!/^[a-f0-9]{64}$/.test(a.sha256))throw Error('invalid artifact');
   const button=document.getElementById(id);button.href=a.url;button.removeAttribute('aria-disabled');button.classList.remove('disabled');button.removeAttribute('download');button.title='SHA-256: '+a.sha256;
   document.getElementById(id+'-meta').textContent='v'+a.version+' · '+Math.round(a.size/1024/1024)+' МБ · '+(id==='windows'?'64 bit':'Apple Silicon / Intel');document.getElementById('release').textContent='Версия '+a.version+' · Unity-прототип · тестовая сборка';
  }
  if(data.artifacts.length<2)document.getElementById('download-error').textContent='Готовим сборки для обеих платформ. Ссылки появятся после проверки.';
 }catch(e){document.getElementById('download-error').textContent='Ссылки временно недоступны. Попробуйте обновить страницу позже.';}
})();
