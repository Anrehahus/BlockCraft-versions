(() => {
  'use strict';
  const api = window.api;

  /* ================================================================ */
  /* Хелперы                                                           */
  /* ================================================================ */
  const $ = (s, el = document) => el.querySelector(s);
  const $$ = (s, el = document) => [...el.querySelectorAll(s)];
  function h(tag, props, ...kids) {
    const el = document.createElement(tag);
    for (const [k, v] of Object.entries(props || {})) {
      if (k === 'class') el.className = v;
      else if (k.startsWith('on')) el.addEventListener(k.slice(2), v);
      else if (v === true) el.setAttribute(k, '');
      else if (v !== false && v != null) el.setAttribute(k, v);
    }
    for (const kid of kids.flat()) if (kid != null && kid !== false) el.append(kid);
    return el;
  }
  const fmtBytes = (n) => {
    if (n == null) return '—';
    const u = ['Б', 'КБ', 'МБ', 'ГБ', 'ТБ'];
    let i = 0;
    while (n >= 1024 && i < u.length - 1) { n /= 1024; i++; }
    return n.toFixed(n >= 100 || i === 0 ? 0 : 1) + ' ' + u[i];
  };
  const fmtDate = (iso) =>
    iso ? new Date(iso).toLocaleDateString('ru-RU', { day: 'numeric', month: 'long', year: 'numeric' }) : '';

  /* ================================================================ */
  /* Пиксельные иконки (8×8)                                           */
  /* ================================================================ */
  const ICONS = {
    play: ['..#.....', '..##....', '..###...', '..####..', '..####..', '..###...', '..##....', '..#.....'],
    box: ['.######.', '#......#', '########', '#.####.#', '#.#..#.#', '#.#..#.#', '#.####.#', '########'],
    shirt: ['##....##', '########', '########', '.######.', '.######.', '.######.', '.######.', '.######.'],
    gear: ['...##...', '.######.', '.##..##.', '###..###', '###..###', '.##..##.', '.######.', '...##...'],
    folder: ['........', '###.....', '########', '#......#', '#......#', '#......#', '#......#', '########'],
    refresh: ['..####.#', '.#....##', '#.....##', '#.......', '#.......', '#......#', '.#....#.', '..####..'],
    download: ['...##...', '...##...', '...##...', '.######.', '..####..', '...##...', '........', '########'],
    upload: ['...##...', '..####..', '.######.', '...##...', '...##...', '...##...', '........', '########'],
    trash: ['..####..', '########', '.######.', '.#.##.#.', '.#.##.#.', '.#.##.#.', '.#.##.#.', '.######.'],
    check: ['........', '......##', '.....##.', '#...##..', '##.##...', '.###....', '..#.....', '........'],
    close: ['##....##', '###..###', '.######.', '..####..', '..####..', '.######.', '###..###', '##....##'],
    min: ['........', '........', '........', '........', '........', '........', '########', '........'],
    max: ['########', '#......#', '#......#', '#......#', '#......#', '#......#', '#......#', '########'],
    globe: ['..####..', '.#.##.#.', '#.####.#', '########', '########', '#.####.#', '.#.##.#.', '..####..'],
    flip: ['..####..', '.#....#.', '#..##..#', '#.####.#', '#.####.#', '#..##..#', '.#....#.', '..####..'],
  };
  const NS = 'http://www.w3.org/2000/svg';
  function icon(name, size) {
    const rows = ICONS[name];
    let d = '';
    rows.forEach((r, y) => [...r].forEach((c, x) => { if (c === '#') d += `M${x} ${y}h1v1h-1z`; }));
    const svg = document.createElementNS(NS, 'svg');
    svg.setAttribute('viewBox', '0 0 8 8');
    svg.setAttribute('shape-rendering', 'crispEdges');
    svg.setAttribute('fill', 'currentColor');
    if (size) { svg.setAttribute('width', size); svg.setAttribute('height', size); }
    const p = document.createElementNS(NS, 'path');
    p.setAttribute('d', d);
    svg.append(p);
    return svg;
  }

  /* ================================================================ */
  /* Процедурные текстуры                                              */
  /* ================================================================ */
  function rng(seed) {
    let s = seed >>> 0;
    return () => { s = (s * 1664525 + 1013904223) >>> 0; return s / 4294967296; };
  }
  const choice = (arr, r) => arr[Math.floor(r() * arr.length)];
  function makeTex(size, pick, seed) {
    const c = document.createElement('canvas');
    c.width = c.height = size;
    const g = c.getContext('2d');
    const r = rng(seed);
    for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) { g.fillStyle = pick(r, x, y); g.fillRect(x, y, 1, 1); }
    return c;
  }
  const DIRT = ['#866043', '#79553a', '#6b4a30', '#8f6a4b', '#5c3f28'];
  const GRASS = ['#5d9a35', '#4f8a2c', '#6aa83f', '#549030'];
  const STONE = ['#7d7d7d', '#8a8a8a', '#757575', '#6f6f6f', '#838383'];
  const T = {
    dirt: () => makeTex(16, (r) => (r() < 0.08 ? choice(['#9a9a9a', '#7f7f7f'], r) : choice(DIRT, r)), 11),
    grassTop: () => makeTex(16, (r) => choice(GRASS, r), 22),
    grassSide: () => makeTex(16, (r, i, j) => {
      const depth = 3 + (i % 3 === 0 ? 1 : 0) + (i % 5 === 2 ? 1 : 0);
      return j < depth ? choice(GRASS, r) : choice(DIRT, r);
    }, 33),
    stone: () => makeTex(16, (r) => choice(STONE, r), 44),
  };
  const TEX = {};
  function buildTextures() {
    const root = document.documentElement.style;
    for (const [name, fn] of Object.entries(T)) TEX[name] = fn();
    root.setProperty('--tex-dirt', `url(${TEX.dirt.toDataURL()})`);
    root.setProperty('--tex-grass', `url(${TEX.grassTop.toDataURL()})`);
    root.setProperty('--tex-stone', `url(${TEX.stone.toDataURL()})`);
  }

  // Изометрический блок травы
  function drawBlock(canvas, s) {
    canvas.width = canvas.height = 32 * s;
    const g = canvas.getContext('2d');
    g.imageSmoothingEnabled = false;
    g.clearRect(0, 0, canvas.width, canvas.height);
    // верх
    g.setTransform(s, 0.5 * s, -s, 0.5 * s, 16 * s, 0);
    g.drawImage(TEX.grassTop, 0, 0, 16, 16);
    // левая грань
    g.setTransform(s, 0.5 * s, 0, s, 0, 8 * s);
    g.drawImage(TEX.grassSide, 0, 0, 16, 16);
    g.fillStyle = 'rgba(0,0,0,.28)'; g.fillRect(0, 0, 16, 16);
    // правая грань
    g.setTransform(s, -0.5 * s, 0, s, 16 * s, 16 * s);
    g.drawImage(TEX.grassSide, 0, 0, 16, 16);
    g.fillStyle = 'rgba(0,0,0,.5)'; g.fillRect(0, 0, 16, 16);
    g.setTransform(1, 0, 0, 1, 0, 0);
  }

  /* ================================================================ */
  /* Фон: пиксельный пейзаж                                            */
  /* ================================================================ */
  function drawScene() {
    const c = $('#bg');
    const W = 320, H = 180;
    c.width = W; c.height = H;
    const g = c.getContext('2d');
    const hour = new Date().getHours();
    const night = hour < 6 || hour >= 21;
    const lerp = (a, b, t) => a.map((v, i) => Math.round(v + (b[i] - v) * t));
    const top = night ? [10, 14, 38] : [88, 150, 230];
    const bot = night ? [30, 36, 78] : [170, 212, 250];
    for (let y = 0; y < 130; y += 4) {
      const col = lerp(top, bot, y / 130);
      g.fillStyle = `rgb(${col})`; g.fillRect(0, y, W, 4);
    }
    const r = rng(7);
    if (night) {
      g.fillStyle = '#fff';
      for (let i = 0; i < 70; i++) { g.globalAlpha = 0.4 + r() * 0.6; g.fillRect(Math.floor(r() * W), Math.floor(r() * 90), 1, 1); }
      g.globalAlpha = 1;
    }
    g.fillStyle = night ? '#e8ecf5' : '#fff6b0';
    g.fillRect(248, 22, 16, 16);
    g.fillStyle = night ? 'rgba(232,236,245,.18)' : 'rgba(255,246,176,.25)';
    g.fillRect(244, 18, 24, 24);

    const layers = [
      { base: 104, amp: 14, color: night ? '#26405a' : '#7ea7c9', step: 4 },
      { base: 122, amp: 12, color: night ? '#1f4a2b' : '#4c8a3a', step: 4 },
      { base: 138, amp: 8, color: night ? '#1a3b22' : '#3f7a2f', step: 4 },
    ];
    layers.forEach((L, li) => {
      g.fillStyle = L.color;
      for (let x = 0; x < W; x += L.step) {
        const hgt = L.base + Math.sin(x * 0.035 + li * 2) * L.amp + Math.sin(x * 0.09 + li) * (L.amp / 2);
        const y = Math.round(hgt / 4) * 4;
        g.fillRect(x, y, L.step, H - y);
      }
    });
    // деревья
    const trees = [28, 96, 188, 276];
    trees.forEach((tx) => {
      const gy = 142;
      g.fillStyle = night ? '#3a2a18' : '#6b4a2b'; g.fillRect(tx + 6, gy - 20, 4, 20);
      g.fillStyle = night ? '#143019' : '#2f6b2a'; g.fillRect(tx - 2, gy - 36, 20, 16);
      g.fillStyle = night ? '#1a3d20' : '#3d8433'; g.fillRect(tx + 2, gy - 44, 12, 10);
    });
    // земля
    g.fillStyle = night ? '#2c4f24' : '#5d9a35'; g.fillRect(0, 146, W, 4);
    for (let y = 150; y < H; y += 2) for (let x = 0; x < W; x += 2) {
      g.fillStyle = choice(DIRT, r); g.fillRect(x, y, 2, 2);
    }
    if (night) { g.fillStyle = 'rgba(0,0,30,.35)'; g.fillRect(0, 0, W, H); }

    // облака
    const cc = $('#clouds');
    cc.width = 640; cc.height = 60;
    const cg = cc.getContext('2d');
    cg.fillStyle = night ? 'rgba(180,190,220,.35)' : 'rgba(255,255,255,.9)';
    const r2 = rng(99);
    for (let i = 0; i < 6; i++) {
      const x = Math.floor(r2() * 300) , y = Math.floor(r2() * 36), w = 30 + Math.floor(r2() * 30);
      for (const off of [0, 320]) {
        cg.fillRect(x + off, y + 6, w, 6);
        cg.fillRect(x + off + 6, y, w - 14, 6);
        cg.fillRect(x + off + 10, y + 12, w - 20, 4);
      }
    }
  }

  /* ================================================================ */
  /* Скины: отрисовка фигурки                                          */
  /* ================================================================ */
  function makeDefaultSkin() {
    const c = document.createElement('canvas');
    c.width = c.height = 64;
    const g = c.getContext('2d');
    const rect = (col, x, y, w, hh) => { g.fillStyle = col; g.fillRect(x, y, w, hh); };
    const SKIN = '#c58c5e', HAIR = '#3b2a1a', SHIRT = '#2aa6a6', PANTS = '#3b3f8f', SHOE = '#555';
    rect(SKIN, 0, 8, 32, 8);           // голова (все стороны)
    rect(HAIR, 8, 0, 16, 8);           // верх
    rect(HAIR, 8, 8, 8, 2);            // чёлка спереди
    rect(HAIR, 24, 8, 8, 8);           // затылок
    rect(HAIR, 0, 8, 8, 3); rect(HAIR, 16, 8, 8, 3);
    rect('#fff', 9, 12, 2, 1); rect('#fff', 13, 12, 2, 1);
    rect('#4a3bb0', 10, 12, 1, 1); rect('#4a3bb0', 13, 12, 1, 1);
    rect('#8a5a3c', 11, 14, 2, 1);
    rect(SHIRT, 16, 20, 24, 12);       // торс
    rect(SKIN, 40, 20, 16, 12);        // правая рука
    rect(SHIRT, 40, 20, 16, 4);
    rect(SKIN, 32, 52, 16, 12);        // левая рука
    rect(SHIRT, 32, 52, 16, 4);
    rect(PANTS, 0, 20, 16, 12);        // правая нога
    rect(PANTS, 16, 52, 16, 12);       // левая нога
    rect(SHOE, 0, 29, 16, 3); rect(SHOE, 16, 61, 16, 3);
    return c;
  }
  let DEFAULT_SKIN = null;

  function drawFigure(canvas, src, { model = 'classic', view = 'front', scale = 8 } = {}) {
    const slim = model === 'slim';
    const k = src.width / 64;
    const legacy = src.height * 2 === src.width;
    canvas.width = 16 * scale;
    canvas.height = 32 * scale;
    const g = canvas.getContext('2d');
    g.imageSmoothingEnabled = false;
    g.clearRect(0, 0, canvas.width, canvas.height);

    const put = (sx, sy, sw, sh, dx, dy, flip) => {
      g.save();
      if (flip) {
        g.translate((dx + sw) * scale, dy * scale);
        g.scale(-1, 1);
        g.drawImage(src, sx * k, sy * k, sw * k, sh * k, 0, 0, sw * scale, sh * scale);
      } else {
        g.drawImage(src, sx * k, sy * k, sw * k, sh * k, dx * scale, dy * scale, sw * scale, sh * scale);
      }
      g.restore();
    };
    const aw = slim ? 3 : 4;
    const lx = 4 - aw; // x левой (для зрителя) руки
    const front = view === 'front';
    // координаты частей: [базовый слой, оверлей, flip]
    let head, body, armL, armR, legL, legR;
    if (front) {
      head = [[8, 8], [40, 8]];
      body = [[20, 20], [20, 36]];
      armL = { base: [44, 20], over: [44, 36], flip: false, legacy: false };
      armR = legacy ? { base: [44, 20], over: null, flip: true } : { base: [36, 52], over: [52, 52], flip: false };
      legL = { base: [4, 20], over: [4, 36], flip: false };
      legR = legacy ? { base: [4, 20], over: null, flip: true } : { base: [20, 52], over: [4, 52], flip: false };
    } else {
      head = [[24, 8], [56, 8]];
      body = [[32, 20], [32, 36]];
      const rb = slim ? 51 : 52;
      const lb = slim ? 43 : 44;
      const lo = slim ? 59 : 60;
      armL = legacy ? { base: [rb, 20], over: null, flip: true } : { base: [lb, 52], over: [lo, 52], flip: false };
      armR = { base: [rb, 20], over: [rb, 36], flip: false };
      legL = legacy ? { base: [12, 20], over: null, flip: true } : { base: [28, 52], over: [12, 52], flip: false };
      legR = { base: [12, 20], over: [12, 36], flip: false };
    }
    const limb = (p, x, y, w, hh, over) => {
      put(p.base[0], p.base[1], w, hh, x, y, p.flip);
    };
    // базовые слои
    limb(legL, 4, 20, 4, 12); limb(legR, 8, 20, 4, 12);
    put(body[0][0], body[0][1], 8, 12, 4, 8);
    limb(armL, lx, 8, aw, 12); limb(armR, 12, 8, aw, 12);
    put(head[0][0], head[0][1], 8, 8, 4, 0);
    // оверлеи
    if (legL.over) put(legL.over[0], legL.over[1], 4, 12, 4, 20);
    if (legR.over) put(legR.over[0], legR.over[1], 4, 12, 8, 20);
    if (!legacy) put(body[1][0], body[1][1], 8, 12, 4, 8);
    if (armL.over) put(armL.over[0], armL.over[1], aw, 12, lx, 8);
    if (armR.over) put(armR.over[0], armR.over[1], aw, 12, 12, 8);
    put(head[1][0], head[1][1], 8, 8, 4, 0);
  }

  function drawHead(canvas, src) {
    const k = src.width / 64;
    const g = canvas.getContext('2d');
    g.imageSmoothingEnabled = false;
    g.clearRect(0, 0, canvas.width, canvas.height);
    g.drawImage(src, 8 * k, 8 * k, 8 * k, 8 * k, 0, 0, canvas.width, canvas.height);
    g.drawImage(src, 40 * k, 8 * k, 8 * k, 8 * k, 0, 0, canvas.width, canvas.height);
  }

  /* ================================================================ */
  /* Состояние                                                         */
  /* ================================================================ */
  const S = {
    info: null,
    settings: null,
    versions: { items: [], latestTag: null, selectedTag: null, error: null, offline: false, checkedAt: null },
    install: null,
    skins: [],
    worlds: { worlds: [], maxSlots: 5 },
    activeSkin: null,
    skinImgs: new Map(),
    skinView: 'front',
    gameRunning: false,
    launcher: { state: 'idle' },
    page: 'play',
    notifiedLatest: null,
  };
  const items = () => S.versions.items;
  const byTag = (t) => items().find((v) => v.tag === t);
  const selected = () => byTag(S.versions.selectedTag);
  const latest = () => byTag(S.versions.latestTag);

  /* ================================================================ */
  /* UI-утилиты: toast, modal                                          */
  /* ================================================================ */
  function toast(msg, type = 'info') {
    const t = h('div', { class: 'toast ' + type }, msg);
    $('#toasts').append(t);
    setTimeout(() => { t.style.transition = 'opacity .3s'; t.style.opacity = '0'; setTimeout(() => t.remove(), 300); }, type === 'err' ? 7000 : 3800);
  }
  function modal({ title, text, buttons }) {
    return new Promise((resolve) => {
      const close = (v) => { back.remove(); resolve(v); };
      const back = h('div', { class: 'modal-back', onclick: (e) => { if (e.target === back) close(null); } },
        h('div', { class: 'modal panel' },
          h('div', { class: 'panel-title' }, title),
          h('p', {}, text),
          h('div', { class: 'modal-actions' },
            buttons.map((b) => h('button', { class: 'btn ' + (b.cls || ''), onclick: () => close(b.value) }, b.label)))));
      document.body.append(back);
    });
  }
  const run = async (fn) => { try { return await fn(); } catch (e) { toast(e.message, 'err'); } };

  /* ================================================================ */
  /* Мини-markdown (без innerHTML)                                     */
  /* ================================================================ */
  function inline(text) {
    const frag = document.createDocumentFragment();
    const re = /(\*\*[^*]+\*\*|`[^`]+`)/g;
    let last = 0, m;
    while ((m = re.exec(text))) {
      if (m.index > last) frag.append(text.slice(last, m.index));
      const tok = m[0];
      frag.append(tok.startsWith('**') ? h('b', {}, tok.slice(2, -2)) : h('code', {}, tok.slice(1, -1)));
      last = m.index + tok.length;
    }
    if (last < text.length) frag.append(text.slice(last));
    return frag;
  }
  function renderMd(text) {
    const root = h('div', { class: 'md' });
    let ul = null;
    for (const raw of String(text || '').split(/\r?\n/)) {
      const line = raw.trimEnd();
      let m;
      if ((m = /^(#{1,3})\s+(.*)$/.exec(line))) { ul = null; root.append(h('h' + m[1].length, {}, inline(m[2]))); }
      else if ((m = /^\s*[-*+]\s+(.*)$/.exec(line))) {
        if (!ul) { ul = h('ul'); root.append(ul); }
        ul.append(h('li', {}, inline(m[1])));
      } else if (line.trim()) { ul = null; root.append(h('p', {}, inline(line))); }
      else ul = null;
    }
    return root;
  }

  /* ================================================================ */
  /* Навигация                                                         */
  /* ================================================================ */
  function go(page) {
    S.page = page;
    $$('.nav-item').forEach((b) => b.classList.toggle('active', b.dataset.page === page));
    $$('.page').forEach((p) => p.classList.toggle('active', p.id === 'page-' + page));
    $('.content').scrollTop = 0;
    if (page === 'versions') renderVersions();
    if (page === 'skins') renderSkins();
    if (page === 'worlds') { renderWorlds(); refreshWorlds(); }
    if (page === 'settings') renderSettings();
    if (page === 'play') updatePlay();
  }

  /* ================================================================ */
  /* Страница «Играть»                                                 */
  /* ================================================================ */
  function buildPlay() {
    $('#page-play').replaceChildren(
      h('div', { class: 'play-grid' },
        h('div', { class: 'hero panel' },
          h('canvas', { id: 'hero-block', class: 'hero-block' }),
          h('div', { class: 'hero-title', id: 'hero-title' }),
          h('div', { class: 'hero-sub', id: 'hero-sub' }),
          h('div', { class: 'hero-controls' },
            h('label', { class: 'field' },
              h('span', { class: 'field-label' }, 'Версия игры'),
              h('select', { id: 'ver-select', class: 'select', onchange: (e) => selectVersion(e.target.value) }))),
          h('button', { id: 'btn-play', class: 'btn green btn-play', onclick: onPlay },
            h('span', { class: 'label' }), h('span', { class: 'sub' })),
          h('div', { id: 'play-progress', class: 'progress hidden' },
            h('div', { class: 'progress-top' }, h('span', { id: 'pg-stage' }), h('span', { id: 'pg-pct' })),
            h('div', { class: 'xp', id: 'pg-xp' }, h('i', { id: 'pg-fill' })),
            h('div', { class: 'progress-bot' }, h('span', { id: 'pg-detail' }),
              h('button', { class: 'btn small red', id: 'pg-cancel', onclick: () => run(() => api.call('versions:cancel')) }, 'Отмена'))),
          h('div', { id: 'update-card' }),
          h('div', { id: 'play-hint', class: 'hint' })),
        h('div', { class: 'news panel' },
          h('div', { class: 'panel-title' }, 'Что нового'),
          h('div', { class: 'news-body', id: 'news-body' }))));
    drawBlock($('#hero-block'), 3.4);
  }

  function updatePlay() {
    if (!$('#ver-select')) return;
    const v = selected();
    $('#hero-title').textContent = S.info.gameName;

    // выпадающий список версий
    const sel = $('#ver-select');
    sel.replaceChildren(...items().map((it) => {
      const tags = [it.isLatest ? 'последняя' : '', it.prerelease ? 'snapshot' : '', it.installed ? 'установлена' : ''].filter(Boolean);
      return h('option', { value: it.tag }, it.version + (tags.length ? '  ·  ' + tags.join(', ') : ''));
    }));
    if (v) sel.value = v.tag;
    sel.disabled = !items().length || !!S.install;

    // подпись
    const sub = $('#hero-sub');
    sub.replaceChildren();
    if (v) {
      sub.append(`Версия ${v.version}`);
      if (v.isLatest) sub.append(h('span', { class: 'badge green' }, 'Последняя'));
      if (v.prerelease) sub.append(h('span', { class: 'badge orange' }, 'Snapshot'));
      if (v.installed) sub.append(h('span', { class: 'badge blue' }, 'Установлена'));
    } else sub.append('Версии не найдены');

    // кнопка
    const btn = $('#btn-play');
    const lab = $('.label', btn), subl = $('.sub', btn);
    const installingThis = S.install && v && S.install.tag === v.tag;
    let disabled = false;
    if (S.gameRunning) { lab.textContent = 'Игра запущена'; subl.textContent = 'Приятной игры!'; disabled = true; }
    else if (S.install) { lab.textContent = installingThis ? 'Установка…' : 'Подождите…'; subl.textContent = 'Идёт установка версии ' + (byTag(S.install.tag)?.version || ''); disabled = true; }
    else if (!v) { lab.textContent = 'Нет версий'; subl.textContent = 'Проверьте настройки репозитория'; disabled = true; }
    else if (!v.installed) { lab.textContent = 'Установить'; subl.textContent = `Версия ${v.version} · ${fmtBytes(v.size)}`; }
    else { lab.textContent = 'Играть'; subl.textContent = `Версия ${v.version}`; }
    btn.disabled = disabled;

    // карточка обновления
    const uc = $('#update-card');
    uc.replaceChildren();
    if (v && v.updateTo && !S.install) {
      const nv = byTag(v.updateTo);
      uc.append(h('div', { class: 'update-card' },
        h('div', { class: 'txt' }, 'Доступна новая версия ', h('b', {}, nv ? nv.version : v.updateTo), nv ? ` · ${fmtBytes(nv.size)}` : ''),
        h('button', { class: 'btn small green', onclick: () => startInstall(v.updateTo, v.tag) }, icon('download'), 'Обновить')));
    }

    // подсказка / ошибка
    const hint = $('#play-hint');
    hint.className = 'hint' + (S.versions.error ? ' err' : '');
    hint.textContent = S.versions.error
      ? (S.versions.offline && items().length ? 'Офлайн-режим: ' : '') + S.versions.error
      : S.versions.checkedAt ? 'Проверено: ' + new Date(S.versions.checkedAt).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' }) : '';

    // новости
    const nb = $('#news-body');
    nb.replaceChildren();
    if (v && v.notes) {
      nb.append(h('div', { class: 'hint', style: 'margin:0 0 8px' }, `${v.title} · ${fmtDate(v.publishedAt)}`), renderMd(v.notes));
    } else nb.append(h('div', { class: 'empty' }, v ? 'Для этой версии нет описания.' : 'Здесь появятся заметки к релизам.'));

    updateProgressBits();
  }

  const STAGES = { downloading: 'Загрузка', verifying: 'Проверка файлов', extracting: 'Распаковка', finalizing: 'Завершение' };
  function updateProgressBits() {
    const box = $('#play-progress');
    if (!box) return;
    const p = S.install;
    box.classList.toggle('hidden', !p);
    if (p) {
      const pct = Math.max(0, Math.min(100, p.percent || 0));
      $('#pg-stage').textContent = `${STAGES[p.stage] || 'Подготовка'} · ${byTag(p.tag)?.version || ''}`;
      $('#pg-pct').textContent = Math.round(pct) + '%';
      $('#pg-fill').style.width = pct + '%';
      $('#pg-xp').classList.toggle('indeterminate', p.stage === 'finalizing' || p.stage === 'verifying');
      $('#pg-detail').textContent = p.stage === 'downloading' && p.total
        ? `${fmtBytes(p.received)} / ${fmtBytes(p.total)}${p.speed ? ' · ' + fmtBytes(p.speed) + '/с' : ''}`
        : '';
      $('#pg-cancel').style.display = p.stage === 'downloading' ? '' : 'none';
    }
    $$('.vrow[data-tag]').forEach((row) => {
      const mini = $('.mini-xp', row);
      if (!mini) return;
      const mine = p && p.tag === row.dataset.tag;
      mini.style.display = mine ? '' : 'none';
      if (mine) { $('i', mini).style.width = (p.percent || 0) + '%'; }
    });
  }

  async function onPlay() {
    const v = selected();
    if (!v) return;
    if (!v.installed) { await startInstall(v.tag); return; }
    await run(() => api.call('game:play'));
  }
  async function selectVersion(tag) {
    S.versions.selectedTag = tag;
    await run(() => api.call('versions:select', tag));
    updatePlay();
    if (S.page === 'versions') renderVersions();
  }

  async function startInstall(tag, replaceTag) {
    if (S.install) return;
    S.install = { tag, stage: 'downloading', percent: 0 };
    updatePlay();
    if (S.page === 'versions') renderVersions();
    try {
      await api.call('versions:install', { tag, replaceTag });
      toast(`Версия ${byTag(tag)?.version || tag} установлена`, 'ok');
    } catch (e) {
      if (e.message !== 'cancelled') toast(e.message, 'err');
      else toast('Установка отменена');
    }
    S.install = null;
    await refreshVersions(false);
    if (S.page === 'worlds') refreshWorlds();
  }

  /* ================================================================ */
  /* Страница «Версии»                                                 */
  /* ================================================================ */
  async function refreshVersions(force, silent = true) {
    try {
      const prevLatest = S.versions.latestTag;
      S.versions = await api.call('versions:list', force);
      const nl = S.versions.latestTag;
      if (nl && prevLatest && nl !== prevLatest && S.notifiedLatest !== nl) {
        S.notifiedLatest = nl;
        toast(`Вышла новая версия ${byTag(nl)?.version || nl}`, 'ok');
      }
      if (!silent && S.versions.error) toast(S.versions.error, 'err');
    } catch (e) {
      toast(e.message, 'err');
    }
    updatePlay();
    if (S.page === 'versions') renderVersions();
    if (S.page === 'settings') renderSettings();
  }

  function renderVersions() {
    const root = $('#page-versions');
    const list = h('div', { class: 'vlist' });
    if (!items().length) {
      list.append(h('div', { class: 'panel empty' }, S.versions.error || 'Версии не найдены. Убедитесь, что в репозитории есть релизы с тегом названием вида Pre-classic-rd-132211 или Classic-0.0.24a и zip-архивом.'));
    }
    for (const it of items()) {
      const isSel = it.tag === S.versions.selectedTag;
      const installingThis = S.install && S.install.tag === it.tag;
      const row = h('div', { class: 'vrow panel' + (isSel ? ' selected' : ''), 'data-tag': it.tag });
      const notesBtn = it.notes
        ? h('button', { class: 'btn small', onclick: () => row.classList.toggle('open') }, 'Что нового')
        : null;
      const actions = [];
      if (it.installed) {
        actions.push(h('button', { class: 'btn small ' + (isSel ? '' : 'green'), disabled: isSel, onclick: () => selectVersion(it.tag) }, icon('check'), isSel ? 'Выбрана' : 'Выбрать'));
        if (it.updateTo) actions.push(h('button', { class: 'btn small green', disabled: !!S.install, onclick: () => startInstall(it.updateTo, it.tag) }, icon('download'), 'Обновить'));
        actions.push(h('button', { class: 'btn small', onclick: () => run(() => api.call('fs:open', 'version:' + it.tag)) }, icon('folder')));
        actions.push(h('button', { class: 'btn small red', disabled: !!S.install || (S.gameRunning && isSel), onclick: () => removeVersion(it) }, icon('trash')));
      } else {
        actions.push(h('button', { class: 'btn small green', disabled: !!S.install, onclick: () => startInstall(it.tag) }, icon('download'), 'Установить'));
      }
      row.append(...[
        h('div', { class: 'vhead' },
          h('canvas', { class: 'vblock', width: 44, height: 44 }),
          h('div', { class: 'vmain' },
            h('div', { class: 'vtitle' }, it.version,
              it.isLatest ? h('span', { class: 'badge green' }, 'Последняя') : null,
              it.prerelease ? h('span', { class: 'badge orange' }, 'Snapshot') : null,
              it.installed ? h('span', { class: 'badge blue' }, 'Установлена') : null,
              it.localOnly ? h('span', { class: 'badge gray' }, 'Локальная') : null),
            h('div', { class: 'vmeta' }, [fmtDate(it.publishedAt), it.size ? fmtBytes(it.size) : ''].filter(Boolean).join(' · ')),
            h('div', { class: 'xp mini-xp', style: installingThis ? '' : 'display:none' }, h('i'))),
          h('div', { class: 'vactions' }, notesBtn, actions)),
        it.notes ? h('div', { class: 'vnotes' }, renderMd(it.notes)) : null,
      ].filter(Boolean));
      list.append(row);
      drawBlock($('.vblock', row), 1.375);
    }

    root.replaceChildren(
      h('div', { class: 'page-head' },
        h('div', {},
          h('div', { class: 'page-title' }, 'Версии'),
          h('div', { class: 'page-sub' }, S.versions.checkedAt
            ? 'Последняя проверка: ' + new Date(S.versions.checkedAt).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })
            : 'Установка, обновление и удаление версий игры')),
        h('div', { class: 'toolbar' },
          h('label', { class: 'toolbar', style: 'cursor:pointer' },
            h('span', { class: 'switch' },
              h('input', { type: 'checkbox', checked: S.settings.showPrereleases, onchange: async (e) => { await patch({ showPrereleases: e.target.checked }); await refreshVersions(false); } }),
              h('span', { class: 'slider' })),
            h('span', {}, 'Snapshot-версии')),
          h('button', { class: 'btn', onclick: async (e) => { e.currentTarget.disabled = true; await refreshVersions(true, false); toast('Список версий обновлён', 'ok'); } }, icon('refresh'), 'Проверить'))),
      list);
    updateProgressBits();
  }

  async function removeVersion(it) {
    const ok = await modal({
      title: 'Удалить версию?',
      text: `Версия ${it.version} будет удалена с диска. Миры и настройки игры сохранятся — они лежат отдельно.`,
      buttons: [{ label: 'Отмена', value: false }, { label: 'Удалить', value: true, cls: 'red' }],
    });
    if (!ok) return;
    await run(() => api.call('versions:remove', it.tag));
    await refreshVersions(false);
  }

  /* ================================================================ */
  /* Страница «Миры»                                                   */
  /* ================================================================ */
  async function refreshWorlds() {
    try { S.worlds = await api.call('worlds:list'); } catch (e) { toast(e.message, 'err'); }
    if (S.page === 'worlds') renderWorlds();
  }
  const fmtDateTime = (ms) => new Date(ms).toLocaleString('ru-RU', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });

  function renderWorlds() {
    const root = $('#page-worlds');
    const list = S.worlds.worlds;
    const groups = new Map();
    for (const w of list) { if (!groups.has(w.tag)) groups.set(w.tag, []); groups.get(w.tag).push(w); }
    const busy = S.gameRunning || !!S.install;

    const body = h('div', {});
    if (!list.length) body.append(h('div', { class: 'panel empty' }, 'Миров пока нет. Запустите игру и создайте мир — он появится здесь.'));
    for (const [, ws] of groups) {
      const first = ws[0];
      body.append(h('div', { class: 'wgroup panel' },
        h('div', { class: 'wgroup-head' },
          h('span', { class: 'vtitle' }, first.version),
          first.installed ? h('span', { class: 'badge blue' }, 'Установлена') : h('span', { class: 'badge gray' }, 'Версия удалена')),
        ws.map((w) => h('div', { class: 'wrow' },
          h('div', {},
            h('div', { class: 'wname' }, 'Мир ' + w.slot),
            h('div', { class: 'vmeta' }, [w.seed != null ? 'Сид ' + w.seed : '', fmtBytes(w.size), 'Изменён ' + fmtDateTime(w.modified)].filter(Boolean).join(' · '))),
          h('div', { class: 'vactions' },
            h('button', { class: 'btn small', onclick: () => run(() => api.call('fs:open', `world:${w.tag}:${w.slot}`)) }, icon('folder'), 'Папка'),
            h('button', { class: 'btn small', onclick: () => backupWorld(w) }, icon('download'), 'Копия'),
            h('button', { class: 'btn small', disabled: busy, onclick: () => transferDialog(w, 'copy') }, 'Копировать…'),
            h('button', { class: 'btn small', disabled: busy, onclick: () => transferDialog(w, 'move') }, 'Перенести…'),
            h('button', { class: 'btn small red', disabled: busy, onclick: () => deleteWorld(w) }, icon('trash')))))));
    }

    root.replaceChildren(
      h('div', { class: 'page-head' },
        h('div', {},
          h('div', { class: 'page-title' }, 'Миры'),
          h('div', { class: 'page-sub' }, 'Миры хранятся отдельно для каждой версии игры (до ' + S.worlds.maxSlots + ' слотов на версию)')),
        h('div', { class: 'toolbar' },
          h('button', { class: 'btn', onclick: () => run(() => api.call('fs:open', 'worlds')) }, icon('folder'), 'Все миры'),
          h('button', { class: 'btn', onclick: () => run(() => api.call('fs:open', 'backups')) }, icon('folder'), 'Резервные копии'),
          h('button', { class: 'btn', onclick: refreshWorlds }, icon('refresh')))),
      h('div', { class: 'memo panel' },
        h('div', { class: 'panel-title' }, 'Памятка'),
        h('ul', {},
          h('li', {}, 'Мир принадлежит той версии, в которой он создан. В другой версии его не будет, пока вы не скопируете или не перенесёте его.'),
          h('li', {}, 'Мир из старой версии обычно открывается в новой, но не наоборот: перенося мир в более старую версию, сначала сделайте копию.'),
          h('li', {}, 'При обновлении лаунчер может сам переносить миры в новую версию (Настройки → Обновления).'),
          h('li', {}, 'Удаление версии миры не удаляет — они остаются здесь, пока вы сами их не удалите.'),
          h('li', {}, 'Работать с мирами можно, только пока игра закрыта.'))),
      body);
  }

  async function backupWorld(w) {
    const dst = await run(() => api.call('worlds:backup', { tag: w.tag, slot: w.slot }));
    if (dst) toast('Резервная копия создана в папке «Резервные копии»', 'ok');
  }

  async function deleteWorld(w) {
    const choice = await modal({
      title: 'Удалить мир?',
      text: `Мир ${w.slot} (${w.version}) будет удалён безвозвратно. Рекомендуем сначала сделать резервную копию.`,
      buttons: [
        { label: 'Отмена', value: null },
        { label: 'Копия и удалить', value: 'backup', cls: 'green' },
        { label: 'Удалить', value: 'del', cls: 'red' },
      ],
    });
    if (!choice) return;
    if (choice === 'backup') { const ok = await run(() => api.call('worlds:backup', { tag: w.tag, slot: w.slot })); if (!ok) return; }
    await run(() => api.call('worlds:delete', { tag: w.tag, slot: w.slot }));
    toast('Мир удалён', 'ok');
    refreshWorlds();
  }

  function transferDialog(w, mode) {
    const targets = items().filter((i) => i.installed);
    if (!targets.length) { toast('Нет установленных версий, в которые можно перенести мир', 'err'); return; }
    const max = S.worlds.maxSlots;
    const occupied = (tag) => S.worlds.worlds.filter((x) => x.tag === tag).map((x) => x.slot);

    const verSel = h('select', { class: 'select' }, targets.map((t) => h('option', { value: t.tag }, t.version + (t.tag === w.tag ? ' (эта же версия)' : ''))));
    verSel.value = (targets.find((t) => t.tag !== w.tag) || targets[0]).tag;
    const slotSel = h('select', { class: 'select' });
    const warn = h('div', { class: 'mwarn' });
    const okBtn = h('button', { class: 'btn green' }, mode === 'move' ? 'Перенести' : 'Копировать');

    const sync = () => {
      const occ = occupied(verSel.value);
      const keep = Number(slotSel.value) || 0;
      slotSel.replaceChildren(...Array.from({ length: max }, (_, i) => i + 1).map((n) =>
        h('option', { value: n }, `Слот ${n} — ${occ.includes(n) ? 'занят, будет заменён' : 'свободен'}`)));
      const same = (n) => verSel.value === w.tag && n === w.slot;
      const free = Array.from({ length: max }, (_, i) => i + 1).find((n) => !occ.includes(n) && !same(n));
      slotSel.value = keep && !same(keep) ? keep : (free || Array.from({ length: max }, (_, i) => i + 1).find((n) => !same(n)));
      check();
    };
    const check = () => {
      const from = items().findIndex((i) => i.tag === w.tag);
      const to = items().findIndex((i) => i.tag === verSel.value);
      // список версий отсортирован от новой к старой
      warn.textContent = from >= 0 && to >= 0 && to > from
        ? 'Целевая версия старше исходной: мир может не открыться. Сначала сделайте резервную копию.' : '';
      okBtn.disabled = verSel.value === w.tag && Number(slotSel.value) === w.slot;
    };
    verSel.addEventListener('change', () => { slotSel.value = ''; sync(); });
    slotSel.addEventListener('change', check);

    const close = () => back.remove();
    okBtn.onclick = async () => {
      const toTag = verSel.value, toSlot = Number(slotSel.value);
      const occ = occupied(toTag).includes(toSlot);
      if (occ) {
        const sure = await modal({ title: 'Слот занят', text: `В слоте ${toSlot} уже есть мир, он будет заменён. Продолжить?`, buttons: [{ label: 'Отмена', value: false }, { label: 'Заменить', value: true, cls: 'red' }] });
        if (!sure) return;
      }
      okBtn.disabled = true;
      const ok = await run(() => api.call('worlds:transfer', { tag: w.tag, slot: w.slot, toTag, toSlot, move: mode === 'move', overwrite: occ }));
      if (ok !== undefined) { close(); toast(mode === 'move' ? 'Мир перенесён' : 'Мир скопирован', 'ok'); refreshWorlds(); } else okBtn.disabled = false;
    };
    const back = h('div', { class: 'modal-back', onclick: (e) => { if (e.target === back) close(); } },
      h('div', { class: 'modal panel' },
        h('div', { class: 'panel-title' }, `${mode === 'move' ? 'Перенести' : 'Копировать'} мир ${w.slot} (${w.version})`),
        h('label', { class: 'field mfield' }, h('span', { class: 'field-label' }, 'В версию'), verSel),
        h('label', { class: 'field mfield' }, h('span', { class: 'field-label' }, 'В слот'), slotSel),
        warn,
        h('div', { class: 'modal-actions' }, h('button', { class: 'btn', onclick: close }, 'Отмена'), okBtn)));
    document.body.append(back);
    sync();
  }

  /* ================================================================ */
  /* Страница «Скины»                                                  */
  /* ================================================================ */
  const loadImg = (src) => new Promise((res, rej) => { const i = new Image(); i.onload = () => res(i); i.onerror = rej; i.src = src; });

  async function refreshSkins() {
    try {
      const r = await api.call('skins:list');
      S.skins = r.items;
      S.activeSkin = r.active;
      S.skinImgs.clear();
      await Promise.all(S.skins.map(async (s) => { try { S.skinImgs.set(s.id, await loadImg(s.dataUrl)); } catch { /* битый файл */ } }));
    } catch (e) { toast(e.message, 'err'); }
    updateAvatar();
    if (S.page === 'skins') renderSkins();
  }
  const activeSkinObj = () => S.skins.find((s) => s.id === S.activeSkin && S.skinImgs.has(s.id)) || null;

  function updateAvatar() {
    const s = activeSkinObj();
    drawHead($('#avatar'), s ? S.skinImgs.get(s.id) : DEFAULT_SKIN);
    $('#p-name').textContent = S.settings.nickname;
    $('#p-sub').textContent = s ? 'Скин: ' + s.name : 'Стандартный скин';
  }

  async function addSkinFiles(files) {
    for (const f of files) {
      if (!/\.png$/i.test(f.name)) { toast(`${f.name}: нужен PNG-файл`, 'err'); continue; }
      try {
        const data = new Uint8Array(await f.arrayBuffer());
        await api.call('skins:add', { name: f.name, data });
        toast(`Скин «${f.name.replace(/\.png$/i, '')}» добавлен`, 'ok');
      } catch (e) { toast(`${f.name}: ${e.message}`, 'err'); }
    }
    await refreshSkins();
  }

  async function pickSkins() {
    try {
      const r = await api.call('skins:pick');
      r.errors.forEach((e) => toast(e, 'err'));
      if (r.added.length) toast(r.added.length === 1 ? `Скин «${r.added[0].name}» добавлен` : `Добавлено скинов: ${r.added.length}`, 'ok');
      await refreshSkins();
    } catch (e) { toast(e.message, 'err'); }
  }

  async function wearSkin(id) {
    await run(() => api.call('skins:select', id));
    S.activeSkin = id;
    updateAvatar();
    renderSkins();
  }

  function renderSkins() {
    const root = $('#page-skins');
    const act = activeSkinObj();
    const src = act ? S.skinImgs.get(act.id) : DEFAULT_SKIN;
    const model = 'classic'; // игра пока поддерживает только обычные руки

    const fig = h('canvas', { class: 'fig', title: 'Нажмите, чтобы повернуть', onclick: () => { S.skinView = S.skinView === 'front' ? 'back' : 'front'; renderSkins(); } });
    drawFigure(fig, src, { model, view: S.skinView, scale: 12 });

    const stage = h('div', { class: 'stage panel' },
      h('div', { class: 'panel-title' }, 'Предпросмотр'),
      h('div', { class: 'stage-view' }, h('div', { class: 'nick' }, S.settings.nickname), fig, h('div', { class: 'floor' })),
      h('div', { class: 'stage-actions' },
        h('div', { class: 'row' },
          h('span', { class: 'field-label' }, 'Вид: ' + (S.skinView === 'front' ? 'спереди' : 'сзади')),
          h('button', { class: 'btn small', onclick: () => { S.skinView = S.skinView === 'front' ? 'back' : 'front'; renderSkins(); } }, icon('flip'), 'Повернуть')),
        h('button', { class: 'btn green', onclick: pickSkins }, icon('upload'), 'Загрузить скин…'),
        h('button', { class: 'btn small', onclick: () => run(() => api.call('fs:open', 'skins')) }, icon('folder'), 'Открыть папку скинов')));

    const mkCard = (id, name, img, mdl, deletable) => {
      const cv = h('canvas', { width: 80, height: 160 });
      drawFigure(cv, img, { model: mdl, view: 'front', scale: 5 });
      const isOn = (S.activeSkin || null) === id;
      return h('div', { class: 'skin-card panel' + (isOn ? ' active' : '') },
        cv,
        h('div', { class: 'skin-name', title: name }, name),
        mdl === 'slim' && id ? h('span', { class: 'badge gray' }, 'Тонкие руки') : null,
        h('div', { class: 'skin-btns' },
          h('button', { class: 'btn small ' + (isOn ? '' : 'green'), disabled: isOn, onclick: () => wearSkin(id) }, icon('check'), isOn ? 'Надет' : 'Надеть'),
          deletable ? h('button', { class: 'btn small red', onclick: () => deleteSkin(id, name) }, icon('trash')) : null));
    };
    const cards = h('div', { class: 'skin-cards' },
      mkCard(null, 'Стандартный', DEFAULT_SKIN, 'classic', false),
      S.skins.filter((s) => S.skinImgs.has(s.id)).map((s) => mkCard(s.id, s.name, S.skinImgs.get(s.id), 'classic', true)));

    root.replaceChildren(
      h('div', { class: 'page-head' },
        h('div', {},
          h('div', { class: 'page-title' }, 'Скины'),
          h('div', { class: 'page-sub' }, 'PNG 64×64 или 64×32, до 32 КБ. Можно перетащить файл прямо в окно лаунчера. Скин виден в игре, когда вы надели его здесь и запустили игру из лаунчера.'))),
      h('div', { class: 'skins-layout' }, stage, cards));
  }

  async function deleteSkin(id, name) {
    const ok = await modal({ title: 'Удалить скин?', text: `Скин «${name}» будет удалён из библиотеки.`, buttons: [{ label: 'Отмена', value: false }, { label: 'Удалить', value: true, cls: 'red' }] });
    if (!ok) return;
    await run(() => api.call('skins:delete', id));
    await refreshSkins();
  }

  /* ================================================================ */
  /* Страница «Настройки»                                              */
  /* ================================================================ */
  let saveTimer = null;
  async function patch(p) {
    try {
      S.settings = await api.call('settings:set', p);
      const f = $('#saved-flash');
      if (f) { f.classList.add('on'); clearTimeout(saveTimer); saveTimer = setTimeout(() => f.classList.remove('on'), 1400); }
      updateAvatar();
    } catch (e) { toast(e.message, 'err'); }
  }

  function row(label, hint, ctl) {
    return h('div', { class: 'set-row' },
      h('div', {}, h('div', { class: 'set-label' }, label), hint ? h('div', { class: 'set-hint' }, hint) : null),
      h('div', { class: 'set-ctl' }, ctl));
  }
  const toggle = (key, after) => h('label', { class: 'switch' },
    h('input', { type: 'checkbox', checked: !!S.settings[key], onchange: async (e) => { await patch({ [key]: e.target.checked }); if (after) after(); } }),
    h('span', { class: 'slider' }));
  const selectCtl = (key, opts) => {
    const s = h('select', { class: 'select', onchange: (e) => patch({ [key]: e.target.value }) },
      opts.map(([v, l]) => h('option', { value: v }, l)));
    s.value = S.settings[key];
    return s;
  };
  const textCtl = (key, props = {}) => h('input', {
    class: 'input', value: S.settings[key] || '', spellcheck: 'false', ...props,
    onchange: async (e) => { await patch({ [key]: e.target.value }); if (key === 'nickname') e.target.value = S.settings.nickname; },
  });

  const LAUNCHER_TXT = {
    idle: () => `Версия лаунчера ${S.info.launcherVersion}`,
    checking: () => 'Проверка обновлений…',
    none: () => `Версия лаунчера ${S.info.launcherVersion} — актуальна`,
    dev: () => `Версия ${S.info.launcherVersion} (режим разработки, автообновление отключено)`,
    available: () => `Доступно обновление лаунчера ${S.launcher.version}`,
    downloading: () => `Загрузка обновления ${S.launcher.version || ''} — ${Math.round(S.launcher.percent || 0)}%`,
    ready: () => `Обновление ${S.launcher.version} готово к установке`,
    error: () => `Не удалось проверить обновление: ${S.launcher.message || 'ошибка'}`,
  };

  function renderSettings() {
    const root = $('#page-settings');
    const resOpts = [['auto', 'Авто — как у монитора']].concat(['1280x720', '1366x768', '1600x900', '1920x1080', '2560x1440'].map((r) => [r, r.replace('x', ' × ')]));
    if (!resOpts.some(([v]) => v === S.settings.resolution)) resOpts.unshift([S.settings.resolution, S.settings.resolution.replace('x', ' × ')]);

    const nick = textCtl('nickname', { maxlength: '16', placeholder: 'Player' });
    nick.addEventListener('input', () => { nick.value = nick.value.replace(/[^A-Za-z0-9_]/g, ''); });

    const pathBox = h('div', { class: 'path-box', id: 'set-path', title: S.settings.installDir }, S.settings.installDir);
    const stats = h('div', { class: 'stats' }, h('span', {}, 'Занято: ', h('b', { id: 'st-used' }, '…')), h('span', {}, 'Свободно: ', h('b', { id: 'st-free' }, '…')), h('span', {}, 'Кэш загрузок: ', h('b', { id: 'st-cache' }, '…')));

    const launcherStatus = h('span', { id: 'launcher-status', class: 'set-hint', style: 'margin:0' }, LAUNCHER_TXT[S.launcher.state]?.() || '');

    root.replaceChildren(
      h('div', { class: 'page-head' },
        h('div', {}, h('div', { class: 'page-title' }, 'Настройки'), h('div', { class: 'page-sub' }, 'Изменения сохраняются автоматически')),
        h('span', { id: 'saved-flash', class: 'saved-flash' }, '✓ Сохранено')),

      h('div', { class: 'set-group panel' },
        h('div', { class: 'panel-title' }, 'Игрок'),
        row('Никнейм', 'Латиница, цифры и «_», от 3 до 16 символов (так требует игра)', nick)),

      h('div', { class: 'set-group panel' },
        h('div', { class: 'panel-title' }, 'Игра'),
        row('Разрешение', '«Авто» — игра сама подстроится под ваш монитор', selectCtl('resolution', resOpts)),
        row('Полноэкранный режим', 'Включён по умолчанию', toggle('fullscreen')),
        row('Графический API', 'Если игра не запускается или артефачит — попробуйте другой', selectCtl('graphicsApi', [['auto', 'Автоматически'], ['d3d11', 'DirectX 11'], ['vulkan', 'Vulkan'], ['opengl', 'OpenGL']])),
        row('После запуска игры', null, selectCtl('afterLaunch', [['minimize', 'Свернуть лаунчер'], ['close', 'Закрыть лаунчер'], ['keep', 'Ничего не делать']])),
        row('Доп. параметры запуска', 'Аргументы командной строки для игры', textCtl('extraArgs', { placeholder: '-logFile log.txt' }))),

      h('div', { class: 'set-group panel' },
        h('div', { class: 'panel-title' }, 'Обновления'),
        row('Проверять при запуске', 'Новые версии игры и лаунчера', toggle('checkUpdatesOnStart')),
        row('Обновлять лаунчер автоматически', 'Обновление скачивается само и ставится при закрытии', toggle('autoUpdateLauncher')),
        row('Показывать snapshot-версии', 'Пре-релизы и тестовые сборки', toggle('showPrereleases', () => refreshVersions(false))),
        row('Удалять старую версию после обновления', 'Освобождает место на диске. Миры не затрагиваются', toggle('removeOldOnUpdate')),
        row('Переносить миры при обновлении', 'Миры из старой версии переедут в новую (только в свободные слоты). Мир, созданный в новой версии, может не открыться в старой', toggle('carryWorlds')),
        row('Лаунчер', null, h('div', { style: 'display:flex;gap:12px;align-items:center' }, launcherStatus,
          h('button', { class: 'btn small', onclick: () => { run(() => api.call('launcher:check')); refreshVersions(true, false); } }, icon('refresh'), 'Проверить')))),

      h('div', { class: 'set-group panel' },
        h('div', { class: 'panel-title' }, 'Место установки'),
        row('Папка игры', 'Версии, скины, миры и кэш лежат здесь',
          h('div', { style: 'display:flex;flex-direction:column;gap:8px;align-items:flex-end' }, pathBox,
            h('div', { style: 'display:flex;gap:8px' },
              h('button', { class: 'btn small', onclick: changeDir }, icon('folder'), 'Изменить…'),
              h('button', { class: 'btn small', onclick: () => run(() => api.call('fs:open', 'root')) }, 'Открыть')))),
        stats,
        row('Кэш загрузок', 'Временные архивы (обычно удаляются сами)',
          h('button', { class: 'btn small red', onclick: async () => { await run(() => api.call('install:clearCache')); toast('Кэш очищен', 'ok'); loadInstallInfo(); } }, icon('trash'), 'Очистить'))),

      h('div', { class: 'set-group panel' },
        h('details', { class: 'adv' },
          h('summary', {}, 'Репозиторий (для разработчика)'),
          row('Владелец', 'Пусто — берётся из launcher.config.json', textCtl('repoOwner', { placeholder: S.info.repo.owner })),
          row('Репозиторий', null, textCtl('repoName', { placeholder: S.info.repo.name })),
          row('GitHub-токен', 'Нужен для приватного репозитория или чтобы обойти лимит API', textCtl('githubToken', { type: 'password', placeholder: 'ghp_…' })),
          row('', null, h('button', { class: 'btn small green', onclick: async () => { await refreshVersions(true, false); S.info = await api.call('app:info'); } }, icon('refresh'), 'Применить и проверить')))));
    loadInstallInfo();
  }

  async function loadInstallInfo() {
    try {
      const i = await api.call('install:info');
      if (!$('#st-used')) return;
      $('#st-used').textContent = fmtBytes(i.used);
      $('#st-free').textContent = fmtBytes(i.free);
      $('#st-cache').textContent = fmtBytes(i.cache);
      $('#set-path').textContent = i.dir;
      $('#set-path').title = i.dir;
    } catch { /* ignore */ }
  }

  async function changeDir() {
    if (S.install || S.gameRunning) { toast('Дождитесь окончания установки и закройте игру', 'err'); return; }
    const dir = await run(() => api.call('install:choose'));
    if (!dir) return;
    const choice = await modal({
      title: 'Перенести файлы?',
      text: `Новая папка:\n${dir}\n\nПеренести установленные версии, скины и сохранения в новое место? Это может занять время.`,
      buttons: [
        { label: 'Отмена', value: null },
        { label: 'Не переносить', value: 'no' },
        { label: 'Перенести', value: 'move', cls: 'green' },
      ],
    });
    if (!choice) return;
    const off = api.on('install:move', (m) => { $('#set-path').textContent = m.name ? `Перенос: ${m.name} (${m.step + 1}/${m.total})…` : 'Завершение…'; });
    try {
      const target = await api.call('install:setDir', { dir, move: choice === 'move' });
      S.settings.installDir = target;
      toast('Папка установки изменена', 'ok');
    } catch (e) { toast(e.message, 'err'); }
    off();
    S.settings = await api.call('settings:get');
    await Promise.all([refreshVersions(false), refreshSkins()]);
    if (S.page === 'settings') renderSettings();
  }

  /* ================================================================ */
  /* Баннер обновления лаунчера                                        */
  /* ================================================================ */
  function renderBanner() {
    const b = $('#banner');
    const L = S.launcher;
    b.replaceChildren();
    if (L.state === 'available') {
      b.append(h('div', { class: 'banner' },
        h('div', { class: 'txt' }, 'Доступно обновление лаунчера ', h('b', {}, L.version)),
        h('button', { class: 'btn small green', onclick: () => run(() => api.call('launcher:download')) }, icon('download'), 'Скачать')));
    } else if (L.state === 'downloading') {
      b.append(h('div', { class: 'banner' },
        h('div', { class: 'txt' }, `Загрузка обновления лаунчера ${L.version || ''}…`),
        h('div', { class: 'xp' }, h('i', { style: `width:${L.percent || 0}%` }))));
    } else if (L.state === 'ready') {
      b.append(h('div', { class: 'banner ready' },
        h('div', { class: 'txt' }, 'Обновление лаунчера ', h('b', {}, L.version), ' готово'),
        h('button', { class: 'btn small green', onclick: () => run(() => api.call('launcher:install')) }, 'Перезапустить и установить')));
    }
    const st = $('#launcher-status');
    if (st) st.textContent = LAUNCHER_TXT[L.state]?.() || '';
  }

  /* ================================================================ */
  /* Запуск                                                            */
  /* ================================================================ */
  function bindEvents() {
    api.on('install:progress', (p) => {
      const stageChanged = !S.install || S.install.stage !== p.stage;
      S.install = p;
      if (stageChanged) updatePlay(); else updateProgressBits();
    });
    api.on('game:state', (g) => {
      S.gameRunning = !!g.running;
      if (g.error) toast(g.error, 'err');
      updatePlay();
      if (S.page === 'worlds') refreshWorlds();
      if (S.page === 'versions') renderVersions();
    });
    api.on('launcher:update', (u) => {
      S.launcher = { ...S.launcher, ...u };
      renderBanner();
    });
  }

  function bindDragDrop() {
    let depth = 0;
    const hasFiles = (e) => e.dataTransfer && [...e.dataTransfer.types].includes('Files');
    window.addEventListener('dragenter', (e) => { if (hasFiles(e) && S.page === 'skins') { depth++; document.body.classList.add('dragging'); } e.preventDefault(); });
    window.addEventListener('dragleave', () => { depth = Math.max(0, depth - 1); if (!depth) document.body.classList.remove('dragging'); });
    window.addEventListener('dragover', (e) => e.preventDefault());
    window.addEventListener('drop', (e) => {
      e.preventDefault();
      depth = 0;
      document.body.classList.remove('dragging');
      if (S.page === 'skins' && e.dataTransfer.files.length) addSkinFiles([...e.dataTransfer.files]);
    });
  }

  async function boot() {
    buildTextures();
    drawScene();
    DEFAULT_SKIN = makeDefaultSkin();
    drawBlock($('#logo-small'), 3);

    $$('.ico').forEach((el) => el.append(icon(el.dataset.icon)));
    $('#tb-min').append(icon('min'));
    $('#tb-max').append(icon('max'));
    $('#tb-close').append(icon('close'));
    $('#tb-min').onclick = () => api.send('win:min');
    $('#tb-max').onclick = () => api.send('win:max');
    $('#tb-close').onclick = () => api.send('win:close');
    $$('.nav-item').forEach((b) => b.addEventListener('click', () => go(b.dataset.page)));

    S.info = await api.call('app:info');
    S.settings = await api.call('settings:get');
    document.title = S.info.gameName + ' Launcher';
    $('#tb-title').textContent = S.info.gameName + ' Launcher';
    $('#brand-name').textContent = S.info.gameName;

    buildPlay();
    bindEvents();
    bindDragDrop();
    const gs = await api.call('game:state').catch(() => null);
    if (gs) S.gameRunning = gs.running;

    await Promise.all([refreshVersions(false, false), refreshSkins()]);
    updatePlay();
    updateAvatar();

    // периодическая проверка новых версий
    setInterval(() => { if (S.settings.checkUpdatesOnStart && !S.install) refreshVersions(true); }, 30 * 60 * 1000);
  }

  boot().catch((e) => { console.error(e); toast('Ошибка запуска: ' + e.message, 'err'); });
})();
