// Демо-заглушка: если открыть index.html просто в браузере (без Electron),
// интерфейс работает на фейковых данных. В самом лаунчере window.api уже есть, и этот файл ничего не делает.
(() => {
  if (window.api) return;
  const listeners = {};
  const emit = (ch, d) => (listeners[ch] || []).forEach((f) => f(d));
  let settings = {
    nickname: 'Steve_01', installDir: 'C:\\Users\\You\\AppData\\Roaming\\.blockcraft', selectedVersion: 'Classic-0.0.23a',
    activeSkin: null, resolution: 'auto', fullscreen: true, graphicsApi: 'auto', extraArgs: '', afterLaunch: 'minimize',
    checkUpdatesOnStart: true, autoUpdateLauncher: true, showPrereleases: false, removeOldOnUpdate: true,
    repoOwner: '', repoName: '', githubToken: '',
  };
  const notes = '## Что нового\n- Добавлены **пещеры** и новые биомы\n- Исправлено падение при загрузке мира\n- Улучшена производительность на `Vulkan`\n\n### Известные проблемы\n- Редкие мерцания теней';
  let versions = [
    { tag: 'Classic-0.0.24a', version: 'Classic 0.0.24a', title: 'Update 1.3', notes, prerelease: false, publishedAt: '2026-11-01T10:00:00Z', size: 412e6, isLatest: true, installed: null, updateTo: null },
    { tag: 'Classic-0.0.23a', version: 'Classic 0.0.23a', title: 'Update 1.2', notes, prerelease: false, publishedAt: '2026-10-01T10:00:00Z', size: 398e6, isLatest: false, installed: { tag: 'Classic-0.0.23a' }, updateTo: 'Classic-0.0.24a' },
    { tag: 'Classic-0.0.22a', version: 'Classic 0.0.22a', title: 'Update 1.1', notes: '', prerelease: false, publishedAt: '2026-09-01T10:00:00Z', size: 380e6, isLatest: false, installed: null, updateTo: null },
  ];
  const state = () => ({ items: versions, latestTag: 'Classic-0.0.24a', selectedTag: settings.selectedVersion, error: null, offline: false, checkedAt: Date.now() });
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

  async function install(tag) {
    const total = 400e6;
    for (let i = 0; i <= 50; i++) { emit('install:progress', { tag, stage: 'downloading', percent: i * 2, received: total * i / 50, total, speed: 18e6 }); await sleep(60); }
    for (let i = 0; i <= 10; i++) { emit('install:progress', { tag, stage: 'extracting', percent: i * 10 }); await sleep(80); }
    versions = versions.map((v) => (v.tag === tag ? { ...v, installed: { tag } } : v));
    settings.selectedVersion = tag;
  }

  const handlers = {
    'app:info': () => ({ gameName: 'BlockCraft', launcherVersion: '1.0.0', platform: 'win32', packaged: false, repoConfigured: true, repo: { owner: 'you', name: 'repo' } }),
    'settings:get': () => settings,
    'settings:set': (p) => (settings = { ...settings, ...p }),
    'versions:list': () => state(),
    'versions:install': ({ tag }) => install(tag),
    'versions:select': (t) => { settings.selectedVersion = t; },
    'versions:remove': (t) => { versions = versions.map((v) => (v.tag === t ? { ...v, installed: null } : v)); },
    'game:state': () => ({ running: false }),
    'game:play': () => { emit('game:state', { running: true }); setTimeout(() => emit('game:state', { running: false }), 3000); },
    'skins:list': () => ({ items: [], active: null }),
    'worlds:list': () => ({ maxSlots: 5, worlds: [
      { tag: 'Classic-0.0.24a', version: 'Classic 0.0.24a', installed: false, slot: 1, size: 3.2e6, modified: Date.now() - 3600e3, seed: 482910 },
      { tag: 'Classic-0.0.23a', version: 'Classic 0.0.23a', installed: true, slot: 1, size: 2.1e6, modified: Date.now() - 86400e3 * 3, seed: -77123 },
      { tag: 'Classic-0.0.23a', version: 'Classic 0.0.23a', installed: true, slot: 2, size: 5.8e6, modified: Date.now() - 86400e3 * 9, seed: 9001 },
    ] }),
    'worlds:backup': () => 'x', 'worlds:delete': () => {}, 'worlds:transfer': () => {}, 'fs:open': () => {},
    'install:info': () => ({ dir: settings.installDir, used: 812e6, cache: 0, free: 96e9 }),
  };

  const ribbon = document.createElement('div');
  ribbon.textContent = 'ДЕМО В БРАУЗЕРЕ — это только превью интерфейса. Настоящее приложение: запустите start.bat';
  ribbon.style.cssText = 'position:fixed;left:0;right:0;bottom:0;z-index:999;background:#a33;color:#fff;font:600 12px sans-serif;text-align:center;padding:6px;letter-spacing:.5px';
  document.addEventListener('DOMContentLoaded', () => document.body.append(ribbon));

  window.api = {
    async call(ch, p) {
      const f = handlers[ch];
      if (!f) throw new Error('Недоступно в демо-режиме: ' + ch);
      return f(p);
    },
    send() {},
    on(ch, cb) { (listeners[ch] = listeners[ch] || []).push(cb); return () => { listeners[ch] = listeners[ch].filter((f) => f !== cb); }; },
  };
  setTimeout(() => emit('launcher:update', { state: 'ready', version: '1.0.1' }), 800);
})();
