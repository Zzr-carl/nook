<div align="right">

[简体中文](README.md) · [English](README.en.md)

</div>

# Nook

把桌面图标收进抽屉，从屏幕顶端垂下来。

屏幕顶边常驻一根极细的轨道，每根刻度是一个抽屉。鼠标推上去停一下，抽屉就垂下来；移开就收回去。

## 为什么写这个

桌面图标堆到 90 多个的时候我受不了了。删舍不得，市面上的整理工具又全是帮我建文件夹的。所以 Nook 把图标分组收进"抽屉"，桌面只留壁纸，要用了从顶边拉下来。

它不隐藏图标，是真的把文件搬进抽屉——同一个磁盘卷内就是移动文件，几乎瞬间完成。原路径记在配置里，随时一键还原回桌面。

## 怎么用

- 鼠标推到屏幕最顶边，轨道出现
- 停在刻度上会先浮一张小卡，显示抽屉名和里面的图标；停半秒抽屉垂下来，直接点刻度则是立刻开
- 按住刻度左右拖，能调整抽屉顺序
- 把文件拖到面板上，就收进当前抽屉
- 右键面板或点 `···` 管理单个抽屉：重命名、图标大小、还原到桌面、备份

托盘图标里有抽屉列表、主题色、备份恢复、开机自启这些开关，几个值得一说的：

- **主题色**——九个预设加自定义取色，刻度、强调条、边框全部跟着换，配壁纸用
- **实时玻璃背景**——面板背后是真·桌面模糊，桌面动了它也动。代价是面板对截图录屏隐形，不然它会拍到面板自己
- **悬停预览**——不用展开就知道抽屉里装了啥
- **Steam 封面**——游戏图标自动从 Steam 抓，比系统图标好看
- **图标快照**——文件挪走后图标不会变白
- **全屏让位**——打游戏时轨道躲起来；最大化的时候顶边停一下还能临时叫出来
- **收起方式**——移开就收，或者点别处才收，托盘里选
- **图标常驻内存**——第二次开抽屉不卡
- **备份**——一键打包，恢复前还会自动再存一份

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
Nook.exe --perf "抽屉名"                                    # 同一个进程连开两次，比较冷热耗时
Nook.exe --reorder "抽屉名" ±1                              # 调整顺序并打印新次序
Nook.exe --selftest                                         # 验证"搬进抽屉/搬回桌面"
Nook.exe --backup path.zip / --restore path.zip
Nook.exe --autostart on|off
```

日志在 `%TEMP%\nook.log`。

## 字体

界面字体用 [Sora](https://github.com/sora-xor/sora-font) 和 [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono)，都是 SIL OFL 1.1，许可证在 `Assets/Fonts/licenses/`。

## 许可证

[MIT](LICENSE)，随便用。
