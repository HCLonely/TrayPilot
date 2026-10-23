'use strict';
let programHotkeyPath=null;
const hotkeyWords=(zh,en)=>state.language==='en-US'?en:zh;
function programHotkeyValue(path){return Object.entries(state.programHotkeys||{}).find(([p])=>p.toLowerCase()===path.toLowerCase())?.[1]||0}
function programHotkeyLabel(path){return hotkeyWords('显示/隐藏快捷键','Show/hide shortcut')+' · '+(programHotkeyValue(path)?preferenceKeyText(programHotkeyValue(path)):hotkeyWords('未设置','Not set'))}
function shortcutKeyHtml(keys){return keys?preferenceKeyHtml({keys}):'<span class="shortcut-unassigned">'+hotkeyWords('未设置','Not set')+'</span>'}
function programHotkeyButtonHtml(path){return '<span class="shortcut-entry-icon">'+icon('keyboard')+'</span><span class="shortcut-entry-copy"><strong>'+hotkeyWords('显示 / 隐藏快捷键','Show / hide shortcut')+'</strong><span class="pref-keycombo">'+shortcutKeyHtml(programHotkeyValue(path))+'</span></span><span class="shortcut-entry-edit">'+icon('edit')+'</span>'}
function programHotkeyMenuHtml(path){return '<span>'+hotkeyWords('显示 / 隐藏快捷键','Show / hide shortcut')+'</span><span class="shortcut-menu-value">'+esc(programHotkeyValue(path)?preferenceKeyText(programHotkeyValue(path)):hotkeyWords('未设置','Not set'))+'</span>'}
function renderShortcutCapture(){
 const keys=preferenceHotkeyValue,empty=!(keys&65535);
 setHtml($('#prefCapturePreview'),empty?'<span class="shortcut-capture-empty">'+icon('keyboard')+hotkeyWords('按下组合键','Press a shortcut')+'</span>':shortcutKeyHtml(keys));
 setText($('.shortcut-record-hint'),hotkeyWords('点击此处，直接录制','Click here to record'));
 setProperty($('#prefCapture'),'value',empty?hotkeyWords('未设置','Not set'):preferenceKeyText(keys));
}
const programHotkeyClear=document.createElement('button');
programHotkeyClear.id='programHotkeyClear';programHotkeyClear.type='button';programHotkeyClear.className='btn';programHotkeyClear.hidden=true;
programHotkeyClear.onclick=()=>saveProgramHotkey(0);
$('#prefHotkeyDialog .dialogfooter').prepend(programHotkeyClear);
function openProgramHotkey(path,name){
 if(pending||state.busy||document.querySelector('dialog[open]'))return;
 programHotkeyPath=path;preferenceHotkeyValue=programHotkeyValue(path);
 $('#prefHotkeyTitle').removeAttribute('data-pref-text');
 setText($('#prefHotkeyTitle'),hotkeyWords('程序显示/隐藏快捷键','Program show/hide shortcut'));
 setText($('#prefHotkeyAction'),hotkeyWords('一组按键，切换整个程序的托盘图标。','One shortcut for all tray icons of this program.'));
 const identity=$('#programHotkeyIdentity'),app=apps.find(a=>a.path.toLowerCase()===path.toLowerCase())||ruleCatalog().find(a=>a.path.toLowerCase()===path.toLowerCase());
 identity.hidden=false;setText(identity.querySelector('strong'),name);setText(identity.querySelector('small'),path.split(/[\\/]/).pop());setAttr(identity,'title',path);
 if(app)patchIcon(identity.querySelector('.appicon'),app);else setHtml(identity.querySelector('.appicon'),icon('tray'));
 $('#programHotkeyNote').hidden=false;setText($('#programHotkeyNote p'),hotkeyWords('恢复图标后，隐藏规则会保留，本次运行期间暂时显示。','Restoring keeps your hide rules and shows the icons for this session.'));
 const codes=[...Array.from({length:26},(_,i)=>65+i),...Array.from({length:10},(_,i)=>48+i),...Array.from({length:11},(_,i)=>112+i)],saved=preferenceHotkeyValue&65535;
 if(!codes.includes(saved))codes.push(saved);
 setHtml($('#prefMainKey'),codes.map(k=>'<option value="'+k+'">'+esc(k?preferenceKeyName(k):hotkeyWords('请选择','Choose a key'))+'</option>').join(''));
 patchPreferenceHotkeyComposer();setProperty($('#prefCapture'),'value',preferenceHotkeyValue?preferenceKeyText(preferenceHotkeyValue):hotkeyWords('未设置','Not set'));
 setText(programHotkeyClear,hotkeyWords('清除绑定','Clear binding'));programHotkeyClear.hidden=!programHotkeyValue(path);
 openPreferencesDialog('prefHotkeyDialog');
}
async function saveProgramHotkey(keys){
 if(pending||state.busy||programHotkeyPath===null)return;
 if(keys!==0&&!validPreferenceKey(keys)){setText($('#prefHotkeyError'),pt('keyError'));return}
 try{await ruleRequest('programHotkey',{path:programHotkeyPath,keys});$('#prefHotkeyDialog').close();toast(hotkeyWords('程序快捷键已保存','Program shortcut saved'))}
 catch(error){setText($('#prefHotkeyError'),error.message);$('#prefHotkeyError').focus()}
}
$('#prefHotkeyDialog').addEventListener('close',()=>{if(!$('#prefHotkeyDialog').open){programHotkeyPath=null;programHotkeyClear.hidden=true;setAttr($('#prefHotkeyTitle'),'data-pref-text','editHotkey');setText($('#prefHotkeyTitle'),pt('editHotkey'))}});

let ruleMenuId=null,ruleMenuFocus=null;
const ruleMenu=document.createElement('div');ruleMenu.id='ruleMenu';ruleMenu.className='item-menu';ruleMenu.hidden=true;ruleMenu.setAttribute('role','menu');document.body.append(ruleMenu);
function closeRuleMenu(restore=false){if(!ruleMenu.hidden)ruleMenu.hidden=true;ruleMenuId=null;if(restore&&ruleMenuFocus?.isConnected)ruleMenuFocus.focus({preventScroll:true})}
function syncRuleMenu(){if(pending||state.busy||state.page!=='rules'||!ruleNodes.get(ruleMenuId)?.isConnected||!visibleRules().some(r=>r.id===ruleMenuId))closeRuleMenu()}
function openRuleMenu(id,event){
 event.preventDefault();closeRuleMenu();closeItemMenu();
 const rule=ruleCatalog().find(r=>r.id===id);
 if(!rule||pending||state.busy||state.page!=='rules'||document.querySelector('dialog[open]'))return;
 chooseRule(id);ruleMenuId=id;ruleMenuFocus=ruleNodes.get(id);ruleMenu.replaceChildren();ruleMenu.setAttribute('aria-label',rule.name);
 const actions=[['edit',rt('edit')],['temporary',rt('temporary'),!rule.liveCount],['hotkey',programHotkeyLabel(rule.path)],['copy',t('copy')],['delete',rt('delete')]];
 for(const [action,title,disabled] of actions){const button=document.createElement('button');button.type='button';button.dataset.action=action;button.setAttribute('role','menuitem');button.textContent=title;if(action==='hotkey')setHtml(button,programHotkeyMenuHtml(rule.path));button.disabled=!!disabled;button.onclick=()=>{
  closeRuleMenu(true);if(pending||state.busy)return;
  if(action==='edit')openRuleEditor(id);
  else if(action==='temporary')ruleAction('ruleTemporary',{id},rt('temporary'));
  else if(action==='hotkey')openProgramHotkey(rule.path,rule.name);
  else if(action==='copy')ruleAction('ruleCopyPath',{id},t('copied'));
  else if(action==='delete')openRuleDelete([id]);
 };ruleMenu.append(button)}
 ruleMenu.hidden=false;const bounds=ruleMenu.getBoundingClientRect(),row=ruleMenuFocus.getBoundingClientRect();
 ruleMenu.style.left=Math.max(8,Math.min(event.clientX||row.left,innerWidth-bounds.width-8))+'px';ruleMenu.style.top=Math.max(8,Math.min(event.clientY||row.bottom,innerHeight-bounds.height-8))+'px';ruleMenu.querySelector('button:not(:disabled)').focus({preventScroll:true});
}
ruleMenu.addEventListener('keydown',event=>{
 const buttons=[...ruleMenu.querySelectorAll('button:not(:disabled)')],index=buttons.indexOf(document.activeElement);
 if(event.key==='Escape'||event.key==='Tab'){closeRuleMenu(event.key==='Escape');if(event.key==='Escape')event.preventDefault();return}
 if(['ArrowDown','ArrowUp','Home','End'].includes(event.key)){event.preventDefault();buttons[event.key==='Home'?0:event.key==='End'?buttons.length-1:(index+(event.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length]?.focus()}
});
document.addEventListener('pointerdown',event=>{if(!ruleMenu.contains(event.target))closeRuleMenu()});
document.addEventListener('scroll',()=>closeRuleMenu(),true);window.addEventListener('resize',()=>closeRuleMenu());window.addEventListener('blur',()=>closeRuleMenu());
