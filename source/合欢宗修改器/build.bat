@echo off
rem 用 Windows 自带的 .NET Framework 4 编译器编译 合欢宗修改器.exe
chcp 65001 >nul
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
cd /d "%~dp0"
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /main:LongYinMod.Program /out:合欢宗修改器.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs
if errorlevel 1 (echo 编译失败 & pause & exit /b 1)
rem 存档修改器 exe：内嵌网页模板（..\网页存档修改器 下运行 python build.py --template 和 python build_slot.py --template 生成）
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /main:LongYinMod.EditorHost /out:龙胤立志传存档修改器.exe /resource:..\网页存档修改器\save_editor_template.html,editor.html /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs
if errorlevel 1 (echo 编译失败 & pause & exit /b 1)
echo 编译完成：合欢宗修改器.exe、龙胤立志传存档修改器.exe
pause
