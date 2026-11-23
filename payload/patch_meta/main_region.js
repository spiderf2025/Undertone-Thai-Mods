//#region custom-font-mod
(function () {
  if (globalThis.__customFontModRegistered) return;
  globalThis.__customFontModRegistered = true;
  const { execFile } = require('child_process');
  let _cachedFonts = null;
  ipcMain.handle('get-system-fonts', () => new Promise((resolve) => {
    if (_cachedFonts) return resolve(_cachedFonts);
    const psScript = `
      [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
      $set = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
      try {
        Add-Type -AssemblyName System.Drawing
        $col = New-Object System.Drawing.Text.InstalledFontCollection
        foreach ($f in $col.Families) {
          if ($f.Name) { [void]$set.Add($f.Name.Trim()) }
        }
      } catch {}
      if ($set.Count -eq 0) {
        foreach ($regPath in @(
          'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts',
          'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts'
        )) {
          try {
            $props = Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue
            if ($props) {
              $props.PSObject.Properties | Where-Object { $_.Name -notmatch '^PS' } | ForEach-Object {
                $n = ($_.Name -replace '\s*\(.*?\)\s*$', '').Trim()
                if ($n) { [void]$set.Add($n) }
              }
            }
          } catch {}
        }
      }
      $arr = @($set | Sort-Object)
      $json = ConvertTo-Json -InputObject $arr -Compress
      [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($json))
    `.trim();
    execFile(
      'powershell.exe',
      ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', psScript],
      { windowsHide: true, maxBuffer: 1024 * 1024 * 8 },
      (err, stdout) => {
        if (err || !stdout) return resolve([]);
        try {
          const decoded = Buffer.from(stdout.trim(), 'base64').toString('utf8');
          const parsed = JSON.parse(decoded);
          _cachedFonts = Array.isArray(parsed) ? parsed : [];
          resolve(_cachedFonts);
        } catch {
          resolve([]);
        }
      }
    );
  }));
  app.on('window-all-closed', () => {
    if (process.platform !== 'darwin') setTimeout(() => app.exit(0), 150);
  });
  app.on('will-quit', () => {
    setTimeout(() => process.exit(0), 100);
  });
})();
//#endregion custom-font-mod
