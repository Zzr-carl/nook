# Nook

把桌面图标收进抽屉，从屏幕顶端垂下来。

一个常驻屏幕顶边的极细轨道，每根刻度是一个抽屉。鼠标推上去、停一下，抽屉从轨道下沿垂落；移开就收回。

## 是什么

Windows 桌面上的图标越堆越多，快捷键又舍不得删。Nook 把它们分组收进"抽屉"，桌面只留一张干净壁纸，需要时从屏幕顶端拉下来。

**它不隐藏桌面图标，而是真的把文件移进抽屉**（同一磁盘卷内的 `.nook-items` 目录），所以几乎瞬间完成，也不怕跨卷移动卡住。原路径记在配置里，随时可以一键还原回桌面。

## 用法

- 鼠标推到屏幕最顶边 → 轨道出现
- 停在某根刻度上约 0.3 秒 → 对应的抽屉垂下来
- 移开 → 抽屉收回，轨道留着
- 把文件拖到面板上 → 收进当前抽屉
- 右键面板 / 点标题右侧的 `···` → 管理菜单（重命名、图标大小、还原到桌面、备份…）

托盘图标里有：抽屉列表、备份与恢复、开机自启、悬停是否展开、轨道是否常驻、始终置顶。

## 特性

- **全屏时自动让位**：检测到前台是独占全屏（游戏、播放器）就收起，退出全屏后恢复
- **图标快照**：添加时就抓一份 PNG，原文件挪走后图标也不会变白
- **Steam 游戏**：从 Steam 库和 CDN 取封面图，比系统图标好看得多
- **玻璃质感**：面板背景是实时抓取的屏幕模糊，不是半透明白
- **备份**：整个抽屉结构 + 图标打包成一个 zip，恢复前会自动再存一份当前状态

## 构建

需要 .NET 9 SDK（Windows）。

```bash
dotnet build -c Release
dotnet run -c Release
```

发布单文件：

```bash
# 依赖运行时，约 1 MB
dotnet publish -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/dep

# 自带运行时，约 146 MB，拷到任何 Win10/11 x64 都能跑
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/portable
```

## 数据在哪

- 配置：`%APPDATA%\Nook\drawers.json`
- 图标快照：`%APPDATA%\Nook\icons\`
- 备份：`%APPDATA%\Nook\backups\`
- 抽屉里的文件：源文件所在磁盘卷下的 `.nook-items\`

删除程序不会动这些文件。抽屉里的项目会留在原处，需要时手动移回。

## 调试入口

打包进程序里的几个自检开关，开发时不用盯着屏幕：

```bash
Nook.exe --capture out.png "抽屉名" [small|medium|large]   # 离屏渲染面板为 PNG
Nook.exe --uicheck                                          # 驱动展开/锚定/收回，结果写日志
Nook.exe --selftest                                         # 验证"搬进抽屉/搬回桌面"
Nook.exe --backup path.zip / --restore path.zip
Nook.exe --autostart on|off
```

日志在 `%TEMP%\nook.log`。

## 字体

界面字体为 [Sora](https://github.com/sora-xor/sora-font) 与 [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono)，均为 SIL Open Font License 1.1，许可证见 `Assets/Fonts/licenses/`。
