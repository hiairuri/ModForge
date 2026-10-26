'use strict';

// ModForge 编辑器。与 EditorServer.cs 中的本地服务通信。
//
// 有两个独立的语言概念，界面上分开呈现：
//   界面语言（uiLanguage）—— 编辑器自身的按钮与标签文字
//   内容语言（contentLanguage）—— 正在填写的卡牌 / 状态文本属于哪种游戏语言

let state = {
  project: null,     // { id, name, ..., cards: [], powers: [], sharedEffects: [], skins: [] }
  schema: null,      // 选项列表，与代码生成器用同一套枚举
  catalog: null,     // { cards: [], powers: [], relics: [] }
  presenter: null,   // 从游戏提取的卡面素材
  terms: null,       // 游戏术语在各语言下的名称（卡牌类型、稀有度、关键词）
  artLocalDir: null, // cardart 文件夹的绝对路径，选择本地卡面时用
  languages: null,   // 游戏支持的语言（来自服务端）
  selCard: -1,
  selPower: -1,
  selShared: -1,
  refCat: 'cards',
  refSel: null,

  // 当前编辑卡牌的占位符数值（服务端随 /api/placeholders 下发），卡面预览用它替换
  // {Damage:diff()} 这类占位符——不在界面里自己再算一遍。
  tokenValues: {},
  settings: null,        // { gameDirectory, exportDirectory, cardArtDirectory, skinDirectory, projectDirectory, projectsRoot }
  settingsPath: null,    // settings.json 的位置
  projectPath: null,     // 当前工程的信息文件（About/About.xml）
  projectRoot: null,     // 当前工程文件夹
  projectBound: false,   // 这份工程有没有真的落到那个文件夹上（启动时的空工程是 false）
  lastProjectDirectory: null, // 上次打开过的工程文件夹（设置里记着的那个）
  projectCards: null,    // 当前工程自己的卡面文件夹（只含用到的贴图）
  projectMod: null,      // 当前工程的 Mod/（导出产物落点）
  projectName: null,     // 工程名，决定新建/另存为时的文件夹名
  sourcesDirectory: null, // 生成源码所在目录
};

/** 正在编辑的卡牌 / 状态文本属于哪种游戏语言。 */
let contentLanguage = 'eng';

// 占位符刷新的序号：输入框每敲一个字都会去问一次服务端，响应不保证按顺序回来，
// 只让最后一次发出的那个写结果（见 fillDescriptionTokens）。
let tokenRequest = 0;

const $ = (id) => document.getElementById(id);

/* ------------------------------------------------------------------ api */

async function api(path, options) {
  const res = await fetch(path, options);
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(`${res.status} ${res.statusText} ${text}`.trim());
  }
  return res.json();
}

function postJson(path, body) {
  return api(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

/* ------------------------------------------------------------------ toast */

let toastTimer;
function toast(message, kind) {
  const el = $('toast');
  el.textContent = message;
  el.className = 'toast show' + (kind ? ' ' + kind : '');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { el.className = 'toast'; }, 2600);
}

function logTo(text) {
  const c = $('console');
  c.textContent += (c.textContent.endsWith('\n') ? '' : '\n') + text;
  c.scrollTop = c.scrollHeight;
}

function setConsole(text) {
  $('console').textContent = text;
  $('console').scrollTop = $('console').scrollHeight;
}

/* ------------------------------------------------------------------ tabs */

document.querySelectorAll('.tab').forEach((tab) => {
  tab.addEventListener('click', () => {
    document.querySelectorAll('.tab').forEach((t) => t.classList.toggle('is-active', t === tab));
    const name = tab.dataset.tab;
    document.querySelectorAll('.pane').forEach((p) => {
      p.classList.toggle('is-active', p.id === 'pane-' + name);
    });

    // The skin list is scanned on demand: it touches the disk and is not needed
    // until the user opens the tab.
    if (name === 'skins' && skinState.installed === null) {
      renderSkins();
    }

    // 这两页显示的都是路径，会随"打开/另存为/改设置"变，所以每次进来都重画一遍。
    if (name === 'settings') {
      renderSettings();
    }

    if (name === 'project') {
      renderProjectFile();
      renderProjectForm();
    }
  });
});

function showOutput() {
  document.querySelector('.tab[data-tab="output"]').click();
}

/* ------------------------------------------------------------------ boot */

async function boot() {
  // 语言偏好先读回来，界面首屏就是用户上次选的语言。
  loadLanguageSettings();

  try {
    const s = await api('/api/state');
    state.project = s.project;
    state.schema = s.schema;
    state.languages = s.languages || null;
    state.terms = s.terms || null;
    // 自动生成描述的句式模块位置，工程页会显示出来（加语言要往那儿放文件）。
    state.sentences = s.sentences || null;
    state.catalogReady = !!s.catalogReady;
    state.catalogCount = s.catalogCount;
    state.gameVersion = s.gameVersion;

    // 路径设置与工程位置：设置页要显示，导出要显示目标目录。
    state.settings = s.settings || null;
    state.settingsPath = s.settingsPath || null;
    state.projectPath = s.projectPath || null;
    state.projectRoot = s.projectRoot || null;
    state.projectCards = s.cardsDirectory || null;
    state.projectMod = s.modDirectory || null;
    state.projectName = s.projectName || '';
    state.sourcesDirectory = s.sourcesDirectory || null;

    // 当前这份工程有没有落到文件夹上（启动时是空工程，false）。
    // 界面据此把话说清楚：现在还没有打开的工程，而不是让路径栏看着像已经打开了什么。
    state.projectBound = !!s.projectBound;
    state.lastProjectDirectory = s.lastProjectDirectory || null;

    if (s.log && s.log.length) setConsole(s.log.join('\n'));

    // Load the vanilla catalog up front: power dropdowns need it, not just the
    // Reference tab.
    if (s.catalogReady) {
      try {
        state.catalog = await api('/api/catalog');
      } catch (err) {
        toast(t('connectFailed') + err.message, 'err');
      }
    }

    // 游戏自带的卡面素材，让预览看起来是真正的卡牌。
    try {
      const pres = await api('/api/presenter');
      state.presenter = pres.available || {};
    } catch (err) {
      state.presenter = {};
    }

    applyInterfaceLanguage();
    renderContentLanguages();
    updateGameInfo();
    renderAll();
  } catch (err) {
    // 连不上服务端（窗口关了、或重启后换了端口）。这时候说清楚**该做什么**：
    // 状态是空的，之后任何"保存"都会提示"还没连上"，用户只会一头雾水。
    toast(t('bootFailed'), 'err');
    $('gameInfo').textContent = t('offline');
  }
}

/* ------------------------------------------------------------------ 语言切换 */

$('uiLang').addEventListener('change', (e) => {
  uiLanguage = I18N[e.target.value] ? e.target.value : 'en';
  saveLanguageSettings();
  applyInterfaceLanguage();
  renderContentLanguages();
  updateGameInfo();

  // 重绘一遍：状态下拉、效果行、术语标签都是按语言生成的文本，改语言后必须重建，
  // 否则"施加状态"里选的状态名还是旧语言。
  renderAll();

  toast(t('uiSwitchedZh'));
});

$('contentLang').addEventListener('change', (e) => {
  // 走统一的切换逻辑：它也会更新卡片页里的内联语言选择器并重绘。
  setContentLanguage(e.target.value);
});

function refreshAll() {
  // 有卡牌却没选中任何一张时，自动选第一张。
  //
  // 少了这一步，**刷新页面之后右半边永远是一句"选择一张卡牌"**：读起来像
  // "卡牌编辑区（含描述、效果那些）没了"，而其实只是没人替你点那一下。
  if (state.selCard < 0 && (state.project?.cards || []).length > 0) {
    state.selCard = 0;
  }

  renderCardList();
  renderCardEditor();
  renderPowerList();
  renderPowerEditor();
  renderSharedList();
  renderSharedEditor();
}

function renderAll() {
  refreshAll();
  renderProjectForm();
  renderProjectFile();
  renderSkins();
  fitCardFaces();
}

/**
 * 只更新"由当前内容推导出来"的部分：卡面预览、占位符标签、效果行摘要。
 *
 * 输入框每敲一个字都调 refreshAll 会重建整个表单，于是滚动位置被重置、正在输入的小数点
 * 也会被丢掉（Number("1.") 等于 1，输入框被写回 "1"）。所以键入时走这条路：只替换
 * 独立的展示节点，输入框本身不动。
 *
 * 会改变字段结构的改动（换效果种类、换数值方式）仍然走 refreshAll。
 */
function refreshDerived() {
  const card = (state.project.cards || [])[state.selCard];

  if (card) {
    const face = document.querySelector('[data-cardface]');
    if (face) face.innerHTML = cardFace(card);

    fillDescriptionTokens(card);
    fillAutoDescription(card);

    const rows = $('effectRows');
    if (rows) fillRowSummaries(card.effects || [], rows);
  }

  const power = (state.project.powers || [])[state.selPower];
  const powerRows = $('triggerRows');
  if (power) {
    const summary = document.querySelector('[data-power-summary]');
    if (summary) summary.textContent = powerSummary(power);

    if (powerRows) fillRowSummaries(power.triggers || [], powerRows);
  }

  fitCardFaces();
}

/* ------------------------------------------------------------------ 语言设置 */

/** 界面语言与内容语言的选择会记住，下次打开保持。 */
function loadLanguageSettings() {
  try {
    const savedUi = localStorage.getItem('modforge.uiLanguage');
    const savedContent = localStorage.getItem('modforge.contentLanguage');

    if (savedUi && I18N[savedUi]) uiLanguage = savedUi;
    if (savedContent) contentLanguage = savedContent;
  } catch (err) {
    // 隐私模式等情况下 localStorage 不可用，用默认值即可。
  }
}

function saveLanguageSettings() {
  try {
    localStorage.setItem('modforge.uiLanguage', uiLanguage);
    localStorage.setItem('modforge.contentLanguage', contentLanguage);
  } catch (err) {
    // 存不下也不影响使用。
  }
}

/**
 * 把静态元素上的文案按当前界面语言重绘。
 *
 * 带 data-i18n 的元素取 textContent，带 data-i18n-ph 的取 placeholder。
 */
function applyInterfaceLanguage() {
  document.documentElement.lang = uiLanguage === 'zh' ? 'zh' : 'en';

  document.querySelectorAll('[data-i18n]').forEach((el) => {
    el.textContent = t(el.dataset.i18n);
  });

  document.querySelectorAll('[data-i18n-ph]').forEach((el) => {
    el.placeholder = t(el.dataset.i18nPh);
  });

  if ($('uiLang')) $('uiLang').value = uiLanguage;
  if (state.project) {
    // 面板内容本身也含文案，需要整体重绘。
    renderAll();
    updateGameInfo();
  }
}

function updateGameInfo() {
  if (!state.catalogReady) {
    $('gameInfo').textContent = t('offline');
    return;
  }

  $('gameInfo').textContent = t('gameVersion', {
    v: state.gameVersion,
    n: state.catalogCount,
  });
}

/** 内容语言下拉框：列出游戏支持的语言，并标出哪些已填了文本。 */
function renderContentLanguages() {
  const select = $('contentLang');
  if (!select) return;

  select.innerHTML = contentLanguageOptions();

  // 卡片页里还有内联的语言选择器（见 langPickerHtml），一并刷新。
  document.querySelectorAll('select[data-lang-picker]').forEach((el) => {
    el.innerHTML = contentLanguageOptions();
    el.value = contentLanguage;
  });
}

/** 内容语言下拉框的 <option> 列表。 */
function contentLanguageOptions() {
  const languages = state.languages || [
    { code: 'eng', name: t('langEn') },
    { code: 'zhs', name: t('langZh') },
  ];

  // 标记哪些语言已经填过内容，方便看出还缺哪些翻译。
  const filled = new Set();
  (state.project?.cards || []).forEach((c) => {
    (c.name && typeof c.name === 'object' ? Object.keys(c.name) : []).forEach((l) => filled.add(l));
    (c.description && typeof c.description === 'object' ? Object.keys(c.description) : []).forEach((l) => filled.add(l));
  });
  (state.project?.powers || []).forEach((p) => {
    (p.name && typeof p.name === 'object' ? Object.keys(p.name) : []).forEach((l) => filled.add(l));
    (p.description && typeof p.description === 'object' ? Object.keys(p.description) : []).forEach((l) => filled.add(l));
  });

  return languages.map((l) => {
    const mark = filled.has(l.code) ? ' ✓' : '';
    return `<option value="${l.code}"${l.code === contentLanguage ? ' selected' : ''}>${escapeHtml((l.name || l.code) + mark)}</option>`;
  }).join('');
}

/**
 * 输入框右上角的内联语言选择器。
 *
 * 内容语言决定"这个输入框里填的文本属于哪种游戏语言"，所以把它放在输入框自己身上，
 * 而不是顶栏——填名字/描述时不用跑去别处切换。
 */
function langPickerHtml() {
  return `<select class="lang-inline" data-lang-picker title="${escapeHtml(t('contentLanguage'))}">
    ${contentLanguageOptions()}
  </select>`;
}

/**
 * 内联语言选择器的联动。
 *
 * 用事件委托：卡牌/状态/共享效果编辑器会重建 DOM，逐个绑定会随重绘失效。
 */
document.addEventListener('change', (e) => {
  const el = e.target;

  if (!el.matches || !el.matches('select[data-lang-picker]')) {
    return;
  }

  setContentLanguage(el.value);
});

/** 切换内容语言：存偏好、刷新编辑器与卡面。 */
function setContentLanguage(code) {
  if (!code || code === contentLanguage) {
    return;
  }

  contentLanguage = code;
  saveLanguageSettings();

  const global = $('contentLang');
  if (global) global.value = contentLanguage;

  document.querySelectorAll('select[data-lang-picker]').forEach((el) => { el.value = contentLanguage; });

  renderAll();
  renderContentLanguages();
}

/* ------------------------------------------------------------------ helpers */

/**
 * 读取按语言存放的文本。
 *
 * 与后端 LocalizedText.GetOrFallback 一致：先取该语言，再回退英语，最后取任意已填语言。
 * 这样列表与预览显示的内容和游戏内一致。
 */
function locGet(text, language) {
  if (text == null) return '';
  if (typeof text === 'string') return text;

  if (text[language]) return text[language];
  if (text.eng) return text.eng;
  const keys = Object.keys(text);
  return keys.length ? text[keys[0]] : '';
}

/** 写入某种语言的文本；传空字符串表示删除该语言。 */
function locSet(text, language, value) {
  const bag = (text && typeof text === 'object' && !Array.isArray(text)) ? text : {};

  if (value == null || String(value).trim() === '') {
    delete bag[language];
  } else {
    bag[language] = value;
  }

  return bag;
}

/** 该语言是否已填（用于提示翻译缺失）。 */
function locHas(text, language) {
  return typeof text === 'object' && text != null && !!text[language];
}

/** 某个文案对象里已填的语言列表。 */
function locLanguages(text) {
  return (text && typeof text === 'object' && !Array.isArray(text)) ? Object.keys(text) : [];
}

function optionList(values, selected) {
  return values.map((v) =>
    `<option value="${v}"${v === selected ? ' selected' : ''}>${v}</option>`
  ).join('');
}

/**
 * 选项列表，显示名取自游戏术语表。
 *
 * <paramref name="group"/> 是枚举名（如 "CardType"），服务端按当前界面语言下发对应的
 * 名称，所以这里显示的是游戏内的叫法（攻击 / 罕见 / 消耗）；术语表拿不到时回退到枚举名。
 */
function namedOptionList(group, values, selected) {
  return values.map((v) => {
    const label = gameTerm(`${group}.${v}`) || v;
    return `<option value="${v}"${v === selected ? ' selected' : ''}>${escapeHtml(label)}</option>`;
  }).join('');
}

/**
 * 查一个枚举值在界面语言下的名称。
 *
 * 界面的语言由 uiLanguage 决定，而术语随界面语言变化——这是"界面语言"而非"内容语言"，
 * 所以不与卡牌文本的语言联动。
 */
function gameTerm(key) {
  // 界面语言用 "zh"/"en"，游戏术语表的键是游戏语言代码 "zhs"/"eng"，必须转换；
  // 直接拿 uiLanguage 当键会查不到，于是静默回退成英文。
  const code = uiLanguage === 'zh' ? 'zhs' : 'eng';
  const table = state.terms?.[code] || state.terms?.eng;
  return table ? (table[key] || null) : null;
}

/**
 * 这张牌上**已定义**的临时牌集合（ID + 定义它的效果编号，1 基）。
 *
 * 判据只有一条：某条效果填了 `setId`。界面不判"这条效果能不能存"——那是后端的事
 * （`meta.canStoreSet` 只用来决定显不显示输入框），这里只是把已有的列出来给作者挑。
 */
function definedSetsOfCard(card) {
  return (card?.effects || [])
    .map((e, i) => ({ id: e.setId, number: i + 1 }))
    .filter((d) => !!d.id);
}

/**
 * 「临时牌集合ID」那一格：把这条效果产生/选出的那几张牌存起来，供同一张牌后面的效果再动它们。
 *
 * 只有**真的产出一组牌**的效果才有（后端的 `meta.canStoreSet`）——伤害、能量那类没东西可存。
 * 名字进运行时的键（`地址.效果编号.集合ID`），所以只允许字母数字下划线；填错了校验器会报。
 */
function cardSetField(eff, meta) {
  if (!meta.canStoreSet) return '';

  return `
    <div class="field">
      <label>${escapeHtml(t('cardSetId'))}</label>
      <input type="text" data-f="setId" value="${escapeHtml(eff.setId || '')}"
             placeholder="${escapeHtml(t('cardSetIdPlaceholder'))}">
      <p class="hint" style="padding:2px 0 0">${escapeHtml(t('cardSetIdHint'))}</p>
    </div>`;
}

/**
 * 作用对象那一格的下拉内容：值与标签**都由后端给**
 * （见 `EffectSchemaEntry.targets` / `targetLabels`，两两对应）。
 *
 * 界面不自己维护"哪一轴有哪些档、各自叫什么"——那份名单的每一条都在目标模块里，
 * 而同一档在两轴上的名字不同（"所有友方"在玩家轴上是"所有玩家"、"目标"在玩家轴上是
 * "任意一名友方"），界面自己判轴就会多出一份会与后端对不上的映射。
 *
 * **玩家宠物那一组**（"…的奥斯提"）按后端的 `petTargets` 归成一组，
 * 在界面上放进一个 optgroup——它是独立的一组目标（从玩家派生：`player.Osty`）。
 *
 * `context` 说明**这张表在哪儿画**，因为同一档在三处的可用性不同：
 *   `{ kind: 'card', target }` —— 卡牌页：要"打出时选定的目标"的那两档，得这张牌给得出
 *                                 一个合用的对象（要哪种由后端的 `playedTargetSources` 说）。
 *   `{ kind: 'power' }`        —— 状态页：状态没有出牌目标，那两档永远用不了。
 *   `{ kind: 'shared' }`       —— 共享效果页：**不判**。引用它的那张卡是什么目标，
 *                                 编辑共享效果时还看不到，所以这里必须能选（校验器在
 *                                 每张引用它的卡上分别判）。
 */
function targetOptionsOf(meta, context) {
  const values = meta?.targets || state.schema.targets || [];
  const labels = meta?.targetLabels || state.schema.targetLabels || [];
  const sourcesByBand = meta?.playedTargetSources || state.schema.playedTargetSources || {};
  const pets = new Set(meta?.petTargets || state.schema.petTargets || []);

  return values.map((value, i) => {
    const needs = sourcesByBand[value];

    // 不需要出牌目标的档总是可用；需要的那两档看这张牌给不给得出。
    let usable = true;

    if (needs) {
      if (context?.kind === 'shared') {
        usable = true;
      } else if (context?.kind === 'card') {
        usable = needs.includes(context.target);
      } else {
        usable = false;
      }
    }

    return {
      value,
      label: labels[i] || gameTerm(`EffectTarget.${value}`) || value,
      pet: pets.has(value),
      usable,
    };
  });
}

/**
 * 那一档**填了但此刻用不了**时给的红字（空串＝没问题）。
 *
 * 两种要出牌目标的档各有各的说法：造物轴的「目标」缺的是"一个目标"，
 * 「目标的奥斯提」缺的是"一名玩家（队友）"。
 */
function targetUnusableReason(meta, context, value) {
  const options = targetOptionsOf(meta, context);
  const band = options.find((o) => o.value === value);

  // 这一档**根本不在候选里**（手改的 JSON 跨了作用域——例如给玩家轴的效果填了个宠物档）。
  // 下拉里没有它、也就显示不出来，模型与界面就此脱节——给一句红字，别让人蒙在鼓里。
  if (!band) return t('targetNotForEffect');
  if (band.usable !== false) return '';

  // 缺的是"一名玩家"还是"一个目标"：前者是玩家轴的目标与宠物档，后者是造物轴的目标。
  const needsPlayer = value === 'TargetPet' || (meta?.scope || '') === 'Player';

  return needsPlayer ? t('targetNeedsPlayer') : t('targetNeedsSelectable');
}

/**
 * 目标那一格的下拉 HTML。
 *
 * 三条规矩：
 *   1. 用不了的档**不显示**（不摆灰项、也不写"（不可用）"——那是噪音）；
 *   2. 但**当前值**恰好是用不了的那一档时照样列出来并选中，否则下拉会显示成别项
 *      而模型里是这一项（界面与数据对不上），同时下方给一句**红字**说明为什么不可用；
 *   3. 玩家宠物那一组归进一个 optgroup（它是独立的一组目标，不跟"自身/所有敌人"混排）。
 */
function targetField(meta, context, selected, attrs = '') {
  const options = targetOptionsOf(meta, context);
  const visible = options.filter((o) => o.usable || o.value === selected);

  const plain = visible.filter((o) => !o.pet);
  const pets = visible.filter((o) => o.pet);

  const render = (list) => list.map((o) =>
    `<option value="${o.value}"${o.value === selected ? ' selected' : ''}>${escapeHtml(o.label)}</option>`).join('');

  const groups = render(plain)
    + (pets.length > 0
      ? `<optgroup label="${escapeHtml(t('petTargetGroup'))}">${render(pets)}</optgroup>`
      : '');

  const reason = targetUnusableReason(meta, context, selected);

  return `
    <select data-f="target" ${attrs}>${groups}</select>
    ${reason ? `<p class="hint warn" style="padding:2px 0 0">${escapeHtml(reason)}</p>` : ''}`;
}




function slug(text) {
  return String(text || '')
    .replace(/([a-z0-9])([A-Z])/g, '$1_$2')
    .toUpperCase()
    .replace(/[^A-Z0-9_]+/g, '_')
    .replace(/^_|_$/g, '');
}

function pascal(text) {
  return String(text || '')
    .split(/[^A-Za-z0-9]+/)
    .filter(Boolean)
    .map((p) => p[0].toUpperCase() + p.slice(1).toLowerCase())
    .join('');
}

/** The power id a card's effect refers to, normalized for comparison. */
function normPowerId(id) {
  if (!id) return '';
  const tail = id.includes('.') ? id.slice(id.lastIndexOf('.') + 1) : id;
  return tail.toUpperCase();
}

/* ================================================================== CARDS */

function newCard() {
  return {
    id: 'NEW_CARD',
    // 名称与描述按语言存放；新卡只填当前正在编辑的语言，其余由游戏回退到英语。
    name: { [contentLanguage]: 'New Card' },
    description: { [contentLanguage]: 'Deal {Damage:diff()} damage.' },
    pool: 'ironclad',
    type: 'Attack',
    rarity: 'Common',
    target: 'AnyEnemy',
    energyCost: 1,
    starCost: -1,
    starCostX: false,
    replayCount: 0,
    keywords: [],
    upgradeCostDelta: 0,
    upgradeStarDelta: 0,

    // 升级上限：1 = 升一次（绝大多数牌）。设 2 以上才出现「升级等级表」。
    maxUpgradeLevel: 1,
    effects: [],
    artFile: null,
  };
}

function renderCardList() {
  const list = $('cardList');
  list.innerHTML = '';
  (state.project.cards || []).forEach((card, i) => {
    const li = document.createElement('li');
    if (i === state.selCard) li.classList.add('is-active');

    const title = locGet(card.name, contentLanguage) || card.id;
    // 缺当前语言的翻译时标出来，方便补全。
    const untranslated = !locHas(card.name, contentLanguage) ? ' ·' : '';

    // 副标题也用游戏术语，和卡面页脚保持一致；ID 除外。
    const sub = [
      gameTerm(`CardPool.${card.pool}`) || card.pool,
      gameTerm(`CardRarity.${card.rarity}`) || card.rarity,
      gameTerm(`CardType.${card.type}`) || card.type,
    ].join(' · ');

    li.innerHTML = `<div>${escapeHtml(title)}${untranslated}</div>
      <div class="li-id">${escapeHtml(sub)}</div>`;
    li.addEventListener('click', () => { state.selCard = i; renderCardList(); renderCardEditor(); });
    list.appendChild(li);
  });
}

function renderCardEditor() {
  const host = $('cardEditor');
  const card = (state.project.cards || [])[state.selCard];
  if (!card) {
    // 一张卡都没有时，"选择一张卡牌"是句没用的话——没有可选的。说清为什么是空的
    // （启动时是一份空工程）以及该做什么，免得看起来像"卡牌/描述模块丢了"。
    const empty = (state.project.cards || []).length === 0 && !state.projectBound
      ? t('noCardsYet')
      : t('selectCard');

    host.innerHTML = `<div class="empty">${escapeHtml(empty)}</div>`;
    return;
  }

  const s = state.schema;
  const langName = languageName(contentLanguage);

  // 当前语言缺翻译时给出来源提示：内容是哪来的（英语兜底，还是本就为空）。
  const nameValue = locGet(card.name, contentLanguage);
  const descValue = locGet(card.description, contentLanguage);
  const nameMissing = !locHas(card.name, contentLanguage);
  const descMissing = !locHas(card.description, contentLanguage);

  host.innerHTML = `
    <h2>${escapeHtml(nameValue || card.id)}</h2>
    <div class="mono">model id: CARD.${escapeHtml(slug(cardTypeName(card)))}</div>

    <!-- 预览在左，基本信息在右。pv-sticky 负责粘住（见 styles.css 的说明）。 -->
    <div class="card-layout">
      <div class="card-layout-preview">
        <div class="pv-sticky" data-cardface>${cardFace(card)}</div>
      </div>

      <div class="card-layout-fields">
        <div class="grid">
          <div class="field span-all">
            <div class="field-head">
              <label>${escapeHtml(t('name'))}</label>
              ${langPickerHtml()}
            </div>
            <input type="text" id="c-name" value="${escapeHtml(nameValue)}"
                   placeholder="${nameMissing ? escapeHtml(locGet(card.name, 'eng')) : ''}">
            ${nameMissing ? `<p class="hint">${escapeHtml(t('translationMissing'))}</p>` : ''}
          </div>
          <div class="field span-all">
            <label>Id — ${escapeHtml(t('idHint'))}</label>
            <input type="text" id="c-id" value="${escapeHtml(card.id)}">
          </div>
          <div class="field">
            <label>${escapeHtml(t('pool'))}</label>
            <select id="c-pool">${namedOptionList('CardPool', s.pools, card.pool)}</select>
          </div>
          <div class="field">
            <label>${escapeHtml(t('type'))}</label>
            <select id="c-type">${namedOptionList('CardType', s.cardTypes, card.type)}</select>
          </div>
          <div class="field">
            <label>${escapeHtml(t('rarity'))}</label>
            <select id="c-rarity">${namedOptionList('CardRarity', s.rarities, card.rarity)}</select>
          </div>
          <div class="field">
            <label>${escapeHtml(t('target'))}</label>
            <select id="c-target">${namedOptionList('CardTargetType', s.cardTargets, card.target)}</select>
          </div>
          <div class="field">
            <label>${escapeHtml(t('multiplayerCard'))}</label>
            <select id="c-multiplayer">${namedOptionList('CardMultiplayerConstraint', s.cardMultiplayerConstraints, card.multiplayer || 'None')}</select>
          </div>
          <div class="field">
            <label>${escapeHtml(t('energyCost'))}</label>
            <input type="text" inputmode="numeric" id="c-cost" value="${card.energyCost}"
                   ${card.energyCostX ? 'disabled' : ''}>
            <label class="check-inline">
              <input type="checkbox" id="c-cost-x" ${card.energyCostX ? 'checked' : ''}>
              <span>${escapeHtml(t('costIsX'))}</span>
            </label>
          </div>
          <div class="field">
            <label>${escapeHtml(t('starCost'))}</label>
            <input type="text" inputmode="numeric" id="c-star" value="${card.starCost ?? -1}"
                   ${card.starCostX ? 'disabled' : ''}>
            <label class="check-inline">
              <input type="checkbox" id="c-star-x" ${card.starCostX ? 'checked' : ''}>
              <span>${escapeHtml(t('costIsXStar'))}</span>
            </label>
          </div>
          ${card.energyCostX ? '' : `
          <div class="field">
            <label>${escapeHtml(t('upgradeCostDelta'))}</label>
            <input type="text" inputmode="numeric" id="c-up-cost" value="${card.upgradeCostDelta || 0}">
          </div>`}
          ${card.starCostX ? '' : `
          <div class="field">
            <label>${escapeHtml(t('upgradeStarDelta'))}</label>
            <input type="text" inputmode="numeric" id="c-up-star" value="${card.upgradeStarDelta || 0}">
          </div>`}
          <div class="field">
            <label>${escapeHtml(t('replayCount'))}</label>
            <input type="text" inputmode="numeric" id="c-replay" value="${card.replayCount || 0}">
          </div>
          <div class="field">
            <label>${escapeHtml(t('maxUpgradeLevel'))}</label>
            <input type="text" inputmode="numeric" id="c-max-level" value="${card.maxUpgradeLevel ?? 1}">
          </div>
          <div class="field span-all">
            <label>${escapeHtml(t('cardArt'))}</label>
            <div class="art-picker">
              <input type="text" id="c-art" value="${escapeHtml(card.artFile || '')}"
                     placeholder="${escapeHtml(t('cardArtPlaceholder'))}">
              <button type="button" class="btn btn-small" id="btnPickArt">${escapeHtml(t('pickArt'))}</button>
              <button type="button" class="btn btn-small" id="btnClearArt">${escapeHtml(t('clearArt'))}</button>
            </div>
          </div>
        </div>

        <div class="field" style="margin-top:14px">
          <label>${escapeHtml(t('keywords'))}</label>
          <div class="kw-row">
            ${s.keywords.map((k) => `
              <label class="kw-chip" for="kw-${k}">
                <input type="checkbox" id="kw-${k}" ${(card.keywords || []).includes(k) ? 'checked' : ''}>
                <span>${escapeHtml(gameTerm('CardKeyword.' + k) || k)}</span>
              </label>`).join('')}
          </div>
        </div>

        <div class="section">
          <div class="section-head">
            <h3>${escapeHtml(t('description'))}</h3>
            <div class="row-actions">
              ${langPickerHtml()}
              <button class="btn btn-small" id="btnFillDesc">${escapeHtml(t('fillFromEffects'))}</button>
            </div>
          </div>
          <div class="field">
            <textarea id="c-desc" rows="4"
                      placeholder="${descMissing ? escapeHtml(locGet(card.description, 'eng')) : ''}">${escapeHtml(descValue)}</textarea>
            ${descMissing ? `<p class="hint">${escapeHtml(t('translationMissing'))}</p>` : ''}
            <div class="token-hint">
              <span class="token-label">${escapeHtml(t('insertValue'))}</span>
              ${descriptionTokens(card)}
            </div>

            <!-- 按当前效果实时生成的描述，随编辑更新，不必先点按钮或校验。 -->
            <div class="desc-preview">
              <div class="desc-preview-head">
                <span>${escapeHtml(t('autoPreview'))}</span>
                <button class="btn btn-small" id="btnUsePreview">${escapeHtml(t('useThis'))}</button>
              </div>
              <div class="desc-preview-body" data-auto-desc></div>
            </div>
          </div>
        </div>

        <div class="section">
          <div class="section-head">
            <h3>${escapeHtml(t('effectsInOrder'))}</h3>
            <div class="row-actions">
              <button class="btn btn-small" id="btnAddEffect">+ ${escapeHtml(t('newEffect'))}</button>
              <button class="btn btn-small" id="btnAddSharedToCard">+ ${escapeHtml(t('useSharedEffect'))}</button>
            </div>
          </div>
          <div id="effectRows"></div>
        </div>

        <div class="section">
          <div class="row-actions">
            <button class="btn btn-danger btn-small" id="btnDelCard">${escapeHtml(t('deleteCard'))}</button>
          </div>
        </div>
      </div>
    </div>
  `;

  bindCard(card);
  renderEffectRows(card.effects || [], $('effectRows'), card, 'card');

  // 占位符与描述预览都由服务端算，需要异步取回。
  fillDescriptionTokens(card);
  fillAutoDescription(card);

  // 正文的自适应字号要在卡面插进 DOM 之后才算得准。
  //
  // 放在函数末尾而不是各个调用方：renderCardEditor 的调用点很多（改任何一个下拉框都会
  // 走 refreshAll 重建整个编辑器），逐个去加迟早漏掉一个，漏掉的表现就是"字号偶尔偏大"。
  fitCardFaces(host);
}

/** 语言代码对应的显示名，按当前界面语言呈现。 */
function languageName(code) {
  const found = (state.languages || []).find((l) => l.code === code);
  if (found) return found.name || found.code;

  // 服务端语言列表还没到时的本地兜底。
  const fallback = {
    eng: t('langEn'),
    zhs: t('langZh'),
  };
  return fallback[code] || code;
}

function cardTypeName(card) {
  const prefix = state.project.typePrefix || '';
  return prefix + pascal(card.id);
}

function previewText(card) {
  // 数值来自服务端（随占位符一起下发，见 fillDescriptionTokens）。
  //
  // 不在这里自己算：以前这里又写了一份"取同种数值里最大的那个"，与服务端的槽位分配
  // 各算各的。那种重复逻辑一旦不同步，卡面预览就会显示一个游戏里不存在的数，
  // 而且看不出来是哪边错了。
  const vars = state.tokenValues || {};

  // 取当前内容语言的文本；未翻译时按游戏规则回退到英语。
  const template = locGet(card.description, contentLanguage);

  // Show base values. The game's {Var:diff()} suffix is what renders the upgraded
  // number, so substituting the upgrade here would misreport the card's real numbers.
  return String(template || '').replace(
    /\{([A-Za-z_][A-Za-z0-9_]*)(?::([^}]*))?\}/g,
    (whole, name) => (name in vars ? String(vars[name]) : whole)
  );
}

/**
 * Renders a card face using the game's own frame, banner and energy art, layered the
 * way the game composites a card. Falls back to plain text when the extracted artwork
 * is unavailable, so the editor still works without Godot installed.
 */
/**
 * 卡面预览。
 *
 * 尺寸与位置照游戏自己的卡牌场景（scenes/cards/card.tscn）：设计尺寸 300×422、原点在中心，
 * 所以百分比 = (150+偏移)/300 与 (211+偏移)/422。字号直接写游戏里的数字（26 / 21 / 16 / 32）。
 *
 * 三种外观（照游戏的 NCard.Reload）：
 *
 *   普通 / 罕见 / 稀有 / 事件 …   立绘 + 内框 + 外框 + 绸带 + 类型底片
 *   先古（Ancient）              立绘铺满整卡 + 特殊外框 + 文字底衬，**没有**内框与绸带
 *   不能打出的牌                 不显示费用（游戏里左上角是"锁"图标，不是数字）
 *
 * 元素叠放顺序（z-index）也很关键：内框要压在立绘之上、外框之下；
 * 游戏就是把 PortraitBorder 画在 Frame 之前的那一层。
 */
function cardFace(card) {
  const p = state.presenter || {};
  const isAncient = String(card.rarity) === 'Ancient';
  const unplayable = (card.keywords || []).includes('Unplayable');

  // 页脚：枚举值一律译成当前界面语言，ID 除外。
  const meta = [
    gameTerm(`CardPool.${card.pool}`) || card.pool,
    gameTerm(`CardRarity.${card.rarity}`) || card.rarity,
    gameTerm(`CardType.${card.type}`) || card.type,
    gameTerm(`CardTargetType.${card.target}`) || card.target,
  ].join(' · ');

  // 先古卡不画外框：游戏里 `_frame.Visible = !flag`，它用的是另一套边框。
  const frame = isAncient ? null : cardFrameFor(card, p);

  if (!frame && !isAncient) {
    return `
      <div><span class="pv-cost">${escapeHtml(costText(card))}⚡</span> <span class="pv-name">${escapeHtml(locGet(card.name, contentLanguage))}</span></div>
      <div class="pv-body">${escapeHtml(previewText(card))}</div>
      <div class="pv-meta">${escapeHtml(meta)}</div>`;
  }

  const art = artPreviewUrl(card.artFile);
  const energy = energyIconFor(card, p);
  const name = escapeHtml(locGet(card.name, contentLanguage));

  // 正文：关键词与描述拼成**同一段文字流**，和游戏一样（游戏把三者 join 成一个
  // RichTextLabel 的 text，再按 12..21 自适应字号）。分成两个盒子放会让关键词溢出、
  // 与描述和类型牌叠在一起。
  const body = bodyFlow(card);

  // 先古卡的底衬：文字底衬（描述后面那块深色）与外框都是单独的资源。
  const ancientTextBg = isAncient
    ? (p[`ancient_card_text_bg_${(ANCIENT_TEXT_BG[card.type] || 'skill')}`] || p['ancient_card_text_bg_skill'])
    : null;

  // 类型底片：所有卡都用同一张（含先古卡）。
  //
  // 游戏里 Reload 开头就无条件调 UpdateTypePlaque()，而先古分支隐藏的只有
  // _portraitBorder / _portrait / _frame / _banner ——**不含 _typePlaque**。
  // 所以先古卡的类型牌底片和普通卡是同一张，只是它上色用的材质随稀有度走。
  const typeBg = p['card_portrait_border_plaque2'];

  // 绸带：先古卡是另一张（带冠饰的暗色缎带），普通卡用共享缎带。
  const bannerArt = isAncient ? p['ancient_banner'] : p['card_banner'];
  const costOutline = energyOutlineFor(card);

  return `
    <div class="cardface${isAncient ? ' is-ancient' : ''}"
         data-pool="${escapeHtml(card.pool)}" data-rarity="${escapeHtml(card.rarity)}"
         style="--cost-outline: ${escapeHtml(costOutline)}"
         title="${escapeHtml(t('cardFaceTitle'))}">
      <div class="cf-art">${art
        ? `<img src="${escapeHtml(art)}" alt="">`
        : `<span class="cf-noart">${escapeHtml(t('noArt'))}</span>`}</div>
      ${isAncient && p['ancient_card_border']
        ? `<img class="cf-ancient-border" src="${escapeHtml(p['ancient_card_border'])}" alt="">`
        : ''}
      ${ancientTextBg ? `<div class="cf-ancient-textbg"><img src="${escapeHtml(ancientTextBg)}" alt=""></div>` : ''}
      ${frame ? `<img class="cf-frame" src="${escapeHtml(frame)}" alt="">` : ''}
      <div class="cf-body"><div class="cf-text">${body}</div></div>
      ${isAncient ? '' : (cardPortraitBorderFor(card, p)
        ? `<img class="cf-inner" src="${cardPortraitBorderFor(card, p)}" alt="">`
        : '')}
      <div class="cf-banner">
        <img src="${escapeHtml(bannerArt || '')}" alt="">
      </div>
      <div class="cf-name">${name}</div>
      <div class="cf-type">${typeBg ? `<img src="${typeBg}" alt="">` : ''}<span>${escapeHtml(gameTerm(`CardType.${card.type}`) || card.type)}</span></div>
      ${unplayable ? '' : `
      <div class="cf-cost">
        ${energy ? `<img class="cf-energy" src="${energy}" alt="">` : ''}
        <span class="cf-cost-num">${escapeHtml(costText(card))}</span>
      </div>`}
    </div>
    <div class="pv-meta">${escapeHtml(meta)}</div>`;
}

/** 描述之前的关键词，顺序与游戏一致。 */
const KEYWORDS_BEFORE_DESCRIPTION = ['Ethereal', 'Sly', 'Retain', 'Innate', 'Unplayable'];

/** 描述之后的关键词。 */
const KEYWORDS_AFTER_DESCRIPTION = ['Exhaust', 'Eternal'];

/** 先古卡的文字底衬按类型分三种（游戏里没有 quest 版，退回技能那张）。 */
const ANCIENT_TEXT_BG = { Attack: 'attack', Skill: 'skill', Power: 'power' };

/**
 * 按"装得下"给卡面正文自动定字号，对应游戏的 MegaRichTextLabel.AdjustFontSize。
 *
 * 游戏那边是 RichTextLabel 自己二分：MaxFontSize 21、MinFontSize 12，先在 21 试，
 * 装不下就在 12..21 之间二分找最大的可行值。CSS 没有这个能力，所以这里照做一遍：
 * 依次把 .cf-body 的字号写小，直到 scrollHeight 不超过 clientHeight。
 *
 * 为什么必须做：正文区高度只有卡面的 32.23%（约 136px），而定长内容（比如"获得 4 点集中。
 * 每回合失去 1 点集中。")在 21px 下要 157px。不缩就会溢出——.cf-body 是 overflow: hidden，
 * 溢出的部分看不到，看起来就是"文字被截断 / 和别的东西叠住"。
 *
 * 缩到 12 还是装不下（卡没有这种现象，但用户的自定义文本可能很长），就加 is-overflow
 * 改成顶对齐，至少留住开头那段完整的话。
 */
const BODY_FONT_MAX = 21;
const BODY_FONT_MIN = 12;

function fitCardFaces(root) {
  const scope = root || document;

  scope.querySelectorAll('.cardface .cf-body').forEach((body) => {
    const text = body.querySelector('.cf-text');
    if (!text) {
      return;
    }

    // --u 是"1 个游戏像素"，定义为卡牌宽度 / 300。计算样式里的 calc(...) 是未求值的
    // 表达式（parseFloat 会得到 NaN），所以用卡面的实测宽度反推。
    const unit = (body.parentElement.getBoundingClientRect().width || 300) / 300;

    // 先复位成最大字号，再往下找；否则上一次算过的内联字号会一直留着。
    body.classList.remove('is-overflow');

    let size = BODY_FONT_MAX;

    for (; size >= BODY_FONT_MIN; size--) {
      body.style.fontSize = `${size * unit}px`;

      // 量的是内层文字块的自然高度：.cf-text 有 flex: 0 0 auto，不会被压扁。
      // 1px 容差是因为浏览器的小数取整容易把"刚好装下"报成差一点。
      if (text.offsetHeight <= body.clientHeight + 1) {
        return;
      }
    }

    // 最小字号仍然溢出：贴着下限，并改成顶对齐（居中裁切会把第一行也切掉一半）。
    body.style.fontSize = `${BODY_FONT_MIN * unit}px`;
    body.classList.add('is-overflow');
  });
}

/**
 * 绸带 / 内框 / 类型底片的配色，按稀有度区分（规则写在 styles.css 里）。
 *
 * 游戏的做法：一张共享贴图（底色是蓝绿 #78e0ee），按稀有度套一层 HSV 位移材质。参数在
 * materials/cards/banners/card_banner_<稀有度>_mat.tres 里，只有 h / s / v 三个数，
 * styles.css 里的规则就是照它们写的：
 *
 *     hue-rotate(h × 360°)  saturate(s)  brightness(v)
 *
 * 色相方向是实测校准过的：诅咒材质 h=0.27，从底色（约 185° 青）旋转 97° 正好落到约 282°
 * （紫罗兰），与游戏里诅咒绸带的实际颜色一致；而普通材质 s=0，旋转多少都该是灰的，
 * 这一点也吻合。（按 (1-h)×360° 解释会得到黄绿色，明显不对。）
 *
 * 三者的材质来源不同，别混：
 *   内框 PortraitBorder   ← Model.BannerMaterial（按**稀有度**）
 *   绸带 Banner           ← Model.BannerMaterial（按**稀有度**）
 *   类型底片 TypePlaque   ← Model.BannerMaterial（按**稀有度**）
 *   外框 Frame            ← Model.FrameMaterial （按**牌池**，见 POOL_FRAME_* 那段 CSS）
 *
 * 注明：CSS 的 hue-rotate 与游戏的 HSV/YIQ 运算不在同一色彩空间，所以颜色是接近而非逐像素
 * 相同；饱和度与亮度是直接对应材质参数的，这部分是准的。
 *
 * 先古卡是个例外：它那几样要么被隐藏（内框/普通绸带），要么不套材质
 * （`_banner.Material = null`，_ancientBanner 根本没有材质），所以那套滤镜不适用。
 */
/**
 * 卡面用的外框图。
 *
 * 游戏按类型选边框，但状态牌与诅咒牌用的是技能边框（游戏自己就是这么映射的）；
 * 缺哪个就依次退到技能、攻击边框，总之不要出现"没有边框"的纯文本回退。
 */
function cardFrameFor(card, presenter) {
  // 游戏 CardModel.FramePath：None / Status / Curse → 技能边框，Quest 走自己的。
  const map = { Attack: 'attack', Skill: 'skill', Power: 'power', Quest: 'skill' };
  const type = map[card.type] || 'skill';

  return presenter[`card_frame_${type}_s`]
    || presenter['card_frame_skill_s']
    || presenter['card_frame_attack_s']
    || null;
}

/**
 * 能量珠的图，按牌池选。
 *
 * 关键是**不能直接用 card.pool 拼路径**：游戏里 EnergyColorName 只有 6 种取值——
 * 五个角色名，其余（无色/诅咒/状态/衍生/事件/任务）**一律是 "colorless"**。
 * 早先直接拼 `energy_<pool>`，于是 event / quest / curse / status / token 全部取不到图，
 * 这就是"事件任务等可打出的牌的费用圆形丢失"。
 *
 * （游戏 PCK 里确实有一张 energy_quest.tres，但没有任何牌池引用它，是未使用的残留。）
 */
const POOL_ENERGY_PREFIX = {
  ironclad: 'ironclad',
  silent: 'silent',
  defect: 'defect',
  regent: 'regent',
  necrobinder: 'necrobinder',
};

function energyIconFor(card, presenter) {
  const prefix = POOL_ENERGY_PREFIX[String(card.pool).toLowerCase()] || 'colorless';

  return presenter[`energy_${prefix}`]
    || presenter['energy_colorless']
    || null;
}

/**
 * 费用数字的描边色。
 *
 * 游戏 NCard.UpdateEnergyCostColor：文字是 cream，描边取 `Model.Pool.EnergyOutlineColor`。
 * 这个色是按**牌池**定的（各角色不同），不是按稀有度；没有覆盖的牌池用基类默认值。
 */
const POOL_ENERGY_OUTLINE = {
  ironclad: '#802020',
  silent: '#1A6625',
  defect: '#1D5673',
  regent: '#803D0E',
  necrobinder: '#803367',
  quest: '#431E14',
};

/** 没有覆盖 EnergyOutlineColor 的牌池一律用基类默认值（CardPoolModel.cs:37）。 */
const DEFAULT_ENERGY_OUTLINE = '#5C5440';

function energyOutlineFor(card) {
  return POOL_ENERGY_OUTLINE[String(card.pool).toLowerCase()] || DEFAULT_ENERGY_OUTLINE;
}

/**
 * 立绘内框（游戏里的 PortraitBorder）。
 *
 * 游戏 CardModel.PortraitBorderPath 的映射是：
 *   None / Status / Curse / Quest → 技能边框
 *   Attack / Skill / Power        → 同名的边框
 * 也就是说**所有卡都用这三张之一**，没有第四种。
 *
 * 注意别被 PCK 里的 `card_portrait_border_plaque_s` 误导：它在整份 PCK 里只出现在 atlas
 * 的清单里，没有任何场景或脚本引用它（卡面用的"牌匾"是 `..._plaque2`，那是类型牌底片，
 * 不是内框）。它还是一张**不透明**的圆角面板——早先把它当内框的退路，于是诅咒、状态、
 * 任务的立绘整块被一块实色盖住。
 */
function cardPortraitBorderFor(card, presenter) {
  const map = { Attack: 'attack', Skill: 'skill', Power: 'power' };
  const type = map[card.type] || 'skill';

  return presenter[`card_portrait_border_${type}_s`]
    || presenter['card_portrait_border_skill_s']
    || null;
}

/** 费用文字：X 费显示 X（游戏里也是这么显示的）。 */
function costText(card) {
  return card.energyCostX ? 'X' : String(card.energyCost ?? 0);
}

/**
 * 关键词在卡面上的文字。
 *
 * 游戏 CardKeywordExtensions.GetCardText 返回 `[gold]名称[/gold]` + 句号，
 * 再由 GetDescriptionForPile 用 join('\n') 拼进正文——所以**每个关键词自成一行**。
 * 这里的 span 只是为了上金色；换行用字面 \n（父级是 white-space: pre-wrap）。
 *
 * 注意描述前的关键词顺序：游戏是 foreach + `list2.Insert(0, …)`，逐个插到最前面，
 * 所以最终显示顺序与 CardKeywordOrder.beforeDescription **相反**（虽然那个数组的注释
 * 写着"they will appear in this order"）。为了和游戏里看到的一致，这里照样倒序。
 */
function keywordHtml(card, order, reverse) {
  const owned = card.keywords || [];

  const parts = order
    .filter((kw) => owned.includes(kw))
    .map((kw) => {
      const label = gameTerm(`CardKeyword.${kw}`) || kw;

      // 直接生成金 span：卡面正文的 [gold] 标记只处理描述文本，拼进来的关键词不再走那一遍。
      return `<span class="cf-keyword cf-gold">${escapeHtml(label)}${escapeHtml(sentenceEnd())}</span>`;
    });

  return (reverse ? parts.reverse() : parts).join('\n');
}

/** 内容语言的句末标点（中文是句号，其它用英文句点）。 */
function sentenceEnd() {
  return String(contentLanguage).startsWith('zh') ? '。' : '.';
}

/**
 * 卡面正文的完整文字流：描述前关键词 + 描述 + 描述后关键词。
 *
 * 三者拼成**一个** HTML 字符串，放进同一个 .cf-text 里。游戏就是这么做的
 * （CardModel.GetDescriptionForPile 把三段 join('\n') 成一个 RichTextLabel 的 text），
 * 这样关键词和描述才会一起换行、一起缩字号。
 *
 * 早先关键词被单独放在自己的盒子里，关键词占了正文区顶部约 25px，而描述因为行高算错
 * 又往外溢，两者于是叠在一起；而且两个盒子各缩各的字号，也缩不出游戏的效果。
 */
function bodyFlow(card) {
  const before = keywordHtml(card, KEYWORDS_BEFORE_DESCRIPTION, true);
  const after = keywordHtml(card, KEYWORDS_AFTER_DESCRIPTION, false);
  const parts = [before, cardBodyHtml(previewText(card)), after].filter((s) => s.length > 0);

  return parts.join('\n');
}


/**
 * 卡面正文：把游戏自己的标记渲染成彩色文字。
 *
 * 描述文本里带游戏用的标记，例如 [gold]格挡[/gold]。直接显示会在卡面上看到方括号，
 * 而游戏里那段是金色的；这里按游戏的做法上色，预览才和实际卡面一致。
 *
 * 先转义再替换标签，所以文本里的尖括号不会被当成 HTML。
 */
function cardBodyHtml(text) {
  const colors = { gold: 'cf-gold', blue: 'cf-blue', purple: 'cf-purple' };

  return escapeHtml(text).replace(
    /\[(\/?)([a-z_]+)\]/gi,
    (match, slash, name) => {
      const cls = colors[name.toLowerCase()];
      if (!cls) return match;          // 不认识的标记原样保留，便于发现写错
      return slash ? '</span>' : `<span class="${cls}">`;
    });
}

/**
 * 立绘的预览地址。
 *
 * 立绘可能来自 cardart 文件夹（用户放的或从原版取用的），也可能是一个原版卡图的引用；
 * 两种都能通过 /api/art 取到，所以浏览器可以直接显示缩略图。识别不了时返回 null，
 * 界面回退成显示文件名。
 */
function artPreviewUrl(artFile) {
  if (!artFile) return null;

  const normalized = String(artFile).split(/[\\/]/).join('/');

  // 原版卡图引用，形如 {"pool":"ironclad","stem":"aggression"}。
  if (normalized.trim().startsWith('{')) {
    try {
      const ref = JSON.parse(normalized);
      if (ref && ref.pool && ref.stem) {
        return `/api/art/vanilla/${encodeURIComponent(ref.pool)}/${encodeURIComponent(ref.stem)}`;
      }
    } catch (err) {
      return null;
    }

    return null;
  }

  // cardart 里的图：引用本身通常就是相对路径（如 "colorless_calm.webp"）。
  // 也兼容旧工程留下的绝对路径——那时取 cardart/ 之后的部分。
  const marker = '/cardart/';
  const at = normalized.toLowerCase().lastIndexOf(marker);

  if (at >= 0) {
    return `/api/art/local/${encodeURIComponent(normalized.slice(at + marker.length))}`;
  }

  // 相对路径：交给服务端在 cardart 里解析。带协议的（http/data）不是本地文件，忽略。
  if (!/^[a-z][a-z0-9+.-]*:/i.test(normalized) && !normalized.startsWith('/')) {
    return `/api/art/local/${encodeURIComponent(normalized)}`;
  }

  return null;
}

/** Shortens a long path for display without losing the file name. */
function shortPath(path) {
  const parts = String(path).split(/[\\/]/);
  return parts.length > 2 ? '…/' + parts.slice(-2).join('/') : path;
}

function bindCard(card) {
  // 文本类字段只写入值，不重绘：重建表单会重置滚动位置，也会丢掉正在输入的小数点。
  // 需要联动的地方（卡面、占位符、行摘要）由 refreshDerived 单独更新。
  const set = (id, fn) => {
    const el = $(id);
    if (el) el.addEventListener('input', () => { fn(el.value); });
  };

  // 改了会影响其它字段结构的字段，才需要重建表单。
  const setStructural = (id, fn) => {
    const el = $(id);
    if (el) el.addEventListener('change', () => { fn(el.value); refreshAll(); });
  };

  set('c-id', (v) => { card.id = v; });
  // 名称与描述写入当前内容语言，其它语言不受影响。
  set('c-name', (v) => { card.name = locSet(card.name, contentLanguage, v); });
  set('c-desc', (v) => { card.description = locSet(card.description, contentLanguage, v); });
  set('c-art', (v) => { card.artFile = v.trim() || null; });

  const pickArt = $('btnPickArt');
  if (pickArt) pickArt.addEventListener('click', () => openArtPicker(card));

  const clearArt = $('btnClearArt');
  if (clearArt) {
    clearArt.addEventListener('click', () => {
      card.artFile = null;
      const field = $('c-art');
      if (field) field.value = '';
      refreshDerived();
    });
  }

  // Convert the chosen image now, so a format the game cannot load is reported here
  // rather than at build time.
  const artInput = $('c-art');
  if (artInput) {
    artInput.addEventListener('change', async () => {
      const path = artInput.value.trim();
      if (!path) return;

      try {
        const res = await postJson('/api/cardart/choose', { path });
        if (!res.ok) {
          toast(res.error, 'err');
          logTo(`card art: ${res.error}`);
          return;
        }

        if (res.converted) {
          card.artFile = res.path;
          artInput.value = res.path;
          toast('Image converted to WebP', 'ok');
          logTo(`card art: converted to ${res.path}`);
        } else {
          toast(res.note || 'Image accepted');
        }
      } catch (err) {
        toast('Could not prepare the image: ' + err.message, 'err');
      }
    });
  }
  // 费用字段：解析不出完整数字时（"-"、""）保留原值，避免吞掉正在输入的内容。
  set('c-cost', (v) => {
    const n = parseNumber(v);
    if (n !== null) card.energyCost = Math.trunc(n);
  });

  // X 费：勾选后显示 X，并禁用旁边的数字输入。改这个会改变表单结构，所以要重建。
  //
  // 同时把"升级时费用变化"清零：X 费牌没有固定费用可升降，那一格会**隐藏**，
  // 留着旧值用户既看不见也改不掉，却会让校验器一直报错。
  const costX = $('c-cost-x');
  if (costX) {
    costX.addEventListener('change', () => {
      card.energyCostX = costX.checked;
      if (costX.checked) {
        card.energyCost = 0;
        card.upgradeCostDelta = 0;
      }
      refreshAll();
    });
  }

  set('c-up-cost', (v) => {
    const n = parseNumber(v);
    if (n !== null) card.upgradeCostDelta = Math.trunc(n);
  });

  // 星辰费用：-1 表示"没有星辰费用"（游戏的约定），所以负数照存不截断。
  set('c-star', (v) => {
    const n = parseNumber(v);
    if (n !== null) card.starCost = Math.trunc(n);
  });

  // X 星：与 X 费同理——禁用数字输入、隐藏"升级时星辰变化"那一格，所以要把它清零
  // （游戏在 X 星上改星辰费用会**抛异常**，那一格留着旧值会让校验器一直报错）。
  const starX = $('c-star-x');
  if (starX) {
    starX.addEventListener('change', () => {
      card.starCostX = starX.checked;
      if (starX.checked) {
        card.starCost = -1;
        card.upgradeStarDelta = 0;
      }
      refreshAll();
    });
  }

  // 升级星辰变化：改的是星辰费用，所以与 X 星冲突（游戏会抛异常、后端会报 error）。
  set('c-up-star', (v) => {
    const n = parseNumber(v);
    if (n !== null) card.upgradeStarDelta = Math.trunc(n);
  });

  // 升级上限会改变效果行里显示几个等级格，所以改完要重建表单。
  set('c-max-level', (v) => {
    const n = parseNumber(v);
    if (n !== null) card.maxUpgradeLevel = Math.max(0, Math.trunc(n));
  });
  $('c-max-level').addEventListener('change', () => refreshAll());

  // 重放次数是卡牌属性（这张牌自己多打几次），不是效果；改名/类型不影响它，所以只写值。
  set('c-replay', (v) => {
    const n = parseNumber(v);
    if (n !== null) card.replayCount = Math.max(0, Math.trunc(n));
  });

  $('c-pool').addEventListener('change', (e) => { card.pool = e.target.value; refreshAll(); });
  $('c-type').addEventListener('change', (e) => { card.type = e.target.value; refreshAll(); });
  $('c-rarity').addEventListener('change', (e) => { card.rarity = e.target.value; refreshAll(); });
  $('c-target').addEventListener('change', (e) => { card.target = e.target.value; refreshAll(); });
  $('c-multiplayer').addEventListener('change', (e) => {
    // 与默认档（None＝两种模式都有）相同就不写进模型：XML 那边也不写，
    // 存档里少一个字段，读的人一眼看得出"这张牌对人数没有要求"。
    if (e.target.value === 'None') {
      delete card.multiplayer;
    } else {
      card.multiplayer = e.target.value;
    }

    refreshAll();
  });

  state.schema.keywords.forEach((k) => {
    const el = $('kw-' + k);
    if (!el) return;
    el.addEventListener('change', () => {
      card.keywords = card.keywords || [];
      if (el.checked) {
        if (!card.keywords.includes(k)) card.keywords.push(k);
      } else {
        card.keywords = card.keywords.filter((x) => x !== k);
      }
      refreshAll();
    });
  });

  // Token buttons insert at the caret, so the author keeps their place in the text.
  const editor = $('cardEditor');
  if (editor) {
    editor.querySelectorAll('.token').forEach((btn) => {
      btn.addEventListener('click', () => {
        const area = $('c-desc');
        if (area) insertAtCaret(area, btn.dataset.token);
      });
    });
  }

  // 填入时按需生成，而不是读预览区的文字。读 DOM 的话，预览一旦还没渲染（例如刚选中
  // 卡牌），就会误报"没有可描述的效果"。
  const applySuggestion = async () => {
    const res = await suggestDescriptionFor(card);

    if (!res.ok) {
      toast(res.error, 'err');
      return;
    }

    card.description = locSet(card.description, contentLanguage, res.description);
    refreshAll();
    toast(t('filled'), 'ok');
  };

  const fill = $('btnFillDesc');
  const usePreview = $('btnUsePreview');
  if (fill) fill.addEventListener('click', applySuggestion);
  if (usePreview) usePreview.addEventListener('click', applySuggestion);

  $('btnAddEffect').addEventListener('click', () => {
    card.effects = card.effects || [];
    card.effects.push(defaultEffect(card));
    refreshAll();
  });

  $('btnAddSharedToCard').addEventListener('click', () => {
    const available = state.project.sharedEffects || [];
    if (available.length === 0) {
      toast('Define a shared effect on the Effects tab first', 'err');
      return;
    }

    const menu = available
      .map((s, i) => `${i + 1}. ${s.name}  (${s.effect.kind} ${s.effect.amount})`)
      .join('\n');
    const choice = Number(prompt(`Which shared effect?\n\n${menu}\n\nEnter a number:`));
    const index = Math.trunc(choice) - 1;

    if (!Number.isInteger(index) || index < 0 || index >= available.length) return;

    card.effects = card.effects || [];
    card.effects.push({
      // 引用行只有两件东西：指向谁、以及这一行自己的升级增量——行为以共享定义为准，
      // 其余字段写了也不会被读（所以不写，免得存档里躺着一堆看着像生效的字段）。
      sharedEffectId: available[index].id,
      upgrade: { amountDelta: 0 },
    });
    refreshAll();
  });

  $('btnDelCard').addEventListener('click', () => {
    state.project.cards.splice(state.selCard, 1);
    state.selCard = state.project.cards.length
      ? Math.min(Math.max(0, state.selCard), state.project.cards.length - 1)
      : -1;
    refreshAll();
  });
}

/**
 * 展开共享效果引用，与服务端的解析保持一致。
 *
 * 只在界面**必须**自己判断结构时用（例如某处要数效果条数）。**数值不要用它算**——
 * 数值一律走服务端下发的 tokenValues（见 fillDescriptionTokens），
 * 界面里再算一遍就会与服务端分叉。
 */
function resolvedEffects(effects) {
  const byId = {};
  (state.project.sharedEffects || []).forEach((s) => { byId[slug(s.id)] = s; });

  const out = [];
  (effects || []).forEach((e) => {
    if (!e.sharedEffectId) {
      out.push(e);
      return;
    }

    const def = byId[slug(e.sharedEffectId)];
    if (def && def.effect) out.push(def.effect);
  });
  return out;
}

/**
 * 点击式数值标签。
 *
 * 占位符由服务端给出，与生成卡牌时声明的变量完全一致，所以界面不会提供一个
 * 生成时并不存在的标签（选了它游戏内会格式化失败）。
 */
function descriptionTokens(card) {
  // 这个容器**必须一直留在页面上**：每次刷新都要靠它把新数值填进去。
  // 所以下面填内容时用 innerHTML，而不是把这个元素本身换掉——换掉之后
  // 下一次刷新就找不到容器，整个占位符区与卡面数值会停在第一次算出来的数上
  // （实测过：把第一条伤害改成 4、第二条改成 9，卡面还写着 6）。
  return '<span class="token-list" data-token-host>…</span>';
}

/** 取回占位符并填充标签区。 */
async function fillDescriptionTokens(card) {
  const host = document.querySelector('[data-token-host]');
  if (!host) return;

  // 每次刷新领一个号。输入框每敲一个字都会来一次，响应不保证按发出顺序回来；
  // 只认最后一次发出的那个，否则慢的旧响应会把新数值盖回去。
  const ticket = ++tokenRequest;

  let res;
  try {
    res = await postJson('/api/placeholders', {
      effects: card.effects || [],
      language: contentLanguage,
    });
  } catch (err) {
    if (ticket === tokenRequest) {
      host.innerHTML = `<span class="token-empty">${escapeHtml(t('addEffectFirst'))}</span>`;
    }
    return;
  }

  if (ticket !== tokenRequest) return;

  const items = (res.placeholders || []);

  // 卡面预览要用这些数值替换描述里的 {Damage:diff()} 一类占位符。
  // 存下来给 previewText 用，而不是在那边自己再算一遍——各算一遍必然分叉
  // （服务端改了槽位分配，预览还按老规则算，卡面就会显示游戏里不存在的数）。
  state.tokenValues = {};
  items.forEach((p) => {
    if (p.value !== null && p.value !== undefined) state.tokenValues[p.placeholder] = p.value;
  });

  if (!res.ok || items.length === 0) {
    host.innerHTML = `<span class="token-empty">${escapeHtml(t('addEffectFirst'))}</span>`;
    return;
  }

  // 数值是刚到的，卡面要按它重画一次：refreshDerived 里那次渲染发生在本次请求返回之前，
  // 当时用的还是上一张卡的值（或空表），占位符会原样停着没被替换。
  const face = document.querySelector('[data-cardface]');
  if (face) face.innerHTML = cardFace(card);

  host.innerHTML = items
    .map((p) => `<button type="button" class="token" data-token="{${escapeHtml(p.placeholder)}:diff()}"
                   title="${escapeHtml(p.placeholder)}">${escapeHtml('{' + p.placeholder + ':diff()}')}<em>${escapeHtml(p.label)}</em></button>`)
    .join('');

  // 重新绑定插入行为（按钮是刚生成的）。
  host.querySelectorAll('.token').forEach((btn) => {
    btn.addEventListener('click', () => {
      const area = $('c-desc');
      if (area) insertAtCaret(area, btn.dataset.token);
    });
  });
}

/** 在光标处插入文本，保持作者的编辑位置。 */
function insertAtCaret(textarea, text) {
  const start = textarea.selectionStart ?? textarea.value.length;
  const end = textarea.selectionEnd ?? start;

  textarea.value = textarea.value.slice(0, start) + text + textarea.value.slice(end);
  textarea.selectionStart = textarea.selectionEnd = start + text.length;
  textarea.focus();
  textarea.dispatchEvent(new Event('input', { bubbles: true }));
}

/** 新建一条效果时的初始值。 */
function defaultEffect(card) {
  const isAttack = card.type === 'Attack';

  // 效果的种类从**界面真正能选的那些**里挑：迁移期间 schema 只列出已经重构过的效果，
  // 挑不到"伤害/格挡"时退回列表里的第一个。写死 DealDamage 的话，下拉里没有这一项，
  // 于是新建出来的效果会显示成列表第一项（"获得能量"）——面板说一套、数据是另一套。
  const available = (state.schema.effects || []).map((e) => e.kind);
  const preferred = isAttack ? 'DealDamage' : 'GainBlock';
  const kind = available.includes(preferred) ? preferred : (available[0] || preferred);
  const meta = state.schema.effects.find((e) => e.kind === kind) || {};

  const effect = {
    kind,
    trigger: 'OnPlay',

    // 初始作用对象由后端按**作用域**给（造物轴 → 目标、玩家轴 → 自己）：玩家轴的效果
    // 默认成"目标"的意思是"给队友"，而"给自己"才是主流。后端没给（老状态）时退回老写法。
    target: meta.defaultTarget || (meta.targets && meta.targets[0]) || (isAttack ? 'Target' : 'Self'),
    amount: isAttack ? 6 : 5,

    // 来源选「无」＝固定值。
    source: { actor: 'Self', kind: 'None', perUnit: 1 },
    powerId: null,
    targetPile: 'DrawPile',
    placement: 'Top',
    cardId: null,
    upgraded: false,
    repeat: 1,
    upgrade: { amountDelta: 0 },

    // "打出一张牌时"的计数器（默认不启用：每张符合条件的牌各触发一次）。
    useCounter: false,
    counterTarget: 3,
    counterReset: 'ThisTurn',
  };

  // 挑牌那十个流程只给**会挑牌**的效果配一份（`meta.pick` 由后端按种类现算）。
  // 不分种类一律带上它的后果实测过：不挑牌的效果（获得能量…）会把一段没有作用的
  // <pick> 写进 XML，导出时还会多出一条永远读不到的选择提示语。
  if (meta.pick) {
    effect.pick = newPick();
  }

  // "加入指定卡牌"的生成方式（指定牌 / 从卡池 N 选一）：候选与筛选只给 N 选一那一路，
  // 方式本身始终带上（导出时 XML 按"与默认相同就不写"处理，Named 一个字都不多）。
  if (meta.hasAddMode) {
    effect.addMode = 'Named';
    effect.candidates = 3;
  }

  return effect;
}

/**
 * 一份默认的选牌配置（十流程）。
 *
 * 形状与后端 `CardPick` 的 JSON 一致：source / piles / 各维取值 / 两个费用比较器 /
 * mode / count（张数是三件套：基础值 + 变量 × 变量系数，见 Modules/Variables/NumericValue）。
 *
 * 没有"启用了哪几维"这个字段：**维度是否生效由内容表达**（取值列表非空、比较器不是 None），
 * 所以"多维并存"不需要模型改动。阈值住在效果自己的数值里（EnergyAmount / StarAmount），
 * 那边走变量三件套、能升级，见 EffectDef。
 */
function newPick() {
  return {
    source: 'Piles',
    piles: ['Hand'],
    types: [],
    rarities: [],
    cardIds: [],
    tags: [],
    keywords: [],
    energyComparison: 'None',
    starComparison: 'None',
    mode: 'Choose',
    count: { base: 1, variable: { actor: 'Self', kind: 'None', perUnit: 1 } },
  };
}

/* -------------------------------------------------- 选牌器十步流程 */

/**
 * 这条效果挑牌用的配置（缺了就补一份）。
 *
 * 老工程（这一轮之前的 XML）里没有 <pick>，读出来就是模型默认值；界面上直接改会被当成
 * 一次编辑，保存时写成显式的 <pick> —— 这正是想要的行为。
 */
function pickOf(eff) {
  return eff.pick || (eff.pick = newPick());
}

/** 张数那一套三件套（缺了就补一份默认）。 */
function pickCountOf(pick) {
  return pick.count || (pick.count = { base: 1, variable: { actor: 'Self', kind: 'None', perUnit: 1 } });
}

/** 选牌下拉里的候选值：某一维该有哪些。 */
function pickDimensionItems(dimension) {
  switch (dimension) {
    case 'CardType':
      return (state.schema.cardTypes || []).map((v) => ({ value: v, label: gameTerm(`CardType.${v}`) || v }));
    case 'Rarity':
      return (state.schema.rarities || []).map((v) => ({ value: v, label: gameTerm(`CardRarity.${v}`) || v }));
    case 'Tag':
      return (state.schema.cardTags || []).map((v) => ({ value: v, label: gameTerm(`CardTag.${v}`) || v }));
    case 'Keyword':
      return (state.schema.keywords || []).map((v) => ({ value: v, label: gameTerm(`CardKeyword.${v}`) || v }));
    case 'CardId':
      return cardComboItems().map((c) => ({ value: c.id, label: c.label }));
    default:
      return [];
  }
}

/** 某一维在下拉里的显示项（修饰语的候选）。 */
function dimensionItems(allowed) {
  return allowed.map((d) => ({ value: d, label: t('pickOption_' + d) }));
}

/** 多值输入框的候选项：按字段名分（`pick.` 前缀的是选牌器那几格）。 */
function multiComboItemsFor(field) {
  if (field === 'orbKinds') {
    return orbComboItems();
  }

  if (field === 'pick.piles') {
    return pileKindItems();
  }

  // 筛选目标那几格（选牌器一份 + "按条件数牌"那五份）：前缀决定它是哪一份。
  const prefix = filterPrefixOf(field);

  if (prefix) {
    const rest = field.slice(prefix.length);

    // "启用了哪几维"那一格：候选是全部七维（选牌器那边画的时候再按策略收窄）。
    if (rest === 'dimensions') {
      return dimensionItems(state.schema.filterDimensions || []);
    }

    const byField = { types: 'CardType', rarities: 'Rarity', tags: 'Tag', keywords: 'Keyword', cardIds: 'CardId' };
    return pickDimensionItems(byField[rest]);
  }

  return [];
}

/** 这个多值框的字段名属于哪一份筛选（`pick.` / `srcFilter.` / …）；不是则 null。 */
function filterPrefixOf(field) {
  return Object.keys(FILTER_PREFIXES).find((p) => field.startsWith(p)) || null;
}

/**
 * 多值输入框写回模型。
 *
 * 筛选那几格按前缀分两种：
 *   `…dimensions` —— 它是"启用了哪几维"，**移除一维时要把它清干净**（取值清空、
 *     费用维度的运算符设回不筛选、阈值清零），否则会留下一条看不见的筛选
 *     （那条筛选还在生效，而界面上没有它的行）。
 *   其余 `…<字段>` —— 直接写进那一份筛选的对应字段。
 */
function writeMultiComboValue(eff, field, values) {
  const prefix = filterPrefixOf(field);

  if (prefix) {
    const target = filterTarget(prefix, eff);
    const rest = field.slice(prefix.length);

    if (rest === 'dimensions') {
      applyFilterDimensions(target, values, state.schema.filterDimensions || []);
      return;
    }

    target.filter[rest] = values;
    return;
  }

  eff[field] = values;
}

/** 牌堆的下拉项（多值输入组件用）。 */
function pileKindItems() {
  return (state.schema.piles || []).map((p) => ({ value: p, label: pileName(p) }));
}

/**
 * 选牌器的十步流程表单。
 *
 * 显示哪几步**完全由策略决定**（`meta.pick`，后端按效果种类现算）：
 *   允许的来源（流程一那几档）、允许的维度（流程一/三）、能不能多选牌堆（流程二）、
 *   有没有流程九/十、目标黑名单（附魔那种）。所以"消耗牌屏蔽卡组"这类规则在界面里
 *   没有第二份实现——后端改策略，这里跟着变。
 *
 * 两条"填了也不生效"的格按同一个理由隐去：
 *   "这张牌自己"时没有牌堆、没有筛选，也没有流程九/十（生成器对这条来源直接给 { this }）；
 *   usesMode 为假时（自动打出）流程九/十整个不显示，张数由效果自己的数值给。
 */
function pickForm(eff, meta, mode, owner) {
  // 先看这条效果有没有选牌器（`meta.pick` 由后端按种类现算），**没有就直接返回**——
  // 取选牌配置的动作本身会建一份出来（pickOf 是懒建的），所以顺序反过来会让不挑牌的效果
  // 也长出一份 pick，然后写进 XML。
  const policy = meta.pick;

  if (!policy) {
    return '';
  }

  const pick = pickOf(eff);
  const sources = policy.sources || [];
  const allowed = policy.dimensions || [];

  // ---- 选择范围：只有一个来源下拉（此牌 / 牌堆 / 卡组） ----
  let html = `
    <div class="field">
      <label>${escapeHtml(t('pickFlow1'))}</label>
      <select data-f="pickSource">${sources.map((s) =>
        `<option value="${s}"${s === (pick.source || 'Piles') ? ' selected' : ''}>${escapeHtml(t('pickOption_' + s))}</option>`).join('')}</select>
    </div>`;

  // ---- 选择牌堆（来源是"牌堆"时；按维度筛时它是"在哪些牌堆里找"的范围） ----
  if (pick.source !== 'ThisCard' && pick.source !== 'Deck') {
    if (policy.singlePile) {
      // 只认一个牌堆的效果（自动打出）：空列表在模型里是"全部战斗牌堆"，那种语义在这里
      // 没有意义，所以给一个普通下拉，默认手牌。
      html += `
        <div class="field">
          <label>${escapeHtml(t('pickSinglePile'))}</label>
          <select data-f="pickPile">${pileOptions((pick.piles || [])[0] || 'Hand')}</select>
        </div>`;
    } else {
      html += `
        <div class="field span-2">
          <label>${escapeHtml(t('pickFlow2'))}</label>
          ${MFComponents.multiCombo({
            field: 'pick.piles',
            values: pick.piles || [],
            items: pileKindItems(),
            placeholder: t('pickPilesPlaceholder'),
          })}
          <p class="hint" style="padding:2px 0 0">${escapeHtml(t('pickPilesHint'))}</p>
        </div>`;
    }
  }

  // ---- 筛选维度：**一个多值输入框**，它的值就是"启用了哪几维" ----
  //
  // 维度可以同时生效（与），每一维各自一行。候选只列策略允许的那几维。
  // 这一段与"按条件数牌"共用同一套渲染（见 filterDimensionRow 等）：同一份筛选目标，
  // 只是字段前缀不同（这里 `pick.`）。
  if (pick.source !== 'ThisCard' && allowed.length > 0) {
    const target = filterTarget('pick.', eff);
    const enabled = filterEnabledDimensions(target, allowed);

    html += `
      <div class="field span-2">
        <label>${escapeHtml(t('pickFilter'))}</label>
        ${MFComponents.multiCombo({
          field: 'pick.dimensions',
          values: enabled,
          items: dimensionItems(allowed),
          placeholder: t('pickFilterPlaceholder'),
        })}
        <p class="hint" style="padding:2px 0 0">${escapeHtml(t('pickFilterHint'))}</p>
      </div>`;

    // 每一维各画自己那一行（取值维度画多值框，费用维度画运算符 + 阈值）。
    for (const d of enabled) {
      html += filterDimensionRow(target, d, mode);
    }
  }

  // ---- 临时牌集合：来源选"牌集合ID"时要填"取哪个集合" ----
  //
  // 候选是**这张牌自己定义的集合**（`effect.setId`，由前面某条效果写进去）。
  // 不可多选——一个集合就是一个集合；给出定义它的效果编号，作者一眼看得出读的是哪一条。
  if (pick.source === 'Set') {
    const defined = definedSetsOfCard(owner);

    html += `
      <div class="field span-2">
        <label>${escapeHtml(t('pickSetId'))}</label>
        <input type="text" data-f="pickSetId" list="pick-set-ids" value="${escapeHtml(pick.setId || '')}"
               placeholder="${escapeHtml(t('pickSetIdPlaceholder'))}">
        <datalist id="pick-set-ids">${defined.map((d) =>
          `<option value="${escapeHtml(d.id)}">${escapeHtml(t('pickSetIdOption', { n: d.number }))}</option>`).join('')}</datalist>
        <p class="hint" style="padding:2px 0 0">${escapeHtml(defined.length
          ? t('pickSetIdHint')
          : t('pickSetIdNone'))}</p>
      </div>`;
  }

  // ---- 取牌方式 / 张数 ----
  if (policy.usesMode && pick.source !== 'ThisCard') {
    html += `
      <div class="field">
        <label>${escapeHtml(t('pickFlow9'))}</label>
        <select data-f="pickMode">${(state.schema.pickModes || []).map((m) =>
          `<option value="${m}"${m === (pick.mode || 'Choose') ? ' selected' : ''}>${escapeHtml(t('pickMode_' + m))}</option>`).join('')}</select>
      </div>`;

    // 流程十：张数。
    //
    // 两条路，由效果自己的策略决定（与生成器、XML 读的是同一份）：
    //   张数就是这条效果的数值（消耗牌）—— **不在这里画**：上面「数值」那三格
    //   （基础值 / 变量来源 / 变量系数）本身就是张数，升级改张数也走它们的增量；
    //   张数由选牌器自己带（升级 / 搬运 / 附魔…）—— 画这一组。
    if ((pick.mode || 'Choose') !== 'All' && !policy.countFromEffect) {
      html += pickCountFields(pick, mode, eff);
    }
  }

  // ---- 目标合法性：解释"为什么某些牌不出现在候选里" ----
  if (policy.constraint && policy.constraint !== 'None') {
    html += `
      <p class="hint span-2" style="padding:0">${escapeHtml(t('pickConstraint_' + policy.constraint))}</p>`;
  }

  return html;
}

/** 这一维是不是取值那种（多值框），费用那两维走另一条路（运算符 + 阈值）。 */
function isCostDimension(d) {
  return d === 'EnergyCost' || d === 'StarCost';
}

/** 该维在模型里对应哪个字段（取值维度才用）。选牌与"按条件数牌"的字段名相同。 */
function dimensionField(d) {
  switch (d) {
    case 'CardType': return 'types';
    case 'Rarity': return 'rarities';
    case 'CardId': return 'cardIds';
    case 'Tag': return 'tags';
    case 'Keyword': return 'keywords';
    default: return null;
  }
}

/** 该维对应的运算符字段（费用维度才用）。 */
function dimensionComparisonField(d) {
  return d === 'EnergyCost' ? 'energyComparison' : 'starComparison';
}

/* ------------------------------------------------------------------ 筛选目标 */

/**
 * 一张卡上**每一份多维筛选**，以及它在界面上的字段前缀。
 *
 * 两处用同一套筛选 UI：选牌器（`pick.`，那份配置**本身就是**筛选）与"按条件数牌"
 * （`…Filter.`，筛选挂在那个 ValueSource 的 `filter` 字段上）。两者筛的是同一种东西、
 * 六个维度字段名也相同，所以界面共用一份渲染——各画一套迟早会出现
 * "这边能按稀有度筛、那边不能"这种静默不一致。
 *
 * 计数的筛选可能出现在**五个位置**（效果数值 / 重复次数 / 选牌张数 / 能量阈值 / 星辰阈值），
 * 所以前缀必须各个不同：`data-f` 与多值框的字段名靠它区分，写回时也靠它找到那一份 ValueSource。
 */
const FILTER_PREFIXES = {
  'pick.': (eff) => ({ filter: pickOf(eff), kind: 'pick' }),

  // "从卡池 N 选一"那份筛选：与选牌器同一套维度，但直接挂在效果上（没有来源与范围——
  // 候选来自出牌者的角色卡池，不是运行时牌堆）。
  'poolFilter.': (eff) => ({ filter: eff.poolFilter || (eff.poolFilter = emptyFilter()), kind: 'pick' }),

  // 启用条件两侧的"符合条件的牌数"来源：各自的筛选挂在条件那一侧自己身上。
  'condLeftFilter.': (eff) => ({ source: conditionSideOf(eff, 'left'), kind: 'count' }),
  'condRightFilter.': (eff) => ({ source: conditionSideOf(eff, 'right'), kind: 'count' }),

  'srcFilter.': (eff) => ({ source: effectSourceOf(eff), kind: 'count' }),
  'repeatFilter.': (eff) => ({ source: repeatSourceOf(eff), kind: 'count' }),
  'pickCountFilter.': (eff) => ({ source: pickCountSourceOf(eff), kind: 'count' }),
  'energyFilter.': (eff) => ({ source: energySourceOf(eff), kind: 'count' }),
  'starFilter.': (eff) => ({ source: starSourceOf(eff), kind: 'count' }),
};

/** 这个前缀属于哪一种筛选。认不出来返回 null（调用方据此跳过）。 */
function filterTarget(prefix, eff) {
  const resolve = FILTER_PREFIXES[prefix];
  if (!resolve) return null;

  const found = resolve(eff);

  if (found.kind === 'pick') {
    return { eff, prefix, filter: found.filter, count: false, source: null };
  }

  return {
    eff,
    prefix,
    filter: ensureFilter(found.source),
    count: true,
    source: found.source,
  };
}

/** 一份空筛选的初值（与后端 CardFilter 的默认值一致）。 */
function emptyFilter() {
  return {
    types: [],
    rarities: [],
    cardIds: [],
    tags: [],
    keywords: [],
    energyComparison: 'None',
    starComparison: 'None',
    energyThreshold: 0,
    starThreshold: 0,
  };
}

/** 那一份 ValueSource 上的筛选（缺了就补一份）。 */
function ensureFilter(source) {
  return source.filter || (source.filter = emptyFilter());
}

/* ---- 五个位置各自的 ValueSource（缺了就补一份默认来源）---- */

function effectSourceOf(eff) {
  return eff.source || (eff.source = { actor: 'Self', kind: 'None', perUnit: 1 });
}

function repeatSourceOf(eff) {
  return eff.repeatSource || (eff.repeatSource = { actor: 'Self', kind: 'None', perUnit: 1 });
}

function pickCountSourceOf(eff) {
  const count = pickCountOf(pickOf(eff));
  return count.variable || (count.variable = { actor: 'Self', kind: 'None', perUnit: 1 });
}

function energySourceOf(eff) {
  return eff.energySource || (eff.energySource = { actor: 'Self', kind: 'None', perUnit: 1 });
}

function starSourceOf(eff) {
  return eff.starSource || (eff.starSource = { actor: 'Self', kind: 'None', perUnit: 1 });
}

/**
 * 已经启用了哪几维。
 *
 * 判据分两类（与后端"有没有在筛"同一套读法）：
 *   取值维度 —— 列表非空就是启用；
 *   费用维度 —— 运算符不是 None 就是启用（所以不需要会话表）。
 * 刚启用、还没填值的取值维度先记在**界面自己的会话表**里（WeakMap，按**那份筛选对象**键）——
 * 它是"我正在填这一维"的界面状态，不该写进工程 JSON。按对象键（而不是按效果）才能让
 * 同一张卡上的多份筛选各记各的。
 */
function filterEnabledDimensions(target, allowed) {
  const filter = target.filter;

  const fromContent = allowed.filter((d) => (isCostDimension(d)
    ? (filter[dimensionComparisonField(d)] || 'None') !== 'None'
    : (filter[dimensionField(d)] || []).length > 0));

  // 会话表只记**取值维度**那种"刚启用、还没填值"的界面状态。
  // 费用维度**不靠它**：那一维有没有启用由比较器本身表达（`None` 就是没启用），
  // 所以加进来时 applyFilterDimensions 会立刻把它设成"大于"。
  //
  // 这一点要紧：要是让会话把费用维度也算成启用，就会画出一行**比较器为 None 的行**
  // ——而 `None` 已经不在下拉的候选里了（它是个哨兵，不是给用户选的档）。
  const session = filterSessionFor(target)
    .filter((d) => allowed.includes(d) && !isCostDimension(d));
  const merged = [...fromContent];

  for (const d of session) {
    if (!merged.includes(d)) merged.push(d);
  }

  // 按策略给的顺序排（界面顺序即流程顺序：类型 / 牌 ID / 标签 / 关键词 / 稀有度 / 能量 / 星辰）。
  return allowed.filter((d) => merged.includes(d));
}

/** 界面会话里"已启用但还没填值"的维度（不进工程 JSON）。 */
const dimensionSession = new WeakMap();

function filterSessionFor(target) {
  if (!dimensionSession.has(target.filter)) {
    dimensionSession.set(target.filter, []);
  }

  return dimensionSession.get(target.filter);
}

function setFilterSession(target, list) {
  dimensionSession.set(target.filter, list);
}

/**
 * 把"启用了哪几维"落到模型上：**新启用的留着等作者填，被移除的连值一起清掉**。
 *
 * 取值维度用界面会话表记"已启用但还没填值"；费用维度不用记（运算符本身就能表达启用：
 * 新启用时默认"大于"，移除时设回"不筛选"并把阈值清零）。
 */
function applyFilterDimensions(target, enabled, allowed) {
  const filter = target.filter;
  const before = filterEnabledDimensions(target, allowed);

  // 会话表就是"作者此刻开着哪几行"——它有值之后由内容本身表达，两者取并集即启用集合。
  setFilterSession(target, [...enabled]);

  for (const d of allowed) {
    const shouldEnable = enabled.includes(d);

    if (!shouldEnable && before.includes(d)) {
      clearFilterDimension(target, d);
    }

    if (shouldEnable && !before.includes(d) && isCostDimension(d)) {
      // 新启用的费用维度：默认"大于"（最常见的那一档），阈值留给作者填。
      filter[dimensionComparisonField(d)] = 'GreaterThan';
    }
  }
}

/** 把某一维从模型里清干净（取值 / 运算符 / 阈值）。 */
function clearFilterDimension(target, d) {
  const filter = target.filter;

  if (isCostDimension(d)) {
    filter[dimensionComparisonField(d)] = 'None';

    if (target.count) {
      filter[d === 'EnergyCost' ? 'energyThreshold' : 'starThreshold'] = 0;
    }

    return;
  }

  filter[dimensionField(d)] = [];
}

/** 某一维那一行（取值维度：多值框；费用维度：运算符 + 阈值）。 */
function filterDimensionRow(target, d, mode) {
  if (isCostDimension(d)) {
    return filterCostFields(target, d, mode);
  }

  const field = dimensionField(d);
  const labelKeys = {
    CardType: 'pickFlow4', Rarity: 'pickFlow5', CardId: 'pickFlow6',
    Tag: 'pickFlow7', Keyword: 'pickFlow8',
  };

  return `
    <div class="field span-2">
      <label>${escapeHtml(t(labelKeys[d]))}</label>
      ${MFComponents.multiCombo({
        field: `${target.prefix}${field}`,
        values: target.filter[field] || [],
        items: pickDimensionItems(d),
        placeholder: t('pickValuePlaceholder'),
      })}
    </div>`;
}

/**
 * 费用维度那一行：**运算符单选** +（不是 X 时）阈值。
 *
 * 运算符两处都住在筛选自己身上（它是筛选时的条件运算）。阈值分两路：
 *   选牌 —— 住在效果自己的数值里（占变量槽、能升级），所以那几格读 eff；
 *   计数 —— 住在筛选自己身上，是一个**常数**（谓词里引用不到局部变量）。
 */
function filterCostFields(target, d, mode) {
  const comparisonField = dimensionComparisonField(d);
  const current = target.filter[comparisonField] || 'None';

  // 标签按维度分开写（"能量比较方式" / "星辰比较方式"）：两个费用维度各占一行，
  // 都写"比较方式"的话，读的人得回头看是哪一行才知道在比什么。
  // 这两个键与同排的阈值标签（costEnergyBase / costStarBase…）同一套命名。
  const comparisonLabel = d === 'EnergyCost' ? 'costEnergyComparison' : 'costStarComparison';

  const comparison = `
    <div class="field">
      <label>${escapeHtml(t(comparisonLabel))}</label>
      <select data-f="${target.prefix}${comparisonField}">${(state.schema.costComparisons || []).map((c) =>
        `<option value="${c}"${c === current ? ' selected' : ''}>${escapeHtml(t('costOp_' + c))}</option>`).join('')}</select>
    </div>`;

  // 选 X 时不读阈值（"填了不生效"的格一律不显示），这一维就只有这一格。
  //
  // 只会是「是 X」：这一行**只在那一维启用时**才画，而费用维度启用就是比较器不是 `None`
  // （见 filterEnabledDimensions）——`None` 因此到不了这里，它也不在下拉的候选里。
  if (current === 'IsX') {
    return comparison;
  }

  // 顺序：**先说"比什么数"（阈值那几格），再说"怎么比"（比较方式）**。
  // 比较方式排在这一维的最后（阈值 → 升级增量 → 变量来源 → 变量系数 → 比较方式）。
  return (target.count ? countThresholdFields(target, d) : costThresholdFields(target.eff, d, mode))
    + comparison;
}

/**
 * 计数那一侧的阈值：**一个数字**（不是三件套）。
 *
 * 生成的是 `Count(c => … c.EnergyCost.Canonical > 2 …)`——lambda 里引用不到外面算好的
 * 局部变量，所以这里只能写常数。要"大于 手牌数"那种写法得用选牌那一侧。
 */
function countThresholdFields(target, d) {
  const energy = d === 'EnergyCost';
  const key = energy ? 'energyThreshold' : 'starThreshold';

  return `
    <div class="field">
      <label>${escapeHtml(t(energy ? 'countEnergyThreshold' : 'countStarThreshold'))}</label>
      <input type="text" inputmode="numeric" data-f="${target.prefix}${key}"
             value="${target.filter[key] ?? 0}">
    </div>`;
}

/**
 * "从哪个范围数"那一格（只在计数那一侧有：它是 ValueSource 上的字段，不是筛选上的）。
 */
function countScopeField(target, mode) {
  const source = target.source || {};

  return `
    <div class="field">
      <label>${escapeHtml(t('countScope'))}</label>
      <select data-f="${target.prefix}scope">${(state.schema.countScopes || []).map((c) =>
        `<option value="${c}"${c === (source.countScope || 'AllPiles') ? ' selected' : ''}>${escapeHtml(t('countScope_' + c))}</option>`).join('')}</select>
    </div>`;
}

/**
 * "按条件数牌"那一整块：**范围 + 多维筛选**（与选牌器同一套维度）。
 *
 * <paramref name="allowed"/> 传 null 时用 schema 里全部七维——计数的条件不挑来源，
 * 七个维度都能用（与选牌器那边按效果策略收窄不同）。
 */
function countFilterSection(target, mode, allowed) {
  const dims = allowed || state.schema.filterDimensions || [];
  const enabled = filterEnabledDimensions(target, dims);

  // "范围"那一格只对**计数**那几份筛选有意义（手牌/牌堆/…）：选牌那份与"从卡池筛"
  // 那份没有 source，也就没有范围可言（选牌的范围在它自己的流程一/二里）。
  let html = target.source ? countScopeField(target, mode) : '';

  html += `
    <div class="field span-2">
      <label>${escapeHtml(t('pickFilter'))}</label>
      ${MFComponents.multiCombo({
        field: `${target.prefix}dimensions`,
        values: enabled,
        items: dimensionItems(dims),
        placeholder: t('pickFilterPlaceholder'),
      })}
      <p class="hint" style="padding:2px 0 0">${escapeHtml(t('countFilterHint'))}</p>
    </div>`;

  for (const d of enabled) {
    html += filterDimensionRow(target, d, mode);
  }

  return html;
}

/**
 * 启用条件的取值：没有就按默认形状补一份（与后端 EffectCondition 的默认一致）。
 * 两侧的来源也走这里拿（缺了就补），所以处理器与筛选前缀都不必判空。
 */
function conditionOf(eff) {
  return eff.condition || (eff.condition = {
    comparison: 'AtLeast',
    leftAmount: 0,
    leftSource: { actor: 'Self', kind: 'None', perUnit: 1 },
    rightAmount: 1,
    rightSource: { actor: 'Self', kind: 'None', perUnit: 1 },
    leftUpgrade: 0,
    leftPerUnitUpgrade: 0,
    rightUpgrade: 0,
    rightPerUnitUpgrade: 0,
  });
}

/** 条件某一侧的来源（缺了就补一份默认）。 */
function conditionSideOf(eff, side) {
  const cond = conditionOf(eff);
  const key = side === 'left' ? 'leftSource' : 'rightSource';
  return cond[key] || (cond[key] = { actor: 'Self', kind: 'None', perUnit: 1 });
}

/**
 * 启用条件窗口：左值组 → 比较方式 → 右值组（比较方式夹在两个值**中间**，
 * 读起来就是"左值 ≥ 右值"这个顺序）。没有启用时什么都不画。
 *
 * 这一组画在**效果选项之前**（效果 / 触发时机 / 目标那一行的上面），画完接一条
 * 分隔线与效果本身的选项隔开（见 renderEffectForm 的网格模板）。
 */
function conditionWindow(eff, mode) {
  if (!eff.condition) {
    return '';
  }

  return conditionSideFields(eff, 'left', mode)
    + comparisonField(eff)
    + conditionSideFields(eff, 'right', mode);
}

/** 比较方式那一格（六档，标签来自界面语言表）。 */
function comparisonField(eff) {
  const cond = conditionOf(eff);

  return `
    <div class="field">
      <label>${escapeHtml(t('condComparison'))}</label>
      <select data-f="condComparison">${(state.schema.conditionComparisons || []).map((c) =>
        `<option value="${c}"${c === (cond.comparison || 'AtLeast') ? ' selected' : ''}>${escapeHtml(t('condOp_' + c))}</option>`).join('')}</select>
    </div>`;
}

/**
 * 条件一侧的那组格子：基础值 → 升级量 → 来源 →（选了来源才有）状态/取自谁/系数/系数升级/
 * 数牌条件——与效果数值、费用阈值那两组完全同构。
 */
function conditionSideFields(eff, side, mode) {
  const cond = conditionOf(eff);
  const source = conditionSideOf(eff, side);
  const sourceUsed = !!source.kind && source.kind !== 'None';
  const cap = side === 'left' ? 'Left' : 'Right';
  const baseLabel = side === 'left' ? 'condLeftBase' : 'condRightBase';
  const sourceLabel = side === 'left' ? 'condLeftSource' : 'condRightSource';
  const perUnitLabel = side === 'left' ? 'condLeftPerUnit' : 'condRightPerUnit';
  const upgradePerUnitLabel = side === 'left' ? 'upgradeCondLeftPerUnit' : 'upgradeCondRightPerUnit';

  let html = `
    <div class="field">
      <label>${escapeHtml(t(baseLabel))}</label>
      <input type="text" inputmode="numeric" data-f="cond${cap}Amount" value="${cond[side + 'Amount'] ?? 0}">
    </div>
    <div class="field">
      <label>${escapeHtml(t('upgradeCond' + cap))}</label>
      <input type="text" inputmode="numeric" data-f="cond${cap}Upgrade" value="${cond[side + 'Upgrade'] ?? 0}">
    </div>
    <div class="field">
      <label>${escapeHtml(t(sourceLabel))}</label>
      <select data-f="cond${cap}SourceKind">${valueSourceOptions(source)}</select>
    </div>`;

  if (source.kind === 'PowerStacks') {
    html += `
    <div class="field">
      <label>${escapeHtml(t('sourcePower'))}</label>
      ${idCombo(`cond${cap}SourcePowerId`, source.powerId, powerComboItems(), t('pickPower'))}
    </div>`;
  }

  if (sourceUsed) {
    html += `
    ${actorOptions(source, mode, `cond${cap}SourceActor`, eff.target)}
    <div class="field">
      <label>${escapeHtml(t(perUnitLabel))}</label>
      <input type="text" inputmode="decimal" data-f="cond${cap}PerUnit" value="${source.perUnit ?? 1}">
    </div>
    <div class="field">
      <label>${escapeHtml(t(upgradePerUnitLabel))}</label>
      <input type="text" inputmode="decimal" data-f="cond${cap}PerUnitUpgrade" value="${cond[side + 'PerUnitUpgrade'] ?? 0}">
    </div>`;

    if (source.kind === 'FilteredCardCount') {
      html += countFilterSection(filterTarget(`cond${cap}Filter.`, eff), mode);
    }
  }

  return html;
}

/** 费用阈值的三件套（基础值 / 变量来源 / 变量系数），与其它三件套同一套取值来源。 */
function costThresholdFields(eff, d, mode) {
  const energy = d === 'EnergyCost';

  // 模型上的字段：EnergyAmount / EnergySource（星辰同理，见 EffectDef）。
  const amountKey = energy ? 'energyAmount' : 'starAmount';
  const sourceKey = energy ? 'energySource' : 'starSource';
  const source = eff[sourceKey] || {};
  const sourceUsed = !!source.kind && source.kind !== 'None';

  const baseLabel = energy ? 'costEnergyBase' : 'costStarBase';
  const sourceLabel = energy ? 'costEnergySource' : 'costStarSource';
  const perUnitLabel = energy ? 'costEnergyPerUnit' : 'costStarPerUnit';

  // 升级那两格与阈值同住一行：它们是这个数值的增量（效果数值那块的"升级时基础值 +"、
  // "升级时变量系数 +"同理）。挂在选牌器这一侧是因为**它们只在筛费用时才有意义**——
  // 没筛费用时显示出来就是两格"填了不生效"的输入框。
  //
  // 基础值与变量系数**两个都有升级格**：阈值的两个数各自占一个卡上的变量
  // （EnergyAmount / EnergyAmountExtra），所以两个增量都落得下去。
  const upgradeKey = energy ? 'upgradeEnergy' : 'upgradeStar';
  const upgradeLabel = energy ? 'upgradeCostEnergy' : 'upgradeCostStar';
  const upgradePerUnitKey = energy ? 'upgradeEnergyPerUnit' : 'upgradeStarPerUnit';
  const upgradePerUnitLabel = energy ? 'upgradeCostEnergyPerUnit' : 'upgradeCostStarPerUnit';

  let html = `
    <div class="field">
      <label>${escapeHtml(t(baseLabel))}</label>
      <input type="text" inputmode="numeric" data-f="${amountKey}" value="${eff[amountKey] ?? 0}">
    </div>
    <div class="field">
      <label>${escapeHtml(t(upgradeLabel))}</label>
      <input type="text" inputmode="numeric" data-f="${upgradeKey}"
             value="${((eff.upgrade || {})[energy ? 'energyDelta' : 'starDelta']) ?? 0}">
    </div>
    <div class="field">
      <label>${escapeHtml(t(sourceLabel))}</label>
      <select data-f="${sourceKey}Kind">${valueSourceOptions(source)}</select>
    </div>`;

  if (source.kind === 'PowerStacks') {
    html += `
    <div class="field">
      <label>${escapeHtml(t('sourcePower'))}</label>
      ${idCombo(`${sourceKey}PowerId`, source.powerId, powerComboItems(), t('pickPower'))}
    </div>`;
  }

  if (sourceUsed) {
    html += actorOptions(source, mode, `${sourceKey}Actor`, eff.target);

    html += `
    <div class="field">
      <label>${escapeHtml(t(perUnitLabel))}</label>
      <input type="text" inputmode="decimal" data-f="${sourceKey}PerUnit" value="${source.perUnit ?? 1}">
    </div>
    <div class="field">
      <label>${escapeHtml(t(upgradePerUnitLabel))}</label>
      <input type="text" inputmode="decimal" data-f="${upgradePerUnitKey}"
             value="${((eff.upgrade || {})[energy ? 'energyPerUnitDelta' : 'starPerUnitDelta']) ?? 0}">
    </div>`;

    // 阈值本身也是"算出来的"时，它的来源同样可能是"按条件数牌"——那就要它的条件与范围。
    if (source.kind === 'FilteredCardCount') {
      html += countFilterSection(filterTarget(energy ? 'energyFilter.' : 'starFilter.', eff), mode);
    }
  }

  return html;
}

/**
 * 流程十：张数。三件套（基础值 + 变量 × 变量系数），与效果数值那一块同一套取值来源，
 * 所以"随机取等于手牌数的牌"这种也填得出来。
 */
function pickCountFields(pick, mode, eff) {
  const count = pickCountOf(pick);
  const source = count.variable || (count.variable = { actor: 'Self', kind: 'None', perUnit: 1 });
  const sourceUsed = !!source.kind && source.kind !== 'None';

  let html = `
    <div class="field">
      <label>${escapeHtml(t('pickCountBase'))}</label>
      <input type="text" inputmode="numeric" data-f="pickCountBase" value="${count.base ?? 1}">
    </div>
    <div class="field">
      <label>${escapeHtml(t('pickCountSource'))}</label>
      <select data-f="pickCountSourceKind">${valueSourceOptions(source)}</select>
    </div>`;

  if (source.kind === 'PowerStacks') {
    html += `
      <div class="field">
        <label>${escapeHtml(t('sourcePower'))}</label>
        ${idCombo('pickCountSourcePowerId', source.powerId, powerComboItems(), t('pickPower'))}
      </div>`;
  }

  if (sourceUsed) {
    html += actorOptions(source, mode, 'pickCountSourceActor', eff.target);

    html += `
      <div class="field">
        <label>${escapeHtml(t('pickCountPerUnit'))}</label>
        <input type="text" inputmode="decimal" data-f="pickCountPerUnit" value="${source.perUnit ?? 1}">
      </div>`;

    // "符合条件的牌数"这个来源要再选范围与条件（多维，与选牌器同一套），其它来源不需要。
    if (source.kind === 'FilteredCardCount') {
      html += countFilterSection(filterTarget('pickCountFilter.', eff), mode);
    }
  }

  return html;
}

/* -------------------------------------------------- 移除状态的两个选项 */

/**
 * 移除状态那两个选项的当前取值（缺省与后端模型默认值一致：整个移除 + 按 id 指定）。
 *
 * 判据只有这一处：数值那一组要不要显示、状态那一格要不要显示，都读它——
 * 各写一份的话，"隐去了不该隐的格"这种偏差只会在界面上表现为少一格。
 */
function powerFields(eff, meta) {
  if (!meta || !meta.hasPowerRemoval) {
    return { removal: 'All', selection: 'PowerId' };
  }

  return {
    removal: eff.removal || 'All',
    selection: eff.selection || 'PowerId',
  };
}

/**
 * 移除状态的两个下拉。
 *
 * 候选由后端按枚举下发（`state.schema.powerRemovals` / `powerSelections`），
 * 文案是 `powerRemoval_<档>` / `powerSelection_<档>`——与选牌器那两个下拉同一个做法，
 * 界面不自己维护一份枚举名清单。
 */
function powerOptionFields(eff) {
  const current = powerFields(eff, { hasPowerRemoval: true });

  const options = (values, prefix) => (values || []).map((v) =>
    `<option value="${v}"${v === current[prefix] ? ' selected' : ''}>${escapeHtml(t((prefix === 'removal' ? 'powerRemoval_' : 'powerSelection_') + v))}</option>`).join('');

  return `
    <div class="field">
      <label>${escapeHtml(t('powerRemoval'))}</label>
      <select data-f="removal">${options(state.schema.powerRemovals, 'removal')}</select>
    </div>
    <div class="field">
      <label>${escapeHtml(t('powerSelection'))}</label>
      <select data-f="selection">${options(state.schema.powerSelections, 'selection')}</select>
    </div>`;
}

/* -------------------------------------------------- 效果行 */

/** 界面认识这个效果种类吗（= schema 里有没有它这一条）。 */
function isEffectKindKnown(kind) {
  return (state.schema.effects || []).some((e) => e.kind === kind);
}

/**
 * 还没重构的效果：只读的一行。
 *
 * 生成与描述那一侧支持全部 31 种效果，所以卡照常能导出；这里不给编辑字段，是因为没有这个
 * 种类的元数据（该显示哪些格、允许多少取值），猜出来的表单会写错东西。
 * 想改成已重构的种类，用这一行里的下拉；想删/挪，用行头那几个按钮。
 */
function renderUnknownKindRow(eff, effects, i) {
  const row = document.createElement('div');
  row.className = 'card-row';

  row.innerHTML = `
    <div class="row-head">
      <span class="row-title">${escapeHtml(eff.kind)}</span>
      <div class="row-actions">
        <button class="btn btn-small" data-act="up" ${i === 0 ? 'disabled' : ''}>↑</button>
        <button class="btn btn-small" data-act="down" ${i === effects.length - 1 ? 'disabled' : ''}>↓</button>
        <button class="btn btn-small btn-danger" data-act="del">${escapeHtml(t('remove'))}</button>
      </div>
    </div>
    <div class="grid">
      <div class="field">
        <label>${escapeHtml(t('effect'))}</label>
        <select data-f="kind">${effectKindOptions(eff.kind)}</select>
      </div>
      <p class="hint span-2" style="padding:0">${escapeHtml(t('effectNotMigrated'))}</p>
    </div>
    <div class="effects-summary" data-summary-for="${i}"></div>`;

  row.querySelector('[data-f="kind"]').addEventListener('change', (e) => {
    eff.kind = e.target.value;
    refreshAll();
  });

  row.querySelectorAll('[data-act]').forEach((btn) => {
    btn.addEventListener('click', () => {
      const act = btn.dataset.act;
      if (act === 'del') effects.splice(i, 1);
      else if (act === 'up' && i > 0) effects.splice(i - 1, 0, effects.splice(i, 1)[0]);
      else if (act === 'down' && i < effects.length - 1) effects.splice(i + 1, 0, effects.splice(i, 1)[0]);
      refreshAll();
    });
  });

  return row;
}

/**
 * 渲染一组效果行。卡牌、状态触发、共享效果三处共用，所以字段的显示规则只有一处实现。
 *
 * 显示哪些字段由 schema 决定（是否有数值、是否需要指定状态/卡牌/牌堆），
 * 所以后端加一个效果种类时，界面会自动跟上。
 */
function renderEffectRows(effects, host, owner, mode) {
  host.innerHTML = '';
  effects.forEach((eff, i) => {
    // 共享效果引用没有内联字段：行为以定义为准，所以这一行只展示它指向什么。
    if (eff.sharedEffectId) {
      host.appendChild(renderSharedRefRow(eff, effects, i));
      return;
    }

    // 迁移期间 schema 只列**已经重构过**的效果，而旧工程里什么种类都有。那种效果生成与描述
    // 照常（那一侧支持全部 31 种），但界面不认识它的字段——给它一行只读的，别拿别的种类的
    // 元数据去渲染它（面板显示一套、数据是另一套，改完存盘才发现改错了东西）。
    if (!isEffectKindKnown(eff.kind)) {
      host.appendChild(renderUnknownKindRow(eff, effects, i));
      return;
    }

    const meta = schemaEffect(eff.kind);
    const row = document.createElement('div');
    row.className = 'card-row';

    /*
     * 作用对象那一格：候选与**标签**都由后端按这条效果的作用域给
     * （造物轴七档 / 玩家轴五档 / 作用于牌只有"自己"）。
     *
     * 「目标」那一档（= 继承这张牌的目标）**始终列出来**，只是在这张牌给不出合用的对象时
     * 禁用并说明原因（见 targetOptionsOf / targetField）。三种情形：
     *   卡牌页 —— 卡牌目标是"任意敌人/任意友方"才可用（要哪种由后端的 playedTargetSources 说）；
     *   状态页 —— 永远不可用（状态没有出牌目标），那格传 null 进去；
     *   共享效果 —— 不判（引用它的卡是什么目标，这里还看不到），所以传 null 表示可用。
     */
    const targetContext = mode === 'power'
      ? { kind: 'power' }
      : mode === 'shared' ? { kind: 'shared' } : { kind: 'card', target: owner?.target };


    // 数值是不是算出来的：由"有没有选来源"决定，与后端 EffectDef.IsCalculated 同一规则。
    const calculated = !!(eff.source && eff.source.kind && eff.source.kind !== 'None');

    // 触发时机只对卡牌有意义（状态自带触发设置）。
    const isTurnTrigger = eff.trigger === 'StartOfTurn' || eff.trigger === 'EndOfTurn';

    /*
     * "打出一张牌时"要说明两件事：哪些牌算数（按标签 / 关键词 / 类型 / 指定卡 / 不限），
     * 以及要不要攒够几张才触发（计数器）。
     *
     * 与伤害增益、附魔共用同一组筛选——它们本来就是同一件事："哪些牌"的判定方式。
     * "不限"时不必显示筛选下拉（那是默认语义），但计数器仍然要显示。
     */
    const isCardPlayedTrigger = eff.trigger === 'CardPlayed';

    const playedFilterField = isCardPlayedTrigger ? `
      <div class="field">
        <label>${escapeHtml(t('cardFilter'))}</label>
        <select data-f="cardFilter">${(state.schema.cardFilters || []).map(
          (f) => `<option value="${f}"${f === (eff.cardFilter || 'Any') ? ' selected' : ''}>${escapeHtml(t('filter_' + f))}</option>`
        ).join('')}</select>
      </div>` : '';

    // 计数器：不勾选就是"每张符合条件的牌各触发一次"（原版踩踏、女妖之嚎），
    // 勾选则攒够 N 张触发一次并归零。
    const counterFields = isCardPlayedTrigger ? `
      <div class="field check">
        <input type="checkbox" data-f="useCounter" ${eff.useCounter ? 'checked' : ''}>
        <label>${escapeHtml(t('useCounter'))}</label>
      </div>
      ${eff.useCounter ? `
      <div class="field">
        <label>${escapeHtml(t('counterTarget'))}</label>
        <input type="text" inputmode="numeric" data-f="counterTarget" value="${eff.counterTarget ?? 3}">
      </div>
      <div class="field">
        <label>${escapeHtml(t('counterReset'))}</label>
        <select data-f="counterReset">${namedOptionList('CounterReset', state.schema.counterResets, eff.counterReset)}</select>
      </div>` : ''}` : '';

    const triggerSelect = `
      <div class="field">
        <label>${escapeHtml(t('trigger'))}</label>
        <select data-f="trigger">${namedOptionList('EffectTrigger', state.schema.triggers, eff.trigger)}</select>
      </div>
      ${isTurnTrigger ? `
      <div class="field">
        <label>${escapeHtml(t('pileScope'))}</label>
        <select data-f="pileScope">${namedOptionList('PileScope', state.schema.pileScopes, eff.pileScope)}</select>
      </div>` : ''}
      ${playedFilterField}
      ${counterFields}`;

    // 数值区：声明了数值的效果才显示。计算型换成"基础值 + 来源 + 每单位"。
    //
    // 计算型的"基础值"**一直显示**，没有勾选框：绝大多数的计算型就是"单位数 × 系数"，
    // 那种情况下这个数留 0 即可（原版"全身撞击"是 0 + 1 × 格挡，"完美打击"是 6 + 2 × 打击牌数）。
    // 早先默认藏起来是因为"一张牌只有一个基础值槽"这条旧限制，那条限制已经没有了
    // （每个计算型效果各占一个基础值变量，见 EffectResolver.EffectSlots），
    // 再拿勾选框挡着只会让人以为"基础值不能填"。
    // 会挑牌的效果才有一份选牌配置——其余种类连字段都不该有（见 defaultEffect 的注释：
    // 带上了会写进 XML）。所以这里**只在真的有** `meta.pick` 时才去取它，
    // 取的动作本身不会凭空造一份出来（那正是当初漏进 XML 的原因）。
    const pick = meta.pick ? pickOf(eff) : null;

    // 有些"作用对象"下这一组整个没有意义——例如"自动打出"选了"这张牌自己"时，
    // 打出的是这张牌本身，没有"打几张"、也没有"数值方式"可言
    // （策略里的 selfNoAmount，见 CardPickPolicies.For(AutoPlay)）。
    //
    // "移除状态"另有一条：选"整个移除"时数值不参与（整层移除没有"减几层"可言），
    // 所以数值那三格也隐去——填了不生效的格一律不显示，与本文件其它地方同一条规则。
    const amountMuted = !!(pick && meta.pick.selfNoAmount && pick.source === 'ThisCard') ||
      (eff.kind === 'RemovePower' && powerFields(eff, meta).removal === 'All');

    // "数值即张数"的效果还有个反向情形：张数用不上时那三格一起隐去——流程九选"全部"时
    // 整堆都动，流程一选"这张牌自己"时只有一张（两者都不读那个数）。
    // （填了不生效的格一律不显示，与本文件其它地方同一条规则。）
    const countUnused = !!(pick && meta.pick.countFromEffect &&
      meta.pick.usesMode && (pick.mode === 'All' || pick.source === 'ThisCard'));

    let amountFields = '';
    if (meta.amount && !amountMuted && !countUnused) {
      // 「效果基础值」——固定值时它就是全部数值；算了来源时它是算式里固定那一段。
      // 有三处这一格的含义特殊，各给一个叫法，否则用户看不出它填的是什么：
      //   附魔   —— 层数（Enchant 的第二个参数）
      //   重放   —— 额外打几次
      //   消耗牌 —— 消耗几张（它的 base 值就是张数，见 CardPickPolicy.CountFromEffect）
      // 升级增量**紧跟**这一格，因为升级的就是它（早先排在来源/每单位后面，读起来像是升级别的）。
      const labelKeys = {
        EnchantCardsInPile: ['enchantLayers', 'upgradeEnchantLayers'],
        AddReplay: ['addReplayTimes', 'upgradeAddReplayTimes'],
      };

      // "数值即张数"的效果整组都叫"张数"（基础值 / 变量来源 / 变量系数），
      // 与流程十同名——那三格本来就承担了流程十。
      const countLike = !!meta.pick && meta.pick.countFromEffect;

      const [amountLabel, upgradeLabel] = countLike
        ? ['pickCountBase', 'upgradePickCount']
        : (labelKeys[eff.kind] ?? ['baseAmount', 'upgradeBase']);

      const sourceLabel = countLike ? 'pickCountSource' : 'valueSource';
      const perUnitLabel = countLike ? 'pickCountPerUnit' : 'perUnit';

      amountFields = `
        <div class="field">
          <label>${escapeHtml(t(amountLabel))}</label>
          <input type="text" inputmode="numeric" data-f="amount" value="${eff.amount ?? 0}">
        </div>
        <div class="field">
          <label>${escapeHtml(t(upgradeLabel))}</label>
          <input type="text" inputmode="numeric" data-f="upgrade" value="${(eff.upgrade || {}).amountDelta ?? 0}">
        </div>`;

      // 来源选「无」＝固定值，所以"取自谁"、"每单位"、"升级时每单位"这些都不显示。
      const sourceKind = (eff.source || {}).kind;
      const sourceUsed = !!sourceKind && sourceKind !== 'None';

      // 选了来源就是算出来的。来源那一格只显示名字（说明另起一行），否则分组标签太长。
      amountFields += `
        <div class="field">
          <label>${escapeHtml(t(sourceLabel))}</label>
          <select data-f="sourceKind">${valueSourceOptions(eff.source)}</select>
        </div>`;

      // "状态层数"要紧接着选**是哪个状态**。原版状态与本工程的状态在同一张表里，
      // 所以自定义状态也能当来源用。
      if (sourceKind === 'PowerStacks') {
        amountFields += `
        <div class="field">
          <label>${escapeHtml(t('sourcePower'))}</label>
          ${idCombo('sourcePowerId', eff.source?.powerId, powerComboItems(), t('pickPower'))}
        </div>`;
      }

      if (sourceUsed) {
        amountFields += `
        ${actorOptions(eff.source, mode, 'sourceActor', eff.target)}
        <div class="field">
          <label>${escapeHtml(t(perUnitLabel))}</label>
          <input type="text" inputmode="decimal" data-f="perUnit" value="${eff.source?.perUnit ?? 1}">
        </div>
        <div class="field">
          <label>${escapeHtml(t('upgradePerUnit'))}</label>
          <input type="text" inputmode="decimal" data-f="upgradePerUnit" value="${(eff.upgrade || {}).perUnitDelta ?? 0}">
        </div>`;

        // "符合条件的牌数"这个来源要再选范围与条件（多维，与选牌器同一套）。
        if (sourceKind === 'FilteredCardCount') {
          amountFields += countFilterSection(filterTarget('srcFilter.', eff), mode);
        }
      }
    }

    /*
     * 重复打出次数。
     *
     * 与数值一样，这里也**不再用勾选框**区分固定/计算：直接显示"次数来源"，
     * 选「无」就是固定次数。少一个开关，也少一种"勾没勾"的状态要对。
     *
     * 次数来源与数值来源是两套独立的东西（原版"扯碎"的伤害固定 5、次数才是算出来的），
     * 所以这里的每一个字段都读写 repeatBase / repeatSource。
     *
     * 名字按效果分："自动打出"重复的是**打出**（不重新选牌），所以叫「重复打出次数」；
     * 其它效果重复的是整条效果，仍叫「重复次数」。两处的语义不同，措辞就不该相同。
     */
    const isAutoPlay = eff.kind === 'AutoPlay';
    const repeatCalculated = !!eff.calculatedRepeat;

    // 与数值那块完全对称：基础值 → 它的升级量 → 来源 → （选了来源才有）取自谁 / 每单位。
    //
    // 次数有两套字段（固定次数读 Repeat、算出来的次数读 RepeatBase），所以这一格的绑定
    // 随"有没有来源"切换——但**标签是同一个**：用户眼里它就是"几次"这个次数，
    // 只是换成了算出来的而已。
    const repeatLabel = isAutoPlay ? 'repeatPlays' : 'repeat';
    const upgradeRepeatLabel = isAutoPlay ? 'upgradeRepeatPlays' : 'upgradeRepeat';

    const repeatFields = `
      <div class="field">
        <label>${escapeHtml(t(repeatLabel))}</label>
        <input type="text" inputmode="numeric" data-f="${repeatCalculated ? 'repeatBase' : 'repeat'}"
               value="${repeatCalculated ? (eff.repeatBase ?? 1) : (eff.repeat ?? 1)}">
      </div>
      <div class="field">
        <label>${escapeHtml(t(upgradeRepeatLabel))}</label>
        <input type="text" inputmode="numeric" data-f="upgradeRepeat" value="${(eff.upgrade || {}).repeatDelta ?? 0}">
      </div>
      <div class="field">
        <label>${escapeHtml(t('repeatSource'))}</label>
        <select data-f="repeatSourceKind">${valueSourceOptions(eff.repeatSource)}</select>
      </div>
      ${(eff.repeatSource || {}).kind === 'PowerStacks' ? `
      <div class="field">
        <label>${escapeHtml(t('sourcePower'))}</label>
        ${idCombo('repeatSourcePowerId', eff.repeatSource?.powerId, powerComboItems(), t('pickPower'))}
      </div>` : ''}
      ${repeatCalculated ? `
      ${actorOptions(eff.repeatSource, mode, 'repeatSourceActor', eff.target)}
      <div class="field">
        <label>${escapeHtml(t('repeatPerUnit'))}</label>
        <input type="text" inputmode="decimal" data-f="repeatPerUnit" value="${eff.repeatSource?.perUnit ?? 1}">
      </div>
      <div class="field">
        <label>${escapeHtml(t('upgradeRepeatPerUnit'))}</label>
        <input type="text" inputmode="decimal" data-f="upgradeRepeatPerUnit"
               value="${(eff.upgrade || {}).repeatPerUnitDelta ?? 0}">
      </div>` : ''}
      ${(eff.repeatSource || {}).kind === 'FilteredCardCount'
        ? countFilterSection(filterTarget('repeatFilter.', eff), mode) : ''}`;

    /*
     * 指定卡牌：本项目或原版卡。
     *
     * "什么时候该显示挑牌框"分两种：
     *
     *   加入指定卡牌   一直要挑（效果本身就是"加入某张牌"）
     *   变化成指定牌   先选"变成什么"，只有选到"指定牌"时才挑
     *   打出一张牌时   先筛（按标签/关键词/类型/指定卡），筛到"指定卡牌"才挑
     *
     * 会挑牌的那几条（附魔 / 伤害增益 / 消耗 / 升级…）**不在这里**：它们的"牌 ID"
     * 是选牌器流程六那一格（见 pickForm），由多值输入框承担，能填多个。
     */
    const filterDrivesCardPick = isCardPlayedTrigger;

    /*
     * "加入指定卡牌"有**生成方式**：指定牌（要挑牌名）或从卡池 N 选一
     * （不挑牌名，改填候选数与多维筛选）。两种方式读的格完全不同，所以
     * 挑牌框的显隐先按方式分支。
     */
    const poolMode = meta.hasAddMode && (eff.addMode || 'Named') === 'ChooseFromPool';

    const wantsCardPick = filterDrivesCardPick
      ? eff.cardFilter === 'SpecificCard'
      : meta.hasAddMode
        ? !poolMode
        : meta.needsTransform
          ? eff.transformInto === 'SpecificCard'
          : !!meta.needsCard;

    // 升级版开关只对"指定了一张具体的牌"有意义："打出一张牌时"按筛选条件走，不需要它。
    const showUpgraded = wantsCardPick && !filterDrivesCardPick;

    const cardField = wantsCardPick ? `
      <div class="field span-2">
        <label>${escapeHtml(t('namedCard'))}</label>
        ${idCombo('cardId', eff.cardId, cardComboItems(), t('pickCard'))}
      </div>
      ${showUpgraded ? `
      <div class="field check">
        <input type="checkbox" data-f="upgraded" ${eff.upgraded ? 'checked' : ''}>
        <label>${escapeHtml(t('upgradedCard'))}</label>
      </div>` : ''}` : '';

    /*
     * "加入指定卡牌"的生成方式与"从卡池 N 选一"那一组：
     *
     *   指定牌   —— 只显示牌名（+ 升级版）。
     *   N 选一   —— 显示候选数（1 = 随机加入，2~3 = 弹游戏的选牌界面）与一份多维筛选
     *               （与选牌器同一套维度；没有"范围"格，因为候选来自角色卡池）。
     *
     * 候选数 ≥ 2 时效果数值不参与（每轮固定加入一张选中的牌）——数值那三格仍显示，
     * 校验器会就这一点提醒；这里给一行小字把原因说清楚。
     */
    const addModeField = meta.hasAddMode ? `
      <div class="field">
        <label>${escapeHtml(t('addMode'))}</label>
        <select data-f="addMode">${(state.schema.addCardModes || ['Named', 'ChooseFromPool']).map((m) =>
          `<option value="${m}"${(eff.addMode || 'Named') === m ? ' selected' : ''}>${escapeHtml(t('addMode_' + m))}</option>`).join('')}</select>
      </div>` : '';

    const poolFields = poolMode ? `
      <div class="field">
        <label>${escapeHtml(t('poolCandidates'))}</label>
        <input type="number" min="1" max="3" data-f="candidates" value="${eff.candidates ?? 3}">
        <p class="hint">${escapeHtml(t('poolCandidatesHint'))}</p>
      </div>
      ${countFilterSection(filterTarget('poolFilter.', eff), mode)}` : '';

    // 生成充能球要选球种（闪电/冰霜/黑暗/等离子/玻璃）。
    const orbField = meta.needsOrb ? `
      <div class="field">
        <label>${escapeHtml(t('orbKind'))}</label>
        <select data-f="orb">${namedOptionList('Orb', state.schema.orbs, eff.orb)}</select>
      </div>` : '';

    // 激发充能球要选**哪些**：哪一头（最右 / 最左 / 全部 / 随机）。
    // 原版两条路都有（"最右侧"是绝大多数，"逝影之力"那样激发最左侧的是少数）；
    // "全部"是原版"碎片"用的；"随机"是编辑器扩的。默认最右侧，所以不选也对。
    const evokeTargetField = meta.needsEvokeTarget ? `
      <div class="field">
        <label>${escapeHtml(t('evokeTarget'))}</label>
        <select data-f="evokeTarget">${
          namedOptionList('OrbEvokeTarget', state.schema.orbEvokeTargets, eff.evokeTarget)}</select>
      </div>` : '';

    // 只激发哪些**种类**的球（可多选，空 = 不限）。用多值输入组件——与"牌 ID"那种框同一套：
    // 带下拉提示，多个值用逗号分隔，失焦时自动纠正格式（补空格、中文逗号也认）。
    const orbFilterField = meta.needsOrbFilter ? `
      <div class="field">
        <label>${escapeHtml(t('orbFilter'))}</label>
        ${MFComponents.multiCombo({
          field: 'orbKinds',
          values: eff.orbKinds || [],
          items: orbComboItems(),
          placeholder: t('orbFilterPlaceholder'),
        })}
      </div>` : '';

    // 附魔要选附魔种类与作用范围（不限 / 按标签 / 指定卡）。
    // 附魔种类。
    //
    // 这里**不再有"作用的卡牌"那一格**：给哪些牌附魔已经由「作用于什么」决定了
    // （某个牌堆 / 指定牌名 / 按条件），再给一格筛选名重复，而且两处一旦不一致
    // 就会出现"看着按打击、实际给全部附魔"这种事。
    let enchantFields = '';
    if (meta.needsEnchantment) {
      enchantFields = `
        <div class="field">
          <label>${escapeHtml(t('enchantmentPick'))}</label>
          <select data-f="enchantmentId">${
            (state.schema.enchantments || []).map((e) =>
              `<option value="ENCHANTMENT.${e}"${`ENCHANTMENT.${e}` === eff.enchantmentId ? ' selected' : ''}>${
                escapeHtml(gameTerm('Enchantment.' + e) || e)}</option>`).join('')
          }</select>
        </div>`;
    }

    // 这张牌自身的伤害增益要选是否永久。
    const permanentField = meta.hasPermanent ? `
      <div class="field check span-2">
        <input type="checkbox" data-f="permanent" ${eff.permanent ? 'checked' : ''}>
        <label>${escapeHtml(t('permanentBoost'))}</label>
      </div>` : '';

    // 移除状态的两个选项：整层移除 / 只减几层；按 id 指定 / 所有正面 / 所有负面。
    //
    // 它们决定后面两处显隐（"哪个状态"那一格只在按 id 指定时出现；数值那三格只在
    // 部分移除时出现），所以换任一个都要重建表单。
    const powerRemovalFields = meta.hasPowerRemoval ? powerOptionFields(eff) : '';

    // 从牌堆里挑哪些牌（十流程）**不在这里**：整块由 pickForm 画（见上面的选牌器那一段），
    // 因为那些格子的显示规则来自效果自己的策略，不是这个渲染函数能猜的。

    // 目的地牌堆（移到哪个 / 加入哪个）仍然是这一格：它说的是"挑完之后放哪儿"，
    // 而"从哪儿挑"已经合进选牌器的流程一/二。
    //
    // 顶/底只对抽牌堆、弃牌堆有意义（手牌与消耗堆没有玩家可见的位置之分），
    // 所以只在目标牌堆是那两种时给出——与"将此牌置于牌堆"当初的规则一致。
    const targetPileValue = eff.targetPile || 'DrawPile';
    const placementVisible = targetPileValue === 'DrawPile' || targetPileValue === 'DiscardPile';

    // 这一格的名字按效果分：搬运是"移到哪个牌堆"，复制是"加入哪个牌堆"
    // （目的地相同，但动作不同——用户读到的是自己做的操作）。
    const targetPileField = meta.needsTargetPile ? `
      <div class="field">
        <label>${escapeHtml(t(meta.targetPileIsAdd ? 'addToPile' : 'targetPile'))}</label>
        <select data-f="targetPile">${pileOptions(eff.targetPile)}</select>
      </div>
      ${placementVisible ? `
      <div class="field">
        <label>${escapeHtml(t('placement'))}</label>
        <select data-f="placement">${namedOptionList('PilePlacement', state.schema.placements, eff.placement)}</select>
      </div>` : ''}` : '';

    /*
     * 挑牌：选牌器的十步流程，整块由 pickForm 画。
     *
     * 这一段必须排在它决定的那些格子**前面**（挑哪些牌一变，后面牌堆/取值/张数那些格
     * 就跟着变），早先的顺序问题就出在这里（"牌堆"看起来凭空出现/消失）。
     * 显示哪几步由效果自己的策略给出（meta.pick），所以界面里没有第二份"哪些效果能用什么"的名册。
     */
    const pickFields = pickForm(eff, meta, mode, owner);

    /*
     * 费用类效果另外要选持续范围。
     *
     * 只列出**这条效果真正支持**的值（由 schema 给出）："可以免费打出"只有本回合与本场
     * 战斗两种接口（游戏没有"免费直到打出"），而"直到打出"只有降费/加费支持。
     * 界面上不给不存在的组合，比事后报错好。
     */
    let costFields = '';
    if ((meta.durations || []).length > 0) {
      const durationOptions = (meta.durations || []).map(
        (d) => `<option value="${d}"${d === (eff.costDuration || meta.durations[0]) ? ' selected' : ''}>${escapeHtml(t('duration_' + d))}</option>`
      ).join('');

      costFields = `
        <div class="field">
          <label>${escapeHtml(t('costDuration'))}</label>
          <select data-f="costDuration">${durationOptions}</select>
        </div>`;
    }

    /*
     * 变化类效果要选"变成哪种牌"。
     *
     * 只有"变成指定牌"才需要再挑一张牌，所以那张牌的选择框由下面的 cardField 负责
     * （见 filterDemandsCard 的判断）——这里只管这一格。
     */
    let transformField = '';
    if (meta.needsTransform) {
      const intoOptions = (state.schema.transformIntos || []).map(
        (i) => `<option value="${i}"${i === (eff.transformInto || 'Random') ? ' selected' : ''}>${escapeHtml(t('transform_' + i))}</option>`
      ).join('');

      transformField = `
        <div class="field">
          <label>${escapeHtml(t('transformInto'))}</label>
          <select data-f="transformInto">${intoOptions}</select>
        </div>`;
    }

    /*
     * 启用条件窗口：一条效果可以挂一个"左值 {比较方式} 右值"的比较，
     * 满足时这条效果才执行（不满足时它什么都不做，但不影响同一张牌的其它效果）。
     *
     * 两侧各自是一个完整的三件套（基础值 + 变量来源 × 变量系数），与效果数值同一套读法；
     * 基础值与系数各有升级增量——"手牌数不小于 3"升级后变"不小于 4"。
     *
     * 勾选框在**标题行**（效果名字的右边，见 row-head）；这一组画在网格的**最前面**——
     * 条件判断的选项全部在效果选项（效果 / 触发时机 / 目标）之前：先说"什么时候才生效"。
     * 勾选与取消都是结构性的（整组格子开出/收起要重建表单）。
     */
    const conditionFields = conditionWindow(eff, mode);

    row.innerHTML = `
      <div class="row-head">
        <span class="row-title">${escapeHtml(uiLabel(meta))}</span>
        <label class="cond-head-toggle">
          <input type="checkbox" data-f="useCondition" ${eff.condition ? 'checked' : ''}>
          <span>${escapeHtml(t('useCondition'))}</span>
        </label>
        <div class="row-actions">
          <button class="btn btn-small" data-act="up" ${i === 0 ? 'disabled' : ''}>↑</button>
          <button class="btn btn-small" data-act="down" ${i === effects.length - 1 ? 'disabled' : ''}>↓</button>
          <button class="btn btn-small btn-danger" data-act="del">${escapeHtml(t('remove'))}</button>
        </div>
      </div>
      <div class="grid">
        ${conditionFields}
        <div class="section-break"></div>
        <div class="field">
          <label>${escapeHtml(t('effect'))}</label>
          <select data-f="kind">${effectKindOptions(eff.kind)}</select>
        </div>
        ${triggerSelect}
        <div class="field">
          <label>${escapeHtml(t('target'))}</label>
          ${targetField(meta, targetContext, eff.target)}
        </div>
        ${pickFields}
        ${cardSetField(eff, meta)}
        ${enchantFields}
        ${targetPileField}
        ${amountFields}
        <div class="section-break"></div>
        ${repeatFields}
        <div class="section-break"></div>
        ${powerRemovalFields}
        ${meta.needsPower && powerFields(eff, meta).selection === 'PowerId' ? `
        <div class="field span-2">
          <label>${escapeHtml(t('powerPick'))}</label>
          ${idCombo('powerId', eff.powerId, powerComboItems(), t('pickPower'))}
        </div>` : ''}
        ${costFields}
        ${transformField}
        ${orbField}
        ${evokeTargetField}
        ${orbFilterField}
        ${addModeField}
        ${poolFields}
        ${cardField}
        ${permanentField}
      </div>
      ${mode === 'card' ? levelTableHtml(owner, eff, i, meta, calculated) : ''}
      <div class="effects-summary" data-summary-for="${i}"></div>
    `;

    row.querySelectorAll('[data-f]').forEach((el) => {
      // 多值输入框（data-multi 里面的那个）由组件自己接：它每次敲键都调这里的话，
      // 写进模型的就是半截字符串（"Lightning, Fr"），而不是值数组。见下面的 bindMultiCombo。
      if (el.closest('[data-multi]')) return;

      // 换效果种类、换来源、换作用对象这类会改变"这一行显示哪些字段"的改动必须重建；
      // 其它改动只更新派生内容（重建会把正在输入的框重置）。
      // 来源决定后面几格出不出现（"取自谁"、"每单位"、升级"每单位"），所以要重建。
      // 选牌器那几格同理：换来源会整块换掉（牌堆 / 筛选 / 取法），换取法要显示张数，
      // 换费用运算符要显示/隐藏阈值那几格。
      // 筛选目标（选牌一份 + 计数五份）里"换运算符"与"换范围"同样会改变行数。
      const filterPrefix = filterPrefixOf(el.dataset.f || '');
      const filterRest = filterPrefix ? el.dataset.f.slice(filterPrefix.length) : '';

      const structural = el.dataset.f === 'kind' ||
        el.dataset.f === 'trigger' || el.dataset.f === 'target' ||
        el.dataset.f === 'addMode' ||
        el.dataset.f === 'useCondition' ||
        el.dataset.f === 'condLeftSourceKind' || el.dataset.f === 'condRightSourceKind' ||
        el.dataset.f === 'pickSource' || el.dataset.f === 'pickMode' ||
        el.dataset.f === 'setId' || el.dataset.f === 'pickSetId' ||
        (filterPrefix && (filterRest === 'energyComparison' || filterRest === 'starComparison')) ||
        el.dataset.f === 'energySourceKind' || el.dataset.f === 'starSourceKind' ||
        el.dataset.f === 'pickCountSourceKind' ||
        el.dataset.f === 'transformInto' ||
        el.dataset.f === 'targetPile' ||
        el.dataset.f === 'sourceKind' || el.dataset.f === 'useCounter' ||
        el.dataset.f === 'repeatSourceKind';

      const handler = () => {
        const f = el.dataset.f;

        // ---- 筛选目标上的字段（选牌一份 + 计数五份）：前缀决定写进哪一份 ----
        //
        // 统一在这里处理，因为"运算符 / 阈值 / 范围"三种字段五处都一样，只有前缀不同；
        // 各写一遍迟早会漏掉某一处（那一处就变成"填了不生效"）。
        if (filterPrefix) {
          const target = filterTarget(filterPrefix, eff);

          if (filterRest === 'energyComparison' || filterRest === 'starComparison') {
            // 换运算符只改变"阈值那几格出不出现"（选 X 时隐去），所以要重建。
            //
            // 这里**没有"选回不筛选就清掉阈值"那一段**：下拉里没有 `None` 这一档
            // （它不参与运算，是模型侧的哨兵）。要停用这一维是在上面那个"筛选维度"
            // 多值框里把它移除——那条路会走 clearFilterDimension（取值/运算符/阈值一起清）。
            target.filter[filterRest] = el.value;

            refreshAll();
            return;
          }

          if (filterRest === 'energyThreshold' || filterRest === 'starThreshold') {
            const num = parseNumber(el.value);
            if (num !== null) target.filter[filterRest] = Math.trunc(num);

            refreshDerived();
            return;
          }

          if (filterRest === 'scope' && target.source) {
            target.source.countScope = el.value;
            refreshDerived();
            return;
          }
        }

        if (f === 'upgraded') {
          eff.upgraded = el.checked;
        } else if (f === 'addMode') {
          // 生成方式在 structural 名单里（上面已 refreshAll），这里只写模型。
          eff.addMode = el.value;
        } else if (f === 'candidates') {
          const num = parseNumber(el.value);
          if (num !== null) eff.candidates = Math.min(3, Math.max(1, Math.trunc(num)));
        } else if (f === 'permanent') {
          eff.permanent = el.checked;
        } else if (f === 'removal' || f === 'selection') {
          // 移除状态的两个选项。两者都改变后面几格的显隐
          //（"整个移除"隐去数值那三格；"所有正面/负面"隐去"哪个状态"那一格），所以要重建。
          eff[f] = el.value;

          refreshAll();
          return;
        } else if (f === 'useCounter') {
          eff.useCounter = el.checked;

          // 勾选后才出现阈值/归零那几格，所以要重建。
          refreshAll();
          return;
        } else if (f === 'enchantmentId') {
          // 下拉里存的是完整模型 id，这里直接写入。
          eff.enchantmentId = el.value;
        } else if (f === 'cardFilter') {
          eff.cardFilter = el.value;

          // 选"指定卡牌"时才有卡牌可选，所以这个改动要重建表单。
          refreshAll();
          return;
        } else if (f === 'upgrade') {
          // 解析不出完整数字（如 "1."、"-"）时保留原值，让用户能继续输入。
          const delta = parseNumber(el.value);
          if (delta !== null) {
            eff.upgrade = eff.upgrade || {};
            eff.upgrade.amountDelta = delta;
          }
        } else if (f === 'upgradePerUnit') {
          const delta = parseNumber(el.value);
          if (delta !== null) {
            eff.upgrade = eff.upgrade || {};
            eff.upgrade.perUnitDelta = delta;
          }
        } else if (f === 'upgradeRepeat') {
          const delta = parseNumber(el.value);
          if (delta !== null) {
            eff.upgrade = eff.upgrade || {};
            eff.upgrade.repeatDelta = Math.trunc(delta);
          }
        } else if (f === 'upgradeRepeatPerUnit') {
          // 次数那块也有变量系数，它的升级增量是独立的一项（"每次多打一次" → "每次多打两次"）。
          const delta = parseNumber(el.value);
          if (delta !== null) {
            eff.upgrade = eff.upgrade || {};
            eff.upgrade.repeatPerUnitDelta = delta;
          }
        } else if (f === 'perUnit' || f === 'repeatPerUnit') {
          const per = parseNumber(el.value);
          if (per !== null) {
            const key = f === 'repeatPerUnit' ? 'repeatSource' : 'source';
            eff[key] = eff[key] || {};
            eff[key].perUnit = per;
          }
        } else if (['amount', 'repeat', 'count', 'counterTarget', 'repeatBase'].includes(f)) {
          const num = parseNumber(el.value);
          if (num !== null) {
            eff[f] = (f === 'counterTarget' || f === 'repeatBase') ? Math.trunc(num) : num;
          }
        } else if (f === 'sourceKind' || f === 'repeatSourceKind') {
          const key = f === 'repeatSourceKind' ? 'repeatSource' : 'source';
          const wasRepeat = key === 'repeatSource';

          eff[key] = eff[key] || {};
          eff[key].kind = el.value;

          // 换成玩家维度的来源后，"从谁身上取"就没有意义了，统一归到自身。
          if (isPlayerScoped(el.value)) {
            eff[key].actor = 'Self';
          }

          if (wasRepeat) {
            // 「无」= 固定次数，选到别的才是计算型。这就是原先那个勾选框的语义，
            // 现在由这一格承担，界面上少一个开关。
            eff.calculatedRepeat = el.value !== 'None';

            // 固定 ↔ 计算 之间切换时把数字搬过去：它们是同一个概念的两条路
            // （数固定次数读 Repeat、算出来的次数读 RepeatBase）。不搬的话用户
            // 会看到刚填的"3 次"变成默认的 1。
            if (eff.calculatedRepeat && eff.repeatBase == null) {
              eff.repeatBase = eff.repeat ?? 1;
            }

            if (!eff.calculatedRepeat) {
              eff.repeat = eff.repeatBase ?? 1;
            }
          }

          // 换来源会改变显示哪些附加项，所以重建。
          refreshAll();
          return;
        } else if (f === 'pickSource') {
          // 选择范围：此牌 / 牌堆 / 卡组 / 临时牌集合。换它会改变下面出现哪几格
          // （牌堆、筛选、取法、集合 ID），所以要重建表单。
          pickOf(eff).source = el.value;

          // 离开"集合"那一档就把集合 ID 清掉：留着的话下次切回来会带着旧值，
          // 而那条效果可能已经被删了（于是读到不存在的集合）。
          if (el.value !== 'Set') {
            delete pickOf(eff).setId;
          } else {
            // 切到"集合"时替作者选上第一个已定义的集合（绝大多数情况就是唯一那一个）。
            // 一条都没定义时留空，让校验器把"读了不存在的集合"报出来。
            const defined = definedSetsOfCard(owner);

            if (defined.length > 0) {
              pickOf(eff).setId = defined[0].id;
            }
          }

          refreshAll();
          return;
        } else if (f === 'setId') {
          // 「临时牌集合ID」：把这条效果产生的牌存起来。空串就是不存。
          //
          // 改的是**已经存在的**集合名时，把读它的那几条一起改过去——改名就该到处都改，
          // 不然那些效果会读到不存在的集合（校验器会报，但没必要让作者逐个去改）。
          // 判据是「改之前这个名字非空」：给一条新效果**新建**集合时改之前是空的，
          // 那不该去动别人的引用。
          const before = eff.setId;
          const after = el.value;

          if (after) {
            eff.setId = after;
          } else {
            delete eff.setId;
          }

          if (before && before !== after) {
            const rest = owner?.effects === effects ? effects : (owner?.effects || []);

            rest.forEach((other) => {
              if (other === eff || other.pick?.source !== 'Set' || other.pick.setId !== before) {
                return;
              }

              if (after) {
                other.pick.setId = after;
              } else {
                delete other.pick.setId;
              }
            });
          }
        } else if (f === 'pickSetId') {
          // 读哪个集合（不可多选）。
          if (el.value) {
            pickOf(eff).setId = el.value;
          } else {
            delete pickOf(eff).setId;
          }
        } else if (f === 'pickPile') {
          // 只认一个牌堆的效果（自动打出）。写成一个元素的列表。
          pickOf(eff).piles = [el.value];
        } else if (f === 'pickMode') {
          pickOf(eff).mode = el.value;

          // 张数那一组只在"选择/随机/至多"时出现，所以要重建。
          refreshAll();
          return;
        } else if (f === 'energyAmount' || f === 'starAmount') {
          const num = parseNumber(el.value);
          if (num !== null) {
            eff[f] = Math.trunc(num);
          }
        } else if (f === 'energySourceKind' || f === 'starSourceKind') {
          const key = f === 'energySourceKind' ? 'energySource' : 'starSource';

          eff[key] = eff[key] || {};
          eff[key].kind = el.value;

          // 换成玩家维度的来源后，"从谁身上取"就没有意义了，统一归到自身。
          if (isPlayerScoped(el.value)) {
            eff[key].actor = 'Self';
          }

          // 换来源会改变显示哪些附加项（状态 / 取自谁 / 系数），所以重建。
          refreshAll();
          return;
        } else if (f === 'energySourcePerUnit' || f === 'starSourcePerUnit') {
          const per = parseNumber(el.value);
          if (per !== null) {
            const key = f === 'energySourcePerUnit' ? 'energySource' : 'starSource';
            eff[key] = eff[key] || {};
            eff[key].perUnit = per;
          }
        } else if (f === 'upgradeEnergy' || f === 'upgradeStar') {
          // 费用阈值的升级增量（挂在这个阈值自己那一行上，见 costThresholdFields）。
          const delta = parseNumber(el.value);
          if (delta !== null) {
            eff.upgrade = eff.upgrade || {};
            eff.upgrade[f === 'upgradeEnergy' ? 'energyDelta' : 'starDelta'] = Math.trunc(delta);
          }
        } else if (f === 'upgradeEnergyPerUnit' || f === 'upgradeStarPerUnit') {
          // 变量系数那一项的升级增量（阈值的两个数各占一个变量，所以各有各的增量）。
          const delta = parseNumber(el.value);
          if (delta !== null) {
            eff.upgrade = eff.upgrade || {};
            eff.upgrade[f === 'upgradeEnergyPerUnit' ? 'energyPerUnitDelta' : 'starPerUnitDelta'] = delta;
          }
        } else if (f === 'energySourcePowerId' || f === 'starSourcePowerId') {
          const key = f === 'energySourcePowerId' ? 'energySource' : 'starSource';
          eff[key] = eff[key] || {};
          eff[key].powerId = el.value || null;
        } else if (f === 'energySourceActor' || f === 'starSourceActor') {
          const key = f === 'energySourceActor' ? 'energySource' : 'starSource';
          eff[key] = eff[key] || {};
          eff[key].actor = el.value;
        } else if (f === 'useCondition') {
          // 勾选框在 structural 名单里（上面已 refreshAll），这里只写模型。
          // 启用即补一份默认形状（左 0 / 右 1、无来源——作者再按意图改）。
          if (el.checked) {
            conditionOf(eff);
          } else {
            delete eff.condition;
          }
        } else if (f === 'condComparison') {
          if (eff.condition) eff.condition.comparison = el.value;
        } else if (f === 'condLeftAmount' || f === 'condRightAmount') {
          const num = parseNumber(el.value);
          if (num !== null && eff.condition) {
            eff.condition[f === 'condLeftAmount' ? 'leftAmount' : 'rightAmount'] = Math.trunc(num);
          }
        } else if (f === 'condLeftUpgrade' || f === 'condRightUpgrade') {
          const num = parseNumber(el.value);
          if (num !== null && eff.condition) {
            eff.condition[f === 'condLeftUpgrade' ? 'leftUpgrade' : 'rightUpgrade'] = Math.trunc(num);
          }
        } else if (f === 'condLeftPerUnitUpgrade' || f === 'condRightPerUnitUpgrade') {
          const num = parseNumber(el.value);
          if (num !== null && eff.condition) {
            eff.condition[f === 'condLeftPerUnitUpgrade' ? 'leftPerUnitUpgrade' : 'rightPerUnitUpgrade'] = num;
          }
        } else if (f === 'condLeftSourceKind' || f === 'condRightSourceKind') {
          const source = conditionSideOf(eff, f === 'condLeftSourceKind' ? 'left' : 'right');

          source.kind = el.value;

          // 换成玩家维度的来源后，"从谁身上取"就没有意义了，统一归到自身。
          if (isPlayerScoped(el.value)) {
            source.actor = 'Self';
          }

          // 换来源会改变显示哪些附加项（状态 / 取自谁 / 系数 / 数牌条件），所以重建。
          refreshAll();
          return;
        } else if (f === 'condLeftPerUnit' || f === 'condRightPerUnit') {
          const per = parseNumber(el.value);
          if (per !== null && eff.condition) {
            conditionSideOf(eff, f === 'condLeftPerUnit' ? 'left' : 'right').perUnit = per;
          }
        } else if (f === 'condLeftSourcePowerId' || f === 'condRightSourcePowerId') {
          conditionSideOf(eff, f === 'condLeftSourcePowerId' ? 'left' : 'right').powerId = el.value || null;
        } else if (f === 'condLeftSourceActor' || f === 'condRightSourceActor') {
          conditionSideOf(eff, f === 'condLeftSourceActor' ? 'left' : 'right').actor = el.value;
        } else if (f === 'pickCountBase') {
          const num = parseNumber(el.value);
          if (num !== null) {
            pickCountOf(pickOf(eff)).base = Math.trunc(num);
          }
        } else if (f === 'pickCountPerUnit') {
          const per = parseNumber(el.value);
          if (per !== null) {
            pickCountOf(pickOf(eff)).variable.perUnit = per;
          }
        } else if (f === 'pickCountSourceKind') {
          const source = pickCountOf(pickOf(eff)).variable;

          source.kind = el.value;

          // 换成玩家维度的来源后，"从谁身上取"就没有意义了，统一归到自身。
          if (isPlayerScoped(el.value)) {
            source.actor = 'Self';
          }

          // 换来源会改变显示哪些附加项（状态 / 取自谁 / 系数 / 条件范围），所以重建。
          refreshAll();
          return;
        } else if (f === 'pickCountSourcePowerId') {
          pickCountOf(pickOf(eff)).variable.powerId = el.value || null;
        } else if (f === 'pickCountSourceActor') {
          pickCountOf(pickOf(eff)).variable.actor = el.value;
        } else if (f === 'sourcePowerId' || f === 'repeatSourcePowerId') {
          // "状态层数"来源引用的那个状态。
          const key = f === 'repeatSourcePowerId' ? 'repeatSource' : 'source';
          eff[key] = eff[key] || {};
          eff[key].powerId = el.value || null;
        } else if (f === 'sourceActor' || f === 'repeatSourceActor') {
          const key = f === 'repeatSourceActor' ? 'repeatSource' : 'source';
          eff[key] = eff[key] || {};
          eff[key].actor = el.value;
        } else if (f === 'target') {
          eff.target = el.value;

          // 换成没有宠物的那一档时清掉"取其宠物"（判据与勾选框的显示条件同一条）。
        } else {
          eff[f] = el.value;
        }

        // 换了效果种类后目标要重新定：**同一个作用域**内候选含义不变，能留就留；
        // 跨了作用域（造物轴 ↔ 玩家轴）或原来那一档在新种类里没有，就落到新种类的默认档。
        //
        // 为什么跨作用域不保留：同名的"目标"在两轴上不是一回事——造物轴上是"选定的那个生物"，
        // 玩家轴上是"选定目标那个玩家"（意思是给队友）。留着就等于替用户改了他这条效果的意思。
        if (f === 'kind') {
          const next = schemaEffect(eff.kind);
          const sameScope = next.scope && meta.scope && next.scope === meta.scope;

          if (!sameScope || !next.targets.includes(eff.target)) {
            eff.target = next.defaultTarget || next.targets[0];
          }


          // 换成**不挑牌**的种类时把选牌配置丢掉：留着的话它会照旧写进 XML，
          // 读文件的人会以为那段在用。（会挑牌的种类不必在这里补——表单自己按需建一份。）
          if (!next.pick) {
            delete eff.pick;
          }

          // 这个种类没有数值（消耗牌 / 丢弃牌这类："动几张"由选牌器流程十给）时，
          // 把数值那一组清掉：界面上那几格本来就隐去了，留着只会在 XML 里写出一段
          // 毫无意义的 <base>（用户读文件时会以为它有用）。
          // 次数相关的升级增量不动——重复次数对它们仍然有意义。
          if (!next.amount) {
            eff.amount = 0;
            eff.source = { actor: 'Self', kind: 'None', perUnit: 1 };
            eff.upgrade = eff.upgrade || {};
            eff.upgrade.amountDelta = 0;
            eff.upgrade.perUnitDelta = 0;
            eff.upgrade.source = null;
          }
        }

        // 切到**计算型**时保留原来那个数当基础值：固定型的 6 到了计算型就是
        // "6 + 每单位 × 来源"里的那个 6。界面上这一格一直在、用户看得见，改不改由他决定。

        // id 输入框旁边那行说明要跟着变（选/打 id 后显示它现在指向什么）。
        // 就地改文本，不重建表单：重建会把光标踢出输入框，正在打的 id 就打不下去了。
        if (f === 'cardId' || f === 'powerId' || f === 'sourcePowerId' ||
            f === 'repeatSourcePowerId' || f === 'pickCountSourcePowerId' ||
            f === 'energySourcePowerId' || f === 'starSourcePowerId') {
          refreshComboHint(el);
        }

        if (structural) {
          refreshAll();
        } else {
          refreshDerived();
        }
      };

      el.addEventListener('input', handler);
      el.addEventListener('change', handler);
    });

    row.querySelectorAll('[data-act]').forEach((btn) => {
      btn.addEventListener('click', () => {
        const act = btn.dataset.act;
        if (act === 'del') effects.splice(i, 1);
        else if (act === 'up' && i > 0) effects.splice(i - 1, 0, effects.splice(i, 1)[0]);
        else if (act === 'down' && i < effects.length - 1) effects.splice(i + 1, 0, effects.splice(i, 1)[0]);
        refreshAll();
      });
    });

    // 等级表的输入（只有卡牌有；升级上限 > 1 时那一行才存在）。
    bindLevelTable(row, eff);

    // 多值输入（只激发哪些种类的球、选牌器的牌堆/取值那几格）：它有自己的下拉与失焦规范化，
    // 所以交给组件接（见 ui/components.js）。改完写回模型，再刷新派生内容。
    row.querySelectorAll('[data-multi]').forEach((box) => {
      const field = box.dataset.multiField;

      MFComponents.bindMultiCombo(box, {
        items: multiComboItemsFor(field),
        // 组件给的是**模型值数组**（框里显示的是 label，见 components.js 的 displayText）。
        onChange: (values) => {
          writeMultiComboValue(eff, field, values);

          // "启用了哪几维"那一格会**增删维度行**，属于结构改动（既要重建表单，也要让新出现
          // 的那一行拿到它自己的候选值），所以走 refreshAll；其余多值框只影响派生内容。
          // 六份筛选（选牌 + 计数五处）的维度框都是这样，所以按"某一份筛选的 dimensions"判，
          // 而不是写死 pick 那一份——写死的话计数的维度框会**只写模型、不画行**（填了看不见）。
          const prefix = filterPrefixOf(field);

          if (prefix && field.slice(prefix.length) === 'dimensions') {
            refreshAll();
            return;
          }

          refreshDerived();
        },
      });
    });

    host.appendChild(row);
  });

  if (effects.length === 0) {
    host.innerHTML = `<p class="hint" style="padding:0">${escapeHtml(t('noEffects'))}</p>`;
    return;
  }

  fillRowSummaries(effects, host);
}

/**
 * 共享效果引用行。
 *
 * 只有升级增量可改，因为行为属于共享定义——"改一处、处处生效"正是共享它的意义。
 */
function renderSharedRefRow(eff, effects, i) {
  const shared = (state.project.sharedEffects || [])
    .find((s) => slug(s.id) === slug(eff.sharedEffectId));

  const row = document.createElement('div');
  row.className = 'card-row is-ref';
  const missing = !shared;

  row.innerHTML = `
    <div class="row-head">
      <span class="row-title">
        <span class="ref-badge">${escapeHtml(t('shared'))}</span>
        ${escapeHtml(missing ? `${t('missing')}: ${eff.sharedEffectId}` : locGet(shared.name, contentLanguage))}
      </span>
      <div class="row-actions">
        <button class="btn btn-small" data-act="up" ${i === 0 ? 'disabled' : ''}>↑</button>
        <button class="btn btn-small" data-act="down" ${i === effects.length - 1 ? 'disabled' : ''}>↓</button>
        <button class="btn btn-small btn-danger" data-act="del">${escapeHtml(t('remove'))}</button>
      </div>
    </div>
    ${missing
      ? `<p class="hint" style="padding:0">${escapeHtml(t('missingEffect'))}</p>`
      : `<div class="grid">
           <div class="field">
             <label>${escapeHtml(t('behaviour'))}（${escapeHtml(t('editOnEffectsTab'))}）</label>
             <div class="ref-summary">${escapeHtml(uiLabel(schemaEffect(shared.effect.kind)))} ${escapeHtml(String(shared.effect.amount ?? 0))}</div>
           </div>
           <div class="field">
             <label>${escapeHtml(t('upgradeAmount'))}</label>
             <input type="text" inputmode="numeric" data-f="upgrade" value="${(eff.upgrade || {}).amountDelta ?? 0}">
           </div>
         </div>`}
  `;

  const upgradeInput = row.querySelector('[data-f="upgrade"]');
  if (upgradeInput) {
    upgradeInput.addEventListener('input', () => {
      eff.upgrade = eff.upgrade || {};
      eff.upgrade.amountDelta = Number(upgradeInput.value) || 0;
      refreshAll();
    });
  }

  row.querySelectorAll('[data-act]').forEach((btn) => {
    btn.addEventListener('click', () => {
      const act = btn.dataset.act;
      if (act === 'del') effects.splice(i, 1);
      else if (act === 'up' && i > 0) effects.splice(i - 1, 0, effects.splice(i, 1)[0]);
      else if (act === 'down' && i < effects.length - 1) effects.splice(i + 1, 0, effects.splice(i, 1)[0]);
      refreshAll();
    });
  });

  // 共享效果引用行**没有**等级表：等级表属于共享定义本身（在「效果」页编辑），
  // 这里只改引用行的升级增量。所以不调 bindLevelTable。

  return row;
}

/**
 * 绑定等级表的输入。
 *
 * 与普通字段的区别：**留空是合法状态**（表示沿用上一级），所以要能写回 null，
 * 不能把空串当成 0。整行都留空时把这一项从表里删掉，工程文件里不留下空壳。
 */
/**
 * 等级表那几个"算式来源"格：界面上的字段名与模型里的键不同名（多一个 Kind）。
 *
 * 四个角色各一份——数值 / 次数 / 能量阈值 / 星辰阈值，见 EffectLevel。
 */
const LEVEL_SOURCE_FIELDS = {
  sourceKind: 'source',
  repeatSourceKind: 'repeatSource',
  energySourceKind: 'energySource',
  starSourceKind: 'starSource',
};

/** 等级表里取小数的格（系数），其余取整数。 */
const DECIMAL_LEVEL_FIELDS = ['perUnit', 'repeatPerUnit', 'energyPerUnit', 'starPerUnit'];

function bindLevelTable(row, eff) {
  const inputs = [...row.querySelectorAll('[data-level]')];
  if (inputs.length === 0) return;

  const readLevel = (el) => {
    const level = Number(el.dataset.level);
    eff.levels = Array.isArray(eff.levels) ? eff.levels : [];

    while (eff.levels.length < level) eff.levels.push(null);
    if (!eff.levels[level - 1]) eff.levels[level - 1] = {};

    return eff.levels[level - 1];
  };

  const prune = () => {
    if (!Array.isArray(eff.levels)) return;

    // 末尾的空项删掉，但**保留中间的空洞**：第 3 级没改时它要靠这个位置沿用上一级，
    // 删掉会让后面的等级继承到错误的值。
    while (eff.levels.length > 0) {
      const last = eff.levels[eff.levels.length - 1];
      const empty = !last || Object.keys(last).every((k) => last[k] === null || last[k] === undefined);
      if (!empty) break;
      eff.levels.pop();
    }

    if (eff.levels.length === 0) delete eff.levels;
  };

  for (const el of inputs) {
    const apply = () => {
      const field = el.dataset.lf;
      const step = readLevel(el);

      if (field in LEVEL_SOURCE_FIELDS) {
        const key = LEVEL_SOURCE_FIELDS[field];

        if (!el.value) {
          delete step[key];
        } else {
          step[key] = step[key] || {};
          step[key].kind = el.value;
        }
      } else {
        const num = parseNumber(el.value);

        // 解析不出来（空串或中间状态）→ 沿用上一级。空串必须清掉这一项，
        // 否则"删掉重填"会留下旧值。
        if (num === null) delete step[field];
        else step[field] = DECIMAL_LEVEL_FIELDS.includes(field) ? num : Math.trunc(num);
      }

      prune();
    };

    el.addEventListener('input', apply);

    // 停下（失焦/回车）再重建：每敲一个字就重画会把输入框重置、正在打的数进不去。
    // 重建是必须的——换了算式来源后别的字段要不要显示也跟着变。
    el.addEventListener('change', () => { apply(); refreshAll(); });
  }
}

/**
 * 多次升级时的**等级表**：每一级一行，可以对这一级的数值做覆盖。
 *
 * 只在卡的升级上限 > 1 时出现——能升一次时等级表的第 2 项起永远读不到，
 * 显示出来只会让人以为配好了（校验器也会就那种配置提示）。
 *
 * 留空 = 沿用上一级。这一点很重要：多数牌只是"数字变大"，只填变化的那几项就够了，
 * 不必把每一级都写满。所以输入框空着是**合法状态**，不是"没填"。
 *
 * 换了算式来源时卡面文字跟不上（游戏描述只有"升级前/升级后"两态，分不出第几级），
 * 所以那一格旁边有说明；界面不假装它能逐级显示。
 */
function levelTableHtml(card, eff, index, meta, calculated) {
  if (!card) return '';

  const max = Number(card.maxUpgradeLevel ?? 1);
  if (!(max > 1)) return '';

  const levels = Array.isArray(eff.levels) ? eff.levels : [];
  const rows = [];

  // 两个费用阈值那几格要不要出现，看这条效果有没有按费用筛（运算符不是"不筛选"）：
  // 没筛时那一级填了也读不到，不该显示。
  const pickForLevel = eff.pick || {};

  for (let level = 1; level <= max; level++) {
    const step = levels[level - 1] || {};

    const num = (field, label, value, mode) => `
      <div class="field">
        <label>${escapeHtml(label)}</label>
        <input type="text" inputmode="${mode}" data-level="${level}" data-lf="${field}"
               value="${value ?? ''}" placeholder="${escapeHtml(t('levelInherit'))}">
      </div>`;

    const cells = [];

    cells.push(num('amount', calculated ? t('levelAmountBase') : t('levelAmount'), step.amount, 'numeric'));

    if (calculated) {
      cells.push(num('perUnit', t('levelPerUnit'), step.perUnit, 'decimal'));
    }

    cells.push(num('repeat', t('levelRepeat'), step.repeat, 'numeric'));

    // 次数的变量系数：次数也是三件套（次数 = 基础值 + 变量 × 系数），所以这一格与数值那边
    // 对称。只在次数是算出来的时给——固定次数没有变量，也就没有系数可改。
    if (eff.calculatedRepeat) {
      cells.push(num('repeatPerUnit', t('levelRepeatPerUnit'), step.repeatPerUnit, 'decimal'));
    }

    // 换算式来源：只在计算型时给，且只给"来源种类"——这是等级表能做到的、
    // 增量模型做不到的那件事（"0 级按手牌数、1 级按弃牌堆数"）。
    const sourceCell = (field, label, current) => `
      <div class="field">
        <label>${escapeHtml(label)}</label>
        <select data-level="${level}" data-lf="${field}">
          <option value="">${escapeHtml(t('levelInherit'))}</option>
          ${state.schema.valueSourceKinds.map((k) => `
            <option value="${escapeHtml(k.value)}" ${(current === k.value) ? 'selected' : ''}>
              ${escapeHtml(uiLanguage === 'zh' ? k.labelZh : k.labelEn)}
            </option>`).join('')}
        </select>
      </div>`;

    if (calculated) {
      cells.push(sourceCell('sourceKind', t('levelSource'), step.source?.kind));
    }

    if (eff.calculatedRepeat) {
      cells.push(sourceCell('repeatSourceKind', t('levelRepeatSource'), step.repeatSource?.kind));
    }

    // 两个费用阈值：**只在筛了对应费用、而且是"比数字"的那几档时给**（没筛、"是 X"、
    // 以及这一级填了也读不到的配置都不显示）。
    //
    // 三格都给——基础值、变量系数、算式来源：阈值的两个数各占一个卡上的变量
    // （EnergyAmount / EnergyAmountExtra），来源则由生成器按等级分支，所以这一级改成
    // "按弃牌堆数"也真的生效。
    for (const [role, dim] of [['energy', 'energyComparison'], ['star', 'starComparison']]) {
      const comparison = pickForLevel[dim];
      if (!comparison || comparison === 'None' || comparison === 'IsX') continue;

      const prefix = role === 'energy' ? 'levelCostEnergy' : 'levelCostStar';

      cells.push(num(`${role}Amount`, t(prefix), step[`${role}Amount`], 'numeric'));
      cells.push(num(`${role}PerUnit`, t(`${prefix}PerUnit`), step[`${role}PerUnit`], 'decimal'));
      cells.push(sourceCell(`${role}SourceKind`, t(`${prefix}Source`), step[`${role}Source`]?.kind));
    }

    rows.push(`
      <div class="level-row">
        <div class="level-head">${escapeHtml(t('levelN', { n: level }))}</div>
        <div class="grid">${cells.join('')}</div>
      </div>`);
  }

  return `
    <details class="level-table">
      <summary>${escapeHtml(t('levelTable', { n: max }))}</summary>
      ${rows.join('')}
    </details>`;
}

/**
 * 解析输入框里的数值。
 *
 * 逐字符输入时会出现 "1."、"-"、"" 这类中间状态，直接 Number() 会得到 1 或 0 并写回模型，
 * 而输入框在下次重绘时被这个值覆盖，小数点就"打不进去"。这里只在能解析出完整数字时才采用，
 * 中间状态保持原样不动。
 */
function parseNumber(text) {
  const trimmed = String(text ?? '').trim();

  // 允许小数，但拒绝只有小数点或负号的中间状态。
  if (trimmed === '' || trimmed === '-' || trimmed === '.' || trimmed === '-.') {
    return null;
  }

  const value = Number(trimmed);
  return Number.isFinite(value) ? value : null;
}

/* -------------------------------------------------- 卡面选择 */

/** 打开卡面选择面板。选中的图片会写进 card.artFile。 */
async function openArtPicker(card) {
  if (!card) return;

  const overlay = document.createElement('div');
  overlay.className = 'modal-backdrop';
  overlay.innerHTML = `
    <div class="modal art-modal">
      <div class="modal-head">
        <h3>${escapeHtml(t('artTitle'))}</h3>
        <button class="btn btn-small" data-act="close">${escapeHtml(t('artClose'))}</button>
      </div>

      <div class="art-tabs">
        <button class="chip is-active" data-src="vanilla">${escapeHtml(t('artVanilla'))}</button>
        <button class="chip" data-src="local">${escapeHtml(t('artLocal'))}</button>
      </div>

      <div class="art-toolbar">
        <input type="text" class="search" data-role="search" placeholder="${escapeHtml(t('artSearchPlaceholder'))}">
        <button class="btn btn-small" data-act="refresh">${escapeHtml(t('artRefresh'))}</button>
      </div>

      <p class="hint" data-role="folder"></p>

      <div class="art-grid" data-role="grid"><p class="hint">${escapeHtml(t('artNone'))}</p></div>
    </div>
  `;

  document.body.appendChild(overlay);

  let source = 'vanilla';
  let items = [];

  const grid = overlay.querySelector('[data-role="grid"]');
  const folderNote = overlay.querySelector('[data-role="folder"]');
  const search = overlay.querySelector('[data-role="search"]');

  const renderGrid = () => {
    const query = search.value.trim().toLowerCase();
    const shown = query
      ? items.filter((it) => `${it.display} ${it.id}`.toLowerCase().includes(query))
      : items;

    if (shown.length === 0) {
      grid.innerHTML = `<p class="hint" style="padding:12px">${escapeHtml(
        source === 'local' ? t('artLocalEmpty') : t('artNone')
      )}</p>`;
      return;
    }

    // 原版卡图有 800 多张，只渲染前若干个，避免一次性插入过多节点。
    grid.innerHTML = shown.slice(0, 200).map((it) => `
      <button type="button" class="art-cell" data-url="${escapeHtml(it.url)}"
              data-kind="${escapeHtml(it.kind)}" data-id="${escapeHtml(it.id)}"
              title="${escapeHtml(it.id)}">
        <img src="${escapeHtml(it.url)}" alt="" loading="lazy">
        <span class="art-cell-name">${escapeHtml(it.display)}</span>
        <span class="art-cell-group">${escapeHtml(it.group)}</span>
      </button>
    `).join('');

    grid.querySelectorAll('.art-cell').forEach((cell) => {
      cell.addEventListener('click', () => applyChosen(cell));
    });
  };

  const applyChosen = async (cell) => {
    const kind = cell.dataset.kind;
    const id = cell.dataset.id;

    if (kind === 'local') {
      // 本地图片已在 cardart 文件夹里，直接作为立绘引用。
      const path = localPathOf(id);
      card.artFile = path;
      finish(path);
      return;
    }

    // 原版卡图要先取出来存成文件，才能打包进 mod。
    const [pool, stem] = id.split('/');
    const label = cell.querySelector('.art-cell-name');

    try {
      if (label) label.textContent = t('artUsing');
      const res = await postJson('/api/art/use-vanilla', { pool, stem });

      if (!res.ok) {
        toast(res.error, 'err');
        if (label) label.textContent = id.split('/')[1];
        return;
      }

      card.artFile = res.path;
      finish(res.path);
    } catch (err) {
      toast(t('previewFailed'), 'err');
    }
  };

  /**
   * 选中后的收尾：写入字段、关面板、刷新预览。
   *
   * 顺序上先关面板再刷新——refreshDerived 会重绘卡面区域，留着面板没有意义；而且面板
   * 移除后即便刷新出错，用户也不会被困在一个点不动的遮罩里。
   */
  const finish = (path) => {
    card.artFile = path;

    overlay.remove();

    const field = $('c-art');
    if (field) field.value = path;

    try {
      refreshDerived();
    } catch (err) {
      // 刷新失败不影响已经选好的卡面，让用户能继续操作。
    }

    toast(t('artApplied') + fileNameOf(path), 'ok');
  };

  /** 从路径里取出文件名，兼容 Windows 与 POSIX 分隔符。 */
  const fileNameOf = (path) => {
    const backslash = String.fromCharCode(92);
    const slash = Math.max(path.lastIndexOf('/'), path.lastIndexOf(backslash));
    return slash >= 0 ? path.slice(slash + 1) : path;
  };

  const load = async () => {
    grid.innerHTML = `<p class="hint" style="padding:12px">…</p>`;

    try {
      const res = await api(`/api/art/list?language=${encodeURIComponent(contentLanguage)}`);
      state.artLocalDir = res.localDir || null;
      items = (source === 'vanilla' ? res.vanilla : res.local) || [];

      folderNote.textContent = source === 'local' ? `${t('artFolder')}${res.localDir || ''}` : '';
      renderGrid();
    } catch (err) {
      grid.innerHTML = `<p class="hint" style="padding:12px">${escapeHtml(t('previewFailed'))}</p>`;
    }
  };

  overlay.querySelectorAll('.art-tabs .chip').forEach((chip) => {
    chip.addEventListener('click', () => {
      overlay.querySelectorAll('.art-tabs .chip').forEach((c) => c.classList.toggle('is-active', c === chip));
      source = chip.dataset.src;
      search.value = '';
      load();
    });
  });

  overlay.querySelector('[data-act="refresh"]').addEventListener('click', load);
  overlay.querySelector('[data-act="close"]').addEventListener('click', () => overlay.remove());

  // 点背景关闭。
  overlay.addEventListener('click', (e) => {
    if (e.target === overlay) overlay.remove();
  });

  search.addEventListener('input', renderGrid);

  await load();
}

/**
 * cardart 文件夹里某个相对路径对应的引用值。
 *
 * 工程里存的是相对 cardart 的路径（不是绝对路径）：这样整个程序目录拷到别处，工程里的
 * 卡图仍然有效。绝对路径只在用户手输时保留，由服务端按原样处理。
 */
function localPathOf(relative) {
  return relative.replace(/\\/g, '/');
}

/** schema 里的效果定义，找不到时退回第一项。 */
function schemaEffect(kind) {
  return state.schema.effects.find((e) => e.kind === kind) || state.schema.effects[0];
}

/**
 * 效果种类的下拉项。
 *
 * 模型里可能出现列表里没有的种类——迁移期间 schema 只列出已经重构的那些，而**旧工程**
 * 里什么种类都有。这时把那一项原样补在最前面（写枚举名），不要让它静默显示成列表第一项：
 * 面板显示"获得能量"而数据是"伤害"，用户改完存盘才发现改的是别的东西。
 */
function effectKindOptions(selected) {
  const known = state.schema.effects.some((e) => e.kind === selected);

  const extra = known || !selected
    ? ''
    : `<option value="${escapeHtml(selected)}" selected>${escapeHtml(selected)}（未重构）</option>`;

  return extra + state.schema.effects
    .map((e) => `<option value="${e.kind}"${e.kind === selected ? ' selected' : ''}>${escapeHtml(uiLabel(e))}</option>`)
    .join('');
}

/**
 * 数值来源的下拉项，按分组排列。
 *
 * <paramref name="which"/> 为 'repeat' 时改读 <c>repeatSource</c>：重复次数用的是与数值
 * 相互独立的一套来源（原版"扯碎"的伤害是固定 5、次数才是算出来的），所以两者要能各选各的。
 */
function valueSourceOptions(source) {
  const src = source || {};
  const zh = uiLanguage === 'zh';
  const groups = { none: [], player: [], stats: [], creature: [] };

  state.schema.valueSourceKinds.forEach((k) => {
    const label = zh ? k.labelZh : k.labelEn;
    const item = `<option value="${k.value}"${k.value === (src.kind ?? 'None') ? ' selected' : ''}>${escapeHtml(label)}</option>`;

    if (k.value === 'None') groups.none.push(item);
    else if (k.playerScoped) groups.player.push(item);
    else if (k.powerType) groups.stats.push(item);
    else groups.creature.push(item);
  });

  const section = (name, items) => items.length
    ? `<optgroup label="${escapeHtml(name)}">${items.join('')}</optgroup>`
    : '';

  return section(t('srcNone'), groups.none) +
         section(t('srcPlayer'), groups.player) +
         section(t('srcStats'), groups.stats) +
         section(t('srcCreature'), groups.creature);
}

/** 某个来源是否属于玩家维度。 */
function isPlayerScoped(kind) {
  const found = (state.schema.valueSourceKinds || []).find((k) => k.value === kind);
  return found ? !!found.playerScoped : false;
}

/**
 * "从谁身上取"的下拉项。
 *
 * 只有非玩家维度（状态层数、生物属性）才需要——玩家资源与牌堆张数跟具体生物无关。
 *
 * 但**目标形同自身时要隐去**（例如这条效果的目标就是自己，那"自身 HP"和"目标 HP"是
 * 同一个数，给两个选项只会让人困惑）。判断依据是这条效果的实际目标。
 */
/**
 * "取自谁"（自身 / 目标）。只在**真的可选**时出现，别的来源（玩家维度）没有这一说。
 *
 * 三个入参各管一件事：source 是那一份来源本身（数值 / 次数 / 张数各一份），
 * field 是写回时用的 data-f 名，target 用来判断"目标"这一档是否存在。
 *
 * 早先这里收的是 `(eff, which)`，而次数那一处调用写成了 `actorOptions(eff, 'repeat')`——
 * 参数错位，于是次数那一格显示并改写的是**数值**的 actor（两处互相串味）。现在按来源对象传，
 * 谁的就是谁的。
 */
function actorOptions(source, owner, field, target) {
  const choices = actorChoicesFor(source, owner, target);

  if (choices.length === 0) {
    return '';
  }

  const labels = {
    Self: t('actorSelf'),
    Target: t('actorTarget'),
  };

  const current = source.actor ?? 'Self';

  return `
    <div class="field">
      <label>${escapeHtml(t('valueActor'))}</label>
      <select data-f="${field}">${choices
        .map((c) => `<option value="${c}"${c === current ? ' selected' : ''}>${escapeHtml(labels[c] || c)}</option>`)
        .join('')}</select>
    </div>`;
}

/**
 * 这条效果能选哪些"谁"。
 *
 * 只有**单个目标**才谈得上"取自目标"：
 *
 * - 目标是自身时，自身与目标是同一个生物，数值必然一样，留一个只有一项的下拉框等于给了
 *   一个点了没反应的控件；
 * - 目标是多个（所有敌人 / 随机一名敌人 / 所有友方）时并没有"那一个目标"。游戏自己就是这样
 *   约定的——`CalculatedVar.Calculate` 的说明写着"作用于多个生物（或没有目标）的效果应当
 *   传 null"。所以这类效果一律按自身取，界面上不给出会落空的选择。
 *
 * 取值仍然照旧按自身算，与实际行为一致。
 * （判定要看效果自己的目标，而不是卡牌的目标：一张攻击牌也可以有一条"给自己加格挡"的效果。）
 */
function actorChoicesFor(source, owner, target) {
  if (!source || isPlayerScoped(source.kind)) {
    return [];
  }

  // 状态里的效果作用于持有者，也就无所谓自身/目标之分。
  if (owner === 'power') {
    return [];
  }

  return target === 'Target' ? ['Self', 'Target'] : [];
}

/**
 * 牌堆名字。
 *
 * 标签直接用 t()：它已经按当前界面语言取好了词，不必再自己挑中英。
 * （早先这里把 t() 的返回值当成 [中文, 英文] 一对来下标取值，而 t() 返回的是**字符串**，
 * 于是取到的是第一个/第二个字符——下拉框里显示成"手""抽""弃""消"。）
 */
function pileName(p) {
  const names = {
    Hand: t('pileHand'),
    DrawPile: t('pileDraw'),
    DiscardPile: t('pileDiscard'),
    ExhaustPile: t('pileExhaust'),
  };

  return names[p] || p;
}

function pileOptions(selected) {
  return state.schema.piles
    .map((p) => `<option value="${p}"${p === selected ? ' selected' : ''}>${escapeHtml(pileName(p))}</option>`)
    .join('');
}

/** 指定卡牌的下拉项：本项目的卡 + 原版卡。 */
function cardComboItems() {
  const items = [];

  (state.project.cards || []).forEach((c) => {
    if (!c.id) return;
    items.push({
      id: `CARD.${slug(cardTypeName(c))}`,
      label: `${locGet(c.name, contentLanguage) || c.id} (${t('thisMod')})`,
    });
  });

  (state.catalog?.cards || []).forEach((c) => {
    const label = uiLanguage === 'zh' ? (c.nameZh || c.nameEn || c.entry) : (c.nameEn || c.entry);
    items.push({ id: c.id, label });
  });

  return items;
}

/** 状态的可输入下拉项：本项目的状态 + 原版状态。 */
function powerComboItems() {
  const items = [];

  (state.project.powers || []).forEach((p) => {
    items.push({
      id: p.id,
      label: `${locGet(p.name, contentLanguage) || p.id} (${t('thisMod')})`,
    });
  });

  (state.catalog?.powers || []).forEach((p) => {
    const label = (uiLanguage === 'zh' ? (p.nameZh || p.nameEn) : p.nameEn) || p.entry;
    items.push({ id: p.id, label });
  });

  return items;
}

/**
 * 能量球的候选项（写进去的是枚举名，显示的是游戏里的叫法）。
 *
 * 给多值输入组件（ui/components.js）用：`{value}` 是值、`{label}` 是显示名，
 * 与 `[{id, label}]` 那套只差一个键名——组件的接口统一用 value。
 */
function orbComboItems() {
  return (state.schema?.orbs || []).map((orb) => ({
    value: orb,
    label: gameTerm(`Orb.${orb}`) || orb,
  }));
}

/**
 * 卡牌 / 状态 id 的可输入下拉框。
 *
 * 为什么不用普通的 select：原版内容有 570+ 张牌、250+ 个状态，在长列表里翻找很慢，
 * 而模型 id 是能查到的（参考页里就写着 `CARD.CLAW` 这种），直接打进去更快。
 * input + datalist 两者都支持：
 *
 *   - 从下拉里挑 —— 列表显示"名字 — id"，选中后把 id 填进输入框；
 *   - 直接输入   —— 打名字或 id 的任何一部分都行，列表会自动筛出匹配项。
 *
 * option 的 value 装 id、可见文字里也带上 id。
 *
 * 不用 `label` 属性来只显示名字：浏览器对 datalist 的筛选依据不完全一致，
 * 有的只按可见文字筛。若可见文字里没有 id，用户按提示（"输入或选择卡牌 id"）
 * 打 `CARD.CLAW` 就会什么都筛不出来——恰好废掉了这个控件的主要用途。
 * 把 id 放进可见文字，名字和 id 两种打法都能筛到；选中后填进输入框的仍是 value。
 *
 * 旁边那行小字显示这个 id 现在指向什么，id 打错时能立刻看出来（见 comboHint）。
 */
let comboSeq = 0;

function idCombo(field, selected, items, placeholder, elementId) {
  const listId = `combo-${field}-${++comboSeq}`;
  const options = items
    .map((it) => `<option value="${escapeHtml(it.id)}">${escapeHtml(`${it.label} — ${it.id}`)}</option>`)
    .join('');

  const idAttr = elementId ? ` id="${escapeHtml(elementId)}"` : '';
  const hint = escapeHtml(comboHint(field, selected));

  return `<div class="combo">
    <input type="text" list="${listId}"${idAttr} data-f="${field}" autocomplete="off"
      value="${escapeHtml(selected || '')}" placeholder="${escapeHtml(placeholder)}">
    <datalist id="${listId}">${options}</datalist>
    <span class="combo-hint">${hint}</span>
  </div>`;
}

/**
 * 一个 id 当前指向什么。查不到就明说"未知 id"，而不是显示空——
 * 打错一个字母时，用户要能立刻看出来，而不是等到导出或进游戏才发现。
 */
function comboHint(field, id) {
  if (!id) {
    return '';
  }

  if (field === 'cardId' || field === 'p-filtercard') {
    const item = cardComboItems().find((c) => c.id === id);
    return item ? item.label : `${t('unknownId')}: ${id}`;
  }

  // 这几个字段都是"引用一个状态"，用同一套查询与回显。
  if (field === 'powerId' || field === 'sourcePowerId' || field === 'repeatSourcePowerId') {
    const norm = normPowerId(id);
    const item = powerComboItems().find((p) => normPowerId(p.id) === norm);
    return item ? item.label : `${t('unknownId')}: ${id}`;
  }

  return '';
}

/** 输入框里的 id 变了之后，就地刷新旁边那行小字（不重建表单，光标不会跳）。 */
function refreshComboHint(el) {
  const hint = el.parentElement?.querySelector('.combo-hint');
  if (hint) {
    hint.textContent = comboHint(el.dataset.f, el.value);
  }
}

/**
 * 取每行的文字说明。
 *
 * 说明由服务端生成，与"按效果自动填充"走同一套逻辑，所以界面上的说法和填进描述里的
 * 句子不会有出入。请求失败时留空，不影响编辑。
 */
async function fillRowSummaries(effects, host) {
  let res;
  try {
    res = await postJson('/api/describe-effects', { effects, language: contentLanguage });
  } catch (err) {
    return;
  }

  if (!res.ok || !res.summaries) return;

  host.querySelectorAll('[data-summary-for]').forEach((el) => {
    const index = Number(el.dataset.summaryFor);
    el.textContent = res.summaries[index] || '';
  });
}

/**
 * 按"当前编辑中的效果"生成描述。
 *
 * 把效果一起发给服务端，而不是只发卡牌 id：后者会用工程里已保存的版本，于是刚改的效果
 * 要等点了校验或生成才反映出来——那样就只能"先改后验"，没法边改边看。
 *
 * 返回结果而不是直接写界面：这样"填入"按钮可以直接用它，不必依赖预览区是否已经渲染过。
 */
async function suggestDescriptionFor(card) {
  try {
    const res = await postJson('/api/suggest-description', {
      id: card.id,
      language: contentLanguage,
      effects: card.effects || [],
      keywords: card.keywords || [],
    });

    return (res.ok && res.description)
      ? { ok: true, description: res.description }
      : { ok: false, error: res.error || t('nothingToDescribe') };
  } catch (err) {
    return { ok: false, error: t('previewFailed') };
  }
}

/** 把生成结果画到预览区。 */
async function fillAutoDescription(card) {
  const host = document.querySelector('[data-auto-desc]');
  if (!host) return;

  const res = await suggestDescriptionFor(card);
  host.dataset.empty = res.ok ? '0' : '1';
  host.textContent = res.ok ? res.description : res.error;
}

/** schema 条目在界面语言下的标签。 */
function uiLabel(meta) {
  return uiLanguage === 'zh'
    ? (meta.labelZh || meta.label || meta.kind)
    : (meta.labelEn || meta.label || meta.kind);
}

/** 数值来源的可读标签，用于下拉框分组之外的即时提示。 */
function valueSourceLabel(source) {
  const found = (state.schema.valueSourceKinds || []).find((k) => k.value === source.kind);
  const label = found ? (uiLanguage === 'zh' ? found.labelZh : found.labelEn) : source.kind;
  const per = source.perUnit ?? 1;

  if (found && found.playerScoped) {
    return per === 1 ? label : `${per} × ${label}`;
  }

  const actor = {
    Self: t('actorSelf'),
    Target: t('actorTarget'),
    AllEnemies: t('actorAllEnemies'),
    AllAllies: t('actorAllAllies'),
  }[source.actor] || '';

  const base = uiLanguage === 'zh' ? `${actor}${label}` : `${actor} ${label}`;
  return per === 1 ? base : `${per} × ${base}`;
}

/**
 * 状态候选（本项目定义的在前、原版在后），供 idCombo 用。
 *
 * 名字跟着**语言**变：
 *   本项目的状态 —— 用内容语言（那是用户自己填的文本，逐语言存放）
 *   原版状态     —— 用界面语言（术语表只有界面语言那份；原版名字来自游戏本地化表，
 *                   编辑器只取了英语与简体中文两种，与参考页一致）
 *
 * 顺带修掉一个真 bug：以前原版那一支固定取 `p.nameEn`，中文界面下永远是英文；
 * 本项目那一支取的是 `p.name`，而它是 {zhs:"…"} 这样的对象，会渲染成 "[object Object]"。
 */
/* powerComboItems 与 cardComboItems 一起放在 idCombo 旁边，两者结构一致。 */

/* ================================================================== POWERS */

function newPower() {
  return {
    id: 'NEW_POWER',
    name: 'New Power',
    description: 'Your attacks deal {Amount} additional damage. Lose 1 stack at the end of your turn.',
    kind: 'Buff',
    stack: 'Counter',
    damageModifier: 'Additive',
    decay: 'OnePerTurnEnd',
    triggers: [],
    iconFile: null,
  };
}

function renderPowerList() {
  const list = $('powerList');
  list.innerHTML = '';
  (state.project.powers || []).forEach((power, i) => {
    const li = document.createElement('li');
    if (i === state.selPower) li.classList.add('is-active');
    const untranslated = !locHas(power.name, contentLanguage) ? ' ·' : '';
    li.innerHTML = `<div>${escapeHtml(locGet(power.name, contentLanguage) || power.id)}${untranslated}</div>
      <div class="li-id">${escapeHtml(power.kind)} · ${escapeHtml(power.stack)}</div>`;
    li.addEventListener('click', () => { state.selPower = i; renderPowerList(); renderPowerEditor(); });
    list.appendChild(li);
  });
}

function renderPowerEditor() {
  const host = $('powerEditor');
  const power = (state.project.powers || [])[state.selPower];
  if (!power) {
    host.innerHTML = '<div class="empty">Select a buff, or create a new one.</div>';
    return;
  }

  const s = state.schema;
  host.innerHTML = `
    <h2>${escapeHtml(locGet(power.name, contentLanguage) || power.id)}</h2>
    <div class="mono">model id: POWER.${escapeHtml(slug(powerTypeName(power)))}</div>

    <div class="grid">
      <div class="field">
        <label>Id (used for the class name)</label>
        <input type="text" id="p-id" value="${escapeHtml(power.id)}">
      </div>
      <div class="field">
        <div class="field-head">
          <label>Name</label>
          ${langPickerHtml()}
        </div>
        <input type="text" id="p-name" value="${escapeHtml(locGet(power.name, contentLanguage))}">
      </div>
      <div class="field">
        <label>Kind</label>
        <select id="p-kind">${namedOptionList('PowerKind', s.powerKinds, power.kind)}</select>
      </div>
      <div class="field">
        <label>Stacking</label>
        <select id="p-stack">${namedOptionList('PowerStack', s.powerStacks, power.stack)}</select>
      </div>
      <div class="field">
        <label>${escapeHtml(t('damageModifier'))}</label>
        <select id="p-dmg">${namedOptionList('DamageModifier', s.damageModifiers, power.damageModifier)}</select>
      </div>
      ${power.damageModifier === 'Additive' ? `
      <div class="field">
        <label>${escapeHtml(t('cardFilter'))}</label>
        <select id="p-filter">${(s.cardFilters || []).map((f) =>
          `<option value="${f}"${f === (power.cardFilter || 'Any') ? ' selected' : ''}>${escapeHtml(t('filter_' + f))}</option>`
        ).join('')}</select>
      </div>
      ${power.cardFilter === 'SpecificCard' ? `
      <div class="field span-all">
        <label>${escapeHtml(t('namedCard'))}</label>
        ${idCombo('p-filtercard', power.filterCardId, cardComboItems(), t('pickCard'), 'p-filtercard')}
      </div>` : ''}` : ''}
      <div class="field">
        <label>${escapeHtml(t('decay'))}</label>
        <select id="p-decay">${namedOptionList('DecayMode', s.decayModes, power.decay)}</select>
      </div>
      <div class="field span-all">
        <label>Description (use {Amount} to show the stack count)</label>
        <textarea id="p-desc">${escapeHtml(locGet(power.description, contentLanguage))}</textarea>
      </div>
    </div>

    <div class="section">
      <div class="section-head">
        <h3>${escapeHtml(t('turnTriggers'))}</h3>
        <button class="btn btn-small" id="btnAddTrigger">+ ${escapeHtml(t('addTrigger'))}</button>
      </div>
      <div id="triggerRows"></div>
    </div>

    <div class="preview">
      <h3>${escapeHtml(t('effectSummary'))}</h3>
      <div class="pv-body">${escapeHtml(powerSummary(power))}</div>
    </div>

    <div class="section">
      <div class="row-actions">
        <button class="btn btn-danger btn-small" id="btnDelPower">${escapeHtml(t('deletePower'))}</button>
      </div>
    </div>
  `;

  bindPower(power);
  renderTriggerRows(power, $('triggerRows'));
}

function powerTypeName(power) {
  const prefix = state.project.typePrefix || '';
  return prefix + pascal(power.id) + 'Power';
}

function powerSummary(power) {
  const parts = [power.kind === 'Debuff' ? 'Debuff' : 'Buff'];
  if (power.damageModifier === 'Additive') parts.push('adds {Amount} damage to the owner\'s attacks');
  if (power.damageModifier === 'Multiplicative') parts.push('increases damage taken by {Amount}%');
  (power.triggers || []).forEach((t) => parts.push(`${t.when}: ${t.effect.kind} ${t.effect.amount}`));
  if (power.decay === 'OnePerTurnEnd') parts.push('loses 1 stack each turn end');
  if (power.decay === 'AllAtTurnEnd') parts.push('expires at turn end');
  return parts.join('; ');
}

function bindPower(power) {
  // 文本类字段只写入值，不重绘：重建表单会重置滚动位置，也会丢掉正在输入的小数点。
  // 需要联动的地方（卡面、占位符、行摘要）由 refreshDerived 单独更新。
  const set = (id, fn) => {
    const el = $(id);
    if (el) el.addEventListener('input', () => { fn(el.value); });
  };

  // 改了会影响其它字段结构的字段，才需要重建表单。
  const setStructural = (id, fn) => {
    const el = $(id);
    if (el) el.addEventListener('change', () => { fn(el.value); refreshAll(); });
  };

  set('p-id', (v) => { power.id = v; });
  set('p-name', (v) => { power.name = locSet(power.name, contentLanguage, v); });
  set('p-desc', (v) => { power.description = locSet(power.description, contentLanguage, v); });

  ['kind', 'stack', 'damageModifier', 'decay'].forEach((f) => {
    const el = $('p-' + ({ kind: 'kind', stack: 'stack', damageModifier: 'dmg', decay: 'decay' }[f]));
    if (el) el.addEventListener('change', (e) => { power[f] = e.target.value; refreshAll(); });
  });

  // 伤害增益的筛选范围，只有"增加伤害"这一种才有意义。
  const filterEl = $('p-filter');
  if (filterEl) {
    filterEl.addEventListener('change', (e) => { power.cardFilter = e.target.value; refreshAll(); });
  }

  const filterCardEl = $('p-filtercard');
  if (filterCardEl) {
    filterCardEl.addEventListener('input', () => {
      power.filterCardId = filterCardEl.value;
      refreshComboHint(filterCardEl);
    });
  }

  $('btnAddTrigger').addEventListener('click', () => {
    power.triggers = power.triggers || [];
    power.triggers.push({
      when: 'TurnStart',
      effect: { kind: 'GainBlock', trigger: 'OnPlay', target: 'Self', amount: 4, source: { actor: 'Self', kind: 'None', perUnit: 1 }, powerId: null, pile: 'Hand', placement: 'Top', count: 1, cardId: null, upgraded: false, repeat: 1, upgrade: { amountDelta: 0 } },
    });
    refreshAll();
  });

  $('btnDelPower').addEventListener('click', () => {
    state.project.powers.splice(state.selPower, 1);
    state.selPower = state.project.powers.length ? Math.min(state.selPower, state.project.powers.length - 1) : -1;
    refreshAll();
  });
}

function renderTriggerRows(power, host) {
  const triggers = power.triggers || [];
  host.innerHTML = '';

  triggers.forEach((trigger, i) => {
    const meta = state.schema.effects.find((e) => e.kind === trigger.effect.kind) || state.schema.effects[0];
    const row = document.createElement('div');
    row.className = 'card-row';
    row.innerHTML = `
      <div class="row-head">
        <span class="row-title">${escapeHtml(t('rowWhen', { trigger: gameTerm('PowerTrigger.' + trigger.when) || trigger.when }))}</span>
        <div class="row-actions">
          <button class="btn btn-small btn-danger" data-act="del">${escapeHtml(t('remove'))}</button>
        </div>
      </div>
      <div class="grid">
        <div class="field">
          <label>${escapeHtml(t('trigger'))}</label>
          <select data-f="when">${namedOptionList('PowerTrigger', state.schema.powerTriggers, trigger.when)}</select>
        </div>
        <div class="field">
          <label>Effect</label>
          <select data-f="kind">${optionList(state.schema.effects.map((e) => e.kind), trigger.effect.kind)}</select>
        </div>
        <div class="field">
          <label>${escapeHtml(t('target'))}</label>
          ${targetField(meta, { kind: 'power' }, trigger.effect.target)}
        </div>
        ${meta.amount ? `
        <div class="field">
          <label>${escapeHtml(t('amount'))}</label>
          <input type="text" inputmode="numeric" data-f="amount" value="${trigger.effect.amount ?? 0}">
        </div>` : ''}
        ${meta.needsPower ? `
        <div class="field span-2">
          <label>${escapeHtml(t('powerPick'))}</label>
          ${idCombo('powerId', trigger.effect.powerId, powerComboItems(), t('pickPower'))}
        </div>` : ''}
      </div>
    `;

    row.querySelectorAll('[data-f]').forEach((el) => {
      el.addEventListener('input', () => {
        const f = el.dataset.f;
        if (f === 'when') trigger.when = el.value;
        else if (f === 'amount') trigger.effect.amount = Number(el.value) || 0;
        else if (f === 'kind') {
          trigger.effect.kind = el.value;

          // 换了种类后目标可能不再合法：候选是按作用域给的，跨了作用域就落到新作用域的默认档。
          const next = schemaEffect(trigger.effect.kind);
          if (!(next.targets || []).includes(trigger.effect.target)) {
            trigger.effect.target = next.defaultTarget || (next.targets || [])[0];
          }

        } else if (f === 'target') {
          trigger.effect.target = el.value;
        } else if (f === 'powerId') trigger.effect.powerId = el.value || null;
        refreshAll();
      });
    });

    row.querySelector('[data-act="del"]').addEventListener('click', () => {
      triggers.splice(i, 1);
      refreshAll();
    });

    host.appendChild(row);
  });

  if (triggers.length === 0) {
    host.innerHTML = `<p class="hint" style="padding:0">${escapeHtml(t('noTriggers'))}</p>`;
  }
}

/* ================================================================== SKINS */

let skinState = {
  installed: null,     // list of installed skin packs
  read: null,          // parsed detail for the selected pack
  selIndex: -1,
};

async function loadInstalledSkins(force) {
  if (skinState.installed && !force) return skinState.installed;
  const res = await api('/api/skins');
  skinState.installed = res.installed || [];
  return skinState.installed;
}

async function renderSkins() {
  const list = $('skinList');
  list.innerHTML = '<li class="hint" style="padding:8px 11px">Scanning installed skins…</li>';

  let packs;
  try {
    packs = await loadInstalledSkins();
  } catch (err) {
    list.innerHTML = `<li class="hint" style="padding:8px 11px">Could not scan skins: ${escapeHtml(err.message)}</li>`;
    return;
  }

  if (packs.length === 0) {
    list.innerHTML = '<li class="hint" style="padding:8px 11px">No skin packs found.</li>';
    return;
  }

  list.innerHTML = '';
  packs.forEach((pack, i) => {
    const li = document.createElement('li');
    if (i === skinState.selIndex) li.classList.add('is-active');
    const targets = (pack.targets || []).join(', ') || '—';
    li.innerHTML = `<div>${escapeHtml(pack.name)}</div>
      <div class="li-id">${escapeHtml(targets)} · ${pack.animationCount} animations</div>`;
    li.addEventListener('click', () => selectSkin(i));
    list.appendChild(li);
  });
}

async function selectSkin(index) {
  skinState.selIndex = index;
  const pack = skinState.installed[index];
  renderSkins();

  const host = $('skinEditor');
  host.innerHTML = '<div class="empty">Reading skeleton files…</div>';

  try {
    skinState.read = await postJson('/api/skins/read', { path: pack.path });
  } catch (err) {
    host.innerHTML = `<div class="empty">Could not read this skin: ${escapeHtml(err.message)}</div>`;
    return;
  }

  renderSkinEditor();
}

function renderSkinEditor() {
  const host = $('skinEditor');
  const read = skinState.read;
  if (!read || !read.ok) {
    host.innerHTML = `<div class="empty">${escapeHtml(read?.error || 'Nothing selected.')}</div>`;
    return;
  }

  // The project may already carry a skin definition for this pack.
  const existing = (state.project.skins || []).find((s) => s.sourcePack === read.sourcePath);
  const bindings = existing?.slotBindings || read.suggested || {};

  const animations = read.animations || [];
  const byTarget = {};
  (read.resources || []).forEach((r) => {
    byTarget[r.target] = byTarget[r.target] || {};
    byTarget[r.target][r.kind] = (byTarget[r.target][r.kind] || 0) + 1;
  });

  host.innerHTML = `
    <h2>${escapeHtml(read.name)}</h2>
    <div class="mono">${escapeHtml(read.sourcePath)}</div>

    <div class="grid">
      <div class="field">
        <label>Applies to</label>
        <select id="sk-target">
          ${(read.targets || []).map((t) =>
            `<option value="${escapeHtml(t)}"${t === (existing?.target || read.targets[0]) ? ' selected' : ''}>${escapeHtml(t)}</option>`).join('')}
        </select>
      </div>
    </div>

    <div class="section">
      <div class="section-head">
        <h3>${escapeHtml(t('resourcesReplaced'))}</h3>
      </div>
      <div class="grid">
        ${Object.entries(byTarget).map(([target, kinds]) => `
          <div class="card-row">
            <div class="row-title">${escapeHtml(target)}</div>
            <div class="effects-summary">
              ${Object.entries(kinds).map(([k, n]) => `${escapeHtml(k)}: ${n}`).join(' · ')}
            </div>
          </div>`).join('') || `<p class="hint" style="padding:0">${escapeHtml(t('noAnimResources'))}</p>`}
      </div>
    </div>

    <div class="section">
      <div class="section-head">
        <h3>${escapeHtml(t('animationSlots'))}</h3>
        <span class="mono">${escapeHtml(t('animationsFound', { n: animations.length }))}</span>
      </div>
      <div class="grid">
        ${(read.slots || []).map((slot) => `
          <div class="field">
            <label title="${escapeHtml(slot.description)}">${escapeHtml(slot.label)} — ${escapeHtml(slot.description)}</label>
            <select data-slot="${escapeHtml(slot.key)}">
              <option value="">${escapeHtml(t('useGameDefault'))}</option>
              ${animations.map((a) =>
                `<option value="${escapeHtml(a)}"${a === bindings[slot.key] ? ' selected' : ''}>${escapeHtml(a)}</option>`).join('')}
            </select>
          </div>`).join('')}
      </div>
    </div>

    <div class="preview">
      <h3>${escapeHtml(t('allAnimations'))}</h3>
      <div class="pv-body mono" style="white-space:pre-wrap">${escapeHtml(animations.join(', '))}</div>
    </div>

    <div class="section">
      <div class="row-actions">
        <button class="btn btn-primary btn-small" id="btnAddSkin">${escapeHtml(t('addSkinToMod'))}</button>
        <button class="btn btn-small" id="btnSuggestSkin">${escapeHtml(t('resetSuggestions'))}</button>
      </div>
    </div>
  `;

  host.querySelectorAll('select[data-slot]').forEach((sel) => {
    sel.addEventListener('change', () => { sel.dataset.chosen = sel.value; });
  });

  $('btnSuggestSkin').addEventListener('click', () => {
    host.querySelectorAll('select[data-slot]').forEach((sel) => {
      sel.value = (read.suggested || {})[sel.dataset.slot] || '';
    });
    toast('Bindings reset to suggestions');
  });

  $('btnAddSkin').addEventListener('click', () => {
    const slotBindings = {};
    host.querySelectorAll('select[data-slot]').forEach((sel) => {
      if (sel.value) slotBindings[sel.dataset.slot] = sel.value;
    });

    const def = {
      sourcePack: read.sourcePath,
      name: read.name,
      target: $('sk-target').value,
      slotBindings,
      availableAnimations: animations.slice(0, 300),
      routes: (read.resources || []).map((r) => r.route).slice(0, 300),
    };

    state.project.skins = state.project.skins || [];
    const at = state.project.skins.findIndex((s) => s.sourcePack === read.sourcePath);
    if (at >= 0) {
      state.project.skins[at] = def;
    } else {
      state.project.skins.push(def);
    }

    toast('Skin added to the mod', 'ok');
    logTo(`skin added: ${def.name} (${def.target}) with ${Object.keys(slotBindings).length} binding(s)`);
  });
}

$('btnRefreshSkins').addEventListener('click', async () => {
  await loadInstalledSkins(true);
  skinState.read = null;
  skinState.selIndex = -1;
  renderSkins();
  $('skinEditor').innerHTML = '<div class="empty">Pick an installed skin to see its animations.</div>';
  toast('Skin list refreshed');
});

/* ================================================================== SHARED EFFECTS */

function newSharedEffect() {
  const used = new Set((state.project.sharedEffects || []).map((s) => slug(s.id)));
  let n = 1;
  while (used.has(`NEW_EFFECT_${n}`)) n++;

  return {
    id: `NEW_EFFECT_${n}`,
    name: `New Effect ${n}`,
    note: '',
    effect:
      { kind: 'DealDamage', trigger: 'OnPlay', target: 'Target', amount: 6, source: { actor: 'Self', kind: 'None', perUnit: 1 }, powerId: null, pile: 'Hand', placement: 'Top', count: 1, cardId: null, upgraded: false, repeat: 1, upgrade: { amountDelta: 0 } },
  };
}

/** How many cards and buffs reference a shared effect, so its reach is visible. */
function usageCount(id) {
  const wanted = slug(id);
  let n = 0;

  (state.project.cards || []).forEach((c) => {
    (c.effects || []).forEach((e) => {
      if (e.sharedEffectId && slug(e.sharedEffectId) === wanted) n++;
    });
  });

  (state.project.powers || []).forEach((p) => {
    (p.triggers || []).forEach((t) => {
      if (t.effect && t.effect.sharedEffectId && slug(t.effect.sharedEffectId) === wanted) n++;
    });
  });

  return n;
}

function renderSharedList() {
  const list = $('sharedList');
  if (!list) return;

  list.innerHTML = '';

  (state.project.sharedEffects || []).forEach((shared, i) => {
    const li = document.createElement('li');
    if (i === state.selShared) li.classList.add('is-active');

    const uses = usageCount(shared.id);
    li.innerHTML = `<div>${escapeHtml(shared.name)}</div>
      <div class="li-id">${escapeHtml(shared.effect.kind)} ${escapeHtml(shared.effect.amount)} · used ${uses}×</div>`;

    li.addEventListener('click', () => { state.selShared = i; refreshAll(); });
    list.appendChild(li);
  });

  if ((state.project.sharedEffects || []).length === 0) {
    list.innerHTML = `<p class="hint">${escapeHtml(t('noSharedEffects'))}</p>`;
  }
}

function renderSharedEditor() {
  const host = $('sharedEditor');
  if (!host) return;

  const shared = (state.project.sharedEffects || [])[state.selShared];

  if (!shared) {
    host.innerHTML = '<div class="empty">Select an effect, or create a new one.</div>';
    return;
  }

  const uses = usageCount(shared.id);

  host.innerHTML = `
    <h2>${escapeHtml(shared.name || 'Effect')}</h2>
    <div class="mono">referenced ${uses} time(s)</div>

    <div class="grid">
      <div class="field">
        <label>Name — shown when picking this effect</label>
        <input type="text" id="s-name" value="${escapeHtml(shared.name)}">
      </div>
      <div class="field">
        <label>Id — used by references, so avoid changing it once used</label>
        <input type="text" id="s-id" value="${escapeHtml(shared.id)}">
      </div>
      <div class="field span-all">
        <label>Note for yourself — not shown to players</label>
        <input type="text" id="s-note" value="${escapeHtml(shared.note || '')}">
      </div>
    </div>

    <div class="section">
      <div class="section-head"><h3>${escapeHtml(t('behaviour'))}</h3></div>
      <div id="sharedEffectRow"></div>
    </div>

    <div class="section">
      <div class="row-actions">
        <button class="btn btn-danger btn-small" id="btnDelShared">${escapeHtml(t('deleteEffect'))}</button>
      </div>
      ${uses > 0 ? `<p class="hint" style="padding:10px 0 0">Used in ${uses} place(s). Deleting it
        leaves those references unresolved; the build reports them.</p>` : ''}
    </div>
  `;

  bindSharedEffect(shared);

  // Reuses the same row renderer as cards, so a shared effect behaves identically.
  renderEffectRows([shared.effect], $('sharedEffectRow'), null, 'shared');
}

function bindSharedEffect(shared) {
  // 文本类字段只写入值，不重绘：重建表单会重置滚动位置，也会丢掉正在输入的小数点。
  // 需要联动的地方（卡面、占位符、行摘要）由 refreshDerived 单独更新。
  const set = (id, fn) => {
    const el = $(id);
    if (el) el.addEventListener('input', () => { fn(el.value); });
  };

  // 改了会影响其它字段结构的字段，才需要重建表单。
  const setStructural = (id, fn) => {
    const el = $(id);
    if (el) el.addEventListener('change', () => { fn(el.value); refreshAll(); });
  };

  set('s-name', (v) => { shared.name = v; });
  set('s-id', (v) => { shared.id = v; });
  set('s-note', (v) => { shared.note = v; });

  $('btnDelShared').addEventListener('click', () => {
    const uses = usageCount(shared.id);
    if (uses > 0 && !confirm(`"${shared.name}" is used ${uses} time(s). Delete it anyway?`)) return;

    state.project.sharedEffects.splice(state.selShared, 1);
    state.selShared = state.project.sharedEffects.length
      ? Math.min(state.selShared, state.project.sharedEffects.length - 1)
      : -1;
    refreshAll();
  });
}

/* ================================================================== PROJECT */

function renderProjectForm() {
  const p = state.project;

  $('projectForm').innerHTML = `
    <div class="field">
      <label>${escapeHtml(t('projId'))}</label>
      <input type="text" id="pr-id" value="${escapeHtml(p.id)}">
    </div>
    <div class="field">
      <label>${escapeHtml(t('projName'))}</label>
      <input type="text" id="pr-name" value="${escapeHtml(p.name)}">
    </div>
    <div class="field">
      <label>${escapeHtml(t('projAuthor'))}</label>
      <input type="text" id="pr-author" value="${escapeHtml(p.author || '')}">
    </div>
    <div class="field">
      <label>${escapeHtml(t('projVersion'))}</label>
      <input type="text" id="pr-version" value="${escapeHtml(p.version || '1.0.0')}">
    </div>
    <div class="field">
      <label>${escapeHtml(t('projPrefix'))}</label>
      <input type="text" id="pr-prefix" value="${escapeHtml(p.typePrefix || '')}">
    </div>
    <div class="field check">
      <input type="checkbox" id="pr-selfcheck" ${p.selfCheckOnStartup ? 'checked' : ''}>
      <label for="pr-selfcheck">${escapeHtml(t('projSelfcheck'))}</label>
    </div>
    <div class="field span-all">
      <label>${escapeHtml(t('projDesc'))}</label>
      <textarea id="pr-desc">${escapeHtml(p.description || '')}</textarea>
    </div>
  `;

  const bind = (id, fn) => {
    const el = $(id);
    if (el) el.addEventListener('input', () => { fn(el.value); });
  };

  bind('pr-id', (v) => { p.id = v; });
  bind('pr-name', (v) => { p.name = v; });
  bind('pr-author', (v) => { p.author = v; });
  bind('pr-version', (v) => { p.version = v; });
  bind('pr-prefix', (v) => { p.typePrefix = v; });
  bind('pr-desc', (v) => { p.description = v; });
  $('pr-selfcheck').addEventListener('change', (e) => { p.selfCheckOnStartup = e.target.checked; });
}

/* ================================================================== REFERENCE */

async function loadCatalog() {
  if (!state.catalog) {
    state.catalog = await api('/api/catalog');
  }
  return state.catalog;
}

async function renderReference() {
  await loadCatalog();
  const query = ($('refSearch').value || '').toLowerCase();
  const items = state.catalog[state.refCat] || [];
  const filtered = query
    ? items.filter((it) =>
        (it.id + ' ' + (it.nameEn || '') + ' ' + (it.nameZh || '')).toLowerCase().includes(query))
    : items;

  const list = $('refList');
  list.innerHTML = '';
  filtered.slice(0, 400).forEach((it) => {
    const li = document.createElement('li');
    li.innerHTML = `<div>${escapeHtml(it.nameEn || it.nameZh || it.entry)}</div>
      <div class="li-id">${escapeHtml(it.id)}</div>`;
    if (state.refSel && state.refSel.id === it.id) li.classList.add('is-active');
    li.addEventListener('click', () => {
      state.refSel = it;
      renderReference();
      $('refDetail').innerHTML = `
        <div class="preview">
          <h3>${escapeHtml(it.id)}</h3>
          <div class="pv-name">${escapeHtml(it.nameEn || '—')}</div>
          <div class="pv-meta">zhs: ${escapeHtml(it.nameZh || '—')}</div>
          <div class="pv-meta">${escapeHtml(it.id)}</div>
        </div>`;
    });
    list.appendChild(li);
  });

  if (filtered.length === 0) {
    list.innerHTML = `<p class="hint">${escapeHtml(t('noMatches'))}</p>`;
  }
}

document.querySelectorAll('#refFilter .chip').forEach((chip) => {
  chip.addEventListener('click', () => {
    state.refCat = chip.dataset.cat;
    document.querySelectorAll('#refFilter .chip').forEach((c) => c.classList.toggle('is-active', c === chip));
    renderReference();
  });
});
$('refSearch').addEventListener('input', () => renderReference());

/* ================================================================== ACTIONS */

$('btnAddCard').addEventListener('click', () => {
  state.project.cards = state.project.cards || [];
  state.project.cards.push(newCard());
  state.selCard = state.project.cards.length - 1;
  refreshAll();
});

$('btnAddPower').addEventListener('click', () => {
  state.project.powers = state.project.powers || [];
  state.project.powers.push(newPower());
  state.selPower = state.project.powers.length - 1;
  refreshAll();
});

$('btnAddShared').addEventListener('click', () => {
  state.project.sharedEffects = state.project.sharedEffects || [];
  state.project.sharedEffects.push(newSharedEffect());
  state.selShared = state.project.sharedEffects.length - 1;
  refreshAll();
});

$('btnValidate').addEventListener('click', async () => {
  try {
    const res = await postJson('/api/validate', state.project);
    renderIssues(res.issues);

    const bad = res.issues.some((i) => i.severity === 'error');
    toast(t(bad ? 'problemsFound' : 'noProblems'), bad ? 'err' : 'ok');
  } catch (err) {
    toast(t('validateFailed') + err.message, 'err');
  }
});

function renderIssues(issues) {
  const host = $('issues');
  if (!issues || issues.length === 0) {
    host.innerHTML = '';
    return;
  }

  host.innerHTML = issues.map((i) => `
    <div class="issue ${escapeHtml(i.severity)}">
      <span class="tag">${escapeHtml(i.severity)}</span>
      <span>${escapeHtml(i.message)}</span>
    </div>`).join('');
}

/* ================================================================== 设置页 */

/**
 * 程序默认路径。
 *
 * 这里放的是"与具体是哪份项目无关"的出入口：游戏本体在哪、导出的 mod 写哪、
 * 去哪里挑卡图、还去哪里找皮肤包，以及新建项目默认建在哪个父目录下。
 *
 * 项目**自己**该放哪儿由"新建项目的父目录"决定；项目内部的目录布局是固定的
 * （project.json + cardart + sources，见项目页），不在这里配置。
 *
 * 它们描述的是"这台机器上东西在哪"，与"项目要做成什么"是两回事，所以存在
 * settings.json 而不是项目文件里——项目因此可以原样分享给别人，各人自己配路径。
 */
function renderSettings() {
  const s = state.settings || {};

  // 路径行 = 输入框 + 一个"浏览…"按钮。浏览走服务端的原生对话框：
  // 浏览器给不了真实路径（<input type=file> 只给文件内容），所以这件事只能服务端做。
  const row = (id, key, label, hint) => `
    <div class="field span-all">
      <label>${escapeHtml(label)}</label>
      <div class="path-row">
        <input type="text" id="${id}" value="${escapeHtml(s[key] || '')}"
               placeholder="${escapeHtml(hint)}" data-path-key="${key}">
        <button class="btn btn-small" data-pick data-for="${id}"
                data-title="${escapeHtml(label)}">${escapeHtml(t('browse'))}</button>
      </div>
    </div>`;

  $('settingsForm').innerHTML = `
    ${row('set-game', 'gameDirectory', t('setGameDir'), t('setGameDirHint'))}
    ${row('set-export', 'exportDirectory', t('setExportDir'), t('setExportDirHint'))}
    ${row('set-cardart', 'cardArtDirectory', t('setCardArtDir'), t('setCardArtDirHint'))}
    ${row('set-skins', 'skinDirectory', t('setSkinDir'), t('setSkinDirHint'))}
    ${row('set-projects-root', 'projectsRoot', t('setProjectsRoot'), t('setProjectsRootHint'))}
    <div class="field span-all">
      <label>${escapeHtml(t('setSettingsFile'))}</label>
      <input type="text" readonly value="${escapeHtml(state.settingsPath || '')}">
    </div>`;

  // 改动先写进 state，点保存时才提交——避免每敲一个字就重建游戏目录那一堆缓存。
  $('settingsForm').querySelectorAll('input[data-path-key]').forEach((el) => {
    el.addEventListener('input', () => { state.settings[el.dataset.pathKey] = el.value; });
  });

  wireBrowseButtons($('settingsForm'));
  $('settingsStatus').textContent = '';

  renderSentences();
}

/**
 * 句式模块的位置与已有语言。
 *
 * 这个文件夹与"在做哪份项目"无关，所以放在设置页。显示出来是因为它是可以自己扩的：
 * 往里丢一个 <语言代码>.json 就能加一种语言，不必重新编译——不写出来没人知道该放哪。
 */
function renderSentences() {
  const host = $('sentencesForm');
  if (!host) {
    return;
  }

  const sentences = state.sentences || null;

  host.innerHTML = sentences ? `
    <div class="grid">
      <div class="field span-all">
        <label>${escapeHtml(t('sentencesFolder'))}</label>
        <input type="text" readonly value="${escapeHtml(sentences.folder || '')}">
      </div>
      <div class="field span-all">
        <label>${escapeHtml(t('sentencesLangs'))}</label>
        <input type="text" readonly value="${escapeHtml((sentences.available || []).join(', '))}">
      </div>
    </div>` : '';
}

/**
 * 给一块区域里的"浏览…"按钮接上原生文件夹选择对话框。
 *
 * 按钮用 data-for 指出结果回填哪个输入框，data-title 是对话框标题（一般就是字段标签，
 * 让用户一眼看出"这次是在选哪个路径"）。
 */
function wireBrowseButtons(scope) {
  scope.querySelectorAll('button[data-pick]').forEach((btn) => {
    btn.addEventListener('click', async () => {
      const input = $(btn.dataset.for);

      btn.disabled = true;
      const original = btn.textContent;

      try {
        // 浏览是阻塞式的（对话框要人操作），所以按钮上给个明确反馈。
        btn.textContent = t('browsing');

        const res = await postJson('/api/pick-folder', {
          startAt: (input?.value || '').trim(),
          title: btn.dataset.title || '',
        });

        if (!res.ok) {
          toast(res.error || t('pickFailed'), 'err');
          return;
        }

        // 取消时什么都不做，输入框保持原样。
        if (res.path) {
          input.value = res.path;
          input.dispatchEvent(new Event('input'));
        }
      } catch (err) {
        toast(t('pickFailed') + err.message, 'err');
      } finally {
        btn.disabled = false;
        btn.textContent = original;
      }
    });
  });
}

/**
 * 项目文件夹：项目建在哪里（最后一个字段，接在项目信息下面）。
 *
 * 项目是一份**独立文件夹**，以项目名命名，里面的布局是固定的（见 README 那八个）。
 * 这些名字与位置不可自定义——这样"打开"一个项目永远知道该去哪里找东西，项目文件夹也可以
 * 原样拷给别人。既然不可配置，界面上就没必要把它们逐条列出来占地方。
 *
 * 这里还要说清一件事：**当前这份工程有没有真的落到那个文件夹上**。编辑器启动时是从
 * 一份空工程开始的（不自动载入上次的项目），而路径栏里显示的是工作目录——不区分的话，
 * 看起来就像已经打开了那个项目，而卡牌列表却是空的。
 */
function renderProjectFile() {
  const cover = state.project?.previewFile || '';

  // 当前这份工程有没有落到文件夹上。**启动时是空工程**（编辑器的设计如此），
  // 那时候路径栏里显示的是"上次那个工程文件夹"——不说清楚的话，看起来就像已经打开了它，
  // 而卡牌列表却是空的（用户会以为卡牌或某个模块丢了）。
  const unbound = !state.projectBound;
  const last = state.lastProjectDirectory || '';

  const stateLine = unbound
    ? `<p class="hint">${escapeHtml(t('projUnbound'))}</p>`
    : '';

  const lastRow = unbound && last
    ? `<div class="field span-all">
         <label>${escapeHtml(t('projLastOpened'))}</label>
         <div class="path-row">
           <input type="text" readonly value="${escapeHtml(last)}">
           <button class="btn btn-small" id="btnOpenLast">${escapeHtml(t('open'))}</button>
         </div>
       </div>`
    : '';

  $('projectFileForm').innerHTML = `
    ${stateLine}
    ${lastRow}
    <div class="field span-all">
      <label>${escapeHtml(t('setProjectRoot'))}</label>
      <div class="path-row">
        <input type="text" id="set-project-root" readonly value="${escapeHtml(state.projectRoot || '')}">
        <button class="btn btn-small" id="btnRevealInline">${escapeHtml(t('reveal'))}</button>
      </div>
    </div>

    <div class="field span-all">
      <label>${escapeHtml(t('projCover'))}</label>
      <div class="path-row">
        <input type="text" id="pr-cover" readonly
               placeholder="${escapeHtml(t('projCoverNone'))}"
               value="${escapeHtml(cover)}">
        <button class="btn btn-small" id="btnPickCover">${escapeHtml(t('browse'))}</button>
        <button class="btn btn-small" id="btnClearCover">${escapeHtml(t('clearArt'))}</button>
      </div>
    </div>`;

  const reveal = $('btnRevealInline');
  if (reveal) reveal.addEventListener('click', () => withBusy(reveal, revealProject));

  // "打开上次的工程"：一键打开设置里记着的那个文件夹（launch 时空工程时的顺手入口）。
  const openLast = $('btnOpenLast');
  if (openLast) {
    openLast.addEventListener('click', () => withBusy(openLast, async () => {
      const res = await postJson('/api/project/open', { directory: state.lastProjectDirectory || '' });

      if (!res.ok) {
        toast(res.error || t('openFailed'), 'err');
        return;
      }

      adoptProjectResult(res);
      toast(t('projectOpened'), 'ok');
    }));
  }

  const pickCover = $('btnPickCover');
  if (pickCover) pickCover.addEventListener('click', () => withBusy(pickCover, chooseCover));

  const clearCover = $('btnClearCover');
  if (clearCover) {
    clearCover.addEventListener('click', () => {
      if (state.project) state.project.previewFile = null;
      renderProjectFile();
    });
  }

  $('projectFileStatus').textContent = '';
}

/**
 * 选一张 mod 封面。
 *
 * 这里选的是**磁盘上的原始文件**：工程里存的是文件名，真正的拷贝发生在保存时
 * （与卡面同一个做法——编辑期间可以随便引用库里的图，保存才收进工程）。
 */
async function chooseCover() {
  const res = await postJson('/api/pick-image', {
    startAt: state.projectRoot || '',
    title: t('projCover'),
  });

  if (!res.ok) {
    toast(res.error || t('saveFailed'), 'err');
    return;
  }

  if (res.cancelled || !res.path) return;   // 取消不是错误

  if (state.project) state.project.previewFile = res.path;
  renderProjectFile();
  logTo(`${t('projCover')}: ${res.path}`);
}

/** 设置里那四个路径提交给服务端。 */
$('btnSaveSettings').addEventListener('click', async () => {
  const btn = $('btnSaveSettings');
  btn.disabled = true;
  try {
    const body = {
      gameDirectory: $('set-game').value.trim(),
      exportDirectory: $('set-export').value.trim(),
      cardArtDirectory: $('set-cardart').value.trim(),
      skinDirectory: $('set-skins').value.trim(),
      projectsRoot: $('set-projects-root').value.trim(),
    };

    const res = await postJson('/api/settings', body);

    if (!res.ok) {
      toast(res.error || t('saveFailed'), 'err');
      $('settingsStatus').textContent = res.error || '';
      return;
    }

    state.settings = res.settings;
    state.projectPath = res.projectPath || state.projectPath;
    state.sourcesDirectory = res.sourcesDirectory || state.sourcesDirectory;

    renderSettings();
    updateGameInfo();

    (res.messages || []).forEach((m) => logTo(m));
    $('settingsStatus').textContent = (res.messages || []).join('　');

    // 游戏目录变了的话，术语与卡面素材都换了，整个界面要重画。
    applyInterfaceLanguage();
    renderAll();
    toast(t('settingsSaved'), 'ok');
  } catch (err) {
    toast(t('saveFailed') + err.message, 'err');
  } finally {
    btn.disabled = false;
  }
});

/** 保存工程：把用到的贴图收进 cardart、清掉没用的、写出 project.json 与源码。 */
async function saveProject(quiet) {
  // 界面还没连上服务端（正在启动）时没有工程可存。直接返回，别发一个空请求——
  // 那会让服务端报 "Project JSON was empty"，看起来像保存坏了。
  if (!state.project) {
    if (!quiet) toast(t('notConnected'), 'err');
    return false;
  }

  const res = await postJson('/api/project/save', state.project);

  if (!res.ok) {
    toast(res.error || t('saveFailed'), 'err');
    return false;
  }

  // 收图/清图之后卡图引用变了，所以要采用服务端返回的那份工程。
  if (res.project) state.project = res.project;

  applyProjectState(res);

  refreshAll();
  (res.messages || []).forEach((m) => logTo(m));

  if (!quiet) {
    $('projectFileStatus').textContent = (res.messages || []).join('　');
    toast(t('projectSaved'), 'ok');
  }

  return true;
}

/** 服务端返回的工程位置信息统一落到 state 上。 */
function applyProjectState(res) {
  if (res.settings) state.settings = res.settings;
  if (res.projectPath) state.projectPath = res.projectPath;
  if (res.projectRoot) state.projectRoot = res.projectRoot;
  if (res.sourcesDirectory) state.sourcesDirectory = res.sourcesDirectory;
  if (res.cardsDirectory) state.projectCards = res.cardsDirectory;
  if (res.modDirectory) state.projectMod = res.modDirectory;
}

/**
 * 顶部与设置页共用的"保存"。
 *
 * 传 quiet=true 时只落盘、不弹提示——用于"打开/另存为之前先保存当前编辑"这种
 * 用户没有主动点保存的场合。
 */
$('btnSave').addEventListener('click', () => withBusy($('btnSave'), () => saveProject(false)));

/**
 * 按钮忙碌包装：禁用按钮、跑一段异步工作、无论成败都恢复。
 * 保存/导出这类操作会阻塞几秒，期间按钮必须不可重复点。
 */
async function withBusy(btn, work) {
  const wasDisabled = btn.disabled;
  btn.disabled = true;

  try {
    return await work();
  } finally {
    btn.disabled = wasDisabled;
  }
}

/** 项目父目录（新建/另存为时默认建在它下面）。 */
function projectsRootValue() {
  const el = $('set-projects-root');
  return ((el?.value ?? '') || state.settings?.projectsRoot || '').trim();
}

/** 把父目录回写到 state（设置页开着时同步到输入框）。 */
function rememberProjectsRoot(root) {
  if (state.settings) state.settings.projectsRoot = root;

  const el = $('set-projects-root');
  if (el) el.value = root;
}

/** 往项目状态行写字；项目页没渲染时只打日志，不报错。 */
function setProjectStatus(text) {
  const el = $('projectFileStatus');
  if (el) el.textContent = text;
}

/** 按效果自动填充项目名：没名字就用 mod id。服务端会做同样的兜底。 */
function suggestedProjectName() {
  return (state.project?.name || state.project?.id || 'MyMod').trim();
}

/** 把服务端返回的项目位置信息记下来并重绘。 */
function adoptProjectResult(res) {
  state.project = res.project;
  applyProjectState(res);

  // 打开 / 新建 / 另存为都算"认领了工程文件夹"；之后保存才写盘。
  if (res.ok && (res.projectRoot || res.projectPath)) {
    state.projectBound = true;
  }

  state.projectName = res.project?.name || '';

  state.selCard = state.project?.cards?.length ? 0 : -1;
  state.selPower = -1;
  state.selShared = -1;

  renderProjectFile();
  renderAll();
  (res.messages || []).forEach((m) => logTo(m));
  setProjectStatus((res.messages || []).join('　'));

  reportReadIssues(res.issues);
}

/**
 * 把读取工程时发现的问题交代出来。
 *
 * 工程**已经打开了**（局部损坏不影响其余），这里只是把每一处列出来——不列的话，
 * "我明明写了却没生效"就成了一场猜谜：作者盯着自己写的那行，而读取器早把它跳过了。
 * 问题少就逐条弹提示，多了只汇总一句（细节都在下方的日志里）。
 */
function reportReadIssues(issues) {
  if (!issues || !issues.length) return;

  issues.forEach((i) => {
    logTo(`${t('readIssue')} ${i.file}（${i.where}）: ${i.message}`);
  });

  const shown = issues.slice(0, 3)
    .map((i) => `${i.file}（${i.where}）: ${i.message}`)
    .join('　');

  toast(
    issues.length > 3
      ? t('readIssuesMore', { n: issues.length, first: shown })
      : shown,
    'err');
}

/**
 * 另存为：选父目录 → 用项目名当文件夹名 → 同名时问一句要不要覆盖。
 *
 * 覆盖不再是个常驻勾选框：那样要么一直开着（危险），要么一直关着（存不进去还得回来改）。
 * 改成"真的撞名了才问"，用户只在那一刻做决定。
 */
async function saveProjectAs() {
  const parent = await pickFolder(projectsRootValue(), t('pickSaveAsParent'));

  if (!parent) {
    return;   // 取消
  }

  rememberProjectsRoot(parent);

  const name = suggestedProjectName();

  // 先问服务端这个目录在不在，再决定要不要弹确认框——不要直接覆盖。
  const check = await postJson('/api/project/exists', { parentDirectory: parent, name });

  if (!check.ok) {
    toast(check.error || t('saveFailed'), 'err');
    return;
  }

  let overwrite = false;

  if (check.exists) {
    overwrite = await confirmDialog(
      t('overwriteTitle'),
      t('overwriteBody', { name, path: check.directory || parent }));

    if (!overwrite) {
      setProjectStatus(t('saveAsCancelled'));
      return;
    }
  }

  const res = await postJson('/api/project/save-as', {
    parentDirectory: parent,
    name,
    overwrite,
  });

  if (!res.ok) {
    toast(res.error || t('saveFailed'), 'err');
    setProjectStatus(res.error || '');
    return;
  }

  adoptProjectResult(res);
  toast(t('projectSaved'), 'ok');
}

$('btnSaveAs').addEventListener('click', () => withBusy($('btnSaveAs'), saveProjectAs));

/** 打开：用原生对话框选项目文件夹。 */
async function openProject() {
  const dir = await pickFolder(state.projectRoot || projectsRootValue(), t('pickOpenProject'));

  if (!dir) {
    return;
  }

  const res = await postJson('/api/project/open', { directory: dir });

  if (!res.ok) {
    toast(res.error || t('openFailed'), 'err');
    setProjectStatus(res.error || '');
    return;
  }

  adoptProjectResult(res);
  toast(t('projectOpened'), 'ok');
}

$('btnOpenProject').addEventListener('click', () => withBusy($('btnOpenProject'), openProject));

/**
 * 新建：选父目录 → 用项目名建出 <父目录>/<项目名>/。
 *
 * 同名时的处理与另存为一致：问一句，别悄悄覆盖别人已有的项目。
 */
async function newProject() {
  const parent = await pickFolder(projectsRootValue(), t('pickNewParent'));

  if (!parent) {
    return;
  }

  rememberProjectsRoot(parent);

  // 新建要先问名字：它决定文件夹名，之后还能在项目页里改。
  const name = await promptDialog(
    t('newNameTitle'), t('newNameBody'), suggestedProjectName());

  if (!name) {
    setProjectStatus(t('newCancelled'));
    return;
  }

  const check = await postJson('/api/project/exists', { parentDirectory: parent, name });

  if (!check.ok) {
    toast(check.error || t('saveFailed'), 'err');
    return;
  }

  let overwrite = false;

  if (check.exists) {
    overwrite = await confirmDialog(
      t('newExistsTitle'),
      t('newExistsBody', { name, path: check.directory || parent }));

    if (!overwrite) {
      setProjectStatus(t('newCancelled'));
      return;
    }
  }

  const res = await postJson('/api/project/new', {
    parentDirectory: parent,
    name,
    overwrite,
  });

  if (!res.ok) {
    toast(res.error || t('saveFailed'), 'err');
    setProjectStatus(res.error || '');
    return;
  }

  adoptProjectResult(res);
  toast(t('projectCreated'), 'ok');
}

$('btnNewProject').addEventListener('click', () => withBusy($('btnNewProject'), newProject));

/** 在文件资源管理器中打开当前项目文件夹。 */
async function revealProject() {
  const res = await postJson('/api/project/reveal', { directory: state.projectRoot || '' });

  if (!res.ok) {
    toast(res.error || t('revealFailed'), 'err');
    return;
  }

  logTo(`${t('revealed')} ${res.path || state.projectRoot}`);
}

$('btnRevealProject').addEventListener('click', () => withBusy($('btnRevealProject'), revealProject));

/**
 * 输入一行文字的对话框（新建项目时问名字）。返回用户输入，取消时返回 null。
 *
 * 用 <dialog> 而不是 window.prompt：这是编辑器自己的界面语言与样式，也不该被浏览器
 * 当"页面脚本弹窗"拦掉。
 */
function promptDialog(title, body, initial) {
  return new Promise((resolve) => {
    const dlg = document.createElement('dialog');
    dlg.className = 'confirm';

    dlg.innerHTML = `
      <h3>${escapeHtml(title)}</h3>
      <p>${escapeHtml(body)}</p>
      <input type="text" data-input value="${escapeHtml(initial || '')}">
      <div class="row-actions">
        <button class="btn btn-primary" data-yes>${escapeHtml(t('confirmYes'))}</button>
        <button class="btn" data-no>${escapeHtml(t('confirmNo'))}</button>
      </div>`;

    const input = dlg.querySelector('[data-input]');

    const finish = (value) => {
      dlg.close();
      dlg.remove();
      resolve(value);
    };

    dlg.querySelector('[data-yes]').addEventListener('click', () => finish(input.value.trim() || null));
    dlg.querySelector('[data-no]').addEventListener('click', () => finish(null));
    dlg.addEventListener('cancel', (e) => { e.preventDefault(); finish(null); });

    // 输入框里按回车等同确认——新建项目时这是最顺手的操作。
    input.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') { e.preventDefault(); finish(input.value.trim() || null); }
    });

    document.body.appendChild(dlg);
    dlg.showModal();
    input.focus();
    input.select();
  });
}

/**
 * 简单的确认对话框：两行文字 + 确认/取消。
 *
 * 用 <dialog> 而不是 window.confirm：这是编辑器自己的界面语言，也不该被浏览器
 * 当成"页面脚本弹窗"拦掉。返回用户是否选了确认。
 */
function confirmDialog(title, body) {
  return new Promise((resolve) => {
    const dlg = document.createElement('dialog');
    dlg.className = 'confirm';

    dlg.innerHTML = `
      <h3>${escapeHtml(title)}</h3>
      <p>${escapeHtml(body)}</p>
      <div class="row-actions">
        <button class="btn btn-primary" data-yes>${escapeHtml(t('confirmYes'))}</button>
        <button class="btn" data-no>${escapeHtml(t('confirmNo'))}</button>
      </div>`;

    const finish = (answer) => {
      dlg.close();
      dlg.remove();
      resolve(answer);
    };

    dlg.querySelector('[data-yes]').addEventListener('click', () => finish(true));
    dlg.querySelector('[data-no]').addEventListener('click', () => finish(false));

    // 点遮罩或按 Esc 也算取消（<dialog> 的默认行为会给 cancel 事件）。
    dlg.addEventListener('cancel', (e) => { e.preventDefault(); finish(false); });

    document.body.appendChild(dlg);
    dlg.showModal();
  });
}

/**
 * 弹原生文件夹选择对话框，返回选中路径；取消时返回 null。
 *
 * 浏览器给不了真实路径，所以这一步是服务端进程弹的系统对话框（见 FolderPicker）。
 */
async function pickFolder(startAt, title) {
  try {
    const res = await postJson('/api/pick-folder', { startAt: startAt || '', title: title || '' });

    if (!res.ok) {
      toast(res.error || t('pickFailed'), 'err');
      return null;
    }

    return res.path || null;
  } catch (err) {
    toast(t('pickFailed') + err.message, 'err');
    return null;
  }
}

/** 导出：把工程编译打包成游戏能加载的 mod，写到设置里的导出路径。 */
async function exportMod() {
  // 导出前先保存：否则导出的是上一次落盘的版本，改了没保存就会以为"改了没用"。
  if (!await saveProject(true)) {
    return;
  }

  const res = await postJson('/api/export', state.project);

  logTo('\n=== export ===');
  (res.messages || []).forEach((m) => logTo(m));

  if (res.ok) {
    logTo('\n' + t('exportDone'));
    logTo(res.outputDirectory || '');
    toast(t('modExported'), 'ok');
  } else {
    toast(t('exportFailed'), 'err');
  }

  const logRes = await api('/api/log');
  setConsole(logRes.log.join('\n'));
}

$('btnExport').addEventListener('click', () => withBusy($('btnExport'), exportMod));

/* ================================================================== utils */

function escapeHtml(text) {
  return String(text ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

boot();
