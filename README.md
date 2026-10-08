# DIYLevel Sorting

独立 BepInEx 扩展，在 OC2DIYLevel 的 DLC“更多关卡”菜单和包内关卡菜单右侧固定显示两个真正展开的下拉列表：排序方法（名称 / 添加时间）、排序方向（正序 / 倒序）。控件位于关卡列表外，不占用列表条目，也不随列表滚动。点击过的关卡在下一次刷新或打开列表时显示绿色圆点；包含点击记录的关卡包也显示圆点。

## 安装和使用

需要 Overcooked! 2、BepInEx 5 和 OC2DIYLevel。DIYLevelFastInit 可选。本机验证基线为 DIYLevel 0.10.0、FastInit 1.4.0、BepInEx 5.4.23.5、Unity 2017 / .NET Framework 3.5。

1. 关闭游戏，把构建的 `bin/Release/OC2DIYLevelSorting.dll` 放入游戏的 `BepInEx/plugins/`。
2. 打开 DLC 的“更多关卡”，展开“排序方法”和“排序方向”选择选项。键鼠可点击，游戏原生提交操作展开/选择，取消操作关闭下拉并返回原控件。
3. 选择会保存到 `BepInEx/config/<PluginGuid>.cfg`，文件名由 `src/SortingPlugin.cs` 中的 `PluginGuid` 决定。点击记录保存在该文件的 `History.ClickedLevels`；清空此项可以清除圆点记录。

仅需要本扩展的一个 DLL；不覆盖 DIYLevel、FastInit 或游戏 DLL。CustomArcade 建房设置中的主题 selector 仍按原业务索引工作。

## 时间、排序和记录语义

- 添加时间采用用户指定的本地文件创建时间：包目录中 DIYLevel 选用的 `info*` 元数据资源文件的 `CreationTimeUtc`。包内关卡共用包时间。复制、解压或还原文件可能改变或保留该时间；它不代表关卡发布时间。文件缺失或读取失败时采用固定最小时间并输出诊断。
- 名称按菜单当前语言排序，中英文使用相应文化的忽略大小写比较，不采用自然数字排序。相同名称或时间以稳定身份升序打破平局，正倒序切换不改变同值条目的身份顺序。
- 稳定身份优先采用包的 `baseUID`，再用 `uid`，最后使用相对包目录；关卡身份再加入原 `sceneName` 和同场景名在包内的出现序号。名称和语言不影响点击身份。baseUID 保持不变时，版本更新和目录改名保留记录；仅有 uid 时版本更新可能丢失匹配，仅有目录时改名会失去匹配。作者重复使用相同 baseUID 的资源视为同一逻辑包；同场景条目的出现次序改变会影响重复场景的对应关系。
- 记录表示点击过，不表示成功载入、通关或联网同步。记录只在状态变化时保存，不覆盖原按钮的其他监听器。只有本机点击会写入本机历史。
- 追加/删除事件在 0.15 秒窗口内合并刷新。没有待处理变化时不扫描 UI 或排序；不提前读取资源包，不修改 `levelSetInfos`、关卡数组、业务索引、场景名或网络协议。FastInit 刷新按钮保留首位和原 Loading 禁用状态。

## 构建和验证

在 PowerShell 中运行：

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\test-unity.ps1
.\scripts\test-unity.ps1 -WithoutFastInit
```

脚本可通过 `-GameRoot` 和 `-MSBuild` 指定本机路径（行为测试只需要 `-MSBuild`）。构建使用原游戏 DLL 的真实访问级别，禁用构建前后事件；不会自动部署发布产物。

行为测试直接执行生产排序、身份、历史及刷新门控代码。Unity 测试使用单独的测试插件，在实际游戏进程和原菜单中验证；在最后的 `OnLevelSelected` 入口观察原场景参数并阻止进入厨房，所以不能替代实际厨房加载或多人联机测试。测试通过原生事件处理器注入提交、方向和鼠标点击事件，并使用真实 UI raycaster 检查选项命中；脚本导航阶段临时关闭物理鼠标输入，避免桌面操作干扰焦点断言，因此不能当作物理手柄实测。

Unity 测试拒绝干预已运行的玩家游戏。脚本备份临时部署涉及的 DLL、配置和日志；无 FastInit 测试临时移出其 DLL，结束后恢复并校验哈希。只允许停止命令行含本次唯一测试目录的测试进程。日志、截图和备份保存在被 Git 忽略的 `.validation/`。如果运行脚本的终端被强制结束，请根据该目录的 `manifest.json` 和 `backup/` 恢复文件。

Steam 测试通过客户端 `-applaunch 728880` 启动，避开直接启动游戏后 `steam://run` 携带参数的确认提示；保留的窗口、日志和唯一输出目录参数仅用于临时验证，不写入 Steam 游戏属性。日常从 Steam 库启动游戏即可，插件不需要启动参数。

测试插件会自动选择排序选项、打开下拉和刷新菜单，这些是验证操作。FastInit 初次加载时只切换一次，随后逐帧检查两个控件的对象、文字和选择保持不变；正式使用只安装 `OC2DIYLevelSorting.dll`。

具体已测结果及未验收项见 [GOAL.md](GOAL.md)。不提交游戏引用 DLL、构建产物、用户配置或反编译源码。
