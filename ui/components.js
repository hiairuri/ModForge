/**
 * 界面复用组件。
 *
 * 与 app.js 分开是为了让它**真的能复用**：一个组件要同时被几处用，就得有自己的文件、
 * 自己的状态与自己的事件，而不是散在某个页面的渲染函数里（那种写法改一处就漏一处——
 * 这个项目的后端模块化是同一个道理）。这里目前只有一个组件（多值输入），
 * 后面"选牌模块"那几个多值格（牌 ID / 标签 / 类型 / 稀有度）都会用它。
 *
 * 依赖：无。只用原生 DOM 与 CSS 类，样式在 styles.css 的 .multicombo 一节。
 */

/** 多值输入。挂成全局（界面没有模块打包，脚本按 <script> 顺序加载）。 */
window.MFComponents = (() => {
  /* ------------------------------------------------------------------ 值 */

  // 分隔符：半角逗号是标准写法，另外三种是用户真会打进来的（中文逗号、顿号、分号）。
  //
  // **必须带 g 标志**：这个正则两处用——split（g 无影响）与 matchAll（不带 g 会抛 TypeError）。
  // 少了 g 的后果不是"少认一种分隔符"，而是每次敲键都在事件处理器里抛异常，
  // 下拉永远不弹（实测：控制台里一堆 TypeError，而输入框看着一切正常）。
  const SEPARATOR = /[,，、;；]/g;

  /** 一段文本 → 值的数组（去空白、去空项、按大小写去重，保留原顺序）。 */
  function splitList(text) {
    const out = [];
    const seen = new Set();

    for (const raw of String(text ?? '').split(SEPARATOR)) {
      const value = raw.trim();
      if (!value) continue;

      const key = value.toLowerCase();
      if (seen.has(key)) continue;

      seen.add(key);
      out.push(value);
    }

    return out;
  }

  /* ------------------------------------------------------------------ 渲染 */

  let seq = 0;

  /**
   * 值的数组 → 显示文本：认识的值显示它的 label（"手牌""闪电"），
   * 不认识的（用户手打的半截）原样显示。
   *
   * 为什么要这一层：框里放的是**模型值**（枚举真值 Hand / Lightning / CARD.XXX），
   * 直接显示出来是英文枚举名，而用户眼里这些格子叫"手牌""闪电"。所以显示用 label、
   * 写回用 value——两个方向各自转换（见 valuesFromText）。
   */
  function displayText(values, items) {
    return (values || []).map((v) => {
      const found = items.find((it) => it.value.toLowerCase() === String(v).toLowerCase());
      return found ? (found.label || found.value) : String(v);
    }).join(', ');
  }

  /**
   * 显示文本 → 值的数组：按 label 或 value 认（大小写不敏感），认不出的原样留着
   * （留着的会在下面那行"认不出的值"里点出来）。
   *
   * label 里若有逗号会被当成两个值——那种 label 在候选里能看见，但手打时无法区分，
   * 所以认不出的那两段会走"认不出的值"提示，不会被静默吞掉。
   */
  function valuesFromText(text, items) {
    return splitList(text).map((token) => {
      const lower = token.toLowerCase();
      const found = items.find((it) =>
        it.value.toLowerCase() === lower || (it.label || '').toLowerCase() === lower);

      return found ? found.value : token;
    });
  }

  /**
   * 一个多值输入框。
   *
   * items：`[{ value, label }]`，下拉里显示 label、写进去的是 value。
   * values：当前值（数组）。elementId：给 <input> 一个 id（与页面别处按 id 找它时用）。
   *
   * 结构：
   *   <div class="multicombo">
   *     <input data-multi-input>          ← 文本，逗号分隔（显示 label）
   *     <div class="multicombo-menu">     ← 智能下拉（按"光标前那一段"筛）
   *     <span class="combo-hint">         ← 认不出的值列在这里
   */
  function multiCombo({ field, values, items, placeholder = '', elementId = '' }) {
    const id = `multi-${field}-${++seq}`;
    const idAttr = elementId ? ` id="${escapeAttr(elementId)}"` : '';
    const list = items || [];

    return `<div class="multicombo" data-multi data-multi-field="${escapeAttr(field)}">
      <input type="text" class="multicombo-input" data-multi-input data-f="${escapeAttr(field)}"
             autocomplete="off" spellcheck="false" aria-autocomplete="list"
             aria-controls="${id}-menu"${idAttr}
             value="${escapeAttr(displayText(values, list))}"
             placeholder="${escapeAttr(placeholder)}">
      <div class="multicombo-menu" id="${id}-menu" data-multi-menu hidden></div>
      <span class="combo-hint" data-multi-hint></span>
    </div>`;
  }

  /**
   * 接上一个多值输入框（渲染之后调用一次）。
   *
   * 行为：
   *   输入时   —— 按"光标前那一段"筛候选，弹出下拉；已选过的项不再列；
   *   点/回车 —— 把那一段补成候选项；
   *   失焦时   —— 整段规范化（补空格、中文逗号与顿号都收成半角逗号、去重），
   *               并把认识的值换成它们的 label 显示；
   *   认不出的 —— 值照留，但在下面那行点出来（像 id 输入框一样，打错字要看得见）。
   *
   * onChange(values, text) 在值真的变了之后调用一次（失焦、选候选项、删除时），
   * **values 是模型值数组**（不是显示文本），调用方据此写回模型——不在每次敲键都写，
   * 避免把"半截输入"当成一个值存下去。
   */
  function bindMultiCombo(container, { items = [], onChange = null } = {}) {
    if (!container) return;

    const input = container.querySelector('[data-multi-input]');
    const menu = container.querySelector('[data-multi-menu]');
    const hint = container.querySelector('[data-multi-hint]');

    if (!input || !menu) return;

    let active = -1;
    let choices = [];

    /** 光标所在的那一段值（逗号之间）。没聚焦时按整段处理（光标位置是 0，不算数）。 */
    const currentToken = () => {
      const focused = document.activeElement === input;
      const caret = focused ? (input.selectionStart ?? input.value.length) : input.value.length;
      const before = input.value.slice(0, caret);

      let cut = 0;
      for (const match of before.matchAll(SEPARATOR)) {
        cut = (match.index ?? 0) + match[0].length;
      }

      return { start: cut, text: before.slice(cut) };
    };

    /** 已经填进去的值（拿去从候选里排除）。按模型值比，与显示文本无关。 */
    const chosen = () => valuesFromText(input.value, items).map((v) => v.toLowerCase());

    const close = () => {
      menu.hidden = true;
      menu.innerHTML = '';
      choices = [];
      active = -1;
    };

    const open = () => {
      const { text } = currentToken();
      const query = text.trim().toLowerCase();
      const taken = chosen();

      choices = items.filter((it) => {
        // 已经填过的就不再列（多选语义），但正在打的那一段自己除外——不然改了半截就没提示。
        const isCurrent = it.value.toLowerCase() === query ||
          (it.label || '').toLowerCase() === query;
        if (!isCurrent && taken.includes(it.value.toLowerCase())) return false;

        if (!query) return true;

        return it.value.toLowerCase().includes(query) ||
          (it.label || '').toLowerCase().includes(query);
      });

      if (choices.length === 0) {
        close();
        return;
      }

      active = 0;
      menu.innerHTML = choices.map((it, i) => `
        <button type="button" class="multicombo-option${i === active ? ' is-active' : ''}"
                data-multi-option="${escapeAttr(it.value)}">
          <span>${escapeHtml(it.label || it.value)}</span>
          <em>${escapeHtml(it.value)}</em>
        </button>`).join('');

      menu.hidden = false;

      menu.querySelectorAll('[data-multi-option]').forEach((btn) => {
        // mousedown 而不是 click：click 之前 input 会先失焦，下拉就没了。
        btn.addEventListener('mousedown', (e) => {
          e.preventDefault();
          accept(btn.dataset.multiOption);
        });
      });
    };

    /** 把光标前那一段换成 value 对应的**显示文本**，并在后面补一个逗号方便接着打。 */
    const accept = (value) => {
      const found = items.find((it) => it.value.toLowerCase() === String(value).toLowerCase());
      const shown = found ? (found.label || found.value) : String(value);

      const { start } = currentToken();
      const caret = input.selectionStart ?? input.value.length;
      const before = input.value.slice(0, start);
      const after = input.value.slice(caret);

      input.value = before + shown + ', ' + after.replace(/^\s*/, '');
      const pos = (before + shown + ', ').length;
      input.setSelectionRange(pos, pos);

      close();
      refreshHint();
      publish();
      input.focus();
      open();
    };

    const refreshHint = () => {
      if (!hint) return;

      const unknown = valuesFromText(input.value, items)
        .filter((v) => !items.some((it) => it.value.toLowerCase() === v.toLowerCase()));

      hint.textContent = unknown.length
        ? `${label('unknownValues')}: ${unknown.join(', ')}`
        : '';
    };

    /** 规范化 + 把已知值换成 label + 通知调用方。 */
    const publish = () => {
      const values = valuesFromText(input.value, items);
      const normalized = displayText(values, items);

      if (normalized !== input.value) {
        input.value = normalized;
      }

      refreshHint();
      if (onChange) onChange(values, normalized);
    };

    input.addEventListener('input', () => { refreshHint(); open(); });

    input.addEventListener('keydown', (e) => {
      if (menu.hidden) {
        if (e.key === 'ArrowDown') { open(); e.preventDefault(); }
        return;
      }

      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        active = (active + (e.key === 'ArrowDown' ? 1 : choices.length - 1)) % choices.length;
        menu.querySelectorAll('[data-multi-option]').forEach((el, i) =>
          el.classList.toggle('is-active', i === active));
        e.preventDefault();
      } else if (e.key === 'Enter' || e.key === 'Tab') {
        if (active >= 0 && choices[active]) { accept(choices[active].value); e.preventDefault(); }
      } else if (e.key === 'Escape') {
        close();
      }
    });

    // 失焦（以及直接改完就走）：把整段规范化，再写回模型。
    input.addEventListener('blur', () => { close(); publish(); });
    input.addEventListener('change', publish);

    refreshHint();
  }

  /* ------------------------------------------------------------------ 小工具 */

  /** 转义：组件自带一份，不依赖页面上别处定义的转义函数（顺序变了也不会坏）。 */
  function escapeHtml(text) {
    return String(text ?? '')
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
  }

  const escapeAttr = escapeHtml;

  // 界面文案的取词函数 t() 由 language/ui.js 提供（同一个页面里的全局函数）。
  // 只有"认不出的值"那一行用它；取不到时退回一个中性说法，不至于显示成空。
  function label(key) {
    return typeof t === 'function' ? t(key) : 'Unknown';
  }

  return {
    multiCombo,
    bindMultiCombo,
    splitList,
  };
})();
