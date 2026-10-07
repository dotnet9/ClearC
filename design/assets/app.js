/* ══════════════════════════════════════════════════════════════
   ClearC 交互原型 — 单窗口状态机控制器
   六个状态（待机 → 扫描 → 结果 → 确认 → 清理 → 完成）全部由
   界面交互推进，不再使用独立状态页。本文件即真实实现的交互基准：
   - 扫描：逐项出现 + 幽灵行 + 进度条 + 日志流
   - 结果：勾选联动（默认勾选策略）、右键菜单、打开所在目录
   - 清理：逐项脉冲高亮 → 置灰释放，可取消、可部分清理
   - 窗口：拖动 / 最小化（任务栏恢复）/ 最大化 / 关闭（重启）
   ══════════════════════════════════════════════════════════════ */
(function () {
  'use strict';

  /* ─────────── 基础工具 ─────────── */
  const $ = (s, el) => (el || document).querySelector(s);
  const $$ = (s, el) => Array.from((el || document).querySelectorAll(s));
  const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  const ico = (id, cls) => '<svg class="icon' + (cls ? ' ' + cls : '') + '"><use href="#' + id + '"/></svg>';
  const pad = (n) => String(n).padStart(2, '0');
  const now = () => { const d = new Date(); return pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds()); };
  const GB = 1e9;
  const fmtSize = (b) => b >= GB ? (b / GB).toFixed(2) + ' GB' : b >= 1e6 ? (b / 1e6).toFixed(1) + ' MB' : Math.round(b / 1e3) + ' KB';
  const fmtGB = (b) => (b / GB).toFixed(1) + ' GB';
  const fmtInt = (n) => Number(n).toLocaleString('en-US');

  /* ─────────── SVG 图标库注入 ─────────── */
  const ICONS = `
  <svg xmlns="http://www.w3.org/2000/svg" style="display:none"><defs>
    <symbol id="i-logo" viewBox="0 0 24 24"><g fill="currentColor" stroke="none"><rect x="5.1" y="16.9" width="13.1" height="2.3" rx="1.15"/><rect x="5.1" y="13.4" width="13.1" height="2.3" rx="1.15"/><rect x="6.6" y="7.9" width="11.8" height="2.3" rx="1.15" transform="rotate(-12 12.5 9)"/><path d="M19.9 4.8Q20.2 5.9 21.3 6.2Q20.2 6.5 19.9 7.6Q19.6 6.5 18.5 6.2Q19.6 5.9 19.9 4.8Z"/></g></symbol>
    <symbol id="i-clean" viewBox="0 0 24 24"><path d="M11 3l1.7 4.6 4.6 1.7-4.6 1.7L11 15.6l-1.7-4.6L4.7 9.3l4.6-1.7z"/><path d="M18.5 14.5l.8 2.2 2.2.8-2.2.8-.8 2.2-.8-2.2-2.2-.8 2.2-.8z"/></symbol>
    <symbol id="i-min" viewBox="0 0 24 24"><path d="M5.5 12h13"/></symbol>
    <symbol id="i-max" viewBox="0 0 24 24"><path d="M7.5 6h9A1.5 1.5 0 0 1 18 7.5v9a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 6 16.5v-9A1.5 1.5 0 0 1 7.5 6z"/></symbol>
    <symbol id="i-restore" viewBox="0 0 24 24"><path d="M8.5 8.5V6.2A1.7 1.7 0 0 1 10.2 4.5h7.6a1.7 1.7 0 0 1 1.7 1.7v7.6a1.7 1.7 0 0 1-1.7 1.7h-2.3"/><path d="M4.5 10.2v7.6a1.7 1.7 0 0 0 1.7 1.7h7.6a1.7 1.7 0 0 0 1.7-1.7v-7.6a1.7 1.7 0 0 0-1.7-1.7H6.2a1.7 1.7 0 0 0-1.7 1.7z"/></symbol>
    <symbol id="i-close" viewBox="0 0 24 24"><path d="M6.5 6.5l11 11M17.5 6.5l-11 11"/></symbol>
    <symbol id="i-moon" viewBox="0 0 24 24"><path d="M20 13.7A8.2 8.2 0 1 1 10.3 4 6.6 6.6 0 0 0 20 13.7z"/></symbol>
    <symbol id="i-search" viewBox="0 0 24 24"><circle cx="11" cy="11" r="6.5"/><path d="M16 16l4.5 4.5"/></symbol>
    <symbol id="i-file" viewBox="0 0 24 24"><path d="M13.5 3.5H7A1.5 1.5 0 0 0 5.5 5v14A1.5 1.5 0 0 0 7 20.5h10a1.5 1.5 0 0 0 1.5-1.5V8.5z"/><path d="M13.5 3.5v5h5"/></symbol>
    <symbol id="i-doc" viewBox="0 0 24 24"><path d="M13.5 3.5H7A1.5 1.5 0 0 0 5.5 5v14A1.5 1.5 0 0 0 7 20.5h10a1.5 1.5 0 0 0 1.5-1.5V8.5z"/><path d="M13.5 3.5v5h5M9 13h6M9 16h6"/></symbol>
    <symbol id="i-trash" viewBox="0 0 24 24"><path d="M4.5 7h15M9.5 7V5A1.5 1.5 0 0 1 11 3.5h2A1.5 1.5 0 0 1 14.5 5v2"/><path d="M6.5 7l.9 12.1a1.5 1.5 0 0 0 1.5 1.4h6.2a1.5 1.5 0 0 0 1.5-1.4L17.5 7"/></symbol>
    <symbol id="i-refresh" viewBox="0 0 24 24"><path d="M19.5 12a7.5 7.5 0 0 1-13.2 4.9M4.5 12a7.5 7.5 0 0 1 13.2-4.9"/><path d="M6.5 17.5v-2.8h2.8M17.5 6.5v2.8h-2.8"/></symbol>
    <symbol id="i-globe" viewBox="0 0 24 24"><circle cx="12" cy="12" r="8.2"/><path d="M3.8 12h16.4M12 3.8c2.6 2.2 3.9 5 3.9 8.2s-1.3 6-3.9 8.2c-2.6-2.2-3.9-5-3.9-8.2s1.3-6 3.9-8.2z"/></symbol>
    <symbol id="i-image" viewBox="0 0 24 24"><path d="M5.5 5.5h13A1.5 1.5 0 0 1 20 7v10a1.5 1.5 0 0 1-1.5 1.5h-13A1.5 1.5 0 0 1 4 17V7a1.5 1.5 0 0 1 1.5-1.5z"/><circle cx="8.7" cy="9.7" r="1.4"/><path d="M20 15.5l-4.6-4.6-7.9 7.9"/></symbol>
    <symbol id="i-bolt" viewBox="0 0 24 24"><path d="M13.2 3.2L5.8 13.4h5.4l-1.4 7.4 7.4-10.2h-5.4z"/></symbol>
    <symbol id="i-alert" viewBox="0 0 24 24"><path d="M12 4.2L3.2 19h17.6zM12 10.2v4M12 16.8h.01"/></symbol>
    <symbol id="i-window" viewBox="0 0 24 24"><path d="M6 5.5h12A1.5 1.5 0 0 1 19.5 7v10a1.5 1.5 0 0 1-1.5 1.5H6A1.5 1.5 0 0 1 4.5 17V7A1.5 1.5 0 0 1 6 5.5zM4.5 9h15"/></symbol>
    <symbol id="i-db" viewBox="0 0 24 24"><path d="M19 6c0-1.4-3.1-2.5-7-2.5S5 4.6 5 6s3.1 2.5 7 2.5S19 7.4 19 6z"/><path d="M5 6v12c0 1.4 3.1 2.5 7 2.5s7-1.1 7-2.5V6M5 12c0 1.4 3.1 2.5 7 2.5s7-1.1 7-2.5"/></symbol>
    <symbol id="i-clock" viewBox="0 0 24 24"><circle cx="12" cy="12" r="8.2"/><path d="M12 7.5V12l3 2"/></symbol>
    <symbol id="i-download" viewBox="0 0 24 24"><path d="M12 4.5v10M8 10.5l4 4 4-4M5 19.5h14"/></symbol>
    <symbol id="i-chat" viewBox="0 0 24 24"><path d="M4.5 6.5a2 2 0 0 1 2-2h11a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2H9l-4.5 3.5z"/></symbol>
    <symbol id="i-chip" viewBox="0 0 24 24"><path d="M9.5 8h5A1.5 1.5 0 0 1 16 9.5v5a1.5 1.5 0 0 1-1.5 1.5h-5A1.5 1.5 0 0 1 8 14.5v-5A1.5 1.5 0 0 1 9.5 8z"/><path d="M9.5 4.8V8M14.5 4.8V8M9.5 16v3.2M14.5 16v3.2M4.8 9.5H8M4.8 14.5H8M16 9.5h3.2M16 14.5h3.2"/></symbol>
    <symbol id="i-folder" viewBox="0 0 24 24"><path d="M4.5 6.8a1.7 1.7 0 0 1 1.7-1.7h3.4l2 2.4h6.2a1.7 1.7 0 0 1 1.7 1.7v8a1.7 1.7 0 0 1-1.7 1.7H6.2a1.7 1.7 0 0 1-1.7-1.7z"/></symbol>
    <symbol id="i-check" viewBox="0 0 24 24"><path d="M5.5 12.5l4 4 9-9"/></symbol>
    <symbol id="i-chev" viewBox="0 0 24 24"><path d="M7.5 10l4.5 4.5L16.5 10"/></symbol>
  </defs></svg>`;
  document.body.insertAdjacentHTML('afterbegin', ICONS);

  /* ─────────── 模拟盘符（对应本机实际磁盘，真实实现用 DriveInfo.GetDrives() 枚举） ─────────── */
  const DRIVES = [
    { letter: 'C', label: 'Win 10 Pro x64', type: '系统盘', media: 'SSD', fs: 'NTFS', total: 535868469248, free: 174513020928 },
    { letter: 'D', label: '软件',           type: '数据盘', media: 'SSD', fs: 'NTFS', total: 462880968704, free: 21963304960 }
  ];

  /* ─────────── 模拟扫描数据（与真实扫描器输出字段一一对应） ─────────── */
  const ITEMS = [
    { id: 'temp',       name: '临时文件',              icon: 'i-file',    color: '#2f6bff', cat: 'temp',    drive: 'C', path: 'C:\\Users\\liu64\\AppData\\Local\\Temp',                        files: 1284,  size: 1.24e9,  access: '今天 09:12', cleanable: true,  risk: 'low',  desc: '程序运行产生的临时文件，关闭相关程序后可安全删除。' },
    { id: 'windowsold', name: '旧版系统 Windows.old',  icon: 'i-window',  color: '#7c5cff', cat: 'cache',   drive: 'C', path: 'C:\\Windows.old',                                                files: 48000, size: 12.6e9,  access: '30 天前',   cleanable: true,  risk: 'high', desc: '系统升级前的完整备份，删除后将无法回滚到旧版本。' },
    { id: 'wu',         name: 'Windows 更新缓存',      icon: 'i-refresh', color: '#2563eb', cat: 'cache',   drive: 'C', path: 'C:\\Windows\\SoftwareDistribution\\Download',                    files: 5102,  size: 2.38e9,  access: '5 天前',    cleanable: true,  risk: 'low',  desc: '已安装更新的下载残留，可安全删除。' },
    { id: 'hiberfil',   name: '休眠文件 hiberfil.sys', icon: 'i-moon',    color: '#8b5cf6', cat: 'sys',     drive: 'C', path: 'C:\\hiberfil.sys',                                               files: 1,     size: 8.21e9,  access: '—',         cleanable: false, risk: 'mid',  desc: '休眠功能的内存镜像，需管理员运行 powercfg /h off 关闭后方可删除。' },
    { id: 'wechat',     name: '微信文件缓存',          icon: 'i-chat',    color: '#10b981', cat: 'user',    drive: 'C', path: 'C:\\Users\\liu64\\Documents\\WeChat Files',                      files: 12800, size: 2.15e9,  access: '今天 09:47', cleanable: true,  risk: 'low',  desc: '聊天图片、视频与文件缓存，删除后文字聊天记录仍保留。' },
    { id: 'downloads',  name: '下载文件夹安装包',      icon: 'i-download',color: '#059669', cat: 'user',    drive: 'C', path: 'C:\\Users\\liu64\\Downloads',                                    files: 87,    size: 1.76e9,  access: '今天 10:01', cleanable: true,  risk: 'mid',  desc: '下载目录中的 exe / zip 安装包，请确认不再需要后删除。' },
    { id: 'pagefile',   name: '页面文件 pagefile.sys', icon: 'i-db',      color: '#0d9488', cat: 'sys',     drive: 'C', path: 'C:\\pagefile.sys',                                               files: 1,     size: 6.14e9,  access: '—',         cleanable: false, risk: 'mid',  desc: '虚拟内存交换文件，系统运行时锁定占用，不可直接清理。' },
    { id: 'recycle',    name: '回收站（C:）',          icon: 'i-trash',   color: '#64748b', cat: 'recycle', drive: 'C', path: 'C:\\$Recycle.Bin',                                               files: 312,   size: 856e6,   access: '昨天 22:40', cleanable: true,  risk: 'low',  desc: 'C 盘已删除文件的暂存区，清空后将无法恢复。' },
    { id: 'restore',    name: '系统还原点',            icon: 'i-clock',   color: '#ea580c', cat: 'cache',   drive: 'C', path: 'C:\\System Volume Information',                                  files: 3,     size: 5.43e9,  access: '7 天前',    cleanable: true,  risk: 'mid',  desc: '系统还原快照，由系统还原机制管理。' },
    { id: 'browser',    name: '浏览器缓存',            icon: 'i-globe',   color: '#6366f1', cat: 'browser', drive: 'C', path: 'C:\\Users\\liu64\\AppData\\Local\\Microsoft\\Edge\\User Data',   files: 8905,  size: 468e6,   access: '今天 08:55', cleanable: true,  risk: 'low',  desc: 'Edge / Chrome 网页缓存，删除后首次访问网页稍慢。' },
    { id: 'thumbcache', name: '缩略图缓存',            icon: 'i-image',   color: '#db2777', cat: 'cache',   drive: 'C', path: 'C:\\Users\\liu64\\AppData\\Local\\Microsoft\\Windows\\Explorer',  files: 6210,  size: 342e6,   access: '3 天前',    cleanable: true,  risk: 'low',  desc: '资源管理器缩略图，删除后打开文件夹时自动重建。' },
    { id: 'memorydmp',  name: '系统内存转储',          icon: 'i-chip',    color: '#e11d48', cat: 'cache',   drive: 'C', path: 'C:\\Windows\\MEMORY.DMP',                                        files: 1,     size: 1.82e9,  access: '12 天前',   cleanable: true,  risk: 'low',  desc: '蓝屏内存转储，排查完故障后可删除。' },
    { id: 'prefetch',   name: '预取文件 Prefetch',     icon: 'i-bolt',    color: '#d97706', cat: 'cache',   drive: 'C', path: 'C:\\Windows\\Prefetch',                                          files: 412,   size: 96e6,    access: '今天 07:30', cleanable: true,  risk: 'low',  desc: '程序启动预读数据，删除后首批启动稍慢，之后自动重建。' },
    { id: 'wer',        name: '错误报告 WER',          icon: 'i-alert',   color: '#dc2626', cat: 'cache',   drive: 'C', path: 'C:\\ProgramData\\Microsoft\\Windows\\WER',                       files: 1540,  size: 218e6,   access: '6 天前',    cleanable: true,  risk: 'low',  desc: '程序崩溃诊断报告，可安全删除。' },
    { id: 'logs',       name: '系统日志',              icon: 'i-doc',     color: '#475569', cat: 'cache',   drive: 'C', path: 'C:\\Windows\\Logs',                                              files: 1096,  size: 174e6,   access: '2 天前',    cleanable: true,  risk: 'mid',  desc: '系统组件日志（CBS / ETL），删除不影响系统稳定性。' },
    { id: 'd-build',    name: 'node_modules 构建依赖', icon: 'i-folder',  color: '#0ea5e9', cat: 'dev',     drive: 'D', path: 'D:\\github\\apps\\WebApp\\node_modules',                         files: 61300, size: 5.80e9,  access: '今天 08:41', cleanable: true,  risk: 'low',  desc: '项目依赖目录，删除后可通过 npm install 重新生成。' },
    { id: 'd-npm',      name: 'npm / pip 下载缓存',    icon: 'i-download',color: '#14b8a6', cat: 'dev',     drive: 'D', path: 'D:\\nodejs\\npm-cache',                                          files: 18420, size: 3.24e9,  access: '昨天 18:22', cleanable: true,  risk: 'low',  desc: '包管理器下载缓存，删除后安装依赖时会自动重新下载。' },
    { id: 'd-iso',      name: '安装包与镜像备份',      icon: 'i-doc',     color: '#059669', cat: 'user',    drive: 'D', path: 'D:\\软件\\安装包备份',                                            files: 96,    size: 6.42e9,  access: '3 个月前',   cleanable: true,  risk: 'mid',  desc: '历史版本安装程序与系统镜像，确认不再需要后可删除。' },
    { id: 'd-vm',       name: '虚拟机磁盘 Win11-Dev.vhdx', icon: 'i-db',  color: '#7c5cff', cat: 'user',    drive: 'D', path: 'D:\\VMs\\Win11-Dev.vhdx',                                        files: 1,     size: 18.6e9,  access: '12 天前',   cleanable: true,  risk: 'high', desc: '虚拟机磁盘镜像，删除后该虚拟机将无法启动，请确认后再清理。' },
    { id: 'd-recycle',  name: '回收站（D:）',          icon: 'i-trash',   color: '#64748b', cat: 'recycle', drive: 'D', path: 'D:\\$Recycle.Bin',                                               files: 214,   size: 1.12e9,  access: '昨天 21:15', cleanable: true,  risk: 'low',  desc: 'D 盘已删除文件的暂存区，清空后将无法恢复。' },
    { id: 'd-pagefile', name: '页面文件 pagefile.sys', icon: 'i-db',      color: '#0d9488', cat: 'sys',     drive: 'D', path: 'D:\\pagefile.sys',                                               files: 1,     size: 4.29e9,  access: '—',         cleanable: false, risk: 'mid',  desc: '系统在 D 盘设置的虚拟内存交换文件，运行时锁定占用，不可直接清理。' }
  ];
  const GROUPS = [
    { key: 'temp', label: '临时文件' }, { key: 'cache', label: '系统缓存' },
    { key: 'recycle', label: '回收站' }, { key: 'browser', label: '浏览器' },
    { key: 'dev', label: '开发缓存' }, { key: 'user', label: '用户文件' },
    { key: 'sys', label: '系统文件' }
  ];
  const RISK = { low: '低风险', mid: '中风险', high: '高风险' };

  const itemOf = (id) => ITEMS.find(i => i.id === id);
  const sumOf = (ids) => ids.reduce((s, id) => s + itemOf(id).size, 0);

  /* 默认勾选策略：C 盘低风险项（影响不大）默认勾选；中/高风险项及其他盘符
     一律默认不勾选，交由用户决策。真实实现按同一规则在扫描器中输出 defaultChecked。 */
  const defChecked = (i) => i.cleanable && i.drive === 'C' && i.risk === 'low';

  /* 清理特例（真实实现由清理器返回实际结果） */
  const OUTCOMES = { 'd-build': { freed: 5.71e9, warn: '2 个文件被占用（node 进程运行中）' } };
  const scanSum = (letter) => {
    const its = ITEMS.filter(i => i.cleanable && i.drive === letter);
    return { n: its.length, sum: its.reduce((s, i) => s + i.size, 0) };
  };
  const scanSumText = (letter) => { const s = scanSum(letter); return '可清理 ' + s.n + ' 项 · ' + fmtSize(s.sum); };

  /* ─────────── 应用状态（状态机） ───────────
     phase: idle → scanning → results → (confirm 模态) → cleaning → done
     「重新分析」从 results / done 回到 scanning；「取消」回退；「关闭」重启回 idle */
  const app = {
    phase: 'idle',
    drives: ['C'],          // 勾选的扫描范围（盘符）
    scanDrives: [],         // 本次扫描范围快照
    scanList: [],           // 本次扫描目标序列
    scanIdx: 0,             // 已扫描数
    scanned: false,         // 当前结果是否有效（false = 需要重新扫描）
    checked: new Set(),     // 结果页勾选项
    expandedId: null,
    collapsed: new Set(),   // 折叠的分组 key（drive:cat）
    cleanedMap: {},         // 本次会话已清理项 id → 实际释放量
    cleanQueue: [], cleanIdx: 0,
    freedNow: 0,            // 本轮清理已释放
    freedLast: 0,           // 上一轮（最近一次完成的）清理释放量
    baselineFreed: {},      // 历史清理按盘累计（环图基准）
    timers: [],
    t0: 0
  };
  DRIVES.forEach(d => { app.baselineFreed[d.letter] = 0; });

  const el = (id) => document.getElementById(id);
  const selDrives = () => DRIVES.filter(d => app.drives.includes(d.letter));
  const scopeDrives = () => DRIVES.filter(d => app.scanDrives.includes(d.letter));
  function later(fn, ms) { const t = setTimeout(fn, ms); app.timers.push(t); return t; }
  function clearTimers() { app.timers.forEach(clearTimeout); app.timers = []; }

  function setPhase(p) {
    app.phase = p;
    document.body.dataset.state = p;
    updateChrome();
  }

  /* ─────────── 环图（已选盘符合计） ─────────── */
  const CIRC = 2 * Math.PI * 56;
  function renderDisk() {
    const ds = selDrives();
    const total = ds.reduce((s, d) => s + d.total, 0);
    const baseline = ds.reduce((s, d) => s + app.baselineFreed[d.letter], 0);
    const free = ds.reduce((s, d) => s + d.free, 0) + baseline + app.freedNow;
    const used = total - free;
    const pct = used / total;
    el('donutFill').style.strokeDasharray = CIRC.toFixed(1);
    el('donutFill').style.strokeDashoffset = (CIRC * (1 - pct)).toFixed(1);
    el('donutPct').textContent = Math.round(pct * 100) + '%';
    el('diskUsed').textContent = fmtGB(used);
    el('diskFree').textContent = fmtGB(free);
    el('diskScope').textContent = ds.length > 1 ? '（' + ds.map(d => d.letter + ':').join(' + ') + ' 合计）' : '';
  }

  /* ─────────── 总览区：标题 / 大数字 / 主副按钮 / 进度条 ─────────── */
  function renderHero() {
    const ds = selDrives();
    el('hiTitle').textContent = ds.map(d => d.letter + ': ' + d.type).join(' · ');
    el('hiSub').textContent = ds.length === 1
      ? ds[0].label + ' · ' + ds[0].media + ' · ' + fmtGB(ds[0].total) + ' · ' + ds[0].fs
      : ds[0].media + ' · 合计 ' + fmtGB(ds.reduce((s, d) => s + d.total, 0)) + ' · ' + ds[0].fs;

    const label = el('heroLabel'), stat = el('heroStat');
    let main, second;
    const checkedSum = sumOf([...app.checked]);

    switch (app.phase) {
      case 'idle':
        label.textContent = '可释放空间';
        main = { label: '扫描分析', icon: 'i-search', cls: 'primary', disabled: !app.drives.length, act: startScan };
        second = { label: '执行清理', icon: null, cls: 'ghost', disabled: true, act: null };
        break;
      case 'scanning':
        label.textContent = '可释放空间';
        main = { label: '扫描中…', icon: 'i-search', cls: 'primary', disabled: true, act: null };
        second = { label: '取消', icon: null, cls: 'ghost', disabled: false, act: cancelScan };
        break;
      case 'results':
        label.textContent = '可释放空间';
        main = { label: app.checked.size ? '执行清理 · ' + fmtSize(checkedSum) : '执行清理', icon: 'i-clean', cls: 'primary', disabled: !app.checked.size, act: openConfirm };
        second = { label: '重新分析', icon: null, cls: 'ghost', disabled: false, act: startScan };
        break;
      case 'cleaning':
        label.textContent = '已释放';
        main = { label: '清理中…', icon: 'i-clean', cls: 'primary', disabled: true, act: null };
        second = { label: '取消', icon: null, cls: 'ghost', disabled: false, act: cancelClean };
        break;
      case 'done':
        label.textContent = '本次已释放';
        main = { label: '执行清理', icon: 'i-clean', cls: 'primary', disabled: true, act: null };
        second = { label: '重新分析', icon: null, cls: 'ghost', disabled: false, act: startScan };
        break;
    }
    stat.textContent = { idle: '待扫描', scanning: '正在分析…', results: app.checked.size ? fmtSize(checkedSum) : '0.00 GB', cleaning: fmtSize(app.freedNow), done: fmtSize(app.freedLast) }[app.phase];
    stat.classList.toggle('placeholder', app.phase === 'idle' || app.phase === 'scanning');

    const mk = (id, btn) => {
      const b = el(id);
      b.className = 'btn ' + btn.cls;
      b.disabled = btn.disabled;
      b.innerHTML = (btn.icon ? ico(btn.icon) : '') + '<span>' + esc(btn.label) + '</span>';
      b.onclick = btn.act;
    };
    mk('btnMain', main);
    mk('btnSecond', second);
  }

  /* ─────────── 盘符选择条 ─────────── */
  function driveScanStatus(letter) {
    const totalD = app.scanList.filter(i => i.drive === letter).length;
    const doneD = app.scanList.slice(0, app.scanIdx).filter(i => i.drive === letter).length;
    if (doneD >= totalD && totalD > 0) { const s = scanSum(letter); return { t: '✓ 可清理 ' + s.n + ' 项 · ' + fmtSize(s.sum) }; }
    if (app.scanIdx < app.scanList.length && app.scanList[app.scanIdx].drive === letter) return { t: '正在扫描 …', wait: true };
    return { t: '等待扫描', wait: true };
  }
  function driveCardScan(d) {
    if (app.phase === 'idle') return '';
    if (app.phase === 'scanning') return driveScanStatus(d.letter).t;
    return scanSumText(d.letter);
  }
  function driveHint() {
    switch (app.phase) {
      case 'idle': return '勾选需要扫描的盘符 · C: 默认选中';
      case 'scanning': return '正在扫描已勾选的 ' + app.scanDrives.length + ' 个盘符 …';
      case 'results': return app.scanned ? '勾选结果项后执行清理 · 修改盘符后请「重新分析」' : '扫描范围已修改 · 请点击「重新分析」';
      case 'cleaning': return '正在清理已勾选的项目 …';
      case 'done': return '如需修改范围，请点击「重新分析」';
    }
    return '';
  }
  function renderDrives() {
    el('dbHint').textContent = driveHint();
    const locked = app.phase === 'scanning' || app.phase === 'cleaning';
    el('dbCards').innerHTML = DRIVES.map(d => {
      const on = app.drives.includes(d.letter);
      const used = d.total - d.free;
      const pct = Math.round(used / d.total * 100);
      const scan = driveCardScan(d);
      const wait = app.phase === 'scanning' && driveScanStatus(d.letter).wait;
      return (
        '<label class="dcard' + (on ? ' on' : '') + (locked ? ' locked' : '') + '" data-drive="' + d.letter + '"' +
          (locked ? ' title="扫描 / 清理过程中不可修改范围"' : '') + '>' +
          '<input type="checkbox"' + (on ? ' checked' : '') + (locked ? ' disabled' : '') + '>' +
          '<span class="box">' + ico('i-check') + '</span>' +
          '<span class="dc-letter">' + d.letter + ':</span>' +
          '<span class="dc-info">' +
            '<span class="dc-line1"><i class="dc-type' + (d.type === '数据盘' ? ' data' : '') + '">' + d.type + '</i>' + esc(d.label) + '</span>' +
            '<span class="dc-bar"><i class="' + (pct >= 90 ? 'hot' : '') + '" style="width:' + pct + '%"></i></span>' +
            '<span class="dc-usage">已用 ' + fmtGB(used) + ' / ' + fmtGB(d.total) + ' · ' + pct + '%</span>' +
          '</span>' +
          (scan ? '<span class="dc-sum' + (wait ? ' wait' : '') + '">' + esc(scan) + '</span>' : '') +
        '</label>');
    }).join('');
    $$('#dbCards .dcard input').forEach(inp => inp.addEventListener('change', () => {
      const letter = inp.closest('.dcard').dataset.drive;
      if (locked) { inp.checked = app.drives.includes(letter); return; }
      if (inp.checked) { if (!app.drives.includes(letter)) app.drives.push(letter); }
      else app.drives = app.drives.filter(l => l !== letter);
      log('INFO', '扫描范围已更新：' + (app.drives.length ? app.drives.map(l => l + ':').join(' · ') : '（未选择任何盘符）'));
      if ((app.phase === 'results' || app.phase === 'done') && app.scanned) {
        const same = app.scanDrives.length === app.drives.length && app.scanDrives.every(l => app.drives.includes(l));
        if (!same) { app.scanned = false; log('WARN', '扫描范围与当前结果不一致 · 请点击「重新分析」'); }
      }
      renderDrives(); renderDisk(); renderHero();
    }));
  }

  /* ─────────── 分组头辅助 ─────────── */
  function driveSelText(letter) {
    const ids = [...app.checked].filter(id => itemOf(id).drive === letter);
    return ids.length ? '已选 ' + ids.length + ' 项 · ' + fmtSize(sumOf(ids)) : '已选 0 项';
  }

  /* ─────────── 结果清单：盘符分区 → 分类分组 → 行 ─────────── */
  function renderList() {
    const list = el('list');
    const st = list.scrollTop;
    if (app.phase === 'idle') {
      list.innerHTML =
        '<div class="empty"><div class="empty-inner">' +
        '<div class="empty-icon">' + ico('i-folder') + '</div>' +
        '<div class="empty-title">尚未分析</div>' +
        '<div class="empty-sub">勾选盘符后 · 点击「扫描分析」开始</div>' +
        '</div></div>';
      return;
    }
    const scanning = app.phase === 'scanning';
    const revealed = scanning ? app.scanList.slice(0, app.scanIdx) : app.scanList;
    const next = scanning && app.scanIdx < app.scanList.length ? app.scanList[app.scanIdx] : null;
    let html = '';
    scopeDrives().forEach(d => {
      const dItems = revealed.filter(i => i.drive === d.letter);
      const hasGhost = next && next.drive === d.letter;
      if (!dItems.length && !hasGhost && app.phase !== 'scanning') return;
      const clean = dItems.filter(i => i.cleanable);
      const autoN = clean.filter(defChecked).length;
      let body = '';
      GROUPS.forEach(g => {
        const items = dItems.filter(i => i.cat === g.key);
        if (!items.length) return;
        const key = d.letter + ':' + g.key;
        const subtotal = items.reduce((s, i) => s + i.size, 0);
        body +=
          '<div class="group' + (app.collapsed.has(key) ? ' collapsed' : '') + '" data-key="' + key + '">' +
            '<div class="g-head">' + ico('i-chev', 'chev') +
              '<span class="g-title">' + g.label + '</span>' +
              '<span class="g-count">' + items.length + ' 项</span>' +
              '<span class="g-size">' + fmtSize(subtotal) + '</span>' +
            '</div>' +
            '<div class="g-body">' + items.map(rowHTML).join('') + '</div>' +
          '</div>';
      });
      if (hasGhost) {
        body += '<div class="row ghost"><div class="row-main">' +
          '<span class="tile" style="background:rgba(47,107,255,.08);border-color:rgba(47,107,255,.3);color:var(--acc)">' + ico('i-search') + '</span>' +
          '<div class="row-info"><div class="ghost-txt">▍ 正在扫描 ' + esc(next.path) + ' …</div></div>' +
          '</div></div>';
      }
      const dsSum = scanning
        ? (app.phase === 'scanning' && dItems.length === app.scanList.filter(i => i.drive === d.letter).length && dItems.length
            ? scanSumText(d.letter)
            : (hasGhost ? '正在扫描 …' : '等待扫描'))
        : (clean.length ? '可清理 ' + clean.length + ' 项 · ' + fmtSize(clean.reduce((s, i) => s + i.size, 0)) : '');
      html +=
        '<div class="drive-sec" data-drive="' + d.letter + '">' +
          '<div class="ds-head">' +
            '<span class="ds-letter">' + d.letter + '</span>' +
            '<span class="ds-title">' + d.letter + ': ' + d.type + '<i>' + esc(d.label) + '</i></span>' +
            (scanning
              ? (autoN ? '<span class="ds-policy ok">策略：低风险项将默认勾选</span>' : '<span class="ds-policy">策略：默认不勾选</span>')
              : (autoN ? '<span class="ds-policy ok">低风险 ' + autoN + ' 项已默认勾选</span>' : '<span class="ds-policy">默认未勾选 · 请自行决策</span>')) +
            '<span class="ds-sel" id="dsSel-' + d.letter + '">' + (app.phase === 'results' || app.phase === 'cleaning' ? driveSelText(d.letter) : '') + '</span>' +
            '<span class="ds-sum">' + dsSum + '</span>' +
          '</div>' +
          '<div class="ds-body">' + body + '</div>' +
        '</div>';
    });
    list.innerHTML = html;
    list.scrollTop = st;
    bindList();
  }

  /* 结果行 */
  function rowHTML(item) {
    const freed = app.cleanedMap[item.id];
    const isDone = freed != null;
    const isPart = isDone && freed !== item.size;
    const isCur = app.phase === 'cleaning' && app.cleanQueue[app.cleanIdx] === item.id;
    const checked = app.phase !== 'done' && !isDone && app.checked.has(item.id);
    const disabled = !item.cleanable || isDone || app.phase !== 'results';
    let sizeHTML = fmtSize(item.size);
    let statusHTML = '';
    if (isDone) {
      sizeHTML = '<s style="opacity:.4;font-weight:400">' + fmtSize(item.size) + '</s><span class="freed">释放 ' + fmtSize(freed) + '</span>';
      statusHTML = '<span class="pill' + (isPart ? ' part' : '') + '" style="color:var(--' + (isPart ? 'amber' : 'green') + ')">' + (isPart ? '◐ 部分清理' : '✓ 已清理') + '</span>';
    }
    return (
      '<div class="row' + (isDone ? (isPart ? ' part' : ' done') : '') + (isCur ? ' current' : '') + (app.expandedId === item.id ? ' expanded' : '') + '" data-id="' + item.id + '" data-cat="' + item.cat + '">' +
        '<div class="row-main">' +
          '<label class="chk"><input type="checkbox"' + (disabled ? ' disabled' : '') + (checked ? ' checked' : '') + '><span class="box">' + ico('i-check') + '</span></label>' +
          '<span class="tile" style="background:' + item.color + '1f;border-color:' + item.color + '55;color:' + item.color + '">' + ico(item.icon) + '</span>' +
          '<div class="row-info">' +
            '<div class="row-name">' + esc(item.name) + (item.cleanable ? '' : ' <span class="tag tag-nc">不可清理</span>') + '</div>' +
            '<div class="row-path" title="' + esc(item.path) + '">' + esc(item.path) + '</div>' +
          '</div>' +
          '<span class="tag-risk risk-' + item.risk + '">' + RISK[item.risk] + '</span>' +
          '<span class="row-meta">' + fmtInt(item.files) + ' 个文件 · ' + item.access + '</span>' +
          '<span class="row-size">' + sizeHTML + '</span>' +
          '<span class="row-status">' + statusHTML + '</span>' +
          '<button class="row-open" title="打开文件（夹）所在目录">' + ico('i-folder') + '</button>' +
          ico('i-chev', 'chev') +
        '</div>' +
        '<div class="row-detail">' +
          '<div><span class="d-label">说明</span>' + esc(item.desc) + '</div>' +
          '<div><span class="d-label">路径</span><span class="d-path">' + esc(item.path) + '</span>' +
            '<button class="mini mini-open" data-act="open">在资源管理器中显示</button></div>' +
          '<div><span class="d-label">建议</span>' + (item.cleanable ? (item.risk === 'high' ? '谨慎清理：删除后不可恢复' : item.risk === 'mid' ? '清理前请确认内容不再需要' : '可安全清理') : '由系统管理，不建议手动删除') + '</div>' +
        '</div>' +
      '</div>');
  }

  function bindList() {
    const list = el('list');
    $$('.g-head', list).forEach((h) => {
      h.addEventListener('click', () => {
        const g = h.parentElement;
        g.classList.toggle('collapsed');
        const key = g.dataset.key;
        if (g.classList.contains('collapsed')) app.collapsed.add(key); else app.collapsed.delete(key);
      });
    });
    $$('.row-main', list).forEach((m) => {
      m.addEventListener('click', (e) => {
        if (e.target.closest('.chk')) return;
        if (e.target.closest('.row-open')) {
          const row = m.closest('.row[data-id]');
          if (row) openLocation(itemOf(row.dataset.id));
          return;
        }
        const row = m.parentElement;
        row.classList.toggle('expanded');
        app.expandedId = row.classList.contains('expanded') ? row.dataset.id : null;
      });
    });
    $$('#list .row[data-id] input[type=checkbox]').forEach((c) => {
      c.addEventListener('change', () => {
        const id = c.closest('.row').dataset.id;
        if (c.checked) app.checked.add(id); else app.checked.delete(id);
        updateSelectionUI();
      });
    });
  }

  /* 勾选联动（结果页实时重算，不整表重绘） */
  function updateSelectionUI() {
    const ids = [...app.checked];
    const sum = sumOf(ids);
    el('selInfo').textContent = app.phase === 'results' || app.phase === 'cleaning'
      ? (ids.length ? '已选 ' + ids.length + ' 项 · ' + fmtSize(sum) : '未选择')
      : el('selInfo').textContent;
    if (app.phase === 'results') {
      const b = el('btnMain');
      b.innerHTML = ico('i-clean') + '<span>' + (ids.length ? '执行清理 · ' + fmtSize(sum) : '执行清理') + '</span>';
      b.disabled = !ids.length;
      el('heroStat').textContent = ids.length ? fmtSize(sum) : '0.00 GB';
      el('heroStat').classList.remove('placeholder');
      DRIVES.forEach(d => {
        const span = el('dsSel-' + d.letter);
        if (span) span.textContent = driveSelText(d.letter);
      });
      const inScope = app.scanList.filter(i => i.cleanable).length;
      el('checkAll').checked = ids.length > 0 && ids.length === inScope;
    }
  }

  /* ─────────── 分组栏 / 状态栏 ─────────── */
  function renderBar() {
    const ca = el('checkAll');
    ca.disabled = app.phase !== 'results';
    const inScope = app.scanList.filter(i => i.cleanable).length;
    ca.checked = app.phase === 'results' && app.checked.size > 0 && app.checked.size === inScope;
    const info = el('selInfo');
    if (app.phase === 'results' || app.phase === 'cleaning') {
      const n = app.checked.size, sum = sumOf([...app.checked]);
      info.textContent = n ? '已选 ' + n + ' 项 · ' + fmtSize(sum) : '未选择';
    } else if (app.phase === 'done') {
      const n = Object.keys(app.cleanedMap).length;
      info.textContent = n ? '已清理 ' + n + ' 项 · ' + fmtSize(app.freedLast) : '未选择';
    } else if (app.phase === 'scanning') {
      info.textContent = '—';
    } else {
      info.textContent = '未选择';
    }
    const sbText = {
      idle: ['SYSTEM READY · 等待指令', ''],
      scanning: ['SCANNING · 正在扫描 ' + app.scanDrives.map(l => l + ':').join(' · '), 'run'],
      results: ['SCAN COMPLETE · 扫描完成', ''],
      cleaning: ['CLEANING · 正在清理', 'run'],
      done: ['TASK COMPLETE · 清理完成', '']
    }[app.phase] || ['SYSTEM READY', ''];
    el('sbText').textContent = sbText[0];
    el('sbDot').className = 'dot ' + sbText[1];
  }

  function updateChrome() {
    renderHero();
    renderDrives();
    renderBar();
    el('progressWrap').hidden = !(app.phase === 'scanning' || app.phase === 'cleaning');
  }

  /* ─────────── 打开所在目录 / 复制路径（原型内以日志 + Toast 模拟） ─────────── */
  function openLocation(item) {
    log('OK', '已在资源管理器中定位：' + item.path);
    flashToast('已打开所在目录：' + item.name, 'ok');
  }
  function copyPath(item) {
    log('INFO', '路径已复制到剪贴板：' + item.path);
    flashToast('路径已复制', 'ok');
  }

  /* ─────────── 右键菜单 ─────────── */
  let ctxRow = null, ctxData = null;
  function closeCtx() { el('ctxMenu').hidden = true; }
  function openCtxMenu(e, row) {
    const item = itemOf(row.dataset.id);
    if (!item) return;
    ctxRow = row; ctxData = item;
    const chk = $('input[type=checkbox]', row);
    const canToggle = app.phase === 'results' && item.cleanable && !(item.id in app.cleanedMap);
    const m = el('ctxMenu');
    m.innerHTML =
      '<div class="ctx-item" data-act="open">' + ico('i-folder') + '打开文件（夹）所在目录</div>' +
      '<div class="ctx-item" data-act="copy">' + ico('i-doc') + '复制完整路径</div>' +
      (canToggle
        ? '<div class="ctx-sep"></div><div class="ctx-item" data-act="toggle">' + ico('i-check') + (chk && chk.checked ? '取消勾选' : '勾选此项') + '</div>'
        : '') +
      '<div class="ctx-item" data-act="expand">' + ico('i-chev') + (row.classList.contains('expanded') ? '收起详情' : '展开详情') + '</div>';
    m.hidden = false;
    const r = m.getBoundingClientRect();
    m.style.left = Math.max(8, Math.min(e.clientX, innerWidth - r.width - 8)) + 'px';
    m.style.top = Math.max(8, Math.min(e.clientY, innerHeight - r.height - 8)) + 'px';
  }
  function initCtx() {
    el('ctxMenu').addEventListener('click', (e) => {
      const it = e.target.closest('.ctx-item');
      if (!it || !ctxData) return;
      const act = it.dataset.act, item = ctxData, row = ctxRow;
      closeCtx();
      if (act === 'open') openLocation(item);
      else if (act === 'copy') copyPath(item);
      else if (act === 'toggle' && row) {
        const c = $('input[type=checkbox]', row);
        if (c && !c.disabled) {
          c.checked = !c.checked;
          if (c.checked) app.checked.add(item.id); else app.checked.delete(item.id);
          updateSelectionUI();
        }
      } else if (act === 'expand' && row) {
        row.classList.toggle('expanded');
        app.expandedId = row.classList.contains('expanded') ? item.id : null;
      }
    });
    el('list').addEventListener('contextmenu', (e) => {
      const row = e.target.closest('.row[data-id]');
      if (!row) return;
      e.preventDefault();
      openCtxMenu(e, row);
    });
    document.addEventListener('click', closeCtx);
    document.addEventListener('contextmenu', (e) => { if (!e.target.closest('#list')) closeCtx(); });
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape') { closeCtx(); el('mask').hidden = true; } });
    el('list').addEventListener('scroll', closeCtx);
    window.addEventListener('resize', closeCtx);
    document.addEventListener('click', (e) => {
      const btn = e.target.closest('.mini-open');
      if (btn) {
        const row = btn.closest('.row[data-id]');
        if (row) openLocation(itemOf(row.dataset.id));
      }
    });
  }

  /* ─────────── 日志 ─────────── */
  let logCount = 0;
  function log(level, msg) {
    const body = el('logBody');
    const div = document.createElement('div');
    div.className = 'log-line';
    div.innerHTML = '<span class="log-time">' + now() + '</span><span class="log-lvl lvl-' + level + '">' + level + '</span>' + esc(msg);
    body.insertBefore(div, body.lastElementChild || null);
    logCount++;
    el('logCount').textContent = logCount;
    body.scrollTop = body.scrollHeight;
  }
  function clearLogs() {
    el('logBody').innerHTML = '<div class="log-line cursor-line"></div>';
    logCount = 0;
    el('logCount').textContent = '0';
  }

  /* ─────────── Toast ─────────── */
  let toastTimer = null;
  function flashToast(text, cls) {
    const t = el('toast');
    t.className = 'toast ' + (cls || '');
    t.textContent = text;
    t.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { t.hidden = true; }, 2400);
  }

  /* ─────────── 扫描流程 ─────────── */
  function startScan() {
    clearTimers();
    closeCtx();
    el('mask').hidden = true;
    if (!app.drives.length) { flashToast('请先勾选需要扫描的盘符', 'warn'); return; }
    app.scanDrives = [...app.drives];
    app.scanList = ITEMS.filter(i => app.scanDrives.includes(i.drive));
    app.scanIdx = 0;
    app.scanned = false;
    app.checked.clear();
    app.cleanedMap = {};
    app.expandedId = null;
    app.freedNow = 0;
    setPhase('scanning');
    renderList();
    log('INFO', '开始扫描已勾选盘符：' + app.scanDrives.map(l => l + ':').join(' · '));
    app.t0 = Date.now();
    later(tickScan, 350);
  }
  function tickScan() {
    if (app.scanIdx >= app.scanList.length) { later(finishScan, 420); return; }
    const item = app.scanList[app.scanIdx];
    app.scanIdx++;
    renderList();
    renderDrives();
    el('pbarFill').style.width = (app.scanIdx / app.scanList.length * 100).toFixed(0) + '%';
    el('progressText').textContent = app.scanIdx + '/' + app.scanList.length + ' · ' + item.path;
    log('INFO', '[' + pad(app.scanIdx).padStart(2, '0') + '/' + app.scanList.length + '] 扫描 ' + item.path);
    log(item.cleanable ? (item.risk === 'low' ? 'INFO' : 'WARN') : 'INFO',
      '  ' + (item.cleanable
        ? fmtInt(item.files) + ' 个文件 · ' + fmtSize(item.size) + ' · ' + (item.risk === 'high' ? '高风险（默认不勾选）' : item.risk === 'mid' ? '中风险（默认不勾选）' : '可清理')
        : '不可清理 · 已标记'));
    later(tickScan, 300);
  }
  function finishScan() {
    app.scanned = true;
    app.scanList.filter(defChecked).forEach(i => app.checked.add(i.id));
    const secs = ((Date.now() - app.t0) / 1000).toFixed(1);
    const totalFiles = app.scanList.reduce((s, i) => s + i.files, 0);
    log('OK', '扫描完成 · 耗时 ' + secs + 's · 定位 ' + fmtInt(totalFiles) + ' 个文件');
    app.scanDrives.forEach(l => log('INFO', l + ': ' + scanSumText(l) + ' · ' + (l === 'C' ? '低风险 ' + app.scanList.filter(i => defChecked(i)).length + ' 项已默认勾选' : '默认未勾选，请决策')));
    const highs = app.scanList.filter(i => i.risk === 'high');
    if (highs.length) log('WARN', '高风险项默认未勾选：' + highs.map(i => i.name + '（' + i.drive + ':）').join(' · '));
    log('INFO', '提示：右键结果行可打开所在目录、复制路径或调整勾选');
    setPhase('results');
    renderList();
  }
  function cancelScan() {
    clearTimers();
    log('WARN', '扫描已取消');
    app.scanned = false;
    app.checked.clear();
    setPhase('idle');
    renderList();
  }

  /* ─────────── 确认清理（模态框，按当前勾选动态生成） ─────────── */
  function openConfirm() {
    if (!app.checked.size) return;
    const targets = [...app.checked].map(itemOf);
    el('mTitle').textContent = '确认清理';
    el('mBody').innerHTML =
      '即将清理以下 <b style="color:var(--acc)">' + targets.length + '</b> 个项目（按盘符分组）：' +
      '<div class="m-list">' +
      scopeDrives().filter(d => targets.some(t => t.drive === d.letter)).map(d => {
        const its = targets.filter(t => t.drive === d.letter);
        return '<div class="m-drive">' + d.letter + ': ' + d.type + '（' + its.length + ' 项 · ' + fmtSize(its.reduce((s, i) => s + i.size, 0)) + '）</div>' +
          its.map(i => '<div><span>' + esc(i.name) + '</span><b>' + fmtSize(i.size) + '</b></div>').join('');
      }).join('') +
      '</div>' +
      '<div class="m-total"><span>合计释放</span><span>' + fmtSize(sumOf([...app.checked])) + '</span></div>' +
      (targets.some(i => i.risk === 'high')
        ? '<div class="m-warn">' + ico('i-alert') + '<span>包含<b style="color:inherit">高风险</b>项目，删除后不可恢复，请确认！</span></div>'
        : '<div class="m-note">' + ico('i-check') + '<span>未包含高风险项：Windows.old、虚拟机磁盘等默认未勾选，如需清理请关闭本窗口后手动勾选。</span></div>');
    const acts = el('mActions');
    acts.innerHTML = '';
    [['取消', 'ghost', closeConfirm], ['开始清理', 'primary', () => { closeConfirm(); startClean(); }]].forEach(a => {
      const b = document.createElement('button');
      b.className = 'btn ' + a[1];
      b.textContent = a[0];
      b.onclick = a[2];
      acts.appendChild(b);
    });
    el('mask').hidden = false;
    log('INFO', '等待用户确认清理 …');
  }
  function closeConfirm() { el('mask').hidden = true; }

  /* ─────────── 清理流程 ─────────── */
  function startClean() {
    clearTimers();
    app.cleanQueue = [...app.checked].sort((a, b) => ITEMS.indexOf(itemOf(a)) - ITEMS.indexOf(itemOf(b)));
    app.cleanIdx = 0;
    app.freedNow = 0;
    if (!app.cleanQueue.length) return;
    setPhase('cleaning');
    log('INFO', '开始清理 ' + app.cleanQueue.length + ' 项 · 预计释放 ' + fmtSize(sumOf(app.cleanQueue)));
    tickClean();
  }
  function tickClean() {
    if (app.cleanIdx >= app.cleanQueue.length) { later(finishClean, 420); return; }
    const item = itemOf(app.cleanQueue[app.cleanIdx]);
    renderList();
    el('pbarFill').style.width = (app.cleanIdx / app.cleanQueue.length * 100).toFixed(0) + '%';
    el('progressText').textContent = (app.cleanIdx + 1) + '/' + app.cleanQueue.length + ' · 正在清理 ' + item.name;
    log('INFO', '[' + (app.cleanIdx + 1) + '/' + app.cleanQueue.length + '] 清理 ' + item.name + ' …');
    later(() => {
      const outcome = OUTCOMES[item.id];
      const freed = outcome ? outcome.freed : item.size;
      app.freedNow += freed;
      app.cleanedMap[item.id] = freed;
      if (outcome && outcome.warn) {
        log('WARN', '  ' + outcome.warn);
        log('OK', '  释放 ' + fmtSize(freed) + '（部分清理）');
      } else if (item.cat === 'recycle') {
        log('OK', '  已清空 · 释放 ' + fmtSize(freed));
      } else {
        log('OK', '  ' + (item.files > 1 ? '已删除 ' + fmtInt(item.files) + ' 个文件 · ' : '') + '释放 ' + fmtSize(freed));
      }
      app.cleanIdx++;
      renderList();
      renderDisk();
      renderHero();
      later(tickClean, 260);
    }, 520);
  }
  function finishClean() {
    const n = app.cleanQueue.length;
    app.freedLast = app.freedNow;
    const perDrive = {};
    Object.keys(app.cleanedMap).forEach(id => {
      const it = itemOf(id);
      perDrive[it.drive] = (perDrive[it.drive] || 0) + app.cleanedMap[id];
    });
    app.freedNow = 0;
    log('OK', '清理完成 · 成功 ' + n + ' 项');
    log('OK', '共释放 ' + fmtSize(app.freedLast));
    scopeDrives().forEach(d => {
      const f = perDrive[d.letter] || 0;
      if (f > 0) {
        const before = d.free + app.baselineFreed[d.letter];
        log('INFO', d.letter + ': 可用 ' + fmtGB(before) + ' → ' + fmtGB(before + f));
      }
      app.baselineFreed[d.letter] += f;
    });
    const highs = app.scanList.filter(i => i.risk === 'high' && !(i.id in app.cleanedMap));
    if (highs.length) log('INFO', '高风险项已保留（' + highs.map(i => i.name).join(' / ') + ' 默认未勾选）');
    log('INFO', '建议运行「重新分析」刷新结果');
    app.checked.clear();
    setPhase('done');
    renderList();
    renderDisk();
    flashToast('清理完成 · 释放 ' + fmtSize(app.freedLast), 'ok');
  }
  function cancelClean() {
    clearTimers();
    const doneN = app.cleanIdx;
    app.freedLast = app.freedNow;
    /* 已完成的项保持“已清理”状态，释放量计入环图基准 */
    Object.keys(app.cleanedMap).forEach(id => {
      app.baselineFreed[itemOf(id).drive] += app.cleanedMap[id];
    });
    app.freedNow = 0;
    app.checked.clear();
    log('WARN', '清理已取消 · 已完成 ' + doneN + ' 项 · 实际释放 ' + fmtSize(app.freedLast));
    setPhase('results');
    renderList();
    renderDisk();
  }

  /* ─────────── 窗口控制：拖动 / 最小化 / 最大化 / 关闭重启 ─────────── */
  function initWindow() {
    const win = el('appWindow');
    let drag = false, ox = 0, oy = 0;
    el('titlebar').addEventListener('mousedown', (e) => {
      if (e.target.closest('button') || win.classList.contains('maximized')) return;
      drag = true; ox = e.clientX - win.offsetLeft; oy = e.clientY - win.offsetTop;
      e.preventDefault();
    });
    document.addEventListener('mousemove', (e) => {
      if (!drag) return;
      win.style.left = Math.max(0, Math.min(e.clientX - ox, innerWidth - 140)) + 'px';
      win.style.top = Math.max(0, Math.min(e.clientY - oy, innerHeight - 80)) + 'px';
    });
    document.addEventListener('mouseup', () => { drag = false; });
    el('btnMax').addEventListener('click', () => {
      const isMax = win.classList.toggle('maximized');
      el('btnMax').innerHTML = ico(isMax ? 'i-restore' : 'i-max');
      if (!isMax) { win.style.left = win.style.top = ''; }
    });
    el('titlebar').addEventListener('dblclick', (e) => {
      if (!e.target.closest('button')) el('btnMax').click();
    });
    /* 最小化 → 任务栏胶囊，点击恢复 */
    el('btnMin').addEventListener('click', () => {
      win.classList.add('minimizing');
      setTimeout(() => { win.hidden = true; el('taskbar').hidden = false; }, 300);
    });
    el('btnRestore').addEventListener('click', () => {
      el('taskbar').hidden = true;
      win.hidden = false;
      requestAnimationFrame(() => requestAnimationFrame(() => win.classList.remove('minimizing')));
    });
    /* 关闭 → 退出应用，重新启动回到待机 */
    el('btnClose').addEventListener('click', () => {
      clearTimers();
      closeCtx();
      el('mask').hidden = true;
      win.classList.add('closing');
      setTimeout(() => { win.hidden = true; el('relaunch').hidden = false; }, 280);
    });
    el('btnRelaunch').addEventListener('click', () => {
      el('relaunch').hidden = true;
      win.hidden = false;
      requestAnimationFrame(() => requestAnimationFrame(() => win.classList.remove('closing')));
      resetApp();
    });
    el('btnClearLog').addEventListener('click', clearLogs);
    el('checkAll').addEventListener('change', function () {
      if (app.phase !== 'results') return;
      app.checked.clear();
      if (this.checked) app.scanList.filter(i => i.cleanable && !(i.id in app.cleanedMap)).forEach(i => app.checked.add(i.id));
      renderList();
      updateSelectionUI();
    });
  }

  /* ─────────── 启动 / 重启（回到待机） ─────────── */
  function resetApp() {
    clearTimers();
    app.drives = ['C'];
    app.scanDrives = [];
    app.scanList = [];
    app.scanIdx = 0;
    app.scanned = false;
    app.checked.clear();
    app.expandedId = null;
    app.collapsed.clear();
    app.cleanedMap = {};
    app.cleanQueue = [];
    app.cleanIdx = 0;
    app.freedNow = 0;
    app.freedLast = 0;
    DRIVES.forEach(d => { app.baselineFreed[d.letter] = 0; });
    clearLogs();
    log('OK', 'ClearC 引擎初始化完成 · v0.1.0-proto');
    log('INFO', '检测到 ' + DRIVES.length + ' 个本地磁盘 · ' + DRIVES.map(d => d.letter + ': ' + fmtGB(d.total)).join(' · '));
    DRIVES.forEach(d => log('INFO', d.letter + ': 已用 ' + fmtGB(d.total - d.free) + '（' + Math.round((d.total - d.free) / d.total * 100) + '%）'));
    log('INFO', '提示：勾选需要扫描的盘符（C: 默认选中），点击「扫描分析」开始');
    setPhase('idle');
    renderList();
    renderDisk();
  }

  /* ─────────── 启动 ─────────── */
  initWindow();
  initCtx();
  resetApp();
})();
