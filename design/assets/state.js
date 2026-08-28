/* ══════════════════════════════════════════════════════════════
   ClearC 界面原型 — 状态渲染器
   每个状态页只设置 window.STATE_KEY，其余全部由本文件按
   STATES 配置渲染，保证六个状态页视觉完全一致。
   ══════════════════════════════════════════════════════════════ */
(function () {
  'use strict';

  /* ─────────── 基础工具 ─────────── */
  const $ = (s, el) => (el || document).querySelector(s);
  const $$ = (s, el) => Array.from((el || document).querySelectorAll(s));
  const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  const ico = (id) => '<svg class="icon"><use href="#' + id + '"/></svg>';
  const pad = (n) => String(n).padStart(2, '0');
  const now = () => { const d = new Date(); return pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds()); };
  const GB = 1e9;
  const fmtSize = (b) => b >= GB ? (b / GB).toFixed(2) + ' GB' : b >= 1e6 ? (b / 1e6).toFixed(1) + ' MB' : Math.round(b / 1e3) + ' KB';
  const fmtInt = (n) => Number(n).toLocaleString('en-US');

  /* ─────────── SVG 图标库注入 ─────────── */
  const ICONS = `
  <svg xmlns="http://www.w3.org/2000/svg" style="display:none"><defs>
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

  /* ─────────── 模拟扫描数据（与真实扫描器输出字段一一对应） ─────────── */
  const ITEMS = [
    { id: 'temp',       name: '临时文件',              icon: 'i-file',    color: '#22d3ee', cat: 'temp',    path: 'C:\\Users\\liu64\\AppData\\Local\\Temp',                        files: 1284,  size: 1.24e9,  access: '今天 09:12', cleanable: true,  risk: 'low',  desc: '程序运行产生的临时文件，关闭相关程序后可安全删除。' },
    { id: 'windowsold', name: '旧版系统 Windows.old',  icon: 'i-window',  color: '#0891b2', cat: 'cache',   path: 'C:\\Windows.old',                                                files: 48000, size: 12.6e9,  access: '30 天前',   cleanable: true,  risk: 'high', desc: '系统升级前的完整备份，删除后将无法回滚到旧版本。' },
    { id: 'wu',         name: 'Windows 更新缓存',      icon: 'i-refresh', color: '#38bdf8', cat: 'cache',   path: 'C:\\Windows\\SoftwareDistribution\\Download',                    files: 5102,  size: 2.38e9,  access: '5 天前',    cleanable: true,  risk: 'low',  desc: '已安装更新的下载残留，可安全删除。' },
    { id: 'hiberfil',   name: '休眠文件 hiberfil.sys', icon: 'i-moon',    color: '#a78bfa', cat: 'sys',     path: 'C:\\hiberfil.sys',                                               files: 1,     size: 8.21e9,  access: '—',         cleanable: false, risk: 'mid',  desc: '休眠功能的内存镜像，需管理员运行 powercfg /h off 关闭后方可删除。' },
    { id: 'wechat',     name: '微信文件缓存',          icon: 'i-chat',    color: '#34d399', cat: 'user',    path: 'C:\\Users\\liu64\\Documents\\WeChat Files',                      files: 12800, size: 2.15e9,  access: '今天 09:47', cleanable: true,  risk: 'low',  desc: '聊天图片、视频与文件缓存，删除后文字聊天记录仍保留。' },
    { id: 'downloads',  name: '下载文件夹安装包',      icon: 'i-download',color: '#4ade80', cat: 'user',    path: 'C:\\Users\\liu64\\Downloads',                                    files: 87,    size: 1.76e9,  access: '今天 10:01', cleanable: true,  risk: 'mid',  desc: '下载目录中的 exe / zip 安装包，请确认不再需要后删除。' },
    { id: 'pagefile',   name: '页面文件 pagefile.sys', icon: 'i-db',      color: '#2dd4bf', cat: 'sys',     path: 'C:\\pagefile.sys',                                               files: 1,     size: 6.14e9,  access: '—',         cleanable: false, risk: 'mid',  desc: '虚拟内存交换文件，系统运行时锁定占用，不可直接清理。' },
    { id: 'recycle',    name: '回收站',                icon: 'i-trash',   color: '#94a3b8', cat: 'recycle', path: 'C:\\$Recycle.Bin',                                               files: 312,   size: 856e6,   access: '昨天 22:40', cleanable: true,  risk: 'low',  desc: '已删除文件的暂存区，清空后将无法恢复。' },
    { id: 'restore',    name: '系统还原点',            icon: 'i-clock',   color: '#fb923c', cat: 'cache',   path: 'C:\\System Volume Information',                                  files: 3,     size: 5.43e9,  access: '7 天前',    cleanable: true,  risk: 'mid',  desc: '系统还原快照，由系统还原机制管理。' },
    { id: 'browser',    name: '浏览器缓存',            icon: 'i-globe',   color: '#818cf8', cat: 'browser', path: 'C:\\Users\\liu64\\AppData\\Local\\Microsoft\\Edge\\User Data',   files: 8905,  size: 468e6,   access: '今天 08:55', cleanable: true,  risk: 'low',  desc: 'Edge / Chrome 网页缓存，删除后首次访问网页稍慢。' },
    { id: 'thumbcache', name: '缩略图缓存',            icon: 'i-image',   color: '#f472b6', cat: 'cache',   path: 'C:\\Users\\liu64\\AppData\\Local\\Microsoft\\Windows\\Explorer',  files: 6210,  size: 342e6,   access: '3 天前',    cleanable: true,  risk: 'low',  desc: '资源管理器缩略图，删除后打开文件夹时自动重建。' },
    { id: 'memorydmp',  name: '系统内存转储',          icon: 'i-chip',    color: '#fb7185', cat: 'cache',   path: 'C:\\Windows\\MEMORY.DMP',                                        files: 1,     size: 1.82e9,  access: '12 天前',   cleanable: true,  risk: 'low',  desc: '蓝屏内存转储，排查完故障后可删除。' },
    { id: 'prefetch',   name: '预取文件 Prefetch',     icon: 'i-bolt',    color: '#fbbf24', cat: 'cache',   path: 'C:\\Windows\\Prefetch',                                          files: 412,   size: 96e6,    access: '今天 07:30', cleanable: true,  risk: 'low',  desc: '程序启动预读数据，删除后首批启动稍慢，之后自动重建。' },
    { id: 'wer',        name: '错误报告 WER',          icon: 'i-alert',   color: '#f87171', cat: 'cache',   path: 'C:\\ProgramData\\Microsoft\\Windows\\WER',                       files: 1540,  size: 218e6,   access: '6 天前',    cleanable: true,  risk: 'low',  desc: '程序崩溃诊断报告，可安全删除。' },
    { id: 'logs',       name: '系统日志',              icon: 'i-doc',     color: '#64748b', cat: 'cache',   path: 'C:\\Windows\\Logs',                                              files: 1096,  size: 174e6,   access: '2 天前',    cleanable: true,  risk: 'mid',  desc: '系统组件日志（CBS / ETL），删除不影响系统稳定性。' }
  ];
  const CATS = [
    { key: 'all', label: '全部' }, { key: 'temp', label: '临时文件' },
    { key: 'cache', label: '系统缓存' }, { key: 'recycle', label: '回收站' },
    { key: 'browser', label: '浏览器' }, { key: 'user', label: '用户文件' },
    { key: 'sys', label: '系统文件' }
  ];
  const RISK = { low: '低风险', mid: '中风险', high: '高风险' };
  const TOTAL = 255e9;
  const BASE_FREE = 73e9;
  const CLEANABLE_SUM = ITEMS.filter(i => i.cleanable).reduce((s, i) => s + i.size, 0); // 29.53 GB

  /* 清理结果（done / cleaning 状态的行状态与实际释放量） */
  const DONE_ALL = { temp: 1.24e9, wu: 2.38e9, wechat: 2.15e9, downloads: 1.56e9, recycle: 856e6, browser: 468e6, thumbcache: 342e6, memorydmp: 1.82e9, prefetch: 96e6, wer: 218e6, logs: 174e6 };
  const SKIP_ALL = ['windowsold', 'restore'];
  const DONE_MID = { temp: 1.24e9, wu: 2.38e9, wechat: 2.15e9, downloads: 1.56e9 };
  const SKIP_MID = ['windowsold'];
  const freedOf = (m) => Object.values(m).reduce((s, v) => s + v, 0);

  /* ─────────── 状态页清单与跳转 ─────────── */
  const ORDER = [
    { key: 'idle',     no: '01', file: 'state-01-idle.html',     label: '初始状态' },
    { key: 'scanning', no: '02', file: 'state-02-scanning.html', label: '分析中' },
    { key: 'results',  no: '03', file: 'state-03-results.html',  label: '分析结果' },
    { key: 'confirm',  no: '04', file: 'state-04-confirm.html',  label: '确认清理' },
    { key: 'cleaning', no: '05', file: 'state-05-cleaning.html', label: '清理中' },
    { key: 'done',     no: '06', file: 'state-06-done.html',     label: '清理完成' }
  ];

  /* ─────────── 各状态配置 ─────────── */
  const STATES = {
    idle: {
      heroLabel: '可释放空间', heroStat: ['待扫描', true], diskFree: BASE_FREE,
      filled: 0, ghost: null, current: null, expanded: null, checked: false,
      progress: null,
      main: { label: '扫描分析', icon: 'i-search', cls: 'primary', disabled: false, href: 'state-02-scanning.html' },
      second: { label: '执行清理', icon: null, cls: 'ghost', disabled: true, href: null },
      checkAll: { disabled: true, checked: false }, selInfo: '未选择',
      sb: { text: 'SYSTEM READY · 等待指令', dot: '' },
      modal: null, toast: null,
      logs: [
        ['OK', 'ClearC 引擎初始化完成 · v0.1.0-proto'],
        ['INFO', '挂载磁盘 C: · SSD · 255.0 GB · NTFS'],
        ['INFO', '已用 182.0 GB · 可用 73.0 GB · 占用率 71.4%'],
        ['INFO', '等待指令 … 点击「扫描分析」开始扫描']
      ],
      stream: null
    },

    scanning: {
      heroLabel: '可释放空间', heroStat: ['正在分析…', true], diskFree: BASE_FREE,
      filled: 6, ghost: 'C:\\Windows\\SoftwareDistribution\\Download', current: null, expanded: null, checked: false,
      progress: { pct: .4, text: '07/15 · C:\\Windows\\SoftwareDistribution\\Download' },
      main: { label: '扫描中…', icon: 'i-search', cls: 'primary', disabled: true, href: null },
      second: { label: '取消', icon: null, cls: 'ghost', disabled: false, href: 'state-01-idle.html' },
      checkAll: { disabled: true, checked: false }, selInfo: '—',
      sb: { text: 'SCANNING · 正在扫描 C 盘', dot: 'run' },
      modal: null, toast: null,
      logs: [
        ['OK', 'ClearC 引擎初始化完成 · v0.1.0-proto'],
        ['INFO', '开始扫描 C 盘 …'],
        ['INFO', '[01/15] 扫描 C:\\Users\\liu64\\AppData\\Local\\Temp'],
        ['INFO', '  1,284 个文件 · 1.24 GB · 可清理'],
        ['INFO', '[02/15] 扫描 C:\\Windows.old'],
        ['WARN', '  48,000 个文件 · 12.60 GB · 高风险（删除后无法回滚）'],
        ['INFO', '[03/15] 扫描 C:\\hiberfil.sys'],
        ['INFO', '  系统休眠镜像 · 不可清理 · 已标记'],
        ['INFO', '[04/15] 扫描 C:\\Windows\\SoftwareDistribution\\Download'],
        ['INFO', '  5,102 个文件 · 2.38 GB · 可清理']
      ],
      stream: { interval: 1700, lines: [
        ['INFO', '[05/15] 扫描 C:\\Users\\liu64\\Documents\\WeChat Files'],
        ['INFO', '  12,800 个文件 · 2.15 GB · 可清理'],
        ['INFO', '[06/15] 扫描 C:\\Users\\liu64\\Downloads'],
        ['INFO', '  87 个文件 · 1.76 GB · 可清理（含安装包）'],
        ['INFO', '[07/15] 扫描 C:\\pagefile.sys'],
        ['INFO', '  虚拟内存交换文件 · 不可清理 · 已标记'],
        ['INFO', '[08/15] 扫描 C:\\$Recycle.Bin'],
        ['INFO', '  312 个文件 · 856.0 MB · 可清理'],
        ['INFO', '[09/15] 扫描 C:\\System Volume Information'],
        ['WARN', '  系统还原点 · 5.43 GB · 由系统管理'],
        ['INFO', '[10/15] 扫描 Edge / Chrome 缓存目录'],
        ['INFO', '  8,905 个文件 · 468.0 MB · 可清理']
      ] }
    },

    results: {
      heroLabel: '可释放空间', heroStat: [fmtSize(CLEANABLE_SUM), false], diskFree: BASE_FREE,
      filled: 15, ghost: null, current: null, expanded: 'wu', checked: true,
      progress: null,
      main: { label: '执行清理 · ' + fmtSize(CLEANABLE_SUM), icon: 'i-clean', cls: 'primary', disabled: false, href: 'state-04-confirm.html' },
      second: { label: '重新分析', icon: null, cls: 'ghost', disabled: false, href: 'state-02-scanning.html' },
      checkAll: { disabled: false, checked: true }, selInfo: '已选 13 项 · 29.53 GB',
      sb: { text: 'SCAN COMPLETE · 扫描完成', dot: '' },
      modal: null, toast: null,
      logs: [
        ['OK', '扫描完成 · 耗时 6.8s · 定位 93,969 个文件'],
        ['INFO', '共 15 个目标位置 · 总占用 44.87 GB'],
        ['INFO', '可清理 13 项 · 共 29.53 GB'],
        ['INFO', '已默认勾选全部可清理项，可手动调整'],
        ['WARN', '高风险项 1 个：Windows.old（12.60 GB，删除后无法回滚）']
      ],
      stream: null
    },

    confirm: {
      heroLabel: '可释放空间', heroStat: [fmtSize(CLEANABLE_SUM), false], diskFree: BASE_FREE,
      filled: 15, ghost: null, current: null, expanded: null, checked: true,
      progress: null,
      main: { label: '执行清理 · ' + fmtSize(CLEANABLE_SUM), icon: 'i-clean', cls: 'primary', disabled: true, href: null },
      second: { label: '重新分析', icon: null, cls: 'ghost', disabled: true, href: null },
      checkAll: { disabled: true, checked: true }, selInfo: '已选 13 项 · 29.53 GB',
      sb: { text: 'AWAIT CONFIRM · 等待确认', dot: 'warn' },
      modal: true, toast: null,
      logs: [
        ['OK', '扫描完成 · 耗时 6.8s · 定位 93,969 个文件'],
        ['INFO', '可清理 13 项 · 共 29.53 GB · 已全选'],
        ['INFO', '等待用户确认清理 …']
      ],
      stream: null
    },

    cleaning: {
      heroLabel: '已释放', heroStat: [fmtSize(freedOf(DONE_MID)), false], diskFree: BASE_FREE + freedOf(DONE_MID),
      filled: 15, ghost: null, current: 'recycle', expanded: null, checked: true,
      done: DONE_MID, skip: SKIP_MID,
      progress: { pct: 5 / 13, text: '06/13 · 正在清理 回收站' },
      main: { label: '清理中…', icon: 'i-clean', cls: 'primary', disabled: true, href: null },
      second: { label: '取消', icon: null, cls: 'ghost', disabled: false, href: 'state-03-results.html' },
      checkAll: { disabled: true, checked: true }, selInfo: '已选 8 项 · 9.41 GB',
      sb: { text: 'CLEANING · 正在清理', dot: 'run' },
      modal: null, toast: null,
      logs: [
        ['INFO', '开始清理 13 项 · 预计释放 29.53 GB'],
        ['INFO', '[01/13] 清理 临时文件 …'],
        ['OK', '  已删除 1,284 个文件 · 释放 1.24 GB'],
        ['INFO', '[02/13] 旧版系统 Windows.old'],
        ['WARN', '  跳过：需要 TrustedInstaller 权限，且删除后无法回滚'],
        ['INFO', '[03/13] 清理 Windows 更新缓存 …'],
        ['OK', '  已删除 5,102 个文件 · 释放 2.38 GB'],
        ['INFO', '[04/13] 清理 微信文件缓存 …'],
        ['OK', '  已删除 12,800 个文件 · 释放 2.15 GB'],
        ['INFO', '[05/13] 清理 下载文件夹安装包 …'],
        ['WARN', '  2 个文件被占用：setup_2026_08.exe / 资料包.zip'],
        ['OK', '  释放 1.56 GB（85/87 个文件）'],
        ['INFO', '[06/13] 清理 回收站 …']
      ],
      stream: { interval: 1900, lines: [
        ['OK', '  已清空回收站 · 释放 856.0 MB'],
        ['INFO', '[07/13] 系统还原点'],
        ['WARN', '  跳过：还原点由系统管理'],
        ['INFO', '[08/13] 清理 浏览器缓存 …'],
        ['OK', '  已删除 8,905 个文件 · 释放 468.0 MB'],
        ['INFO', '[09/13] 清理 缩略图缓存 …'],
        ['OK', '  已删除 6,210 个文件 · 释放 342.0 MB']
      ] }
    },

    done: {
      heroLabel: '本次已释放', heroStat: [fmtSize(freedOf(DONE_ALL)), false], diskFree: BASE_FREE + freedOf(DONE_ALL),
      filled: 15, ghost: null, current: null, expanded: null, checked: false,
      done: DONE_ALL, skip: SKIP_ALL,
      progress: null,
      main: { label: '执行清理', icon: 'i-clean', cls: 'primary', disabled: true, href: null },
      second: { label: '重新分析', icon: null, cls: 'ghost', disabled: false, href: 'state-02-scanning.html' },
      checkAll: { disabled: true, checked: false }, selInfo: '未选择',
      sb: { text: 'TASK COMPLETE · 清理完成', dot: '' },
      modal: null, toast: { text: '清理完成 · 释放 11.30 GB', cls: 'ok' },
      logs: [
        ['INFO', '开始清理 13 项 · 预计释放 29.53 GB'],
        ['OK', '清理完成 · 成功 11 项 · 跳过 2 项 · 占用失败 2 个文件'],
        ['OK', '共释放 11.30 GB'],
        ['INFO', 'C: 可用空间 73.0 GB → 84.3 GB'],
        ['INFO', '高风险项 Windows.old 与系统还原点已保留，可手动处理'],
        ['INFO', '建议运行「重新分析」刷新结果']
      ],
      stream: null
    }
  };

  /* ─────────── 渲染 ─────────── */
  const state = STATES[window.STATE_KEY] || STATES.idle;
  const el = (id) => document.getElementById(id);

  /* 磁盘环图 */
  const CIRC = 2 * Math.PI * 56;
  function renderDisk() {
    const used = TOTAL - state.diskFree;
    const pct = used / TOTAL;
    el('donutFill').style.strokeDasharray = CIRC.toFixed(1);
    el('donutFill').style.strokeDashoffset = (CIRC * (1 - pct)).toFixed(1);
    el('donutPct').textContent = Math.round(pct * 100) + '%';
    el('diskUsed').textContent = (used / GB).toFixed(1) + ' GB';
    el('diskFree').textContent = (state.diskFree / GB).toFixed(1) + ' GB';
  }

  /* 总览统计与按钮 */
  function renderHero() {
    el('heroLabel').textContent = state.heroLabel;
    const hs = el('heroStat');
    hs.textContent = state.heroStat[0];
    hs.classList.toggle('placeholder', state.heroStat[1]);
    const mk = (id, btn) => {
      const b = el(id);
      b.className = 'btn ' + btn.cls;
      b.disabled = btn.disabled;
      b.innerHTML = (btn.icon ? ico(btn.icon) : '') + '<span>' + esc(btn.label) + '</span>';
      if (btn.href) b.addEventListener('click', () => { location.href = btn.href; });
    };
    mk('btnMain', state.main);
    mk('btnSecond', state.second);
    if (state.progress) {
      el('progressWrap').hidden = false;
      el('pbarFill').style.width = (state.progress.pct * 100).toFixed(0) + '%';
      el('progressText').textContent = state.progress.text;
    } else {
      el('progressWrap').hidden = true;
    }
  }

  /* 筛选 chips */
  function renderChips() {
    const rows = ITEMS.slice(0, state.filled);
    const n = { all: rows.length };
    CATS.slice(1).forEach(c => { n[c.key] = rows.filter(i => i.cat === c.key).length; });
    el('chips').innerHTML = CATS.map(c =>
      '<button class="chip' + (c.key === 'all' ? ' on' : '') + '" data-cat="' + c.key + '">' +
      c.label + '<em>' + (rows.length ? n[c.key] : '') + '</em></button>').join('');
    el('chips').addEventListener('click', (e) => {
      const b = e.target.closest('.chip'); if (!b) return;
      $$('.chip', el('chips')).forEach(x => x.classList.toggle('on', x === b));
      const cat = b.dataset.cat;
      $$('.row', el('list')).forEach(r => {
        r.style.display = (cat === 'all' || r.dataset.cat === cat) ? '' : 'none';
      });
    });
  }

  /* 结果行 */
  function rowHTML(item) {
    const isDone = state.done && item.id in state.done;
    const isSkip = state.skip && state.skip.includes(item.id);
    const isCur = state.current === item.id;
    const checked = state.checked && item.cleanable && !isDone && !isSkip;
    let sizeHTML = fmtSize(item.size);
    let statusHTML = '';
    if (isDone) {
      sizeHTML = '<s style="opacity:.4;font-weight:400">' + fmtSize(item.size) + '</s><span class="freed">释放 ' + fmtSize(state.done[item.id]) + '</span>';
      statusHTML = '<span class="pill" style="color:var(--green)">✓ 已清理</span>';
    } else if (isSkip) {
      statusHTML = '<span class="pill" style="color:var(--amber)">已跳过</span>';
    }
    return (
      '<div class="row' + (isDone || isSkip ? ' done' : '') + (isCur ? ' current' : '') + (state.expanded === item.id ? ' expanded' : '') + '" data-cat="' + item.cat + '">' +
        '<div class="row-main">' +
          '<label class="chk"><input type="checkbox"' + (item.cleanable && !isDone && !isSkip ? '' : ' disabled') + (checked ? ' checked' : '') + '><span class="box">' + ico('i-check') + '</span></label>' +
          '<span class="tile" style="background:' + item.color + '1f;border-color:' + item.color + '55;color:' + item.color + '">' + ico(item.icon) + '</span>' +
          '<div class="row-info">' +
            '<div class="row-name">' + esc(item.name) + (item.cleanable ? '' : ' <span class="tag tag-nc">不可清理</span>') + '</div>' +
            '<div class="row-path" title="' + esc(item.path) + '">' + esc(item.path) + '</div>' +
          '</div>' +
          '<span class="tag-risk risk-' + item.risk + '">' + RISK[item.risk] + '</span>' +
          '<span class="row-meta">' + fmtInt(item.files) + ' 个文件 · ' + item.access + '</span>' +
          '<span class="row-size">' + sizeHTML + '</span>' +
          '<span class="row-status">' + statusHTML + '</span>' +
          ico('i-chev') +
        '</div>' +
        '<div class="row-detail">' +
          '<div><span class="d-label">说明</span>' + esc(item.desc) + '</div>' +
          '<div><span class="d-label">路径</span><span class="d-path">' + esc(item.path) + '</span></div>' +
          '<div><span class="d-label">建议</span>' + (item.cleanable ? (item.risk === 'high' ? '谨慎清理：删除后不可恢复' : item.risk === 'mid' ? '清理前请确认内容不再需要' : '可安全清理') : '由系统管理，不建议手动删除') + '</div>' +
        '</div>' +
      '</div>');
  }

  function renderList() {
    const list = el('list');
    if (!state.filled) {
      list.innerHTML =
        '<div class="empty"><div class="empty-inner">' +
        '<div class="empty-icon">' + ico('i-folder') + '</div>' +
        '<div class="empty-title">尚未分析</div>' +
        '<div class="empty-sub">// 等待扫描指令 · 点击「扫描分析」</div>' +
        '</div></div>';
      return;
    }
    let html = ITEMS.slice(0, state.filled).map(rowHTML).join('');
    if (state.ghost) {
      html += '<div class="row ghost"><div class="row-main">' +
        '<span class="tile" style="background:rgba(34,211,238,.08);border-color:rgba(34,211,238,.35);color:var(--cyan)">' + ico('i-search') + '</span>' +
        '<div class="row-info"><div class="ghost-txt">▍ 正在扫描 ' + esc(state.ghost) + ' …</div></div>' +
        '</div></div>';
    }
    list.innerHTML = html;
    $$('.row-main', list).forEach((m) => {
      m.addEventListener('click', (e) => {
        if (e.target.closest('.chk')) return;
        m.parentElement.classList.toggle('expanded');
      });
    });
  }

  function renderBar() {
    const ca = el('checkAll');
    ca.disabled = state.checkAll.disabled;
    ca.checked = state.checkAll.checked;
    el('selInfo').textContent = state.selInfo;
    el('sbText').textContent = state.sb.text;
    el('sbDot').className = 'dot ' + (state.sb.dot || '');
    el('sbRight').textContent = 'CLEARC · PROTO · STATE ' + (ORDER.find(o => o.key === (window.STATE_KEY || 'idle')) || ORDER[0]).no + '/06';
  }

  /* 日志 */
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
  function renderLogs() {
    const body = el('logBody');
    body.innerHTML = '';
    const cur = document.createElement('div');
    cur.className = 'log-line cursor-line';
    body.appendChild(cur);
    state.logs.forEach(l => log(l[0], l[1]));
    if (state.stream) {
      let i = 0;
      setInterval(() => {
        const l = state.stream.lines[i % state.stream.lines.length];
        log(l[0], l[1]);
        i++;
      }, state.stream.interval);
    }
  }

  /* 模态框（确认清理） */
  function renderModal() {
    if (!state.modal) return;
    const targets = ITEMS.filter(i => i.cleanable);
    el('mTitle').textContent = '确认清理';
    el('mBody').innerHTML =
      '即将清理以下 <b style="color:var(--cyan-b)">' + targets.length + '</b> 个项目：' +
      '<div class="m-list">' + targets.map(i =>
        '<div><span>' + esc(i.name) + '</span><b>' + fmtSize(i.size) + '</b></div>').join('') + '</div>' +
      '<div class="m-total"><span>合计释放</span><span>' + fmtSize(CLEANABLE_SUM) + '</span></div>' +
      '<div class="m-warn">' + ico('i-alert') + '<span>包含<b style="color:inherit">高风险</b>项目（Windows.old），删除后不可恢复，请确认！</span></div>';
    const acts = el('mActions');
    acts.innerHTML = '';
    [['取消', 'ghost', 'state-03-results.html'], ['开始清理', 'primary', 'state-05-cleaning.html']].forEach(a => {
      const b = document.createElement('button');
      b.className = 'btn ' + a[1];
      b.textContent = a[0];
      b.addEventListener('click', () => { location.href = a[2]; });
      acts.appendChild(b);
    });
    el('mask').hidden = false;
  }

  /* Toast */
  function renderToast() {
    if (!state.toast) return;
    const t = el('toast');
    t.className = 'toast ' + (state.toast.cls || '');
    t.textContent = state.toast.text;
    t.hidden = false;
  }

  /* 状态导航 */
  function renderNav() {
    const idx = ORDER.findIndex(o => o.key === (window.STATE_KEY || 'idle'));
    const cur = ORDER[idx];
    const prev = ORDER[idx - 1], next = ORDER[idx + 1];
    el('stateNav').innerHTML =
      (prev ? '<a href="' + prev.file + '">◀ ' + prev.no + ' ' + prev.label + '</a><i class="sep">│</i>' : '') +
      '<span class="cur">' + cur.no + ' · ' + cur.label + '</span>' +
      (next ? '<i class="sep">│</i><a href="' + next.file + '">' + next.no + ' ' + next.label + ' ▶</a>' : '') +
      '<i class="sep">│</i><a href="index.html">◎ 总览</a>';
  }

  /* 窗口控制（原型演示） */
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
    el('btnMin').addEventListener('click', () => {
      win.style.transition = 'opacity .25s, transform .25s';
      win.style.opacity = '0';
      win.style.transform = 'scale(.92)';
      setTimeout(() => { win.style.opacity = '1'; win.style.transform = ''; }, 900);
    });
    el('btnClose').addEventListener('click', () => { location.href = 'index.html'; });
    el('btnClearLog').addEventListener('click', () => {
      el('logBody').querySelectorAll('.log-line:not(.cursor-line)').forEach(n => n.remove());
      logCount = 0;
      el('logCount').textContent = '0';
    });
  }

  /* ─────────── 启动 ─────────── */
  renderDisk();
  renderHero();
  renderChips();
  renderList();
  renderBar();
  renderLogs();
  renderModal();
  renderToast();
  renderNav();
  initWindow();
})();
