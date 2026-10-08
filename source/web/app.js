
const paths={brand:'M4 15l2 5h12l2-5M7 6v5M12 3v8M17 7v4',tray:'M4 5h16v14H4zM4 13h5l1 3h4l1-3h5',rules:'M6 5h14M6 12h14M6 19h14M3 5h.01M3 12h.01M3 19h.01',monitor:'M3 4h18v12H3zM8 21h8M12 16v5',settings:'M4 7h16M4 17h16M8 4v6M16 14v6',info:'M12 8h.01M12 11v6M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0',restore:'M3 5v5h5M3 10a9 9 0 1 1 2 8',search:'M21 21l-5-5M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0',refresh:'M20 7V3l-3 3M20 6a9 9 0 0 0-15 2M4 17v4l3-3M4 18a9 9 0 0 0 15-2',list:'M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01',grid:'M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM14 14h7v7h-7z',eye:'M2 12s3-7 10-7 10 7 10 7-3 7-10 7S2 12 2 12M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0',hidden:'M3 3l18 18M9 5c7-2 13 7 13 7l-3 4M6 6l-4 6s3 7 10 7c2 0 4-1 5-2',minus:'M5 12h14',square:'M5 5h14v14H5z',close:'M5 5l14 14M5 19L19 5',sun:'M12 8a4 4 0 1 1 0 8 4 4 0 0 1 0-8M12 2v2M12 20v2M2 12h2M20 12h2M5 5l1 1M18 18l1 1M5 19l1-1M18 6l1-1',moon:'M20 14A9 9 0 0 1 10 3a9 9 0 1 0 10 11'};
const icon=n=>`<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${paths[n]||paths.info}"/></svg>`;
Object.assign(paths,{plus:'M12 4v16M4 12h16',edit:'M4 16l12-12 4 4-12 12H4zM14 6l4 4',pause:'M8 5v14M16 5v14',clock:'M12 8v5l3 2M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0'});
document.querySelectorAll('[data-icon]').forEach(e=>e.innerHTML=icon(e.dataset.icon));document.querySelector('#light').innerHTML=icon('sun')+'浅色';document.querySelector('#dark').innerHTML=icon('moon')+'深色';
'use strict';
let apps=[],current=null,mode='all',grid=false,selected=new Set(),anchor=null;
let state={rules:0,paused:false,busy:true,theme:'light',language:'zh-CN',autoRefresh:true,page:'icons',ruleCatalog:[],ruleUndo:false};
let sortKey='name',descending=false,sequence=0,pending=0,toastTimer,detailSignature='';
let pendingAction='',lastReceivedState='',detailIdentity='';
let startupPresented=false;
const rowNodes=new Map();
const requests=new Map();
const $=s=>document.querySelector(s);
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const words={
 title:['图标管理','Icon management'],subtitle:['留下需要的图标，其余安静运行。','Keep what you need. Let the rest run quietly.'],
 restore:['全部恢复','Restore managed icons'],hide:['隐藏','Hide'],show:['恢复','Show'],visible:['可见','Visible'],hidden:['已隐藏','Hidden'],
 search:['搜索图标名称或路径…','Search icon names or paths…'],all:['全部','All'],program:['此程序全部图标','All program icons'],single:['仅此图标','This icon only'],
 noResults:['未找到匹配图标','No matching icons'],noIcons:['暂时没有可管理的图标','No manageable icons yet'],clear:['清空搜索','Clear search'],refresh:['刷新列表','Refresh list'],
 emptyHelp:['确认程序已经运行并具有托盘图标；系统音量、网络和时钟请到“系统图标”管理。','Start an application with a tray icon. Manage volume, network and clock under System icons.'],
 searchHelp:['尝试其他名称或路径，或者清空搜索查看全部图标。','Try another name or path, or clear your search.'],
 rulesRunning:['自动隐藏运行中','Auto-hide running'],rulesPaused:['自动隐藏已暂停','Auto-hide paused'],noRules:['尚未设置自动隐藏规则','No auto-hide rules yet'],
 busy:['正在读取或更新图标状态…','Reading or updating icon states…'],failed:['操作未完成','Operation incomplete'],done:['操作已完成','Operation completed'],
 software:['普通软件托盘图标','Application tray icon'],running:['软件仍在运行','Application still running'],process:['进程名称','Process name'],path:['程序路径','Program path'],
 copy:['复制完整路径','Copy full path'],properties:['程序属性','Program properties'],copied:['完整路径已复制','Full path copied'],
 createRule:['创建隐藏规则','Create hide rule'],viewRule:['查看对应规则','View matching rules'],ruleHelp:['手动隐藏只改变当前显示，创建规则可自动隐藏再次出现的图标。','Manual hiding changes visibility once. Rules hide matching icons when they reappear.'],
 ruleKeep:['本次运行暂时显示，原规则保留','Show for this session; keep the rule'],noClose:['仅改变图标显示，不会关闭软件','Changes icon visibility; keeps the application running'],
 temporary:['暂时显示图标','Show for this session'],hideCurrent:['隐藏此图标','Hide this icon'],selectHint:['选择一个图标，查看程序和规则详情','Select an icon to see program and rule details'],
 selected:['已选择','Selected'],hideSelected:['隐藏选中','Hide selected'],showSelected:['恢复选中','Show selected'],results:['个结果','results'],icons:['个图标','icons'],
 rules:['条规则','rules'],refreshOn:['自动刷新 · 每 2.5 秒','Auto-refresh · every 2.5 seconds'],refreshOff:['手动刷新','Manual refresh'],
 keyboard:['双击隐藏 / 恢复 · Ctrl / Shift 多选','Double-click to toggle · Ctrl / Shift to select'],
 light:['浅色','Light'],dark:['深色','Dark'],nameHeader:['应用 / 进程','App / process'],stateHeader:['当前状态','Visibility'],ruleHeader:['自动隐藏规则','Auto-hide rule'],
 workspace:['工作空间','Workspace'],rulesNav:['隐藏规则','Hide rules'],systemNav:['系统图标','System icons'],settingsNav:['设置','Settings'],aboutNav:['关于与更新','About and updates'],
 detail:['图标详情','Icon details'],selectedOne:['选中 1 项','1 item focused'],actionHeader:['操作','Action'],ordinary:['仅普通软件图标','Application icons only'],closeToTray:['关闭窗口后继续在托盘运行','Continue in the tray after closing'],restoreCurrent:['恢复此图标','Show this icon']
};
const t=k=>(words[k]||[k,k])[state.language==='en-US'?1:0];
const appIcon=a=>`<span class="appicon ${a.darkPlate?'monochrome':''} ${a.lightPlate?'dark-glyph':''}"><img src="${esc(a.image)}" alt="" width="32" height="32"></span>`;
function toast(message,error=false){$('#message').textContent=message;$('#toast').classList.toggle('error',error);$('#toast').classList.add('show');clearTimeout(toastTimer);toastTimer=setTimeout(()=>$('#toast').classList.remove('show'),6500)}
function send(action,data={}){
 if(!window.chrome?.webview){toast('WebView2 connection unavailable',true);return Promise.reject(Error('Native connection unavailable'))}
 const requestId=String(++sequence);
 return new Promise((resolve,reject)=>{
  const timer=setTimeout(()=>{requests.delete(requestId);reject(Error('Response timeout'))},30000);
  requests.set(requestId,{resolve,reject,timer});window.chrome.webview.postMessage({action,requestId,...data});
 });
}
async function command(action,data={},message=''){
 if(pending||state.busy)return;
 pending++;pendingAction=action;updateBusy();
 try{const result=await send(action,data);if(message&&result?.phase!=='failed')toast(message);return result}catch(e){toast(e.message,true)}finally{pending--;pendingAction='';updateBusy()}
}
function setTheme(theme){return command('theme',{value:theme})}
function applyTheme(theme){setAttr(document.documentElement,'data-theme',theme);for(const id of ['light','dark']){const active=id===theme;$('#'+id).classList.toggle('active',active);setAttr($('#'+id),'aria-pressed',String(active));setHtml($('#'+id),icon(id==='light'?'sun':'moon')+t(id))}}
function upcoming(name,page){goPage(page===2?'rules':page===3?'system':page===5||name.includes('关于')?'about':'settings')}
function refresh(){command('refresh')}
function restoreAll(){command('restore')}
function setAutoRefresh(value){command('autoRefresh',{value})}
function change(id){const a=apps.find(a=>a.id===id);if(a)command('change',{ids:[id],hidden:!a.hidden})}
function changeCurrent(){if(current)change(current)}
function batch(hidden){if(selected.size)command('change',{ids:[...selected],hidden})}
function copyPath(){if(current)command('copyPath',{id:current},t('copied'))}
function openProperties(){if(current)showWebProperties(current)}
function openRule(){if(current)command('rule',{id:current})}
function filter(value){mode=value;render()}
function setView(value){grid=value;$('#entries').classList.toggle('grid',grid);render()}
function sortBy(key){descending=sortKey===key?!descending:false;sortKey=key;render()}
function matching(){
 const query=$('#search').value.trim().toLocaleLowerCase();
 const rank=a=>a.rule==='program'?2:a.rule==='single'?1:0;
 return apps.filter(a=>(mode==='all'||a.hidden===(mode==='hidden'))&&(a.name+' '+a.path+' '+a.tooltip).toLocaleLowerCase().includes(query))
 .sort((a,b)=>{let result=sortKey==='hidden'?Number(a.hidden)-Number(b.hidden):sortKey==='rule'?rank(a)-rank(b):a.name.localeCompare(b.name,state.language);if(result===0)result=a.name.localeCompare(b.name,state.language)||a.id.localeCompare(b.id);return descending?-result:result});
}
function choose(id,event){
 const results=matching(),start=results.findIndex(a=>a.id===anchor),end=results.findIndex(a=>a.id===id);
 if(event.shiftKey&&start>=0)results.slice(Math.min(start,end),Math.max(start,end)+1).forEach(a=>selected.add(a.id));
 else if(event.ctrlKey||event.metaKey){selected.has(id)?selected.delete(id):selected.add(id);anchor=id}
 else anchor=id;
 current=id;render();[...document.querySelectorAll('.entry')].find(e=>e.dataset.id===id)?.focus({preventScroll:true});
}
function updateBusy(){
 const working=state.busy||pending>0;
 setText($('#busyNote'),t('busy'));$('#busyNote').classList.toggle('show',pending>0&&['change','restore'].includes(pendingAction));
 setAttr($('.listpanel'),'aria-busy',String(working));
 $('#iconsPage').querySelectorAll('.top-actions button:not(#visibilityUndoButton),.entry .rowaction,.detailactions button,.rulebox button,.pathbox button,.tablefoot button').forEach(b=>setProperty(b,'disabled',working));
 setProperty($('#autoRefresh'),'disabled',working);
 if(typeof updateRulesBusy==='function')updateRulesBusy();
 if(typeof updateSystemBusy==='function')updateSystemBusy();
 if(typeof updatePreferencesBusy==='function')updatePreferencesBusy();
 if(typeof updateFeedbackBusy==='function')updateFeedbackBusy();
}

function setText(node,value){value=String(value);if(node.textContent!==value)node.textContent=value}
function setHtml(node,value){if(node._html!==value){node.innerHTML=value;node._html=value}}
function setAttr(node,name,value){value=String(value);if(node.getAttribute(name)!==value)node.setAttribute(name,value)}
function setProperty(node,name,value){if(node[name]!==value)node[name]=value}
function patchIcon(node,a){
 node.classList.toggle('monochrome',!!a.darkPlate);
 node.classList.toggle('dark-glyph',!!a.lightPlate);
 let image=node.querySelector('img');
 if(!image){node.replaceChildren();image=document.createElement('img');image.alt='';image.width=32;image.height=32;node.append(image)}
 if(image.getAttribute('src')!==a.image){image.onerror=()=>setHtml(node,icon('monitor'));image.src=a.image}
}
function createRow(a){
 const row=document.createElement('div');row.className='entry';row.dataset.id=a.id;row.tabIndex=0;
 row.innerHTML='<input type="checkbox"><div class="appname"><span class="appicon"></span><div><strong></strong><small></small></div></div><span class="state"></span><span class="rule"></span><button class="rowaction"></button>';
 const id=a.id;
 row.onclick=e=>{if(!e.target.closest('button,input'))choose(id,e)};
 row.ondblclick=e=>{if(!e.target.closest('button,input'))change(id)};
 row.onkeydown=e=>{if(e.target!==row)return;if(e.key==='Enter'||e.key===' '){e.preventDefault();if(e.key===' ')selected.has(id)?selected.delete(id):selected.add(id);choose(id,{ctrlKey:false,shiftKey:false})}};
 row.querySelector('input').onchange=e=>{e.target.checked?selected.add(id):selected.delete(id);current=id;render()};
 row.querySelector('button').onclick=()=>change(id);
 return row;
}
function patchRow(row,a){
 const signature=JSON.stringify([a,state.language]);
 if(row._signature!==signature){
  row._signature=signature;
  setAttr(row,'aria-label',a.name+' · '+t(a.hidden?'hidden':'visible'));
  setAttr(row.querySelector('input'),'aria-label',t('selected')+' '+a.name);
  setText(row.querySelector('strong'),a.name);setText(row.querySelector('small'),a.proc);
  patchIcon(row.querySelector('.appicon'),a);
  const visibility=row.querySelector('.state');visibility.classList.toggle('visible',!a.hidden);
  setHtml(visibility,icon(a.hidden?'hidden':'eye')+t(a.hidden?'hidden':'visible'));
  setText(row.querySelector('.rule'),a.rule?t(a.rule):'—');
  setText(row.querySelector('button'),t(a.hidden?'show':'hide'));
  setAttr(row.querySelector('button'),'aria-label',t(a.hidden?'show':'hide')+' '+a.name);
 }
 row.classList.toggle('selected',a.id===current);
 setProperty(row.querySelector('input'),'checked',selected.has(a.id));
}
function patchEntries(result,background){
 const container=$('#entries'),scroll=container.scrollTop,active=document.activeElement;
 const wanted=new Set(result.map(a=>a.id)),live=new Set(apps.map(a=>a.id));
 const top=container.getBoundingClientRect().top;
 const anchorRow=background&&scroll>0?[...container.children].find(row=>wanted.has(row.dataset.id)&&row.getBoundingClientRect().bottom>top):null;
 const offset=anchorRow?anchorRow.getBoundingClientRect().top-top:0;
 for(const [id,row] of rowNodes)if(!live.has(id)){row.remove();rowNodes.delete(id)}
 if(!result.length){
  for(const row of [...container.children])if(row.dataset.id)row.remove();
  let empty=container.querySelector('.empty');
  if(!empty){empty=document.createElement('div');empty.className='empty';container.append(empty)}
  setHtml(empty,`${icon(apps.length?'search':'tray')}<strong>${t(apps.length?'noResults':'noIcons')}</strong><p>${t(apps.length?'searchHelp':'emptyHelp')}</p><button class="btn primary" onclick="${apps.length?'clearSearch()':'refresh()'}">${t(apps.length?'clear':'refresh')}</button>${apps.length?'':`<button class="btn feedback-empty-system" onclick="goPage('system')">${t('systemNav')}</button>`}`);
 }else{
  for(const row of [...container.children])if(!wanted.has(row.dataset.id))row.remove();
  let cursor=container.firstElementChild;
  for(const a of result){
   let row=rowNodes.get(a.id);if(!row){row=createRow(a);rowNodes.set(a.id,row)}
   patchRow(row,a);
   if(row===cursor)cursor=cursor.nextElementSibling;else container.insertBefore(row,cursor);
  }
 }
 if(anchorRow?.isConnected){const delta=anchorRow.getBoundingClientRect().top-container.getBoundingClientRect().top-offset;if(Math.abs(delta)>.5)container.scrollTop+=delta}
 else if(container.scrollTop!==scroll)container.scrollTop=scroll;
 if(active?.isConnected&&document.activeElement!==active)active.focus({preventScroll:true});
}
function patchDetails(){
 const a=apps.find(a=>a.id===current),identity=(a?.id??'')+'|'+state.language,signature=JSON.stringify([a,state.language]);
 if(signature===detailSignature)return;
 detailSignature=signature;
 const detail=$('#detail'),scroll=detail.scrollTop;
 if(identity!==detailIdentity){
  detailIdentity=identity;
  detail.innerHTML=a?`<div class="detailhero"><span class="appicon"></span><div><h3></h3><p>${t('software')}</p></div></div><span class="pill"></span><dl><dt>${t('process')}</dt><dd class="process-value"></dd><dt>${t('path')}</dt><dd class="pathbox"><div class="path"></div><button onclick="copyPath()">${t('copy')}</button></dd></dl><div class="rulebox"><strong></strong><p></p><button onclick="openRule()"></button></div>`:`<p>${t('selectHint')}</p>`;
  $('#detailactions').innerHTML=a?'<button class="btn primary" onclick="changeCurrent()"></button><p></p><button class="btn secondary-action" onclick="openProperties()"></button><button class="rowaction feedback-end" onclick="openTerminate(current)"></button>':'';
 }
 if(a){
  patchIcon(detail.querySelector('.appicon'),a);setText(detail.querySelector('h3'),a.name);
  setHtml(detail.querySelector('.pill'),icon(a.hidden?'hidden':'eye')+t(a.hidden?'hidden':'visible')+' · '+t('running'));
  setText(detail.querySelector('.process-value'),a.proc);setText(detail.querySelector('.path'),a.path);
  setHtml(detail.querySelector('.rulebox strong'),icon('rules')+t(a.rule?'viewRule':'createRule'));
  setHtml(detail.querySelector('.rulebox p'),a.rule?esc(t(a.rule))+'<br>'+t('ruleKeep'):t('ruleHelp'));
  setText(detail.querySelector('.rulebox button'),t(a.rule?'viewRule':'createRule')+' →');
  setHtml($('#detailactions .primary'),icon(a.hidden?'eye':'hidden')+t(a.hidden?(a.rule?'temporary':'restoreCurrent'):'hideCurrent'));
  setText($('#detailactions p'),t(a.hidden&&a.rule?'ruleKeep':'noClose'));setText($('#detailactions .secondary-action'),t('properties'));
  const end=$('#detailactions .feedback-end');end.hidden=!a.canTerminate;setText(end,state.language==='en-US'?'End task…':'结束任务…');
 }
 if(detail.scrollTop!==scroll)detail.scrollTop=scroll;
}
function render(background=false){
 const result=matching(),count=apps.filter(a=>a.hidden).length;
 setHtml($('#summary'),`<span><b>${apps.length}</b>${t('icons')}</span><i></i><span><b>${apps.length-count}</b>${t('visible')}</span><span><b>${count}</b>${t('hidden')}</span><span class="rule-status">${icon('rules')}${state.rules} ${t('rules')} · ${t(state.paused?'rulesPaused':state.rules?'rulesRunning':'noRules')}</span>`);
 setHtml($('.health strong'),'<i class="dot"></i>'+t(state.paused?'rulesPaused':state.rules?'rulesRunning':'noRules'));setText($('.health p'),state.rules+' '+t('rules'));
 const counters=document.querySelectorAll('.nav .number');setText(counters[0],apps.length);setText(counters[1],state.rules);
 setText($('.top h2'),t('title'));setText($('.top p'),t('subtitle'));setText(document.querySelector('[data-text=restore]'),t('restore'));
 setText($('.navlabel'),t('workspace'));document.querySelectorAll('.nav').forEach((b,i)=>{const label=t(['title','rulesNav','systemNav','settingsNav','aboutNav'][i]);setText(b.querySelector('span'),label);setAttr(b,'aria-label',label)});
 setText($('.filterrow small'),t('ordinary'));setText($('.tablehead>span:last-child'),t('actionHeader'));setText($('.bottom>span'),t('closeToTray'));
 const heading=$('.detailhead').firstChild;if(heading.nodeValue!==t('detail'))heading.nodeValue=t('detail');setText($('.detailhead span'),current?t('selectedOne'):'—');
 setProperty($('#search'),'placeholder',t('search'));setAttr(document.documentElement,'lang',state.language);
 document.querySelectorAll('[data-filter]').forEach(b=>{const active=b.dataset.filter===mode;b.classList.toggle('active',active);setAttr(b,'aria-pressed',active);setText(b,t(b.dataset.filter==='all'?'all':b.dataset.filter))});
 for(const key of ['name','hidden','rule']){const b=$('#sort-'+key);setText(b,t(key==='name'?'nameHeader':key==='hidden'?'stateHeader':'ruleHeader')+(sortKey===key?(descending?' ↓':' ↑'):''));b.classList.toggle('sort-active',sortKey===key)}
 document.querySelectorAll('.views button').forEach(b=>{const active=(b.id==='gridView')===grid;b.classList.toggle('active',active);setAttr(b,'aria-pressed',active)});
 patchEntries(result,background);
 setProperty($('#selectAll'),'checked',result.length>0&&result.every(a=>selected.has(a.id)));setProperty($('#selectAll'),'indeterminate',result.some(a=>selected.has(a.id))&&!$('#selectAll').checked);setProperty($('#selectAll'),'disabled',!result.length);
 setHtml($('#tablefoot'),selected.size?`<span>${t('selected')} ${selected.size}</span><span><button class="rowaction" onclick="batch(true)">${t('hideSelected')}</button><button class="rowaction" onclick="batch(false)">${t('showSelected')}</button></span>`:`<span>${result.length} ${t('results')}</span><span>${t('keyboard')}</span>`);
 setProperty($('#autoRefresh'),'checked',state.autoRefresh);setText($('#refreshLabel'),t(state.autoRefresh?'refreshOn':'refreshOff'));
 patchDetails();applyTheme(state.theme);updateBusy();
}
function clearSearch(){$('#search').value='';mode='all';render();$('#search').focus()}
function presentStartup(){
 if(startupPresented)return;startupPresented=true;
 const images=[...document.images].filter(i=>{const r=i.getBoundingClientRect();return r.width&&r.height&&r.bottom>0&&r.top<innerHeight});
 Promise.race([Promise.allSettled(images.map(i=>i.decode())),new Promise(resolve=>setTimeout(resolve,100))]).then(()=>{
  let done=false;const present=()=>{if(done)return;done=true;window.chrome?.webview?.postMessage({action:'presented'})};
  if(document.hidden)present();else{requestAnimationFrame(present);setTimeout(present,100)}
 });
}
function receive(message){
 if(message.type==='state'){
  const {busy,...snapshot}=message,signature=JSON.stringify(snapshot);
  if(signature===lastReceivedState){state=message;updateBusy();return;}
  lastReceivedState=signature;
  const live=new Set(message.entries.map(a=>a.id));selected=new Set([...selected].filter(id=>live.has(id)));
  apps=message.entries;state=message;if(!live.has(current))current=apps[0]?.id??null;render(true);
  if(typeof renderRules==='function'){renderRules(true);syncWebPage()}if(typeof renderSystem==='function')renderSystem();if(typeof renderPreferences==='function')renderPreferences();if(typeof renderFeedback==='function')renderFeedback();presentStartup();
 }else if(message.type==='result'){
  const request=requests.get(message.requestId);if(!request)return;clearTimeout(request.timer);requests.delete(message.requestId);
  if(message.success)request.resolve(message.data);else{const error=Error(message.error||t('failed'));error.data=message.data;request.reject(error)}
 }else if(message.type==='settingsLeave'&&typeof requestSettingsLeave==='function'){requestSettingsLeave(message.page);
 }else if(message.type==='ruleTarget'&&typeof selectRuleTarget==='function'){
  selectRuleTarget(message);
 }else if(message.type==='exitPrompt'){openWebExit();
 }else if(message.type==='terminatePrompt'){openTerminate(message.id);
 }else if(message.type==='operationError'){toast(message.error,true);
 }
}
$('#search').oninput=()=>render();
$('#selectAll').onchange=e=>{matching().forEach(a=>e.target.checked?selected.add(a.id):selected.delete(a.id));render()};
document.addEventListener('keydown',e=>{if(state.page!=='icons'||document.querySelector('dialog[open]'))return;if(e.ctrlKey&&e.key.toLowerCase()==='k'){e.preventDefault();$('#search').focus()}if(e.key==='Escape')$('#toast').classList.remove('show');const digit=e.code?.match(/^Digit([123])$/)?.[1]??e.key;if(e.ctrlKey&&e.shiftKey&&['1','2','3'].includes(digit)){e.preventDefault();sortBy(['name','hidden','rule'][Number(digit)-1])}});
window.chrome?.webview?.addEventListener('message',e=>receive(e.data));
render();
if(window.chrome?.webview)window.chrome.webview.postMessage({action:'ready'});
