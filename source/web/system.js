let systemCurrent=1,systemFilter='all',systemDetailIdentity='';
const systemNodes=new Map(),systemDialogFocus=new Map();
paths.check='M8 12l3 3 5-6M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0';
const systemWords={
 title:['系统图标','System icons'],subtitle:['按你的习惯，整理主任务栏上的系统控件。','Arrange the system controls on your main taskbar.'],
 restore:['恢复全部系统图标','Restore all system icons'],scheme:['识别方案','Discovery method'],retry:['重试连接','Retry connection'],reconnect:['重新识别','Identify again'],
 connected:['系统图标已连接 · 主任务栏','System icons connected · Main taskbar'],connecting:['正在识别系统图标…','Identifying system icons…'],failed:['暂时无法连接 · 隐藏选择已保留','Connection unavailable · Preferences retained'],
 connectingHelp:['首次使用或 Windows 更新后，可能需要联网准备兼容资源。','First use or a Windows update may require downloading compatibility resources.'],
 modern:['新版兼容方案','Modern compatibility method'],legacy:['旧方案','Legacy method'],
 all:['全部','All'],common:['常用控件','Common'],indicators:['状态提示','Indicators'],taskbar:['任务栏','Taskbar'],
 guide:['开关开启 = 显示图标','Switch on = Show icon'],missingHint:['当前未出现的控件仍可预设隐藏','Hide preferences can be set for controls that are currently absent'],autoSaved:['选择自动保存','Preferences save automatically'],
 details:['控件详情','Control details'],mainTaskbar:['主任务栏','Main taskbar'],independent:['仅管理主任务栏 · 系统控件独立保存','Main taskbar only · System preferences saved separately'],independentHelp:['系统选择独立保存，普通规则暂停不影响此页。','System preferences are saved separately. Pausing application rules does not affect these controls.'],unaffected:['不影响普通软件图标与隐藏规则','Application icons and hide rules keep their preferences'],
 show:['显示','Show'],hide:['隐藏此控件','Hide this control'],showAction:['恢复显示','Show this control'],
 saveTitle:['独立保存系统选择','System preferences are saved separately'],saveHelp:['恢复此项会清除它的隐藏选择；普通图标与隐藏规则不受影响。退出时是否恢复由设置决定。','Showing a control clears its hide preference. Application icons and rules keep their preferences. Exit behavior follows your settings.'],
 waitingTitle:['出现后自动应用','Apply when the control appears'],waitingHelp:['当前未出现不等于识别失败。可预设隐藏，控件出现后自动应用。','An absent control is not a discovery failure. Set a hide preference and apply it when the control appears.'],
 sharedTitle:['麦克风与定位共享提示','Shared microphone and location indicator'],sharedHelp:['部分系统使用同一枚提示图标。需要同时隐藏麦克风与定位，才能隐藏共享提示。','Some Windows environments share one indicator. Both microphone and location must be hidden to hide it.'],
 unavailable:['连接恢复前，显示开关暂不可操作。请重试连接或切换识别方案。','Visibility switches are unavailable until connected. Retry or choose another discovery method.'],
 diagnostic:['连接诊断','Connection diagnostics'],undo:['撤销上次修改','Undo last change'],saving:['正在保存，等待任务栏确认…','Saving · Waiting for taskbar acknowledgement…'],saved:['系统图标选择已保存','System icon preference saved'],restored:['已恢复系统控件，并清除全部系统隐藏选择','System icons restored and all system hide preferences cleared'],undone:['已撤销上次系统图标修改','Last system icon change undone'],
 methodHelp:['根据当前 Windows 环境选择兼容方式。','Choose a method for your Windows environment.'],methodTitle:['系统图标识别方式','System icon discovery method'],recommended:['推荐','Recommended'],
 modernHelp:['适配新版任务栏。首次使用或 Windows 更新后，可能需要联网准备兼容资源。','Supports the modern taskbar. First use or a Windows update may require downloading compatibility resources.'],
 legacyHelp:['新版方案无法连接时，可尝试此方案。具体可识别项目取决于当前 Windows 环境。','Try this method if the modern method cannot connect. Supported controls depend on your Windows environment.'],
 reconnectTitle:['切换后会重新识别','Changing the method reconnects'],reconnectHelp:['保留已保存的系统图标隐藏选择，连接成功后重新应用。普通软件图标与规则不受影响。','Keep saved system hide preferences and apply them after connecting. Application icons and rules keep their preferences.'],
 saveMethod:['保存并重新识别','Save and reconnect'],cancel:['取消','Cancel'],close:['关闭','Close'],
 restoreTitle:['恢复全部系统图标？','Restore all system icons?'],restoreHelp:['恢复主任务栏的系统控件，并清除全部系统隐藏选择，包括尚未出现的项目。','Restore system controls on the main taskbar and clear all system hide preferences, including absent controls.'],restoreConfirm:['恢复并清除隐藏选择','Restore and clear preferences'],
 recognized:['项当前识别','recognized'],hidden:['项已隐藏','hidden'],waiting:['项等待出现后隐藏','waiting to hide'],configurable:['12 项可配置','12 configurable controls'],retained:['项隐藏选择已保留，连接成功后重新应用。','hide preferences retained; applied after connecting.']
};
const st=k=>(systemWords[k]||[k,k])[state.language==='en-US'?1:0];
const systemState=()=>state.system||{items:[],requested:0,found:0,hidden:0,connected:false};
const systemItem=bit=>systemState().items.find(i=>i.bit===bit);
const bitCount=value=>{let n=0;for(;value;value&=value-1)n++;return n};
function createSystemCard(item){
 const card=document.createElement('div');card.className='system-card';card.dataset.bit=item.bit;card.tabIndex=0;
 card.innerHTML='<div class="system-card-top"><span class="system-glyph"><img alt=""></span><strong></strong><button class="system-switch" role="switch"></button></div><p class="system-status"></p>';
 card.onclick=e=>{if(!e.target.closest('button'))chooseSystem(item.bit)};
 card.onkeydown=e=>{if(e.target!==card)return;if(['Enter',' '].includes(e.key)){e.preventDefault();chooseSystem(item.bit)}else if(['ArrowRight','ArrowLeft','ArrowDown','ArrowUp','Home','End'].includes(e.key)){e.preventDefault();const cards=[...$('#systemEntries').children].filter(c=>!c.hidden);const index=cards.indexOf(card),columns=getComputedStyle($('#systemEntries')).gridTemplateColumns.split(' ').length;const next=e.key==='Home'?0:e.key==='End'?cards.length-1:index+({ArrowRight:1,ArrowLeft:-1,ArrowDown:columns,ArrowUp:-columns}[e.key]);const target=cards[Math.max(0,Math.min(cards.length-1,next))];chooseSystem(Number(target.dataset.bit));target.focus()}};
 card.querySelector('button').onclick=()=>toggleSystem(item.bit);return card;
}
function patchSystemGlyph(node,item){const img=node.querySelector('img');setAttr(img,'src',item.image)}
function chooseSystem(bit){systemCurrent=bit;renderSystem();systemNodes.get(bit)?.focus({preventScroll:true})}
function filterSystem(value){systemFilter=value;renderSystem()}
function renderSystem(){
 const sys=systemState();if(!sys.items.length)return;
 $('#systemPage').querySelectorAll('[data-system-text]').forEach(n=>setText(n,st(n.dataset.systemText)));
 document.querySelectorAll('.system-dialog [data-system-text]').forEach(n=>setText(n,st(n.dataset.systemText)));
 document.querySelectorAll('.system-dialog .close-btn').forEach(n=>setAttr(n,'aria-label',st('close')));
 for(const theme of ['light','dark']){const b=$('#system-'+theme);setText(b,t(theme));b.classList.toggle('active',state.theme===theme);setAttr(b,'aria-pressed',String(state.theme===theme));setAttr(b,'aria-label',t(theme))}
 const connecting=sys.connecting||sys.working&&!sys.connected;
 $('#systemConnection').classList.toggle('loading',connecting);$('#systemConnection').classList.toggle('failed',!connecting&&!sys.connected);
 setHtml($('#systemConnectionIcon'),icon(connecting?'refresh':sys.connected?'check':'info'));
 setText($('#systemConnectionTitle'),st(connecting?'connecting':sys.connected?'connected':'failed'));
 setText($('#systemConnectionDescription'),st(connecting?'connectingHelp':sys.legacy?'legacy':'modern'));
 setText($('#systemReconnect'),st(sys.connected?'reconnect':'retry'));
 setText($('#systemSummary'),sys.connected?`${bitCount(sys.found)} ${st('recognized')} · ${bitCount(sys.hidden&sys.found)} ${st('hidden')} · ${bitCount(sys.requested&~sys.found)} ${st('waiting')} · ${st('configurable')}`:`${bitCount(sys.requested)} ${st('retained')}`);
 $('#systemPage').querySelectorAll('[data-system-filter]').forEach(n=>{n.classList.toggle('active',n.dataset.systemFilter===systemFilter);setAttr(n,'aria-pressed',String(n.dataset.systemFilter===systemFilter))});
 const container=$('#systemEntries');
 for(const item of [...sys.items].sort((a,b)=>({common:0,indicators:1,taskbar:2}[a.group]-{common:0,indicators:1,taskbar:2}[b.group])||a.bit-b.bit)){
  let card=systemNodes.get(item.bit);if(!card){card=createSystemCard(item);systemNodes.set(item.bit,card);container.append(card)}
  setProperty(card,'hidden',systemFilter!=='all'&&item.group!==systemFilter);card.classList.toggle('selected',item.bit===systemCurrent);
  setAttr(card,'aria-label',item.name+' · '+item.status);setText(card.querySelector('strong'),item.name);setAttr(card.querySelector('strong'),'title',item.name);patchSystemGlyph(card.querySelector('.system-glyph'),item);
  const button=card.querySelector('button');setAttr(button,'aria-checked',String(!(sys.requested&item.bit)));setAttr(button,'aria-label',st('show')+' '+item.name);
  const status=card.querySelector('p');setText(status,item.status);setAttr(status,'title',item.status);status.classList.toggle('is-hidden',item.state==='systemNativeHidden');status.classList.toggle('waiting',item.state==='systemNativeWaiting'||item.state==='systemSharedIndicator');
 }
 patchSystemDetails();updateSystemBusy();
}
function patchSystemDetails(){
 const item=systemItem(systemCurrent);if(!item)return;
 const sys=systemState(),body=$('#systemDetail'),identity=systemCurrent+'|'+state.language;
 if(identity!==systemDetailIdentity){systemDetailIdentity=identity;setHtml(body,'<div class="system-hero"><span class="system-glyph"><img alt=""></span><div><h3></h3><p></p></div></div><p class="system-explanation"></p><div class="system-infobox system-missing"><strong></strong><p></p></div><div class="system-infobox system-shared"><strong></strong><p></p></div><div class="system-infobox system-save"><strong></strong><p></p></div><p class="system-unavailable"></p><details class="system-technical"><summary></summary><pre></pre></details>')}
 patchSystemGlyph(body.querySelector('.system-glyph'),item);setText(body.querySelector('h3'),item.name);setText(body.querySelector('.system-hero p'),item.status);setText(body.querySelector('.system-explanation'),item.description);
 for(const [selector,title,help] of [['.system-missing','waitingTitle','waitingHelp'],['.system-shared','sharedTitle','sharedHelp'],['.system-save','saveTitle','saveHelp']]){const box=body.querySelector(selector);setText(box.querySelector('strong'),st(title));setText(box.querySelector('p'),st(help))}
 setProperty(body.querySelector('.system-missing'),'hidden',!sys.connected||!!(sys.found&item.bit));setProperty(body.querySelector('.system-shared'),'hidden',!(systemCurrent&48));
 setText(body.querySelector('.system-unavailable'),st('unavailable'));setProperty(body.querySelector('.system-unavailable'),'hidden',sys.connected);
 const technical=body.querySelector('.system-technical');setProperty(technical,'hidden',!sys.error&&!sys.diagnostic);setText(technical.querySelector('summary'),st('diagnostic'));setText(technical.querySelector('pre'),[sys.error,sys.diagnostic].filter(Boolean).join('\n'));
 setText($('#systemToggleSelected'),st(sys.requested&systemCurrent?'showAction':'hide'));
}
function updateSystemBusy(){
 const sys=systemState(),working=pending>0||state.busy||sys.working||sys.connecting;
 $('#systemPage').querySelectorAll('[data-system-command]').forEach(n=>setProperty(n,'disabled',working||n.hasAttribute('data-system-connected')&&!sys.connected||n.id==='systemUndo'&&!sys.undo));
 $('#systemPage').querySelectorAll('.system-switch').forEach(n=>setProperty(n,'disabled',working||!sys.connected));
 document.querySelectorAll('.system-dialog button,.system-dialog input').forEach(n=>setProperty(n,'disabled',working));
 setAttr($('#systemEntries'),'aria-busy',String(working));setProperty($('#systemUndo'),'hidden',!sys.undo);
 setText($('#systemBusyNote'),st(sys.working&&sys.connected||pendingAction==='systemToggle'||pendingAction==='systemRestore'||pendingAction==='systemUndo'?'saving':'connecting'));
 $('#systemBusyNote').classList.toggle('show',state.page==='system'&&(sys.working||pending>0&&pendingAction.startsWith('system')));
}
async function systemRequest(action,data={}){
 if(pending||state.busy||systemState().working||systemState().connecting)throw Error(t('busy'));
 const focus=document.activeElement,card=focus.closest('.system-card');if(card)card.focus({preventScroll:true});
 pending++;pendingAction=action;updateBusy();
 try{return await send(action,data)}finally{pending--;pendingAction='';updateBusy();if(focus.isConnected&&!focus.disabled&&(document.activeElement===card||document.activeElement===document.body))focus.focus({preventScroll:true})}
}
async function toggleSystem(bit=systemCurrent){systemCurrent=bit;renderSystem();try{await systemRequest('systemToggle',{bit,show:!!(systemState().requested&bit)});toast(st('saved'))}catch(e){toast(e.message,true)}}
async function reconnectSystem(){try{await systemRequest('systemReconnect');toast(st('connected'))}catch(e){toast(e.message,true)}}
async function undoSystem(){try{await systemRequest('systemUndo');toast(st('undone'))}catch(e){toast(e.message,true)}}
function openSystemDialog(id){if(pending||state.busy||systemState().working||systemState().connecting)return;const dialog=$('#'+id);systemDialogFocus.set(id,document.activeElement);setText(dialog.querySelector('.error'),'');if(id==='systemSchemeDialog')setProperty(dialog.querySelector('input[value="'+(systemState().legacy?'legacy':'modern')+'"]'),'checked',true);dialog.showModal();updateSystemBusy();dialog.querySelector('[data-system-cancel]').focus()}
function closeSystemDialog(id){if(!pending&&!state.busy&&!systemState().working)$('#'+id).close()}
for(const id of ['systemSchemeDialog','systemRestoreDialog']){
 const dialog=$('#'+id);dialog.addEventListener('cancel',e=>{if(pending||state.busy||systemState().working)e.preventDefault()});dialog.addEventListener('close',()=>{if(!dialog.open)systemDialogFocus.get(id)?.focus({preventScroll:true})});
 dialog.querySelector('form').onsubmit=async e=>{e.preventDefault();const scheme=id==='systemSchemeDialog';try{await systemRequest(scheme?'systemScheme':'systemRestore',scheme?{legacy:dialog.querySelector('input:checked').value==='legacy'}:{});dialog.close();toast(st(scheme?'connected':'restored'))}catch(error){const node=dialog.querySelector('.error');setText(node,error.message);node.focus()}};
}
renderSystem();
