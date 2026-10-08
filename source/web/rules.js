let ruleCurrent=null,ruleMode='all',ruleSelected=new Set(),ruleAnchor=null,ruleSortKey='name',ruleDescending=false;
let ruleDetailIdentity='',ruleDetailSignature='',ruleEditing=null,ruleDeleting=[],ruleValidationSequence=0,ruleValidationTimer,ruleEditorValid=false;
let queuedRuleTarget=null;
const pageScroll=new Map();
const ruleNodes=new Map(),dialogFocus=new Map();
const ruleWords={
 rulesTitle:['隐藏规则','Hide rules'],rulesSubtitle:['设定一次，让图标自动保持清爽。','Set it once. Keep your tray organized.'],
 create:['创建规则','Create rule'],edit:['编辑隐藏规则','Edit hide rule'],editScope:['编辑匹配范围','Edit matching scope'],save:['保存修改','Save changes'],
 all:['全部规则','All rules'],programFilter:['整个程序','Programs'],singleFilter:['单个图标','Single icons'],
 scope:['匹配范围','Matching scope'],action:['操作','Action'],details:['规则详情','Rule details'],programType:['程序规则','Program rule'],singleType:['单图标规则','Single-icon rule'],
 program:['此程序的所有图标','All icons for this program'],single:['仅此图标','This icon only'],programShort:['所有图标','All icons'],
 hidden:['已自动隐藏','Auto-hidden'],temporary:['本次暂时显示','Show for this session'],pending:['等待应用','Pending application'],waiting:['等待程序运行','Waiting for application'],paused:['自动隐藏已暂停','Auto-hide paused'],
 active:['自动隐藏运行中','Auto-hide running'],noRules:['尚未设置自动隐藏规则','No auto-hide rules yet'],
 activeHelp:['匹配图标出现时自动隐藏，下次启动仍生效。','Hide matching icons when they appear; rules remain saved.'],
 pausedHelp:['规则仍保留，已经隐藏的图标保持当前状态。','Keep saved rules and the current visibility of all icons.'],
 emptyHelp:['为不常用的托盘图标创建规则，之后出现时自动隐藏。','Create a rule to hide an infrequently used tray icon whenever it appears.'],
 pause:['暂停自动隐藏','Pause auto-hide'],resume:['继续自动隐藏','Resume auto-hide'],apply:['立即应用','Apply now'],
 applyingHelp:['立即应用会继续自动隐藏，并清除本次暂时显示例外。','Apply now resumes auto-hide and clears temporary visibility exceptions.'],
 search:['搜索规则名称或程序路径…','Search rule names or program paths…'],
 behaviorHint:['暂时显示仅在本次运行生效，原规则仍保留。\n立即应用会清除暂时显示例外；删除默认保留当前图标状态。','Temporary visibility lasts for this session; the rule remains saved.\nApply now clears exceptions. Deleting keeps current icon visibility by default.'],
 checking:['规则检查 · 每 2.5 秒','Rule checks · every 2.5 seconds'],keepRunning:['隐藏图标不会结束对应程序','Hiding icons keeps applications running'],
 undo:['撤销上次规则修改','Undo last rule change'],noSelection:['选择一条规则，查看匹配范围和当前状态。','Select a rule to see its matching scope and status.'],
 saved:['规则已保存','Rule saved'],deleted:['规则已删除','Rule deleted'],undone:['已撤销上次规则修改','Last rule change undone'],
 currentIcons:['当前匹配图标','Current matching icons'],matched:['个匹配','matched'],hiddenCount:['个已隐藏','hidden'],nextAppearance:['目标图标出现时自动匹配','Match automatically when the target icon appears'],
 matchTitle:['匹配行为','Matching behavior'],singleMatch:['按所选托盘图标的标识匹配，不影响同程序的其他图标。','Match the selected tray icon identity; keep other icons for the same application unchanged.'],
 programMatch:['匹配相同程序路径，包括之后新出现的普通托盘图标。','Match the same program path, including application icons that appear later.'],
 technical:['高级匹配依据','Advanced matching identity'],delete:['删除规则','Delete rule'],deleteCurrent:['删除此规则','Delete this rule'],deleteSelected:['删除选中','Delete selected'],
 deleteTitle:['删除隐藏规则？','Delete hide rules?'],deleteHelp:['删除后不再自动隐藏匹配图标。已经隐藏的图标会保持当前状态，直到手动恢复。','Matching icons will no longer be hidden automatically. Already hidden icons keep their current state until you restore them.'],
 restoreAfterDelete:['同时恢复这些规则匹配的已隐藏图标','Also restore hidden icons matched by these rules'],cancel:['取消','Cancel'],
 editorSubtitle:['选择目标与匹配范围，软件保持正常运行。','Choose a target and matching scope. The application keeps running.'],chooseTarget:['选择托盘图标','Choose a tray icon'],
 targetHelp:['从当前识别到的普通托盘图标中选择；同程序图标以提示和标识区分。','Choose a current application tray icon. Tooltips and identities distinguish icons from the same program.'],
 targetLocked:['编辑时保留原程序路径；可选择该程序的其他图标。','Keep the original program path; you can choose another icon from this program.'],
 keepIdentity:['保留已保存的匹配标识 · 程序未运行','Keep the saved identity · application not running'],noTargets:['尚未识别到普通托盘图标','No application tray icons detected'],
 singleHelp:['精确匹配所选托盘图标，不影响同程序的其他图标。','Match only the selected tray icon, keeping its other icons unchanged.'],
 programHelp:['匹配相同程序路径，包括之后新出现的托盘图标。','Match this program path, including icons that appear later.'],
 recommended:['推荐','Recommended'],preview:['规则生效预览','Rule preview'],previewSingle:['仅隐藏所选托盘图标。','Hide only the selected tray icon.'],previewProgram:['隐藏该程序路径下的所有普通托盘图标。','Hide all application tray icons for this program path.'],
 previewActive:['保存后立即应用，之后出现时自动隐藏。','Apply on save and hide matching icons when they reappear.'],previewPaused:['自动隐藏当前已暂停，保存后等待继续执行。','Auto-hide is paused. Save the rule and apply it when you resume.'],
 runtimeHint:['规则仅在 TrayPilot 运行时执行','Rules run while TrayPilot is running'],validating:['正在检查匹配范围…','Checking the matching scope…'],
 working:['正在更新规则和图标状态…','Updating rules and icon states…'],saving:['正在保存…','Saving…'],deleting:['正在删除…','Deleting…'],selected:['已选择','Selected'],rulesCount:['条规则','rules'],clear:['清空搜索','Clear search'],noResults:['未找到匹配规则','No matching rules'],
 noResultsHelp:['尝试其他名称或路径，或者清空搜索查看全部规则。','Try another name or path, or clear your search.'],
 ruleSaveFailed:['保存未完成，请检查目标和匹配范围。','Unable to save. Check the target and matching scope.'],listKeyboard:['双击编辑 · Delete 删除选中','Double-click to edit · Delete to remove selected rules'],autoSaved:['规则会自动保存','Rules are saved automatically']
};
const rt=k=>(ruleWords[k]||[k,k])[state.language==='en-US'?1:0];
const ruleCatalog=()=>state.ruleCatalog||[];
const ruleRank=r=>({hidden:0,temporary:1,pending:2,waiting:3,paused:4}[r.state]??3);
function goPage(page){command('navigate',{page})}
function syncWebPage(){
 const pages=[['icons','iconsPage',['entries','detail']],['rules','rulesPage',['ruleEntries','ruleDetail']],['system','systemPage',['systemEntries','systemDetail']],['settings','settingsPage',['prefSettingsBody']],['about','aboutPage',['prefAboutBody']]];
 for(const [name,id,containers] of pages){
  const hidden=state.page!==name,node=$('#'+id);if(node.hidden===hidden)continue;
  if(hidden)for(const key of containers)pageScroll.set(key,$('#'+key).scrollTop);
  setProperty(node,'hidden',hidden);
  if(!hidden)for(const key of containers)if(pageScroll.has(key))$('#'+key).scrollTop=pageScroll.get(key);
 }
 document.querySelectorAll('.nav').forEach((node,i)=>{const active=i===pages.findIndex(p=>p[0]===state.page);node.classList.toggle('active',active);if(active)setAttr(node,'aria-current','page');else if(node.hasAttribute('aria-current'))node.removeAttribute('aria-current')});
}
function visibleRules(){
 const query=$('#ruleSearch').value.trim().toLocaleLowerCase();
 return ruleCatalog().filter(r=>(ruleMode==='all'||ruleMode===r.scope)&&(r.name+' '+r.path+' '+r.tooltip).toLocaleLowerCase().includes(query))
 .sort((a,b)=>{let order=ruleSortKey==='state'?ruleRank(a)-ruleRank(b):a.name.localeCompare(b.name,state.language);if(!order)order=a.name.localeCompare(b.name,state.language)||a.id.localeCompare(b.id);return ruleDescending?-order:order});
}
function filterRules(value){ruleMode=value;renderRules()}
function sortRules(value){ruleDescending=ruleSortKey===value?!ruleDescending:false;ruleSortKey=value;renderRules()}
function chooseRule(id,event={}){
 const rows=visibleRules(),start=rows.findIndex(r=>r.id===ruleAnchor),end=rows.findIndex(r=>r.id===id);
 if(event.shiftKey&&start>=0)rows.slice(Math.min(start,end),Math.max(start,end)+1).forEach(r=>ruleSelected.add(r.id));
 else if(event.ctrlKey||event.metaKey){ruleSelected.has(id)?ruleSelected.delete(id):ruleSelected.add(id);ruleAnchor=id}
 else ruleAnchor=id;
 ruleCurrent=id;renderRules();ruleNodes.get(id)?.focus({preventScroll:true});
}
function ruleStateHtml(rule){return icon(rule.state==='hidden'?'hidden':rule.state==='temporary'?'eye':rule.state==='paused'?'pause':'clock')+esc(rt(rule.state))}
function createRuleRow(rule){
 const row=document.createElement('div');row.className='rule-entry';row.dataset.id=rule.id;row.tabIndex=0;
 row.innerHTML='<input type="checkbox"><div class="appname"><span class="appicon"></span><div><strong></strong><small></small></div></div><span class="scope-pill"></span><span class="rule-state"></span><button class="rowaction"></button>';
 const id=rule.id;
 row.onclick=e=>{if(!e.target.closest('button,input'))chooseRule(id,e)};
 row.ondblclick=e=>{if(!e.target.closest('button,input'))openRuleEditor(id)};
 row.onkeydown=e=>{if(e.target!==row)return;if(e.key==='Enter'){e.preventDefault();openRuleEditor(id)}else if(e.key===' '){e.preventDefault();const checkbox=row.querySelector('input');checkbox.checked=!checkbox.checked;checkbox.dispatchEvent(new Event('change'))}};
 row.querySelector('input').onchange=e=>{e.target.checked?ruleSelected.add(id):ruleSelected.delete(id);ruleCurrent=id;renderRules()};
 row.querySelector('button').onclick=()=>openRuleEditor(id);return row;
}
function patchRuleRow(row,rule){
 const signature=JSON.stringify([rule,state.language]);
 if(row._signature!==signature){
  row._signature=signature;setAttr(row,'aria-label',rule.name+' · '+rt(rule.state));setAttr(row.querySelector('input'),'aria-label',rt('selected')+' '+rule.name);
  setText(row.querySelector('strong'),rule.name);setText(row.querySelector('small'),rule.proc);patchIcon(row.querySelector('.appicon'),rule);
  setHtml(row.querySelector('.scope-pill'),icon(rule.scope==='program'?'grid':'tray')+esc(rt(rule.scope==='program'?'programShort':'single')));
  const status=row.querySelector('.rule-state');setHtml(status,ruleStateHtml(rule));status.classList.toggle('ok',rule.state==='hidden');status.classList.toggle('temp',rule.state==='temporary');
  setText(row.querySelector('button'),rt('editScope').split(state.language==='en-US'?' ':'匹配')[0]);setAttr(row.querySelector('button'),'aria-label',rt('edit')+' '+rule.name);
 }
 row.classList.toggle('selected',rule.id===ruleCurrent);setProperty(row.querySelector('input'),'checked',ruleSelected.has(rule.id));
}
function patchRuleEntries(result,background){
 const container=$('#ruleEntries'),scroll=container.scrollTop,active=document.activeElement,top=container.getBoundingClientRect().top;
 const wanted=new Set(result.map(r=>r.id)),live=new Set(ruleCatalog().map(r=>r.id));
 const anchor=background&&scroll>0?[...container.children].find(row=>wanted.has(row.dataset.id)&&row.getBoundingClientRect().bottom>top):null;
 const offset=anchor?anchor.getBoundingClientRect().top-top:0;
 for(const [id,row] of ruleNodes)if(!live.has(id)){row.remove();ruleNodes.delete(id)}
 if(!result.length){
  for(const row of [...container.children])if(row.dataset.id)row.remove();
  let empty=container.querySelector('.empty');if(!empty){empty=document.createElement('div');empty.className='empty';container.append(empty)}
  setHtml(empty,`<strong>${rt(ruleCatalog().length?'noResults':'noRules')}</strong><p>${rt(ruleCatalog().length?'noResultsHelp':'emptyHelp')}</p><button class="btn primary" onclick="${ruleCatalog().length?'clearRuleSearch()':'openRuleEditor()'}">${rt(ruleCatalog().length?'clear':'create')}</button>`);
 }else{
  for(const row of [...container.children])if(!wanted.has(row.dataset.id))row.remove();
  let cursor=container.firstElementChild;
  for(const rule of result){let row=ruleNodes.get(rule.id);if(!row){row=createRuleRow(rule);ruleNodes.set(rule.id,row)}patchRuleRow(row,rule);if(row===cursor)cursor=cursor.nextElementSibling;else container.insertBefore(row,cursor)}
 }
 if(anchor?.isConnected){const delta=anchor.getBoundingClientRect().top-container.getBoundingClientRect().top-offset;if(Math.abs(delta)>.5)container.scrollTop+=delta}
 else if(container.scrollTop!==scroll)container.scrollTop=scroll;
 if(active?.isConnected&&document.activeElement!==active)active.focus({preventScroll:true});
}
function patchRuleDetails(){
 const rule=ruleCatalog().find(r=>r.id===ruleCurrent),signature=JSON.stringify([rule,state.language]);
 if(signature===ruleDetailSignature)return;ruleDetailSignature=signature;
 const container=$('#ruleDetail'),scroll=container.scrollTop,identity=(rule?.id??'')+'|'+state.language;
 if(identity!==ruleDetailIdentity){
  ruleDetailIdentity=identity;
  container.innerHTML=rule?`<div class="detailhero"><span class="appicon"></span><div><h3></h3><p></p></div></div><span class="pill"></span><dl><dt>${rt('scope')}</dt><dd class="rule-scope-value"></dd><dt>${t('path')}</dt><dd class="pathbox"><div class="path"></div></dd><dt>${rt('currentIcons')}</dt><dd class="rule-count-value"></dd></dl><div class="match-box"><strong>${icon('rules')}${rt('matchTitle')}</strong><p></p></div><details class="technical"><summary>${rt('technical')}</summary><code></code></details>`:`<p class="rule-detail-empty">${rt('noSelection')}</p>`;
 }
 setProperty($('#ruleDetailActions'),'hidden',!rule);
 setText($('#ruleDetailType'),rule?rt(rule.scope==='program'?'programType':'singleType'):'—');
 if(rule){
  patchIcon(container.querySelector('.appicon'),rule);setText(container.querySelector('h3'),rule.name);setText(container.querySelector('.detailhero p'),rule.proc);
  setHtml(container.querySelector('.pill'),ruleStateHtml(rule));setText(container.querySelector('.rule-scope-value'),rt(rule.scope));setText(container.querySelector('.path'),rule.path);
  setText(container.querySelector('.rule-count-value'),rule.liveCount?`${rule.liveCount} ${rt('matched')} · ${rule.hiddenCount} ${rt('hiddenCount')}`:rt('nextAppearance'));
  setText(container.querySelector('.match-box p'),rt(rule.scope==='program'?'programMatch':'singleMatch'));
  setProperty(container.querySelector('.technical'),'hidden',rule.scope==='program');setText(container.querySelector('.technical code'),rule.identity);
 }
 if(container.scrollTop!==scroll)container.scrollTop=scroll;
}
function renderRules(background=false){
 const live=new Set(ruleCatalog().map(r=>r.id));ruleSelected=new Set([...ruleSelected].filter(id=>live.has(id)));if(!live.has(ruleCurrent))ruleCurrent=ruleCatalog()[0]?.id??null;
 document.querySelectorAll('[data-rule-text]').forEach(node=>setText(node,rt(node.dataset.ruleText)));
 const programCount=ruleCatalog().filter(r=>r.scope==='program').length;
 setHtml($('#ruleSummary'),`<span><b>${ruleCatalog().length}</b>${rt('rulesCount')}</span><i></i><span><b>${programCount}</b>${rt('programFilter')}</span><span><b>${ruleCatalog().length-programCount}</b>${rt('singleFilter')}</span><span class="rule-status">${icon('rules')}${rt(state.paused?'paused':ruleCatalog().length?'active':'noRules')}</span>`);
 setText($('#ruleAutoTitle'),rt(state.paused?'paused':ruleCatalog().length?'active':'noRules'));setText($('#ruleAutoDescription'),rt(state.paused?'pausedHelp':ruleCatalog().length?'activeHelp':'emptyHelp'));setText($('#rulePause'),rt(state.paused?'resume':'pause'));
 setProperty($('#ruleSearch'),'placeholder',rt('search'));
 setAttr($('#ruleSearch'),'aria-label',rt('search'));setAttr($('#ruleSelectAll'),'aria-label',t('selected')+' '+rt('all'));setAttr($('#ruleTarget'),'aria-label',rt('chooseTarget'));
 document.querySelectorAll('[data-rule-filter]').forEach(node=>{const active=node.dataset.ruleFilter===ruleMode;node.classList.toggle('active',active);setAttr(node,'aria-pressed',active);setText(node,rt(node.dataset.ruleFilter==='all'?'all':node.dataset.ruleFilter+'Filter'))});
 for(const key of ['name','state']){const node=$('#ruleSort'+(key==='name'?'Name':'State'));setText(node,(key==='name'?t('nameHeader'):t('stateHeader'))+(ruleSortKey===key?(ruleDescending?' ↓':' ↑'):''));node.classList.toggle('sort-active',ruleSortKey===key)}
 const result=visibleRules();patchRuleEntries(result,background);
 setProperty($('#ruleSelectAll'),'checked',result.length>0&&result.every(r=>ruleSelected.has(r.id)));setProperty($('#ruleSelectAll'),'indeterminate',result.some(r=>ruleSelected.has(r.id))&&!$('#ruleSelectAll').checked);setProperty($('#ruleSelectAll'),'disabled',!result.length);
 setHtml($('#ruleTablefoot'),ruleSelected.size?`<span>${rt('selected')} ${ruleSelected.size}</span><button class="rowaction" data-rule-command onclick="openRuleDelete([...ruleSelected])">${rt('deleteSelected')}</button>`:`<span>${result.length} ${rt('rulesCount')}</span><span>${rt('listKeyboard')}</span>`);
 setText($('#ruleUndo'),rt('undo'));setProperty($('#ruleUndo'),'hidden',!state.ruleUndo);setText($('#ruleDelete'),rt('deleteCurrent'));
 for(const theme of ['light','dark']){const node=$('#rules-'+theme),active=theme===state.theme;node.classList.toggle('active',active);setAttr(node,'aria-pressed',active);setHtml(node,icon(theme==='light'?'sun':'moon')+t(theme))}
 patchRuleDetails();updateRulesBusy();
 if($('#ruleEditor').open)updateRulePreview(false);
}
function updateRulesBusy(){
 const working=state.busy||pending>0,rule=ruleCatalog().find(r=>r.id===ruleCurrent);
 setAttr($('#ruleListPanel'),'aria-busy',String(working));setText($('#ruleBusyNote'),rt('working'));$('#ruleBusyNote').classList.toggle('show',working&&pendingAction.startsWith('rule')&&pendingAction!=='ruleValidate');
 document.querySelectorAll('[data-rule-command],.rule-entry .rowaction').forEach(node=>setProperty(node,'disabled',working||(node.id==='ruleTemporary'&&!rule?.liveCount)||(['ruleEdit','ruleDelete'].includes(node.id)&&!rule)));
 for(const dialog of [$('#ruleEditor'),$('#ruleDeleteDialog')]){
  if(!dialog.open)continue;
  setAttr(dialog.querySelector('form'),'aria-busy',String(working));
  dialog.querySelectorAll('button,select,input').forEach(node=>{if(node.id!=='ruleSave'&&!(node.name==='ruleScope'&&node.value==='single'))setProperty(node,'disabled',working)});
 }
 if($('#ruleEditor').open){const original=ruleCatalog().find(r=>r.id===ruleEditing);setProperty(document.querySelector('input[name=ruleScope][value=single]'),'disabled',working||(!$('#ruleTarget').value&&original?.scope!=='single'));setProperty($('#ruleSave'),'disabled',working||!ruleEditorValid);setText($('#ruleSave'),rt(working?'saving':ruleEditing?'save':'create'))}
 if($('#ruleDeleteDialog').open)setText($('#ruleDeleteConfirm'),rt(working?'deleting':'delete'));
 if(!working&&queuedRuleTarget){const target=queuedRuleTarget;queuedRuleTarget=null;focusRuleTarget(target)}
}
async function ruleRequest(action,data={}){
 if(pending||state.busy)throw Error(rt('working'));
 pending++;pendingAction=action;updateBusy();
 try{return await send(action,data)}finally{pending--;pendingAction='';updateBusy()}
}
async function ruleAction(action,data,message){try{await ruleRequest(action,data);toast(message)}catch(error){toast(error.message,true)}}
function toggleRulePause(){ruleAction('rulePause',{value:!state.paused},rt(state.paused?'active':'paused'))}
function applyRules(){ruleAction('ruleApply',{},rt('active'))}
function temporarilyShowRule(){if(ruleCurrent)ruleAction('ruleTemporary',{id:ruleCurrent},rt('temporary'))}
function undoRules(){ruleAction('ruleUndo',{},rt('undone'))}
function clearRuleSearch(){$('#ruleSearch').value='';ruleMode='all';renderRules();$('#ruleSearch').focus()}
function openRuleDialog(id){const dialog=$('#'+id);dialogFocus.set(id,document.activeElement);dialog.showModal();updateRulesBusy()}
function closeRuleDialog(id){if(!pending&&!state.busy)$('#'+id).close()}
function restoreRuleDialogFocus(id){const original=dialogFocus.get(id);if(original?.isConnected&&!original.closest('[hidden]'))original.focus({preventScroll:true});else $('#ruleSearch').focus()}
function openRuleEditor(id=null,targetId=null){
 if(pending||state.busy)return;
 ruleEditing=id;ruleEditorValid=false;ruleValidationSequence++;
 const original=ruleCatalog().find(r=>r.id===id),available=apps.filter(a=>!original||a.path.toLocaleLowerCase()===original.path.toLocaleLowerCase());
 const target=$('#ruleTarget');target.replaceChildren();
 if(!available.length){const option=new Option(original?rt('keepIdentity'):rt('noTargets'),'');target.add(option)}
 for(const app of available){const hint=(app.tooltip||app.proc).replace(/\s+/g,' '),ambiguous=available.some(other=>other.id!==app.id&&other.path===app.path&&(other.tooltip||other.proc).replace(/\s+/g,' ')===hint);target.add(new Option(`${app.name} · ${hint}${ambiguous?' · '+app.identity:''}`,app.id))}
 const desired=targetId??(original?available.find(a=>original.scope==='program'||a.identity===original.identity)?.id:null);
 if(desired&&available.some(a=>a.id===desired))target.value=desired;
 document.querySelector(`input[name=ruleScope][value=${original?.scope??'single'}]`).checked=true;
 setText($('#ruleEditorTitle'),rt(original?'edit':'create'));setText($('#ruleSave'),rt(original?'save':'create'));setText($('#ruleTargetHelp'),rt(original?'targetLocked':'targetHelp'));
 setText($('#ruleError'),'');target.removeAttribute('aria-invalid');renderRules();openRuleDialog('ruleEditor');updateRulePreview();target.focus();
}
function ruleDraft(){return {originalId:ruleEditing,targetId:$('#ruleTarget').value||null,scope:document.querySelector('input[name=ruleScope]:checked').value}}
function updateRulePreview(validate=true){
 const original=ruleCatalog().find(r=>r.id===ruleEditing),app=apps.find(a=>a.id===$('#ruleTarget').value)??original;
 const selected=$('#ruleChosenApp');
 if(!selected.querySelector('.appicon'))selected.innerHTML='<span class="appicon"></span><div><strong></strong><br><code></code></div>';
 if(app){patchIcon(selected.querySelector('.appicon'),app);setText(selected.querySelector('strong'),app.name);setText(selected.querySelector('code'),app.path)}
 else{selected.querySelector('.appicon').replaceChildren();setText(selected.querySelector('strong'),rt('noTargets'));setText(selected.querySelector('code'),'')}
 const scope=document.querySelector('input[name=ruleScope]:checked').value;
 setText($('#rulePreviewText'),rt(scope==='program'?'previewProgram':'previewSingle')+' '+rt(state.paused?'previewPaused':'previewActive'));
 updateRulesBusy();if(validate)validateRuleDraft();
}
function validateRuleDraft(){
 clearTimeout(ruleValidationTimer);const generation=++ruleValidationSequence;ruleEditorValid=false;setProperty($('#ruleSave'),'disabled',true);setText($('#ruleError'),'');
 ruleValidationTimer=setTimeout(async()=>{
  try{await send('ruleValidate',ruleDraft());if(generation!==ruleValidationSequence||!$('#ruleEditor').open)return;ruleEditorValid=true;$('#ruleTarget').removeAttribute('aria-invalid');setText($('#ruleError'),'')}
  catch(error){if(generation!==ruleValidationSequence||!$('#ruleEditor').open)return;setText($('#ruleError'),error.message);setAttr($('#ruleTarget'),'aria-invalid','true')}
  finally{if(generation===ruleValidationSequence)updateRulesBusy()}
 },120);
}
async function saveWebRule(event){
 event.preventDefault();if(!ruleEditorValid||pending||state.busy)return;
 setText($('#ruleError'),'');ruleValidationSequence++;clearTimeout(ruleValidationTimer);
 try{const result=await ruleRequest('ruleSave',ruleDraft());ruleCurrent=result.id;ruleSelected=new Set();ruleMode='all';$('#ruleSearch').value='';$('#ruleEditor').close();renderRules();toast(rt('saved'))}
 catch(error){if(error.data?.saved){ruleEditing=error.data.id;setText($('#ruleEditorTitle'),rt('edit'));setText($('#ruleSave'),rt('save'))}setText($('#ruleError'),error.message);$('#ruleError').focus();ruleEditorValid=false;updateRulesBusy()}
}
function openRuleDelete(ids){
 if(pending||state.busy)return;ruleDeleting=ids.filter(id=>ruleCatalog().some(r=>r.id===id));if(!ruleDeleting.length)return;
 const first=ruleCatalog().find(r=>r.id===ruleDeleting[0]);setText($('#ruleDeleteTarget'),ruleDeleting.length===1?first.name+' · '+rt(first.scope):ruleDeleting.length+' '+rt('rulesCount'));
 setProperty($('#ruleRestoreAfterDelete'),'checked',false);setText($('#ruleDeleteError'),'');openRuleDialog('ruleDeleteDialog');$('#ruleDeleteDialog button[type=button]').focus();
}
async function deleteWebRules(event){
 event.preventDefault();if(pending||state.busy)return;
 try{await ruleRequest('ruleDelete',{ids:ruleDeleting,restore:$('#ruleRestoreAfterDelete').checked});$('#ruleDeleteDialog').close();toast(rt('deleted'))}
 catch(error){setText($('#ruleDeleteError'),error.message);$('#ruleDeleteError').focus()}
}
function selectRuleTarget(message){queuedRuleTarget=message;if(!pending&&!state.busy){queuedRuleTarget=null;focusRuleTarget(message)}}
function focusRuleTarget(message){
 if(message.ruleId){ruleCurrent=message.ruleId;ruleMode='all';$('#ruleSearch').value='';renderRules();const row=ruleNodes.get(ruleCurrent);row?.scrollIntoView({block:'nearest'});row?.focus({preventScroll:true})}
 else openRuleEditor(null,message.id);
}
$('#ruleSearch').oninput=()=>renderRules();$('#ruleSelectAll').onchange=event=>{visibleRules().forEach(r=>event.target.checked?ruleSelected.add(r.id):ruleSelected.delete(r.id));renderRules()};
$('#ruleTarget').onchange=()=>updateRulePreview();document.querySelectorAll('input[name=ruleScope]').forEach(node=>node.onchange=()=>updateRulePreview());
$('#ruleEditorForm').onsubmit=saveWebRule;$('#ruleDeleteForm').onsubmit=deleteWebRules;
for(const id of ['ruleEditor','ruleDeleteDialog']){const dialog=$('#'+id);dialog.addEventListener('cancel',event=>{if(pending||state.busy)event.preventDefault()});dialog.addEventListener('close',()=>{if(dialog.open)return;ruleValidationSequence++;clearTimeout(ruleValidationTimer);restoreRuleDialogFocus(id)})}
document.addEventListener('keydown',event=>{
 if(state.page!=='rules'||document.querySelector('dialog[open]'))return;
 if(event.ctrlKey&&event.key.toLowerCase()==='k'){event.preventDefault();$('#ruleSearch').focus()}
 if(event.key==='Escape'&&document.activeElement===$('#ruleSearch'))clearRuleSearch();
 const digit=event.code?.match(/^Digit([12])$/)?.[1];if(event.ctrlKey&&event.shiftKey&&digit){event.preventDefault();sortRules(digit==='1'?'name':'state')}
 if(event.key==='Delete'&&document.activeElement.closest('.rule-entry')){event.preventDefault();openRuleDelete(ruleSelected.size?[...ruleSelected]:[ruleCurrent])}
});
renderRules();syncWebPage();
