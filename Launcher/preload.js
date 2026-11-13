'use strict';
const { contextBridge, ipcRenderer } = require('electron');

const EVENTS = new Set(['install:progress', 'install:move', 'game:state', 'launcher:update']);

contextBridge.exposeInMainWorld('api', {
  // Вызов обработчика в main. Ошибки приходят как обычные исключения.
  async call(channel, payload) {
    const r = await ipcRenderer.invoke(channel, payload);
    if (!r || !r.ok) throw new Error((r && r.error) || 'Неизвестная ошибка');
    return r.data;
  },
  // Односторонние команды окну
  send(channel) {
    if (['win:min', 'win:max', 'win:close'].includes(channel)) ipcRenderer.send(channel);
  },
  // Подписка на события; возвращает функцию отписки
  on(channel, cb) {
    if (!EVENTS.has(channel)) return () => {};
    const fn = (_e, data) => cb(data);
    ipcRenderer.on(channel, fn);
    return () => ipcRenderer.removeListener(channel, fn);
  },
});
