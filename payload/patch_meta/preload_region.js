//#region custom-font-mod
try {
  contextBridge.exposeInMainWorld('undertoneFontAPI', {
    getSystemFonts: () => ipcRenderer.invoke('get-system-fonts')
  });
} catch (e) {}
//#endregion custom-font-mod
