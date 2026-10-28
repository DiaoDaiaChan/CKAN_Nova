CKAN UI {{VERSION}} —— Windows x64 {{FLAVOR_NAME}}（未签名）
============================================================

一、这是什么
  CKAN UI 是 KSP 模组管理器 CKAN 的新界面外壳：界面用 Avalonia 重写，
  模组数据、解析与安装逻辑复用 CKAN Core。本包是"解压即用"的便携版，
  不写注册表、不需要安装程序、删除目录即卸载。

二、怎么用
  1. 把整个压缩包解压到任意目录（建议路径不含空格，例如 D:\Games\CkanUI）。
  2. 双击 CkanUI.App.exe 启动。
  3. {{RUNTIME_NOTE}}
  4. 首次启动会生成配置文件；之后可在界面里选择 / 切换 KSP 实例。

三、目录里有什么
  CkanUI.App.exe            主程序
  *.dll                     Avalonia、CKAN Core、log4net 等依赖
  log4net.xml               日志配置；程序只在启动时读一次，改完需重启
  i18n\packs\*.json         内置界面语言包（en-US / zh-CN）
  Assets\ckan.ico           应用图标
  licenses\CKAN-LICENSE.md  CKAN Core 的许可证原文
  THIRD-PARTY-NOTICES.txt   第三方组件与许可证清单
  README.txt                本文件

四、配置与日志放在哪
  配置：%LocalAppData%\CKAN\config.json
        （若该目录不可写，程序会退回本程序目录下的 config.json，并在日志里告警）
  日志：%LocalAppData%\CKAN\logs\ 下的滚动日志
  卸载：删掉解压出来的整个目录即可；用户配置按上面的路径手动删除。

五、体积说明
  {{SIZE_NOTE}}

六、这是未签名版本
  本包没有代码签名证书，Windows SmartScreen 可能提示"未知发布者"。
  下载后请先核对 SHA256 再运行：
    文件：<压缩包文件名>
    命令：certutil -hashfile <压缩包文件名> SHA256
  与发布页 SHA256SUMS.txt 里的值逐字比对，不一致就不要运行。

七、遇到问题
  先看日志目录里最新的那个文件；提 issue 时附上日志与 Windows 版本。
