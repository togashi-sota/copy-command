// 【完成版・2026-10-20】セーブ・プロフィールを書いたあと、ブラウザの保存領域（IndexedDB）へ同期する。
mergeInto(LibraryManager.library, {
  CopyCommand_SyncFiles: function () {
    if (typeof FS !== 'undefined' && FS.syncfs) {
      FS.syncfs(false, function (err) { if (err) console.warn('[WebStorage] syncfs', err); });
    }
  }
});
