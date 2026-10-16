// 原型与宿主共享语义资源键；状态切换只更新视图，保留扫描范围、选择与日志。
(() => {
  'use strict';
  const { icons, drives, items, groups } = window.CLEARC_DATA;
  document.body.insertAdjacentHTML('afterbegin', icons);
  const el = id => document.getElementById(id);
  const esc = value => String(value).replace(/[&<>"']/g, char => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[char]));
  const ico = id => `<svg class="icon"><use href="#${id}"/></svg>`;
  const paletteTokens = window.CLEARC_PALETTES;
  const saved = (() => { try { return JSON.parse(localStorage.getItem('clearc.prototype')) || {}; } catch { return {}; } })();
  const state = {
    culture: saved.culture === 'en-US' ? 'en-US' : 'zh-CN',
    mode: ['System','Light','Dark'].includes(saved.mode) ? saved.mode : 'System',
    palette: Object.hasOwn(paletteTokens, saved.palette) ? saved.palette : 'DotNet',
    phase:'idle', drives:['C'], scope:[], scanList:[], scanIndex:0, checked:new Set(), collapsed:new Set(),
    expanded:null, cleaned:{}, queue:[], cleanIndex:0, freed:0, lastFreed:0, baseline:{C:0,D:0}, logs:[], timers:[],
    stale:false, hideZero:true, fast:true, update:'idle'
  };
  const t = (key,...args) => {
    const full = key.startsWith('Prototype.') || key.startsWith('ClearC.') ? key : 'ClearC.'+key;
    const value = window.CLEARC_RESOURCES[state.culture][full];
    if (value == null) throw Error('Missing resource: '+state.culture+' '+full);
    return value.replace(/\{(\d+)(?::[^}]+)?\}/g, (_,index) => args[index] ?? '{'+index+'}');
  };
  const name = item => t(item.nameKey);
  const size = bytes => bytes >= 1024**3 ? (bytes/1024**3).toFixed(2)+' GB' : bytes >= 1024**2 ? (bytes/1024**2).toFixed(2)+' MB' : (bytes/1024).toFixed(2)+' KB';
  const itemOf = id => items.find(item => item.id === id);
  const total = ids => ids.reduce((sum,id) => sum + itemOf(id).size,0);
  const selected = () => [...state.checked].map(itemOf);
  const busy = () => ['scanning','cleaning'].includes(state.phase);
  const defaultChecked = item => item.cleanable && item.drive === 'C' && item.risk === 'low';
  const later = (callback,delay) => state.timers.push(setTimeout(callback,delay));
  const stopTimers = () => { state.timers.forEach(clearTimeout); state.timers=[]; };
  const log = (level,key,...args) => { state.logs.push({level,key,args,time:new Date().toLocaleTimeString('en-GB')}); renderLogs(); };
  const toast = (key,...args) => { el('toast').textContent=t(key,...args);el('toast').hidden=false;setTimeout(()=>el('toast').hidden=true,2400); };
  function persist() { localStorage.setItem('clearc.prototype',JSON.stringify({culture:state.culture,mode:state.mode,palette:state.palette})); }
  function appearance() {
    const dark=state.mode==='Dark'||(state.mode==='System'&&matchMedia('(prefers-color-scheme: dark)').matches);
    const token=paletteTokens[state.palette][dark?'Dark':'Light'];
    for(const [variable,key]of Object.entries({'--win':'StudioBackgroundColor','--bg':'StudioSurfaceColor','--panel':'StudioSurfaceColor','--raised':'StudioRaisedColor','--line':'StudioBorderColor','--line2':'StudioBorderColor','--text':'StudioTextColor','--text2':'StudioMutedColor','--text3':'StudioMutedColor','--acc':'StudioAccentColor','--acc2':'StudioSecondaryColor','--primary':'StudioPrimaryColor','--on-primary':'StudioOnPrimary'}))document.documentElement.style.setProperty(variable,token[key]);
    document.documentElement.style.setProperty('--grad',`linear-gradient(135deg,${token.StudioGradientStartColor},${token.StudioSecondaryColor})`);
    document.body.dataset.theme=dark?'dark':'light';document.documentElement.lang=state.culture;
    const stops=document.querySelectorAll('#dg stop');stops[0].setAttribute('stop-color',token.StudioGradientStartColor);stops[1].setAttribute('stop-color',token.StudioSecondaryColor);
    el('themeMode').textContent=({System:'▣',Light:'☀',Dark:'☾'})[state.mode];el('themeMode').dataset.mode=state.mode;el('themeMode').title=t('Appearance.Mode')+': '+t('Appearance.'+state.mode);el('themeMode').setAttribute('aria-label',el('themeMode').title);
    el('languageButton').title=t('Appearance.Language');el('languageButton').setAttribute('aria-label',el('languageButton').title);
    document.querySelectorAll('[data-culture]').forEach(button=>{button.textContent=t('Appearance.'+(button.dataset.culture==='zh-CN'?'Chinese':'English'));button.setAttribute('aria-checked',button.dataset.culture===state.culture);});
    for(const button of document.querySelectorAll('[data-palette-key]')) { button.classList.toggle('on',button.dataset.paletteKey===state.palette);button.title=t('Appearance.'+button.dataset.paletteKey);button.setAttribute('aria-pressed',button.dataset.paletteKey===state.palette); }
    document.querySelectorAll('[data-i18n]').forEach(node=>node.textContent=t(node.dataset.i18n));
    document.querySelectorAll('[data-title]').forEach(node=>node.title=t(node.dataset.title));
  }
  function renderHero() {
    const scope=drives.filter(drive=>state.drives.includes(drive.letter));
    const capacity=scope.reduce((sum,drive)=>sum+drive.total,0);
    const free=scope.reduce((sum,drive)=>sum+drive.free+state.baseline[drive.letter],0)+state.freed;
    const ratio=capacity?(capacity-free)/capacity:0;
    el('donutFill').style.strokeDasharray=2*Math.PI*56;el('donutFill').style.strokeDashoffset=2*Math.PI*56*(1-ratio);
    el('donutPct').textContent=Math.round(ratio*100)+'%';
    el('hiTitle').textContent=scope.map(drive=>drive.letter+': '+t(drive.typeKey)).join(' · ')||t('Workspace.NoSelection');
    el('hiSub').textContent=scope.length?size(capacity)+' · NTFS':'';
    el('diskUsed').textContent=size(capacity-free);el('diskFree').textContent=size(free);
    el('heroLabel').textContent=t(state.phase==='cleaning'?'Main.HeroReleased':state.phase==='done'?'Main.HeroReleasedThisRun':'Main.HeroAvailable');
    el('heroStat').textContent=state.phase==='idle'?t('Main.HeroPending'):state.phase==='scanning'?t('Main.HeroAnalyzing'):size(state.phase==='cleaning'?state.freed:state.phase==='done'?state.lastFreed:total([...state.checked]));
    el('heroStat').classList.toggle('placeholder',['idle','scanning'].includes(state.phase));
    const main=el('btnMain'),second=el('btnSecond');
    const mainKey={idle:'Main.Scan',scanning:'Main.Scanning',results:'Main.Clean',cleaning:'Main.Cleaning',done:'Main.Clean'}[state.phase];
    main.innerHTML=ico(['results','cleaning','done'].includes(state.phase)?'i-clean':'i-search')+'<span>'+esc(state.phase==='results'&&state.checked.size?t('Main.CleanWithSize',size(total([...state.checked]))):t(mainKey))+'</span>';
    main.disabled=busy()||state.phase==='done'||!state.drives.length||(state.phase==='results'&&(!state.checked.size||state.stale));
    main.onclick=()=>state.phase==='idle'?startScan():openConfirm();
    second.textContent=t(busy()?'Main.Cancel':state.phase==='idle'?'Main.Clean':'Main.Reanalyze');second.disabled=state.phase==='idle';second.onclick=()=>busy()?cancel():startScan();
    el('progressWrap').hidden=!busy();el('pbarFill').style.width=(state.phase==='scanning'?state.scanIndex/Math.max(1,state.scanList.length):state.cleanIndex/Math.max(1,state.queue.length))*100+'%';
    const current=state.phase==='scanning'?state.scanList[state.scanIndex]:itemOf(state.queue[state.cleanIndex]);
    el('progressText').textContent=current?(state.phase==='scanning'?current.path:name(current)):'';
  }
  function renderDrives() {
    el('dbHint').textContent=t({idle:'Main.ScopeIdle',scanning:'Main.ScopeScanning',results:state.stale?'Main.ScopeResultsStale':'Main.ScopeResults',cleaning:'Main.ScopeCleaning',done:'Main.ScopeOther'}[state.phase],state.scope.length);
    el('dbCards').innerHTML=drives.map(drive=>`<label class="dcard${state.drives.includes(drive.letter)?' on':''}${busy()?' locked':''}" data-drive="${drive.letter}"><input type="checkbox" ${state.drives.includes(drive.letter)?'checked':''} ${busy()?'disabled':''}><span class="box">${ico('i-check')}</span><span class="dc-letter">${drive.letter}:</span><span class="dc-info"><span class="dc-line1"><span class="dc-type${drive.letter==='C'?'':' data'}">${t(drive.typeKey)}</span></span><span class="dc-bar"><i style="width:${(drive.total-drive.free)/drive.total*100}%"></i></span><span class="dc-usage">${t('Workspace.Used')} ${size(drive.total-drive.free)} / ${size(drive.total)} · ${Math.round((drive.total-drive.free)/drive.total*100)}%</span></span></label>`).join('');
    el('dbCards').querySelectorAll('input').forEach(input=>input.onchange=()=>{const letter=input.closest('[data-drive]').dataset.drive;state.drives=input.checked?[...state.drives,letter]:state.drives.filter(value=>value!==letter);if(state.phase==='results')state.stale=true;render();});
  }
  function row(item) {
    const done=Object.hasOwn(state.cleaned,item.id),partial=done&&state.cleaned[item.id]!==item.size;
    const current=state.phase==='cleaning'&&state.queue[state.cleanIndex]===item.id;
    return `<div class="row${done?' done':''}${current?' current':''}${state.expanded===item.id?' expanded':''}" data-id="${item.id}"><div class="row-main"><label class="chk"><input type="checkbox" ${state.checked.has(item.id)&&!done?'checked':''} ${state.phase!=='results'||!item.cleanable||done?'disabled':''}><span class="box">${ico('i-check')}</span></label><span class="tile" style="color:${item.color};background:${item.color}1f;border-color:${item.color}55">${ico(item.icon)}</span><div class="row-info"><div class="row-name">${esc(name(item))}${item.cleanable?'':` <span class="tag tag-nc">${t('Item.CannotClean')}</span>`}</div><div class="row-path" title="${esc(item.path)}">${esc(item.path)}</div></div><span class="tag-risk risk-${item.risk}">${t('Item.'+({low:'LowRisk',mid:'MediumRisk',high:'HighRisk'})[item.risk])}</span><span class="row-meta">${t('Item.Files',item.files.toLocaleString('en-US'))}</span><span class="row-size">${done?`<s>${size(item.size)}</s><span class="freed">${t('Prototype.Freed',size(state.cleaned[item.id]))}</span>`:size(item.size)}</span><span class="row-status">${done?(partial?t('Prototype.Partial'):t('Item.ResultCompleted')):''}</span><button class="row-open" data-open title="${t('Item.OpenDirectory')}">${ico('i-folder')}</button>${ico('i-chev')}</div><div class="row-detail"><div><span class="d-label">${t('Item.Description')}</span>${esc(t(item.descriptionKey))}</div><div><span class="d-label">${t('Item.Path')}</span><span class="d-path">${esc(item.path)}</span><button class="mini mini-open" data-open>${t('Item.OpenLocation')}</button></div><div><span class="d-label">${t('Item.Recommendation')}</span>${t(!item.cleanable?'Item.RecommendationSystem':item.risk==='low'?'Item.RecommendationLow':'Item.RecommendationRisk')}</div></div></div>`;
  }
  function renderList() {
    const list=el('list'),scroll=list.scrollTop;
    if(state.phase==='idle'){list.innerHTML=`<div class="empty"><div class="empty-inner"><div class="empty-icon">${ico('i-folder')}</div><div class="empty-title">${t('Workspace.Pending')}</div><div class="empty-sub">${t('Workspace.EmptyDriveHint')}</div></div></div>`;return;}
    const revealed=state.phase==='scanning'?state.scanList.slice(0,state.scanIndex):state.scanList;
    list.innerHTML=drives.filter(drive=>state.scope.includes(drive.letter)).map(drive=>{
      const result=revealed.filter(item=>item.drive===drive.letter&&(!state.hideZero||item.size>0));
      const next=state.phase==='scanning'?state.scanList[state.scanIndex]:null;
      const body=groups.map(group=>{const members=result.filter(item=>item.cat===group.key);if(!members.length)return '';const key=drive.letter+':'+group.key;return `<div class="group${state.collapsed.has(key)?' collapsed':''}" data-key="${key}"><div class="g-head">${ico('i-chev')}<span class="g-title">${t(group.labelKey)}</span><span class="g-count">${t('Group.Count',members.length)}</span><span class="g-size">${size(members.reduce((sum,item)=>sum+item.size,0))}</span></div><div class="g-body">${members.map(row).join('')}</div></div>`;}).join('');
      const active=next?.drive===drive.letter?`<div class="row ghost"><div class="row-main"><span class="tile">${ico('i-search')}</span><span class="ghost-txt">${t('Drive.Scanning')} ${esc(next.path)}</span></div></div>`:'';
      return `<div class="drive-sec"><div class="ds-head"><span class="ds-letter">${drive.letter}</span><span class="ds-title">${drive.letter}: ${t(drive.typeKey)}</span><span class="ds-policy${drive.letter==='C'?' ok':''}">${t(drive.letter==='C'?'Prototype.PolicyAuto':'Prototype.PolicyManual')}</span><span class="ds-sel">${t('Main.SelectedDriveSummary',selected().filter(item=>item.drive===drive.letter).length,size(selected().filter(item=>item.drive===drive.letter).reduce((sum,item)=>sum+item.size,0)))}</span><span class="ds-sum">${t('Group.Count',result.length)}</span></div><div class="ds-body">${body+active}</div></div>`;
    }).join('');
    list.scrollTop=scroll;
    list.querySelectorAll('.g-head').forEach(header=>header.onclick=()=>{const key=header.parentElement.dataset.key;const opening=state.collapsed.has(key);groups.forEach(group=>state.scope.forEach(drive=>state.collapsed.add(drive+':'+group.key)));if(opening)state.collapsed.delete(key);renderList();});
    list.querySelectorAll('.row[data-id]').forEach(node=>{
      const item=itemOf(node.dataset.id);
      node.querySelector('input').onchange=event=>{event.target.checked?state.checked.add(item.id):state.checked.delete(item.id);render();};
      node.querySelector('.row-main').onclick=event=>{if(event.target.closest('label,button'))return;state.expanded=state.expanded===item.id?null:item.id;renderList();};
      node.querySelectorAll('[data-open]').forEach(button=>button.onclick=()=>toast('Prototype.Opened'));
      node.oncontextmenu=event=>{event.preventDefault();openContext(event,item);};
    });
  }
  function renderBar() {
    const check=el('checkAll');check.disabled=state.phase!=='results';check.checked=state.checked.size>0&&state.checked.size===state.scanList.filter(item=>item.cleanable&&!Object.hasOwn(state.cleaned,item.id)).length;
    el('selInfo').textContent=state.checked.size?t('Main.SelectedSummary',state.checked.size,size(total([...state.checked]))):t('Main.NoSelection');
    el('sbText').textContent=t({idle:'Main.StatusReady',scanning:'Main.StatusScanning',results:'Main.StatusComplete',cleaning:'Main.StatusCleaning',done:'Main.StatusDone'}[state.phase],state.scope.map(letter=>letter+':').join(' · '));
    el('sbDot').className='dot'+(busy()?' run':'');el('stateCode').textContent='CLEARC · v__APP_VERSION__ · '+state.phase.toUpperCase();
  }
  function renderLogs() { el('logCount').textContent=state.logs.length;el('logBody').innerHTML=state.logs.map(entry=>`<div class="log-line"><span class="log-time">${entry.time}</span><span class="log-lvl lvl-${entry.level}">${entry.level}</span>${esc(t(entry.key,...entry.args))}</div>`).join('')+'<div class="cursor-line"></div>';el('logBody').scrollTop=el('logBody').scrollHeight; }
  function render() { document.body.dataset.state=state.phase;appearance();renderHero();renderDrives();renderBar();renderList();renderLogs();renderUpdate();if(!el('mask').hidden&&el('mask').dataset.kind==='confirm')openConfirm(false); }
  function startScan() {
    if(!state.drives.length){toast('Prototype.SelectDrive');return;}
    stopTimers();closeModal();state.scope=[...state.drives];state.scanList=items.filter(item=>state.scope.includes(item.drive));state.scanIndex=0;state.checked.clear();state.cleaned={};state.freed=0;state.stale=false;state.phase='scanning';render();log('INFO','Prototype.LogScanStart',state.scope.join(' · '));later(scanTick,350);
  }
  function scanTick() { if(state.scanIndex===state.scanList.length){finishScan();return;}const item=state.scanList[state.scanIndex++];log('INFO','Prototype.LogScanItem',item.path);render();later(scanTick,160); }
  function finishScan() { stopTimers();state.scanIndex=state.scanList.length;state.scanList.filter(defaultChecked).forEach(item=>state.checked.add(item.id));groups.forEach(group=>state.scope.forEach(drive=>state.collapsed.add(drive+':'+group.key)));state.phase='results';log('OK','Prototype.LogScanDone',state.scanList.reduce((sum,item)=>sum+item.files,0).toLocaleString('en-US'));render(); }
  function cancel() { stopTimers();if(state.phase==='scanning'){state.phase='idle';state.checked.clear();log('WARN','Prototype.LogScanCanceled');}else{state.lastFreed=state.freed;for(const [id,freed]of Object.entries(state.cleaned))state.baseline[itemOf(id).drive]+=freed;state.freed=0;state.checked.clear();state.phase='results';log('WARN','Prototype.LogCleanCanceled',size(state.lastFreed));}render(); }
  function closeModal() { el('mask').hidden=true; }
  function modal(title,body,actions,kind) { el('mask').dataset.kind=kind;el('mTitle').textContent=t(title);el('mBody').innerHTML=body;el('mActions').replaceChildren(...actions.map(([key,callback,primary])=>{const button=document.createElement('button');button.className='btn '+(primary?'primary':'ghost');button.textContent=t(key);button.onclick=callback;return button;}));el('mask').hidden=false; }
  function openConfirm(writeLog=true) {
    if(!state.checked.size)return;
    modal('Workflow.ConfirmClean',`<p>${t('Workflow.AboutToClean')} <b>${state.checked.size}</b>${t('Workflow.Projects')}</p><div class="m-list">${selected().map(item=>`<div><span>${esc(name(item))}</span><b>${size(item.size)}</b></div>`).join('')}</div><div class="m-total"><span>${t('Workflow.TotalFreed')}</span><span>${size(total([...state.checked]))}</span></div><div class="m-warn">${ico('i-alert')}<span>${t(selected().some(item=>item.risk!=='low')?'Main.ConfirmationRisk':'Main.ConfirmationNormal')}</span></div>`,[['Main.Cancel',closeModal],['Workflow.StartClean',()=>{closeModal();startClean();},true]],'confirm');
    if(writeLog)log('INFO','Prototype.LogConfirm');
  }
  function startClean() { stopTimers();state.phase='cleaning';state.queue=[...state.checked];state.cleanIndex=0;state.freed=0;log('INFO','Prototype.LogCleanStart',state.queue.length,size(total(state.queue)));render();later(cleanTick,520); }
  function cleanTick() { if(state.cleanIndex===state.queue.length){finishClean();return;}const item=itemOf(state.queue[state.cleanIndex++]);const freed=item.id==='d-build'?5.71e9:item.size;state.cleaned[item.id]=freed;state.freed+=freed;log('OK','Prototype.LogCleanItem',name(item));if(freed!==item.size)log('WARN','Prototype.LogBusyFiles');render();later(cleanTick,460); }
  function finishClean() { stopTimers();state.lastFreed=state.freed;for(const [id,freed]of Object.entries(state.cleaned))state.baseline[itemOf(id).drive]+=freed;state.freed=0;state.phase='done';state.checked.clear();log('OK','Prototype.LogCleanDone',size(state.lastFreed));render();toast('Prototype.LogCleanDone',size(state.lastFreed)); }
  function openContext(event,item) { const menu=el('ctxMenu');menu.replaceChildren();for(const [key,action]of [['Item.OpenDirectory',()=>toast('Prototype.Opened')],['Item.CopyPath',()=>{navigator.clipboard?.writeText(item.path).catch(()=>{});toast('Prototype.Copied');}],['Item.Select',()=>{if(state.phase==='results'&&item.cleanable){state.checked.has(item.id)?state.checked.delete(item.id):state.checked.add(item.id);render();}}],['Item.Expand',()=>{state.expanded=item.id;renderList();}]]){const button=document.createElement('button');button.className='ctx-item';button.textContent=t(key);button.onclick=()=>{action();menu.hidden=true;};menu.append(button);}menu.hidden=false;menu.style.left=Math.min(event.clientX,innerWidth-230)+'px';menu.style.top=Math.min(event.clientY,innerHeight-180)+'px'; }
  function renderUpdate() {
    const statuses={idle:'',checking:'About.Checking',available:'About.Available',downloading:'About.DownloadProgress',downloaded:'About.Downloaded',installed:'Prototype.UpdateInstalled',canceled:'About.Canceled',failed:'About.Failed'};
    el('updateStatus').textContent=statuses[state.update]?t(statuses[state.update],state.update==='failed'?t('Prototype.UpdateError'):state.update==='downloading'?'52%':'__UPDATE_VERSION__'):'';
    el('updateAction').textContent=t(state.update==='available'?'Prototype.UpdateDownload':state.update==='downloaded'?'Prototype.UpdateInstall':state.update==='checking'?'About.Checking':state.update==='downloading'?'About.Downloading':'Update.Check');
    el('updateCancel').hidden=!['checking','downloading'].includes(state.update);el('updateProgress').hidden=state.update!=='downloading';
  }
  function reset() { stopTimers();Object.assign(state,{phase:'idle',drives:['C'],scope:[],scanList:[],scanIndex:0,checked:new Set(),collapsed:new Set(),cleaned:{},freed:0,lastFreed:0,baseline:{C:0,D:0},logs:[],expanded:null});render();log('OK','Prototype.LogInit');log('INFO','Prototype.LogDisks',drives.length);log('INFO','Main.ScopeIdle'); }
  el('themeMode').onclick=()=>{const modes=['System','Light','Dark'];state.mode=modes[(modes.indexOf(state.mode)+1)%3];persist();render();};
  el('languageButton').onclick=event=>{event.stopPropagation();const open=el('languageMenu').hidden;el('languageMenu').hidden=!open;el('languageButton').setAttribute('aria-expanded',open);};
  document.querySelectorAll('[data-culture]').forEach(button=>button.onclick=()=>{state.culture=button.dataset.culture;el('languageMenu').hidden=true;el('languageButton').setAttribute('aria-expanded','false');persist();render();});
  document.addEventListener('click',event=>{if(!event.target.closest('.language-picker')){el('languageMenu').hidden=true;el('languageButton').setAttribute('aria-expanded','false');}});
  document.addEventListener('keydown',event=>{if(event.key==='Escape'){el('languageMenu').hidden=true;el('languageButton').setAttribute('aria-expanded','false');el('languageButton').focus();}});
  for(const palette of Object.keys(paletteTokens)){const button=document.createElement('button');button.className='palette-dot';button.dataset.paletteKey=palette;button.style.setProperty('--swatch',paletteTokens[palette].Light.StudioAccentColor);button.onclick=()=>{state.palette=palette;persist();render();};el('paletteDots').append(button);}
  el('checkAll').onchange=event=>{state.checked.clear();if(event.target.checked)state.scanList.filter(item=>item.cleanable&&!Object.hasOwn(state.cleaned,item.id)).forEach(item=>state.checked.add(item.id));render();};
  el('hideZero').onchange=event=>{state.hideZero=event.target.checked;renderList();};el('fastMode').onchange=event=>state.fast=event.target.checked;
  el('btnClearLog').onclick=()=>{state.logs=[];renderLogs();};el('btnCopyLog').onclick=()=>{navigator.clipboard?.writeText(el('logBody').innerText).catch(()=>{});toast('Prototype.LogCopied');};
  el('btnExportLog').onclick=()=>{const url=URL.createObjectURL(new Blob([el('logBody').innerText],{type:'text/plain;charset=utf-8'}));const link=document.createElement('a');link.href=url;link.download='ClearC.log';link.click();setTimeout(()=>URL.revokeObjectURL(url),500);toast('Prototype.Exported');};
  el('btnAdmin').onclick=()=>toast('Prototype.Opened');el('btnAbout').onclick=()=>el('about').showModal();el('aboutClose').onclick=()=>el('about').close();
  el('aboutMinimize').onclick=()=>el('about').close();el('aboutMaximize').onclick=()=>el('about').classList.toggle('about-maximized');
  el('updateAction').onclick=()=>{state.update=({idle:'checking',checking:'available',available:'downloading',downloading:'downloaded',downloaded:'installed',installed:'checking',canceled:'checking',failed:'checking'})[state.update];renderUpdate();};el('updateCancel').onclick=()=>{state.update='canceled';renderUpdate();};
  el('btnMin').onclick=()=>{el('appWindow').hidden=true;el('taskbar').hidden=false;};el('btnRestore').onclick=()=>{el('appWindow').hidden=false;el('taskbar').hidden=true;};
  el('btnMax').onclick=()=>el('appWindow').classList.toggle('maximized');el('titlebar').ondblclick=event=>{if(!event.target.closest('button,select'))el('btnMax').click();};
  function exit(){stopTimers();closeModal();el('ctxMenu').hidden=true;el('appWindow').hidden=true;el('relaunch').hidden=false;}
  el('btnClose').onclick=()=>busy()?modal('Workflow.ConfirmClose',`<p>${t('Prototype.ExitWarning')}</p>`,[['Main.Cancel',closeModal],['Workflow.StopAndExit',exit,true]],'exit'):exit();el('btnRelaunch').onclick=()=>{el('relaunch').hidden=true;el('appWindow').hidden=false;reset();};
  document.addEventListener('click',event=>{if(!event.target.closest('#ctxMenu'))el('ctxMenu').hidden=true;});document.addEventListener('keydown',event=>{if(event.key==='Escape'){closeModal();el('ctxMenu').hidden=true;}});
  let drag=null;el('titlebar').onpointerdown=event=>{if(event.target.closest('button,select,.palette-dots')||el('appWindow').classList.contains('maximized'))return;const rect=el('appWindow').getBoundingClientRect();drag={x:event.clientX-rect.x,y:event.clientY-rect.y};};document.addEventListener('pointermove',event=>{if(!drag)return;const win=el('appWindow');win.style.position='fixed';win.style.left=Math.max(0,event.clientX-drag.x)+'px';win.style.top=Math.max(0,event.clientY-drag.y)+'px';});document.addEventListener('pointerup',()=>drag=null);
  matchMedia('(prefers-color-scheme: dark)').addEventListener('change',()=>render());
  window.ClearCPrototype={set(change){for(const key of ['culture','mode','palette'])if(change[key])state[key]=change[key];persist();render();},getState:()=>({culture:state.culture,mode:state.mode,palette:state.palette,phase:state.phase,checked:[...state.checked]}),openAbout:()=>el('about').showModal(),setUpdate(value){state.update=value;renderUpdate();}};
  reset();
})();
