'use strict';
const { app, BrowserWindow, ipcMain, dialog, shell, net, screen } = require('electron');
const path = require('path');
const fs = require('fs');
const fsp = fs.promises;
const crypto = require('crypto');
const { spawn } = require('child_process');
const { autoUpdater } = require('electron-updater');
const extractZip = require('extract-zip');
const BRAND = require('./launcher.config.json');

const UA = `${BRAND.gameName}-Launcher/${app.getVersion()}`;
const PLACEHOLDER_OWNER = 'YOUR_GITHUB_NAME';

let win = null;
let settings = null;
let gameProc = null; // { child, tag }
let installJob = null; // { tag, controller }
let releasesCache = null; // { at, list }

/* ------------------------------------------------------------------ */
/* Утилиты                                                             */
/* ------------------------------------------------------------------ */
const emit = (ch, data) => {
  if (win && !win.isDestroyed()) win.webContents.send(ch, data);
};
const safe = (s) => String(s).replace(/[^\w.+-]/g, '_');
const exists = async (p) => {
  try {
    await fsp.access(p);
    return true;
  } catch {
    return false;
  }
};
const rmrf = (p) => fsp.rm(p, { recursive: true, force: true, maxRetries: 3, retryDelay: 200 });

async function dirSize(p) {
  let total = 0;
  let ents;
  try {
    ents = await fsp.readdir(p, { withFileTypes: true });
  } catch {
    return 0;
  }
  for (const e of ents) {
    const f = path.join(p, e.name);
    if (e.isDirectory()) total += await dirSize(f);
    else if (e.isFile()) {
      try {
        total += (await fsp.stat(f)).size;
      } catch {
        /* ignore */
      }
    }
  }
  return total;
}

function splitArgs(s) {
  return (String(s).match(/"[^"]*"|'[^']*'|\S+/g) || []).map((t) => t.replace(/^["']|["']$/g, ''));
}

/* ------------------------------------------------------------------ */
/* Настройки                                                           */
/* ------------------------------------------------------------------ */
const settingsFile = () => path.join(app.getPath('userData'), 'settings.json');

const defaults = () => ({
  settingsVersion: 2,
  nickname: 'Player',
  installDir: path.join(app.getPath('appData'), '.' + BRAND.gameName.replace(/\W+/g, '')),
  selectedVersion: null,
  activeSkin: null,
  resolution: 'auto', // auto = разрешение монитора
  fullscreen: true,
  graphicsApi: 'auto',
  extraArgs: '',
  afterLaunch: 'minimize', // minimize | close | keep
  checkUpdatesOnStart: true,
  autoUpdateLauncher: true,
  showPrereleases: false,
  removeOldOnUpdate: true,
  carryWorlds: true, // при обновлении переносить миры в новую версию
  repoOwner: '',
  repoName: '',
  githubToken: '',
});

const CLEAN = {
  nickname: (v) => String(v).replace(/[^A-Za-z0-9_]/g, '').slice(0, 16), // игра принимает только латиницу, цифры и _ (3-16)
  resolution: (v) => (v === 'auto' || /^\d{3,5}x\d{3,5}$/.test(v) ? v : 'auto'),
  fullscreen: Boolean,
  graphicsApi: (v) => (['auto', 'd3d11', 'vulkan', 'opengl'].includes(v) ? v : 'auto'),
  extraArgs: (v) => String(v).slice(0, 500),
  afterLaunch: (v) => (['minimize', 'close', 'keep'].includes(v) ? v : 'minimize'),
  checkUpdatesOnStart: Boolean,
  autoUpdateLauncher: Boolean,
  showPrereleases: Boolean,
  removeOldOnUpdate: Boolean,
  carryWorlds: Boolean,
  repoOwner: (v) => String(v).trim().slice(0, 100),
  repoName: (v) => String(v).trim().slice(0, 100),
  githubToken: (v) => String(v).trim().slice(0, 300),
};

function loadSettings() {
  let saved = {};
  try {
    saved = JSON.parse(fs.readFileSync(settingsFile(), 'utf8'));
  } catch {
    /* первый запуск */
  }
  settings = { ...defaults(), ...saved };
  if (!/^[A-Za-z0-9_]{3,16}$/.test(settings.nickname)) settings.nickname = 'Player';
  // Новые значения по умолчанию (полный экран, авторазрешение, удаление старых версий)
  // применяем и к настройкам, сохранённым старой сборкой лаунчера
  if ((saved.settingsVersion || 1) < 2) {
    Object.assign(settings, { fullscreen: true, resolution: 'auto', removeOldOnUpdate: true, settingsVersion: 2 });
  }
}

function saveSettings() {
  fs.mkdirSync(path.dirname(settingsFile()), { recursive: true });
  fs.writeFileSync(settingsFile(), JSON.stringify(settings, null, 2));
}

const repo = () => ({
  owner: settings.repoOwner || BRAND.repo.owner,
  name: settings.repoName || BRAND.repo.name,
});
const repoConfigured = () => {
  const r = repo();
  return !!r.owner && !!r.name && r.owner !== PLACEHOLDER_OWNER;
};

const dirs = () => {
  const root = settings.installDir;
  return {
    root,
    versions: path.join(root, 'versions'),
    skins: path.join(root, 'skins'),
    worlds: path.join(root, 'worlds'),
    backups: path.join(root, 'backups'),
    cache: path.join(root, 'cache'),
  };
};
async function ensureDirs() {
  for (const d of Object.values(dirs())) await fsp.mkdir(d, { recursive: true });
}

/* ------------------------------------------------------------------ */
/* Версии (GitHub Releases)                                            */
/* ------------------------------------------------------------------ */
// Названия релизов: «Pre-classic-rd-132211», «Classic-0.0.23a» (регистр, пробелы и дефисы не важны).
// Кириллические двойники латинских букв (в тегах бывает «0.0.15а») приводим к латинице.
const LOOKALIKE = { 'а': 'a', 'е': 'e', 'о': 'o', 'р': 'p', 'с': 'c' };
const normName = (s) => String(s || '').replace(/[аеорс]/g, (c) => LOOKALIKE[c]).trim();

// Возвращает описание версии или null, если релиз — не версия игры
function parseGame(...names) {
  for (const raw of names) {
    const s = normName(raw);
    let m = /pre[\s_-]*classic[\s_-]*rd[\s_-]*(\d+)/i.exec(s);
    if (m) return { kind: 0, nums: [parseInt(m[1], 10)], suffix: '', label: 'Pre-classic rd-' + m[1] };
    m = /classic[\s_-]*(\d+(?:\.\d+)*)\s*([a-z]?)/i.exec(s);
    if (m) {
      return {
        kind: 1,
        nums: m[1].split('.').map((n) => parseInt(n, 10)),
        suffix: m[2].toLowerCase(),
        label: 'Classic ' + m[1] + m[2].toLowerCase(),
      };
    }
  }
  return null;
}
// Порядок как в истории игры: все pre-classic, затем classic по номерам
function cmpVer(a, b) {
  const A = parseGame(a) || { kind: -1, nums: [], suffix: '' };
  const B = parseGame(b) || { kind: -1, nums: [], suffix: '' };
  if (A.kind !== B.kind) return A.kind - B.kind;
  for (let i = 0; i < Math.max(A.nums.length, B.nums.length); i++) {
    const d = (A.nums[i] || 0) - (B.nums[i] || 0);
    if (d) return d;
  }
  return A.suffix.localeCompare(B.suffix);
}

function ghHeaders(extra = {}) {
  const h = {
    'User-Agent': UA,
    Accept: 'application/vnd.github+json',
    'X-GitHub-Api-Version': '2022-11-28',
    ...extra,
  };
  if (settings.githubToken) h.Authorization = 'Bearer ' + settings.githubToken;
  return h;
}

function pickAsset(assets) {
  const zips = assets.filter((a) => /\.zip$/i.test(a.name));
  if (!zips.length) {
    // Нет zip — берём одиночный исполняемый файл под текущую систему
    const bin = { win32: /\.exe$/i, linux: /\.(x86_64|appimage)$/i }[process.platform];
    return (bin && assets.find((a) => bin.test(a.name))) || null;
  }
  const keys = {
    win32: ['win', 'windows'],
    darwin: ['mac', 'osx', 'darwin'],
    linux: ['linux'],
  };
  const mine = keys[process.platform] || [];
  const others = Object.entries(keys)
    .filter(([p]) => p !== process.platform)
    .flatMap(([, k]) => k);
  const has = (a, list) => list.some((k) => a.name.toLowerCase().includes(k));
  return (
    zips.find((a) => has(a, mine)) ||
    zips.find((a) => !has(a, others)) ||
    null
  );
}

function mapRelease(r) {
  const assets = (r.assets || []).map((a) => ({
    name: a.name,
    size: a.size,
    url: a.url,
    browserUrl: a.browser_download_url,
    digest: a.digest || null,
  }));
  const asset = pickAsset(assets);
  const g = parseGame(r.tag_name, r.name);
  if (!g) return null;
  return {
    tag: r.tag_name,
    version: g.label,
    title: r.name || r.tag_name,
    notes: r.body || '',
    prerelease: !!r.prerelease,
    publishedAt: r.published_at,
    size: asset ? asset.size : 0,
    asset,
  };
}

const releasesDiskCache = () => path.join(app.getPath('userData'), 'releases-cache.json');

async function fetchReleases(force) {
  if (!repoConfigured()) {
    throw new Error('Репозиторий не настроен. Откройте «Настройки → Репозиторий» или launcher.config.json.');
  }
  if (!force && releasesCache && Date.now() - releasesCache.at < 60000) return releasesCache.list;
  const { owner, name } = repo();
  let res;
  try {
    res = await net.fetch(`https://api.github.com/repos/${owner}/${name}/releases?per_page=100`, {
      headers: ghHeaders(),
      signal: AbortSignal.timeout(15000),
    });
  } catch {
    throw new Error('Нет соединения с GitHub');
  }
  if (!res.ok) {
    if (res.status === 401) throw new Error('GitHub отклонил токен (401)');
    if (res.status === 404) throw new Error('Репозиторий не найден. Если он приватный — укажите токен в настройках.');
    if (res.status === 403 || res.status === 429) {
      throw new Error('Лимит запросов GitHub исчерпан. Укажите токен в настройках или подождите.');
    }
    throw new Error(`GitHub вернул ${res.status}`);
  }
  const json = await res.json();
  const list = json
    .filter((r) => !r.draft)
    .map(mapRelease)
    .filter((r) => r && r.asset)
    .sort((a, b) => cmpVer(b.tag, a.tag));
  releasesCache = { at: Date.now(), list };
  try {
    fs.writeFileSync(releasesDiskCache(), JSON.stringify(list));
  } catch {
    /* ignore */
  }
  return list;
}

function readDiskReleases() {
  try {
    return JSON.parse(fs.readFileSync(releasesDiskCache(), 'utf8'));
  } catch {
    return [];
  }
}

async function listInstalled() {
  const { versions } = dirs();
  let names = [];
  try {
    names = await fsp.readdir(versions);
  } catch {
    return [];
  }
  const out = [];
  for (const n of names) {
    if (n.startsWith('.')) continue;
    try {
      const meta = JSON.parse(await fsp.readFile(path.join(versions, n, '.install.json'), 'utf8'));
      out.push({ ...meta, dir: path.join(versions, n) });
    } catch {
      /* не наша папка */
    }
  }
  return out;
}

// Версии, поставленные прежним лаунчером (папка versions/<тег> без .install.json), берём под своё управление
async function adoptLegacy(releases) {
  const { versions } = dirs();
  let names = [];
  try {
    names = await fsp.readdir(versions);
  } catch {
    return;
  }
  for (const n of names) {
    if (n.startsWith('.')) continue;
    const dir = path.join(versions, n);
    if (await exists(path.join(dir, '.install.json'))) continue;
    const rel = releases.find((r) => r.tag === n || safe(r.tag) === n);
    if (!rel) continue;
    try {
      await fsp.writeFile(
        path.join(dir, '.install.json'),
        JSON.stringify({ tag: rel.tag, version: rel.version, installedAt: Date.now(), publishedAt: rel.publishedAt, asset: rel.asset ? rel.asset.name : null, size: await dirSize(dir), adopted: true })
      );
    } catch {
      /* ignore */
    }
  }
}

async function getVersionsState(force) {
  let releases = [];
  let error = null;
  let offline = false;
  try {
    releases = await fetchReleases(force);
  } catch (e) {
    error = e.message;
    offline = true;
    releases = readDiskReleases();
  }
  if (releases.length && !gameProc && !installJob) await adoptLegacy(releases);
  const installed = await listInstalled();
  const stable = releases.filter((r) => settings.showPrereleases || !r.prerelease);
  const latest = stable[0] || null;

  const items = [];
  for (const r of releases) {
    const inst = installed.find((i) => i.tag === r.tag) || null;
    if (!settings.showPrereleases && r.prerelease && !inst) continue;
    items.push({ ...r, installed: inst, isLatest: !!latest && r.tag === latest.tag });
  }
  for (const i of installed) {
    if (items.some((x) => x.tag === i.tag)) continue;
    items.push({
      tag: i.tag,
      version: i.version || i.tag,
      title: i.tag,
      notes: '',
      prerelease: false,
      publishedAt: i.publishedAt || null,
      size: i.size || 0,
      asset: null,
      installed: i,
      isLatest: false,
      localOnly: true,
    });
  }
  items.sort((a, b) => cmpVer(b.tag, a.tag));
  for (const it of items) {
    it.updateTo = it.installed && latest && cmpVer(latest.tag, it.tag) > 0 ? latest.tag : null;
  }

  let selectedTag = settings.selectedVersion;
  if (!items.some((i) => i.tag === selectedTag)) {
    selectedTag = (items.find((i) => i.installed) || items[0] || {}).tag || null;
  }
  return { items, latestTag: latest ? latest.tag : null, selectedTag, error, offline, checkedAt: Date.now() };
}

/* ------------------------------------------------------------------ */
/* Миры                                                                */
/* ------------------------------------------------------------------ */
// Игра пишет миры в <папка версии>/saves/world_N. Чтобы миры не пропадали вместе с версией
// при обновлении/удалении, реальные файлы лежат в <установка>/worlds/<версия>/, а
// <папка версии>/saves — ссылка (junction) на эту папку. Работает и со старыми сборками игры.
const MAX_SLOTS = 5;
const sameFile = (a, b) =>
  process.platform === 'win32' ? path.resolve(a).toLowerCase() === path.resolve(b).toLowerCase() : path.resolve(a) === path.resolve(b);

async function lstatOrNull(p) {
  try {
    return await fsp.lstat(p);
  } catch {
    return null;
  }
}

// Удаляет только саму ссылку, содержимое не трогает
async function dropLink(p) {
  const st = await lstatOrNull(p);
  if (!st || !st.isSymbolicLink()) return false;
  await fsp.unlink(p).catch(() => fsp.rmdir(p));
  return true;
}

async function ensureWorldLink(inst) {
  const target = path.join(dirs().worlds, safe(inst.tag));
  const link = path.join(inst.dir, 'saves');
  await fsp.mkdir(target, { recursive: true });
  const st = await lstatOrNull(link);

  if (st && st.isSymbolicLink()) {
    const real = await fsp.realpath(link).catch(() => null);
    if (real && sameFile(real, await fsp.realpath(target))) return;
    await dropLink(link); // ссылка вела на старое место (папку установки меняли)
  } else if (st && st.isDirectory()) {
    // Миры, созданные прямо в папке версии: переносим в хранилище
    const tmp = link + '.migrating';
    await rmrf(tmp);
    await fsp.rename(link, tmp);
    try {
      await fsp.symlink(target, link, 'junction');
    } catch {
      await fsp.rename(tmp, link); // ссылки не поддерживаются на этом диске — оставляем как есть
      return;
    }
    for (const n of await fsp.readdir(tmp)) {
      let dst = path.join(target, n);
      if (await exists(dst)) dst += '.old-' + Date.now(); // ничего не перезаписываем
      await fsp.rename(path.join(tmp, n), dst);
    }
    await rmrf(tmp);
    return;
  } else if (st) {
    await fsp.rm(link, { force: true });
  }
  try {
    await fsp.symlink(target, link, 'junction');
  } catch {
    await fsp.mkdir(link, { recursive: true }); // запасной вариант: миры лежат внутри версии
  }
}

// Перед удалением/переустановкой версии: убираем ссылку (миры остаются в хранилище)
// или, если ссылку создать не удалось, забираем настоящую папку saves в хранилище
async function detachWorlds(inst) {
  const link = path.join(inst.dir, 'saves');
  const st = await lstatOrNull(link);
  if (!st) return;
  if (st.isSymbolicLink()) {
    await dropLink(link);
  } else if (st.isDirectory()) {
    const target = path.join(dirs().worlds, safe(inst.tag));
    await fsp.mkdir(target, { recursive: true });
    for (const n of await fsp.readdir(link)) {
      let dst = path.join(target, n);
      if (await exists(dst)) dst += '.old-' + Date.now();
      await fsp.rename(path.join(link, n), dst);
    }
  }
}

// Где сейчас лежат миры версии
async function worldsPath(tag) {
  const inst = (await listInstalled()).find((i) => i.tag === tag);
  if (inst) {
    const st = await lstatOrNull(path.join(inst.dir, 'saves'));
    if (st && st.isDirectory() && !st.isSymbolicLink()) return path.join(inst.dir, 'saves');
  }
  return path.join(dirs().worlds, safe(tag));
}

async function readSeed(levelPath) {
  try {
    const fh = await fsp.open(levelPath, 'r');
    try {
      const buf = Buffer.alloc(512);
      const { bytesRead } = await fh.read(buf, 0, 512, 0);
      const m = /"Seed"\s*:\s*(-?\d+)/.exec(buf.toString('utf8', 0, bytesRead));
      return m ? Number(m[1]) : null;
    } finally {
      await fh.close();
    }
  } catch {
    return null;
  }
}

async function listWorlds() {
  const d = dirs();
  const installed = await listInstalled();
  for (const inst of installed) {
    if (gameProc && gameProc.tag === inst.tag) continue;
    await ensureWorldLink(inst).catch(() => {});
  }
  const tags = new Map(); // safe(tag) -> реальный тег
  for (const i of installed) tags.set(safe(i.tag), i.tag);
  let known = [];
  try {
    known = await fsp.readdir(d.worlds);
  } catch {
    /* ещё нет */
  }
  for (const n of known) if (!tags.has(n)) tags.set(n, n);

  const out = [];
  for (const [, tag] of tags) {
    const base = await worldsPath(tag);
    let names = [];
    try {
      names = await fsp.readdir(base);
    } catch {
      continue;
    }
    for (const n of names) {
      const m = /^world_(\d+)$/.exec(n);
      if (!m) continue;
      const dir = path.join(base, n);
      const level = path.join(dir, 'level.dat');
      let st;
      try {
        st = await fsp.stat(level);
      } catch {
        continue; // пустой слот
      }
      const g = parseGame(tag);
      out.push({
        tag,
        version: g ? g.label : tag,
        installed: installed.some((i) => i.tag === tag),
        slot: Number(m[1]),
        size: await dirSize(dir),
        modified: st.mtimeMs,
        seed: await readSeed(level),
      });
    }
  }
  out.sort((a, b) => cmpVer(b.tag, a.tag) || a.slot - b.slot);
  return { worlds: out, maxSlots: MAX_SLOTS };
}

const worldDir = async (tag, slot) => path.join(await worldsPath(tag), 'world_' + Number(slot));
const needIdle = () => {
  if (gameProc) throw new Error('Закройте игру перед работой с мирами');
  if (installJob) throw new Error('Дождитесь окончания установки');
};

async function relocate(src, dst, move) {
  await fsp.mkdir(path.dirname(dst), { recursive: true });
  if (move) {
    try {
      await fsp.rename(src, dst);
      return;
    } catch (e) {
      if (e.code !== 'EXDEV') throw e;
    }
  }
  await fsp.cp(src, dst, { recursive: true, force: true });
  if (move) await rmrf(src);
}

async function transferWorld({ tag, slot, toTag, toSlot, move, overwrite }) {
  needIdle();
  const src = await worldDir(tag, slot);
  const dst = await worldDir(toTag, toSlot);
  if (sameFile(src, dst)) throw new Error('Мир уже находится в этом слоте');
  if (!(await exists(src))) throw new Error('Исходный мир не найден');
  if (await exists(dst)) {
    if (!overwrite) throw new Error('Слот занят');
    await rmrf(dst);
  }
  await relocate(src, dst, !!move);
}

async function deleteWorld({ tag, slot }) {
  needIdle();
  await rmrf(await worldDir(tag, slot));
}

async function backupWorld({ tag, slot }) {
  const src = await worldDir(tag, slot);
  if (!(await exists(src))) throw new Error('Мир не найден');
  const ts = new Date().toISOString().replace(/[:T]/g, '-').slice(0, 19);
  const dst = path.join(dirs().backups, `${safe(tag)}_world_${slot}_${ts}`);
  await fsp.cp(src, dst, { recursive: true });
  return dst;
}

// При обновлении: переносим миры из старой версии в новую (только в свободные слоты)
async function carryWorlds(fromTag, toTag, move) {
  const from = await worldsPath(fromTag);
  let names = [];
  try {
    names = await fsp.readdir(from);
  } catch {
    return 0;
  }
  let n = 0;
  for (const name of names) {
    if (!/^world_\d+$/.test(name)) continue;
    const dst = path.join(await worldsPath(toTag), name);
    if (await exists(dst)) continue;
    await relocate(path.join(from, name), dst, move);
    n++;
  }
  return n;
}

/* ------------------------------------------------------------------ */
/* Загрузка и установка                                                */
/* ------------------------------------------------------------------ */
async function download(url, dest, { headers, signal, total, onProgress }) {
  const res = await net.fetch(url, { headers, signal, redirect: 'follow' });
  if (!res.ok) throw new Error(`Сервер вернул ${res.status} ${res.statusText || ''}`.trim());
  const size = Number(res.headers.get('content-length')) || total || 0;
  const hash = crypto.createHash('sha256');
  const out = fs.createWriteStream(dest);
  const errP = new Promise((_, rej) => out.on('error', rej));
  errP.catch(() => {});
  let received = 0;
  try {
    const reader = res.body.getReader();
    for (;;) {
      const { done, value } = await Promise.race([reader.read(), errP]);
      if (done) break;
      hash.update(value);
      if (!out.write(value)) await Promise.race([new Promise((r) => out.once('drain', r)), errP]);
      received += value.length;
      onProgress(received, size);
    }
    await new Promise((resolve, reject) => {
      out.once('error', reject);
      out.end(resolve);
    });
  } catch (e) {
    out.destroy();
    throw e;
  }
  return hash.digest('hex');
}

async function installVersion(tag, replaceTag) {
  if (installJob) throw new Error('Уже идёт установка другой версии');
  const rel = (await fetchReleases(false)).find((r) => r.tag === tag);
  if (!rel || !rel.asset) throw new Error('Для этой версии нет архива под вашу систему');

  const controller = new AbortController();
  installJob = { tag, controller };
  const d = dirs();
  await ensureDirs();
  const st = safe(tag);
  const zipPath = path.join(d.cache, st + '.zip');
  const tmp = path.join(d.versions, '.tmp-' + st);
  const finalDir = path.join(d.versions, st);

  let lastEmit = 0;
  let lastBytes = 0;
  let lastTime = Date.now();
  let speed = 0;
  const onProgress = (rec, tot) => {
    const now = Date.now();
    if (now - lastEmit < 100 && rec !== tot) return;
    const dt = (now - lastTime) / 1000;
    if (dt > 0.4) {
      speed = (rec - lastBytes) / dt;
      lastBytes = rec;
      lastTime = now;
    }
    lastEmit = now;
    emit('install:progress', {
      tag,
      stage: 'downloading',
      percent: tot ? (rec / tot) * 100 : 0,
      received: rec,
      total: tot,
      speed,
    });
  };

  try {
    emit('install:progress', { tag, stage: 'downloading', percent: 0, received: 0, total: rel.asset.size, speed: 0 });
    await rmrf(tmp);

    const useApi = !!settings.githubToken;
    const sha = await download(useApi ? rel.asset.url : rel.asset.browserUrl, zipPath, {
      headers: useApi ? ghHeaders({ Accept: 'application/octet-stream' }) : { 'User-Agent': UA },
      signal: controller.signal,
      total: rel.asset.size,
      onProgress,
    });

    if (rel.asset.digest && rel.asset.digest.startsWith('sha256:')) {
      emit('install:progress', { tag, stage: 'verifying', percent: 100 });
      if (sha !== rel.asset.digest.slice(7)) {
        throw new Error('Контрольная сумма архива не совпала. Попробуйте скачать заново.');
      }
    }

    emit('install:progress', { tag, stage: 'extracting', percent: 0 });
    await fsp.mkdir(tmp, { recursive: true });
    if (!/\.zip$/i.test(rel.asset.name)) {
      // Одиночный файл (например, .exe) — просто кладём в папку версии
      await fsp.rename(zipPath, path.join(tmp, rel.asset.name));
    } else {
    let n = 0;
    let lastX = 0;
    await extractZip(zipPath, {
      dir: tmp,
      onEntry: (_e, zf) => {
        n++;
        const now = Date.now();
        if (now - lastX > 100 || n === zf.entryCount) {
          lastX = now;
          emit('install:progress', { tag, stage: 'extracting', percent: (n / zf.entryCount) * 100 });
        }
      },
    });
    }
    if (controller.signal.aborted) throw new Error('cancelled');

    emit('install:progress', { tag, stage: 'finalizing', percent: 100 });
    // Если внутри архива одна корневая папка — поднимаем содержимое на уровень выше
    let root = tmp;
    const ents = await fsp.readdir(tmp);
    if (ents.length === 1) {
      const p = path.join(tmp, ents[0]);
      if ((await fsp.stat(p)).isDirectory() && !/\.app$/i.test(ents[0])) root = p;
    }
    await detachWorlds({ tag, dir: finalDir }).catch(() => {});
    await rmrf(finalDir);
    await fsp.rename(root, finalDir);
    if (root !== tmp) await rmrf(tmp);

    const size = await dirSize(finalDir);
    await fsp.writeFile(
      path.join(finalDir, '.install.json'),
      JSON.stringify({
        tag,
        version: rel.version,
        installedAt: Date.now(),
        publishedAt: rel.publishedAt,
        asset: rel.asset.name,
        size,
      })
    );
    await fsp.rm(zipPath, { force: true }).catch(() => {});

    await ensureWorldLink({ tag, dir: finalDir }).catch(() => {});
    if (replaceTag && replaceTag !== tag && settings.carryWorlds) {
      await carryWorlds(replaceTag, tag, settings.removeOldOnUpdate).catch(() => {});
    }

    settings.selectedVersion = tag;
    saveSettings();
    if (replaceTag && replaceTag !== tag && settings.removeOldOnUpdate) {
      await removeVersion(replaceTag).catch(() => {});
    }
    return true;
  } catch (e) {
    await rmrf(tmp).catch(() => {});
    await fsp.rm(zipPath, { force: true }).catch(() => {});
    if (controller.signal.aborted || e.name === 'AbortError' || e.message === 'cancelled') {
      throw new Error('cancelled');
    }
    throw e;
  } finally {
    installJob = null;
  }
}

async function removeVersion(tag) {
  if (gameProc && gameProc.tag === tag) throw new Error('Эта версия сейчас запущена');
  if (installJob && installJob.tag === tag) throw new Error('Эта версия сейчас устанавливается');
  const inst = (await listInstalled()).find((i) => i.tag === tag);
  if (!inst) return;
  await detachWorlds(inst); // миры остаются в хранилище
  await rmrf(inst.dir);
  if (settings.selectedVersion === tag) {
    settings.selectedVersion = null;
    saveSettings();
  }
}

/* ------------------------------------------------------------------ */
/* Запуск игры                                                         */
/* ------------------------------------------------------------------ */
async function findExecutable(dir) {
  const want = BRAND.gameExecutable && BRAND.gameExecutable[process.platform];
  if (want) {
    const p = path.join(dir, want);
    if (await exists(p)) return p;
  }
  const skip = /crash|unins|setup|redist/i;
  const queue = [[dir, 0]];
  while (queue.length) {
    const [d, depth] = queue.shift();
    let ents;
    try {
      ents = await fsp.readdir(d, { withFileTypes: true });
    } catch {
      continue;
    }
    for (const e of ents) {
      const f = path.join(d, e.name);
      if (process.platform === 'win32' && e.isFile() && /\.exe$/i.test(e.name) && !skip.test(e.name)) return f;
      if (process.platform === 'darwin' && e.isDirectory() && /\.app$/i.test(e.name)) return f;
      if (process.platform === 'linux' && e.isFile() && /\.(x86_64|x86|arm64|AppImage)$/i.test(e.name)) return f;
    }
    if (depth < 2) {
      for (const e of ents) {
        if (e.isDirectory() && !/\.app$/i.test(e.name) && !/_Data$|^MonoBleedingEdge$|^D3D12$/.test(e.name)) {
          queue.push([path.join(d, e.name), depth + 1]);
        }
      }
    }
  }
  throw new Error('Не найден исполняемый файл игры. Укажите его имя в launcher.config.json → gameExecutable.');
}

// Реальное (физическое) разрешение монитора, на котором сейчас лаунчер
function monitorSize() {
  const b = win && !win.isDestroyed() ? win.getBounds() : screen.getPrimaryDisplay().bounds;
  const d = screen.getDisplayMatching(b);
  return [Math.round(d.size.width * d.scaleFactor), Math.round(d.size.height * d.scaleFactor)];
}

async function playGame() {
  if (gameProc) throw new Error('Игра уже запущена');
  const inst = (await listInstalled()).find((i) => i.tag === settings.selectedVersion);
  if (!inst) throw new Error('Выбранная версия не установлена');
  await ensureDirs();
  const d = dirs();
  const exe = await findExecutable(inst.dir);
  let [w, h] = monitorSize();
  if (settings.resolution !== 'auto') [w, h] = settings.resolution.split('x').map(Number);
  else if (!settings.fullscreen) {
    // В окне при «авто» не раздуваем игру на весь экран
    w = Math.min(w, 1280);
    h = Math.min(h, 720);
  }

  const args = [
    '-username', settings.nickname, // игра читает именно «-username»
    '--gameVersion', inst.tag,
    '--launcherVersion', app.getVersion(),
  ];

  let skinPath = null;
  let skinModel = 'classic';
  if (settings.activeSkin) {
    const idx = await readSkinIndex();
    const s = idx.items.find((x) => x.id === settings.activeSkin);
    const p = s && path.join(d.skins, s.id + '.png');
    // игра принимает скин только до 32 КБ, иначе молча игнорирует
    if (s && (await exists(p)) && (await fsp.stat(p)).size <= SKIN_MAX_BYTES) {
      skinPath = p;
      skinModel = s.model;
      args.push('-skin', p); // игра читает именно «-skin <путь к PNG>»
    }
  }

  // Встроенные параметры Unity Player
  args.push('-screen-fullscreen', settings.fullscreen ? '1' : '0', '-screen-width', String(w), '-screen-height', String(h));
  const api = { d3d11: '-force-d3d11', vulkan: '-force-vulkan', opengl: '-force-glcore' }[settings.graphicsApi];
  if (api) args.push(api);
  args.push(...splitArgs(settings.extraArgs));

  await ensureWorldLink(inst).catch(() => {}); // миры версии — в общем хранилище

  let child;
  if (process.platform === 'darwin' && exe.endsWith('.app')) {
    child = spawn('open', ['-W', '-n', '-a', exe, '--args', ...args], { stdio: 'ignore', detached: true });
  } else {
    if (process.platform !== 'win32') await fsp.chmod(exe, 0o755).catch(() => {});
    child = spawn(exe, args, { cwd: path.dirname(exe), stdio: 'ignore', detached: true });
  }
  child.unref();
  gameProc = { child, tag: inst.tag };
  emit('game:state', { running: true, tag: inst.tag });

  const finish = (error) => {
    if (!gameProc || gameProc.child !== child) return;
    gameProc = null;
    emit('game:state', { running: false, error: error || null });
    if (settings.afterLaunch === 'minimize' && win && !win.isDestroyed()) {
      win.restore();
      win.focus();
    }
  };
  child.on('error', (e) => finish('Не удалось запустить игру: ' + e.message));
  child.on('exit', (code) => finish(code ? `Игра завершилась с кодом ${code}` : null));

  if (settings.afterLaunch === 'close') setTimeout(() => app.quit(), 1200);
  else if (settings.afterLaunch === 'minimize' && win) win.minimize();
  return true;
}

/* ------------------------------------------------------------------ */
/* Скины                                                               */
/* ------------------------------------------------------------------ */
const skinIndexFile = () => path.join(dirs().skins, 'index.json');

async function readSkinIndex() {
  try {
    return JSON.parse(await fsp.readFile(skinIndexFile(), 'utf8'));
  } catch {
    return { items: [] };
  }
}
async function writeSkinIndex(idx) {
  await fsp.mkdir(dirs().skins, { recursive: true });
  await fsp.writeFile(skinIndexFile(), JSON.stringify(idx, null, 2));
}

// Ограничения самой игры (LauncherSkin.cs): PNG 64x64 или 64x32, до 32 КБ
const SKIN_MAX_BYTES = 32 * 1024;

function checkPng(buf) {
  const sig = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
  if (buf.length < 33 || !buf.subarray(0, 8).equals(sig)) throw new Error('Это не PNG-файл');
  const w = buf.readUInt32BE(16);
  const h = buf.readUInt32BE(20);
  if (w !== 64 || (h !== 64 && h !== 32)) {
    throw new Error(`Неверный размер скина ${w}×${h}. Игра принимает только 64×64 или 64×32.`);
  }
}

async function addSkin(buf, rawName) {
  if (buf.length > SKIN_MAX_BYTES) throw new Error('Файл больше 32 КБ (ограничение игры)');
  checkPng(buf);
  const id = crypto.randomBytes(5).toString('hex');
  const name = String(rawName || 'Скин').replace(/\.png$/i, '').replace(/[<>:"/\\|?*\x00-\x1f]/g, '').trim().slice(0, 32) || 'Скин';
  await fsp.mkdir(dirs().skins, { recursive: true });
  await fsp.writeFile(path.join(dirs().skins, id + '.png'), buf);
  const idx = await readSkinIndex();
  const item = { id, name, model: 'classic', addedAt: Date.now() };
  idx.items.push(item);
  await writeSkinIndex(idx);
  settings.activeSkin = id;
  saveSettings();
  return item;
}

async function listSkins() {
  const idx = await readSkinIndex();
  const out = [];
  for (const s of idx.items) {
    try {
      const buf = await fsp.readFile(path.join(dirs().skins, s.id + '.png'));
      out.push({ ...s, dataUrl: 'data:image/png;base64,' + buf.toString('base64') });
    } catch {
      /* файл удалён вручную */
    }
  }
  return { items: out, active: settings.activeSkin };
}

/* ------------------------------------------------------------------ */
/* Место установки                                                     */
/* ------------------------------------------------------------------ */
async function installInfo() {
  await ensureDirs();
  const d = dirs();
  let free = null;
  try {
    const s = await fsp.statfs(d.root);
    free = Number(s.bavail) * Number(s.bsize);
  } catch {
    /* старый Node */
  }
  return {
    dir: d.root,
    used: await dirSize(d.root),
    cache: await dirSize(d.cache),
    free,
  };
}

async function setInstallDir(newDirRaw, move) {
  if (installJob || gameProc) throw new Error('Дождитесь окончания установки и закройте игру');
  const old = settings.installDir;
  let target = path.resolve(newDirRaw);
  try {
    const ents = await fsp.readdir(target);
    // Если выбрана чужая непустая папка — создаём внутри неё свою
    if (ents.length && !ents.includes('versions')) target = path.join(target, BRAND.gameName);
  } catch {
    /* папки ещё нет */
  }
  if (path.resolve(target) === path.resolve(old)) return old;
  const rel = path.relative(old, target);
  if (rel && !rel.startsWith('..') && !path.isAbsolute(rel)) {
    throw new Error('Нельзя переносить папку внутрь самой себя');
  }
  await fsp.mkdir(target, { recursive: true });
  await fsp.access(target, fs.constants.W_OK).catch(() => {
    throw new Error('Нет прав на запись в выбранную папку');
  });

  if (move && (await exists(old))) {
    for (const i of await listInstalled()) await dropLink(path.join(i.dir, 'saves')); // ссылки пересоздадутся при запуске
    const parts = ['versions', 'worlds', 'backups', 'skins', 'cache'];
    let i = 0;
    for (const p of parts) {
      emit('install:move', { step: i++, total: parts.length, name: p });
      const src = path.join(old, p);
      if (!(await exists(src))) continue;
      const dst = path.join(target, p);
      if (!(await exists(dst))) {
        try {
          await fsp.rename(src, dst);
          continue;
        } catch (e) {
          if (e.code !== 'EXDEV') throw e;
        }
      }
      await fsp.cp(src, dst, { recursive: true, force: false, errorOnExist: false });
      await rmrf(src);
    }
    emit('install:move', { step: parts.length, total: parts.length, name: '' });
    // Старая папка, если опустела
    await fsp.rmdir(old).catch(() => {});
  }
  settings.installDir = target;
  saveSettings();
  await ensureDirs();
  return target;
}

/* ------------------------------------------------------------------ */
/* Автообновление лаунчера                                             */
/* ------------------------------------------------------------------ */
function configureUpdater() {
  autoUpdater.autoDownload = settings.autoUpdateLauncher;
  autoUpdater.autoInstallOnAppQuit = true;
  autoUpdater.allowPrerelease = false;
  if (!repoConfigured()) return;
  const { owner, name } = repo();
  autoUpdater.setFeedURL({
    provider: 'generic',
    url: `https://github.com/${owner}/${name}/releases/download/${BRAND.launcherTag}`,
  });
}

function bindUpdaterEvents() {
  autoUpdater.on('checking-for-update', () => emit('launcher:update', { state: 'checking' }));
  autoUpdater.on('update-available', (i) =>
    emit('launcher:update', { state: settings.autoUpdateLauncher ? 'downloading' : 'available', version: i.version, percent: 0 })
  );
  autoUpdater.on('update-not-available', () => emit('launcher:update', { state: 'none' }));
  autoUpdater.on('download-progress', (p) => emit('launcher:update', { state: 'downloading', percent: p.percent }));
  autoUpdater.on('update-downloaded', (i) => emit('launcher:update', { state: 'ready', version: i.version }));
  autoUpdater.on('error', (e) => emit('launcher:update', { state: 'error', message: e == null ? 'ошибка' : e.message }));
}

async function checkLauncherUpdate() {
  if (!app.isPackaged) {
    emit('launcher:update', { state: 'dev' });
    return;
  }
  if (!repoConfigured()) {
    emit('launcher:update', { state: 'error', message: 'Репозиторий не настроен' });
    return;
  }
  try {
    await autoUpdater.checkForUpdates();
  } catch (e) {
    emit('launcher:update', { state: 'error', message: e.message });
  }
}

/* ------------------------------------------------------------------ */
/* IPC                                                                 */
/* ------------------------------------------------------------------ */
function handle(channel, fn) {
  ipcMain.handle(channel, async (_e, payload) => {
    try {
      return { ok: true, data: await fn(payload) };
    } catch (e) {
      return { ok: false, error: e && e.message ? e.message : String(e) };
    }
  });
}

function registerIpc() {
  handle('app:info', () => ({
    gameName: BRAND.gameName,
    launcherVersion: app.getVersion(),
    platform: process.platform,
    packaged: app.isPackaged,
    repoConfigured: repoConfigured(),
    repo: repo(),
  }));

  handle('settings:get', () => settings);
  handle('settings:set', (patch) => {
    let repoChanged = false;
    for (const [k, v] of Object.entries(patch || {})) {
      if (!(k in CLEAN)) continue;
      const nv = CLEAN[k](v);
      if (k === 'nickname' && nv.length < 3) continue;
      if (['repoOwner', 'repoName', 'githubToken'].includes(k) && settings[k] !== nv) repoChanged = true;
      settings[k] = nv;
    }
    saveSettings();
    autoUpdater.autoDownload = settings.autoUpdateLauncher;
    if (repoChanged) {
      releasesCache = null;
      configureUpdater();
    }
    return settings;
  });

  handle('versions:list', (force) => getVersionsState(!!force));
  handle('versions:install', ({ tag, replaceTag }) => installVersion(tag, replaceTag));
  handle('versions:cancel', () => {
    if (installJob) installJob.controller.abort();
  });
  handle('versions:remove', (tag) => removeVersion(tag));
  handle('versions:select', (tag) => {
    settings.selectedVersion = tag;
    saveSettings();
  });

  handle('game:play', () => playGame());
  handle('game:state', () => ({ running: !!gameProc, tag: gameProc ? gameProc.tag : null }));

  handle('skins:list', () => listSkins());
  handle('skins:pick', async () => {
    const r = await dialog.showOpenDialog(win, {
      title: 'Выберите скин (PNG 64×64 или 64×32)',
      filters: [{ name: 'PNG', extensions: ['png'] }],
      properties: ['openFile', 'multiSelections'],
    });
    if (r.canceled) return { added: [], errors: [] };
    const added = [];
    const errors = [];
    for (const f of r.filePaths) {
      try {
        added.push(await addSkin(await fsp.readFile(f), path.basename(f)));
      } catch (e) {
        errors.push(`${path.basename(f)}: ${e.message}`);
      }
    }
    return { added, errors };
  });
  handle('skins:add', async ({ name, data }) => addSkin(Buffer.from(data), name));
  handle('skins:select', (id) => {
    settings.activeSkin = id || null;
    saveSettings();
  });
  handle('skins:model', async ({ id, model }) => {
    const idx = await readSkinIndex();
    const s = idx.items.find((x) => x.id === id);
    if (s) {
      s.model = model === 'slim' ? 'slim' : 'classic';
      await writeSkinIndex(idx);
    }
  });
  handle('skins:delete', async (id) => {
    const idx = await readSkinIndex();
    idx.items = idx.items.filter((x) => x.id !== id);
    await writeSkinIndex(idx);
    await fsp.rm(path.join(dirs().skins, id + '.png'), { force: true });
    if (settings.activeSkin === id) {
      settings.activeSkin = null;
      saveSettings();
    }
  });

  handle('worlds:list', () => listWorlds());
  handle('worlds:delete', (a) => deleteWorld(a));
  handle('worlds:transfer', (a) => transferWorld(a));
  handle('worlds:backup', (a) => backupWorld(a));

  handle('install:info', () => installInfo());
  handle('install:choose', async () => {
    const r = await dialog.showOpenDialog(win, {
      title: 'Выберите папку для установки игры',
      properties: ['openDirectory', 'createDirectory'],
      defaultPath: settings.installDir,
    });
    return r.canceled ? null : r.filePaths[0];
  });
  handle('install:setDir', ({ dir, move }) => setInstallDir(dir, !!move));
  handle('install:clearCache', async () => {
    await rmrf(dirs().cache);
    await ensureDirs();
  });

  handle('fs:open', async (what) => {
    const d = dirs();
    await ensureDirs();
    let p = d.root;
    if (what === 'skins') p = d.skins;
    else if (what === 'worlds') p = d.worlds;
    else if (what === 'backups') p = d.backups;
    else if (what && what.startsWith('world:')) {
      const [, tag, slot] = what.split(':');
      p = await worldDir(tag, slot);
    }
    else if (what && what.startsWith('version:')) {
      const inst = (await listInstalled()).find((i) => i.tag === what.slice(8));
      if (inst) p = inst.dir;
    }
    const err = await shell.openPath(p);
    if (err) throw new Error(err);
  });

  handle('launcher:check', () => checkLauncherUpdate());
  handle('launcher:download', () => autoUpdater.downloadUpdate());
  handle('launcher:install', () => autoUpdater.quitAndInstall(true, true)); // тихо, без окна установщика, и снова запустить

  ipcMain.on('win:min', () => win && win.minimize());
  ipcMain.on('win:max', () => win && (win.isMaximized() ? win.unmaximize() : win.maximize()));
  ipcMain.on('win:close', () => win && win.close());
}

/* ------------------------------------------------------------------ */
/* Окно                                                                */
/* ------------------------------------------------------------------ */
function createWindow() {
  win = new BrowserWindow({
    width: 1120,
    height: 700,
    minWidth: 980,
    minHeight: 640,
    frame: false,
    show: false,
    backgroundColor: '#14110d',
    icon: path.join(__dirname, 'build', 'icon.png'),
    title: BRAND.gameName + ' Launcher',
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });
  win.once('ready-to-show', () => win.show());
  win.loadFile(path.join(__dirname, 'renderer', 'index.html'));
  win.webContents.setWindowOpenHandler(({ url }) => {
    if (/^https:\/\//.test(url)) shell.openExternal(url);
    return { action: 'deny' };
  });
  win.webContents.on('will-navigate', (e) => e.preventDefault());
  win.on('closed', () => {
    win = null;
  });
}

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.on('second-instance', () => {
    if (win) {
      if (win.isMinimized()) win.restore();
      win.focus();
    }
  });

  app.whenReady().then(() => {
    loadSettings();
    registerIpc();
    configureUpdater();
    bindUpdaterEvents();
    createWindow();
    win.webContents.once('did-finish-load', () => {
      if (settings.checkUpdatesOnStart) setTimeout(checkLauncherUpdate, 1500);
    });
    // Раз в час проверяем обновление лаунчера
    setInterval(() => {
      if (settings.checkUpdatesOnStart) checkLauncherUpdate();
    }, 60 * 60 * 1000);
  });

  app.on('window-all-closed', () => app.quit());
}
