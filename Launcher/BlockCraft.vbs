' Запуск лаунчера без окна консоли (двойной щелчок).
' Если зависимости ещё не установлены, один раз откроется start.bat для установки.
Set fso = CreateObject("Scripting.FileSystemObject")
Set sh = CreateObject("WScript.Shell")
dir = fso.GetParentFolderName(WScript.ScriptFullName)
exe = dir & "\node_modules\electron\dist\electron.exe"
sh.CurrentDirectory = dir
If fso.FileExists(exe) Then
  sh.Run """" & exe & """ .", 1, False
Else
  sh.Run """" & dir & "\start.bat""", 1, False
End If
