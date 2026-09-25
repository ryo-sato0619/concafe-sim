// WebGL では persistentDataPath への書き込みがメモリ上の IndexedDB キャッシュに
// 留まるため、明示的に同期しないとタブを閉じた時点でセーブが消える。
// FileSaveStorage.Write / Delete から呼ぶ。
mergeInto(LibraryManager.library, {
  ConcafeSyncFs: function () {
    FS.syncfs(false, function (err) {
      if (err) console.error('セーブの同期に失敗した', err);
    });
  },
});
