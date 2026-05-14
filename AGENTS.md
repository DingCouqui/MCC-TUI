# AGENTS.md

## 项目目标
构建一个极简 TUI 启动器（最小可行模型），用于从 `config` 目录中选择 `.ini` 配置文件并启动 Minecraft-Console-Client（MCC）。该 TUI 为独立可执行文件，与 `MinecraftClient.exe` 置于同一目录，双击即可运行。启动 MCC 后 TUI 保持运行，不自动退出。

## 技术选型
- **语言**：C#（与 MCC 一致，可直接复用其配置逻辑，且团队学习成本最低）
- **TUI 框架**：[Terminal.Gui](https://github.com/gui-cs/Terminal.Gui)（类似 WinForms 的事件驱动模型，提供标签、按钮、列表等控件）
- **构建与发布**：.NET SDK 8.0 + `dotnet CLI`，最终通过 `dotnet publish` 生成单文件自包含可执行程序
- **输出名称**：`MCC-TUI.exe`
- **命名空间**：`MccTui`
- **版本控制**：Git，远程仓库 `https://github.com/DingCouqui/MCC-TUI`（main 分支）

## 功能需求
1. **主界面第一行**：显示 `[管理] [+]`，该行称为"一级标签页"。
   - `[管理]` 为可获取焦点的标签。
   - `[+]` 为可获取焦点的按钮。
2. **焦点导航**：
   - 使用左右方向键在 `[管理]` 和 `[+]` 之间切换焦点。
   - 在主界面按 ESC 不会退出程序。
3. **交互流程**：
   - 用户按 `Enter` 点击 `[+]`。
   - 在按钮下方动态显示一个文件列表，列出与 `MinecraftClient.exe` 同级 `config` 目录下的所有 `.ini` 文件。
   - 列表第一列为文件名称（不含路径），左对齐。
   - 用户可使用上下方向键移动光标选择文件，按 `Enter` 确认。
   - 按 `ESC` 或 `Backspace` 可返回主界面焦点。
4. **启动 MCC**：
   - 根据选中的 `.ini` 文件，拼接启动参数作为第一个位置参数传入 `"config/选中文件.ini"`（MCC 通过 `args[0]` 作为配置文件路径，非 `--config=` 选项）。
   - 使用 `System.Diagnostics.Process.Start` 启动同目录下的 `MinecraftClient.exe`（或 Linux/macOS 下的 `MinecraftClient`）。
   - **启动后 TUI 保持运行，不关闭。用户可继续操作或手动关闭 TUI。**
5. **实例标签页**：
   - 每通过 `[+]` 打开一个 MCC 窗口，在一级标签页中 `[+]` 左侧自动新增一个实例标签。
   - **命名规则**：取配置文件名去除 `.ini` 后缀，格式为 `[文件名]`。例如使用 `Couqui@BC.ini` 则标签为 `[Couqui@BC]`。
   - 实例标签与 `[管理]`、`[+]` 之间通过左右方向键自由切换焦点。
   - 同一配置文件可重复打开，此时会新增同名标签。
6. **实例实时输出**：
   - 光标移动至某实例标签页时按 `Enter`，下方内容区域实时显示该 MCC 实例的 stdout/stderr 输出。
   - 输出行缓冲上限 10,000 行，超出时自动移除旧行。
   - 新输出到达时自动滚动到底部。
   - 进程退出后输出区域追加退出提示。
   - 按 `ESC` 可从输出视图返回标签页焦点。
7. **输出视图交互**：
   - **鼠标拖拽框选文本**，选区内颜色反转（前景↔背景交换）。
   - **鼠标单击（无拖拽）清除选区**。
   - **Ctrl+C 复制选中文本**到剪贴板（剥离 Minecraft 颜色码 `§`）。
   - **ESC 退出输出视图，返回标签页焦点**。

## 多语言支持
- **配置文件**：`MCC-TUI.yml`，位于 exe 同目录的 `MCC-TUI-config/` 子目录下，通过 `language` 字段切换：
  ```yaml
  language: zh_cn   # 中文
  language: en_us   # 英文
  ```
- **语言文件**：`lang/zh_cn.yml` 和 `lang/en_us.yml`，YAML 格式键值对存储
- **实现**：`LocalizationManager` 在启动时读取配置并加载对应语言文件，通过 `L("key")` 便捷方法获取字符串
- **回退**：配置文件缺失或无效时默认使用 `zh_cn`

## 架构约束
- **子进程启动**：MCC 通过 P/Invoke `CreateProcess` + `CREATE_NEW_CONSOLE` + `SW_SHOWMINNOACTIVE` 启动。`CREATE_NEW_CONSOLE` 给 MCC 独立控制台（`ClassicConsoleBackend` 的 `BufferWidth` / `ReadConsoleInput` 等 API 均能正常工作），`SW_SHOWMINNOACTIVE` 让新窗口**最小化且不抢焦点**（TUI 保持响应用户输入）。stdout/stderr 通过 `cmd /c "... > tempFile 2>&1"` 重定向到临时文件，TUI 在后台线程中**增量读取 tempFile** 捕获输出。**不使用 .NET Process.Start 的 RedirectStandardOutput** 管道重定向（见已知问题：MCC 连接失败）。stdin 不做重定向，子进程自动继承独立控制台的输入句柄，不与 TUI 的 NetDriver 竞争 `ReadConsoleInput()`。
- **后台线程**：MCC 实例的 tempFile 读取和进程退出监视跑在 `Task.Factory.StartNew(..., LongRunning)` 专用线程上，不消耗 ThreadPool。轮询延迟用 `await Task.Delay(100)` 非阻塞等待，避免 `Thread.Sleep` 占住线程。
- **MCC 启动参数**：仅传入配置文件路径作为第一个位置参数（`MinecraftClient.exe "config/xxx.ini"`），**不传 `BasicIO` 标志**。MCC 使用默认 `ClassicConsoleBackend`，其内部最终调用 `System.Console.WriteLine()`，重定向仍能正常捕获输出。
- **线程模型**：`OutputDataReceived` / `ErrorDataReceived` / `Exited` 事件在 ThreadPool 线程触发。输出通过 `ConcurrentQueue` 暂存。**轮询方式**：使用 `System.Threading.Timer`（后台线程） + `Application.Invoke`（调度到 UI 主线程），每 200ms 批量消费 `ConcurrentQueue` 并更新 UI。**不使用 `Application.AddTimeout`**（见已知问题）。
- **文件系统依赖**：假设启动器所在目录即为 MCC 根目录，包含 `MinecraftClient.exe` 和 `config` 子文件夹。
- **MCC 命令行接口**：MCC 通过 `args[0]` 接收配置文件路径作为第一个位置参数（`MinecraftClient.exe "config/xxx.ini"`），并非 `--config=` 选项。经查 MCC `Program.cs` 源码确认（`mcc\Minecraft-Console-Client-master\MinecraftClient\Program.cs:148`）。
- **输出净化**：MCC 输出的 ANSI 转义序列（`\x1b[...m`）在入队前通过 `GeneratedRegex` 剥离；`§` Minecraft 颜色码保留，由 `ColoredOutputView` 渲染为彩色文本。

## 已知问题与注意事项

### 中文/CJK 字符显示乱码

**现象**：使用 Terminal.Gui v2 默认的 `WindowsDriver` 时，中文字符后方出现乱码字符。

**原因**：`WindowsDriver` 直接操作 Windows 控制台缓冲区，对 CJK 宽字符（双列宽）渲染存在兼容性问题。

**解决方案**：
1. 在 `Application.Init()` 之前设置控制台编码为 UTF-8：
   ```csharp
   Console.OutputEncoding = System.Text.Encoding.UTF8;
   Console.InputEncoding = System.Text.Encoding.UTF8;
   ```
2. 使用 `NetDriver` 替代默认的 `WindowsDriver`：
   ```csharp
   Application.Init(driverName: "NetDriver");
   ```

**Tips**：有效的驱动名称完整列表：`CursesDriver`、`FakeDriver`、`NetDriver`、`WindowsDriver`。名称必须精确匹配（如 `"NetDriver"` 而非 `"Net"`）。

### JetBrains.Annotations 程序集缺失

**现象**：单文件发布后运行报 `FileNotFoundException: JetBrains.Annotations`。

**原因**：Terminal.Gui 2.0.0-pre.1802 内部引用 `JetBrains.Annotations`，单文件发布裁剪时未包含。

**解决方案**：显式添加包引用：
```bash
dotnet add package JetBrains.Annotations
```

### `Application.AddTimeout` 阻断 NetDriver 键盘输入

**现象**：使用 `Application.AddTimeout(50ms, callback)` 后，仅第一次按键生效，后续按键全部无反应。

**原因**：Terminal.Gui v2 pre-release 中，`AddTimeout` 的定时回调在主循环中执行，与 NetDriver 的输入轮询存在竞争，导致键盘事件管道中断。

**解决方案**：改用 `System.Threading.Timer`（后台线程） + `Application.Invoke`（调度到 UI 线程）：
```csharp
var pollTimer = new System.Threading.Timer(_ =>
{
    Application.Invoke(() => PollOutputOnIdle());
}, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(200));
```
定时器跑在后台线程，不干扰 NetDriver 的事件轮询。

### `ListView` 渲染 `String.Substring(-1)` 崩溃

**现象**：使用 `ListView` 显示 MCC 输出时，`Terminal.Gui.ListWrapper.RenderUstr` 抛出 `ArgumentOutOfRangeException: startIndex ('-1')`。

**原因**：Terminal.Gui v2 pre-release 的 `ListView.ListWrapper` 渲染器在处理 CJK 宽字符或特定文本内容时，子串偏移计算产生负数索引。即使剥离 `§` 颜色码和 ANSI 转义序列后，`ListView` 仍可能崩溃。

**解决方案**：放弃 `ListView`，创建自定义 `ColoredOutputView : View`，覆写 `OnDrawContent`，使用 `ConsoleDriver.AddRune/AddStr` 逐字符直接绘制。同时以此实现 `§` → 彩色渲染。

### MCC `BasicIO` 模式导致 `NullReferenceException`

**现象**：MCC 以 `BasicIO` 参数启动后，输出 `Initialization failed.` 退出。

**原因**：`BasicIO` 模式下 `ConsoleIO.Backend` 为 null，`McClient.StartConsoleSession()` 无条件调用 `ConsoleIO.Backend.BeginReadThread()` 触发 NPE。MCC 未实现 `BasicConsoleBackend`。

**解决方案**：**不传 `BasicIO` 参数**。MCC 使用默认 `ClassicConsoleBackend`，其内部最终通过 `System.Console.WriteLine()` 输出，重定向仍能捕获。

### `MouseFlags` 组合标志不匹配

**现象**：鼠标拖拽框选时不动态显示选区，仅在释放时更新。

**原因**：NetDriver 上报鼠标拖拽事件时，`MouseFlags` 同时置起多个位（如 `Button1Pressed | ReportMousePosition`）。代码使用精确相等 `flags == MouseFlags.ReportMousePosition` 无法匹配组合标志。

**解决方案**：使用按位掩码 `(flags & MouseFlags.ReportMousePosition) != 0` 替代精确相等比较。

### Ctrl+C 复制无效 + 拖拽状态卡死

**现象**：
1. 在 `ColoredOutputView` 中按 Ctrl+C 复制选中文本无效
2. 按 Ctrl+C 后无法再通过鼠标框选文字——后续点击均被忽略，输出 `unhandled flags=Button1Pressed`

**原因**（两个独立问题叠加）：

1. **键盘层面**：Windows 控制台默认 `TreatControlCAsInput = false`，Ctrl+C 被操作系统捕获为 SIGINT 信号，不进入 `Console.ReadKey()` 输出流。NetDriver 使用 `Console.ReadKey()` 获取输入，因此从未收到 Ctrl+C 按键事件，`ColoredOutputView.OnKeyDown` 中的 `Key.C.WithCtrl` 分支永远不触发。

2. **状态机层面**：修复问题 1 后，Ctrl+C 能到达 `OnKeyDown`，但 handler 只执行 `Clipboard.TrySetClipboardData()`，未重置 `_isDragging = false`。当用户在拖拽过程中按下 Ctrl+C（鼠标按钮可能仍按住，或 Button1Released 事件因 Ctrl 修饰键干扰而丢失），`_isDragging` 保持 `true`。后续 `OnMouseEvent` 中 `Button1Pressed` 检查 `!_isDragging` 失败，新选区无法开始。

**解决方案**：

1. 在 `Program.cs` 的 `Application.Init()` 之前设置：
   ```csharp
   Console.TreatControlCAsInput = true;
   ```
   确保 Ctrl+C 作为键盘事件进入 NetDriver 输入流。

2. 在 `ColoredOutputView.OnKeyDown` 的 Ctrl+C 分支中，`Clipboard.TrySetClipboardData` 之后添加：
   ```csharp
   _isDragging = false;
   ```
   确保复制操作后拖拽状态机复位，后续鼠标选区正常。

### MCC `RedirectStandardOutput` 导致连接失败 + 多实例键盘失效

**现象**：
1. 通过 TUI 启动 MCC 后，`[MCC] 失去连接`，`TimeoutDetector` 触发，随后退出清理时 `Console.get_BufferWidth()` 崩溃（`IOException: 句柄无效`）
2. 单实例正常，启动第 2 个 MCC 后 TUI 键盘/鼠标事件全部失效

**原因**（多层问题叠加）：

1. **管道重定向覆盖 STD_OUTPUT_HANDLE**：`.NET Process.Start` 的 `RedirectStandardOutput = true` 设置 `STARTF_USESTDHANDLES`，将子进程 `STD_OUTPUT_HANDLE` 替换为管道句柄。`ClassicConsoleBackend` 退出时调用 `Console.get_BufferWidth()` → `GetStdHandle(STD_OUTPUT_HANDLE)` 拿到的不是控制台句柄 → `IOException`。`CreateNoWindow` 参数无法解决此问题。

2. **共享控制台导致输入竞争**：TUI 使用 `NetDriver` 通过 `ReadConsoleInput()` 获取键盘事件。如果 MCC 与 TUI **共享同一个控制台**（无 `CREATE_NEW_CONSOLE`），MCC 的 `ClassicConsoleBackend` 读线程也会调用 `ReadConsoleInput()`。两个进程从同一输入队列消费事件 → 用户按键被 MCC 抢走 → TUI 收不到 → UI 操作全部失效。

3. **新控制台窗口抢焦点**：修复问题 2 时加入 `CREATE_NEW_CONSOLE`，MCC 获得独立控制台，但 Windows 默认将新窗口置于前台 → TUI 失去焦点 → 键盘事件仍无法到达 TUI。

4. **ThreadPool 线程耗尽**：每个 MCC 实例消耗 2 个 `Task.Run()` 线程（`ReadOutputFile` + `WaitForSingleObject`），线程内部使用 `Thread.Sleep` 阻塞等待。ThreadPool 默认最小线程数有限，多实例时可能耗尽 → 定时器回调 (`System.Threading.Timer`) 无法按时执行 → 输出轮询中断。

**解决方案**：

1. 用 **P/Invoke `CreateProcess`** 替代 `.NET Process.Start`，设置 `CREATE_NEW_CONSOLE`（独立控制台 = 不竞争 `ReadConsoleInput()`）+ `STARTF_USESHOWWINDOW` + `SW_SHOWMINNOACTIVE`(7)（窗口最小化且不激活 = TUI 不失焦）：
   ```csharp
   si.dwFlags = (int)STARTF_USESHOWWINDOW;
   si.wShowWindow = SW_SHOWMINNOACTIVE;
   uint flags = NORMAL_PRIORITY_CLASS | CREATE_NEW_CONSOLE;
   ```

2. 用 **`cmd /c "... > tempFile 2>&1"`** 替代管道重定向，将 MCC 输出重定向到临时文件。TUI 在后台线程中增量读取 tempFile：
   ```csharp
   var cmdLine = new StringBuilder(
       $"cmd /c \"\"{exePath}\" {arguments} > \"{tempFile}\" 2>&1\"", 4096);
   ```

3. 使用 **`Task.Factory.StartNew(..., TaskCreationOptions.LongRunning)`** 替代 `Task.Run()`，避免消耗 ThreadPool；轮询用 **`await Task.Delay(100)`** 替代 `Thread.Sleep(100)`，非阻塞释放线程。

4. 在输出处理中加入 **`EnqueueFiltered`** 过滤 `IOException` 崩溃堆栈（`BufferWidth` 仍会因 `STARTF_USESTDHANDLES` 而在退出时失败，但崩溃输出被抑制）。

### ESC 键盘路由丢失

**现象**：在 `ColoredOutputView` 中按 ESC 无反应，无法退出输出视图。

**原因**：`ColoredOutputView.OnKeyDown` 仅在有键盘焦点时触发。鼠标交互后焦点可能不在该 View 上，ESC 被 `Window.KeyDown` 无条件拦截（`e.Handled = true`），无法路由到退出逻辑。

**解决方案**：**ESC 全权由窗口层 `Window.KeyDown` 做优先级分发**：
```csharp
if (e == Key.Esc)
{
    if (_instanceOutputFrame.Visible)   { ExitInstanceView(); }  // ① 退出输出视图
    else if (_fileListFrame.Visible)    { 隐藏文件列表; }        // ② 关闭文件列表
    else { e.Handled = true; }                                   // ③ 阻止退出程序
}
```
子控件不再各自处理 ESC，统一由窗口层路由。Backspace 在文件列表中保留作为退出键，在输出视图中预留为未来文本输入退格键。

### 输出文本选区设计

**核心设计**：`ColoredOutputView` 自定义绘制，选区通过**可视列坐标**管理。

| 概念 | 说明 |
|---|---|
| 可视列 | 文本中剥离 `§` 码后的字符偏移量（`§` 码不占视觉宽度） |
| `_selStart` / `_selEnd` | `(Line, Col)` 元组，Col 为可视列 |
| `_didDrag` | 区分单击（清选区）与拖拽（保留选区） |
| 选区渲染 | 每行解析 `§` 码段 → 选区内段落颜色反转（前景↔背景） |
| `GetSelectedText` | 遍历选区覆盖的所有行和列，剥离 `§` 码输出纯文本 |

### Terminal.Gui `Color` 枚举可用值

Terminal.Gui v2 pre-release 的 `Color` 枚举包含以下值（注意缺少 `DarkBlue`、`DarkGreen`、`Brown`）：

```
Black, Blue, Green, Cyan, Red, Magenta, Yellow, White,
Gray, DarkGray,
BrightBlue, BrightGreen, BrightCyan, BrightRed, BrightMagenta, BrightYellow
```

`ColoredOutputView` 中 `§1` 映射为 `Color.Blue`（非 DarkBlue），`§2` 映射为 `Color.Green`（非 DarkGreen），`§6` 映射为 `Color.BrightYellow`（非 Brown/Gold）。

## 文件结构
```
MCC-TUI/
├── AGENTS.md
├── .gitignore                              # 排除 bin/obj/publish/mcc/test/config
├── MCC-TUI/                                # 源代码目录
│   ├── MCC-TUI.csproj                      # .NET 8 项目文件
│   ├── Program.cs                          # 主入口 + TUI 界面逻辑 + 实例管理
│   ├── ColoredOutputView.cs                # 自定义 View：§ 颜色渲染 + 鼠标框选 + Ctrl+C 复制
│   ├── DebugLogger.cs                      # Debug 日志（按启动时间戳命名文件）
│   ├── LocalizationManager.cs              # 多语言管理器
│   └── MCC-TUI-config/                     # 语言配置目录
│       ├── MCC-TUI.yml                     # 配置（language / debug 开关）
│       └── lang/
│           ├── zh_cn.yml                   # 中文字符串
│           └── en_us.yml                   # 英文字符串
├── test/                                   # 部署测试目录（仅保留单文件 exe + 必要配置）
│   ├── MCC-TUI.exe                         # 自包含单文件（构建自动产出）
│   ├── MCC-TUI-config/                     # 部署用配置目录
│   │   ├── MCC-TUI.yml                     # 部署用配置（language / debug）
│   │   └── lang/                           # 部署用语言文件
│   ├── MinecraftClient.exe                 # MCC 可执行文件
│   └── config/                             # MCC 配置文件目录（不上传）
└── mcc/                                    # MCC 上游源码（不上传）
```

## 实现步骤

### 1. 项目初始化
```bash
dotnet new console -n MCC-TUI
cd MCC-TUI
dotnet add package Terminal.Gui --version 2.0.0-pre.1802
dotnet add package JetBrains.Annotations
dotnet add package YamlDotNet
```

### 2. 配置 .csproj
设置 `<AssemblyName>MCC-TUI</AssemblyName>` 并添加内容文件：
```xml
<ItemGroup>
  <Content Include="MCC-TUI-config\MCC-TUI.yml">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <Link>MCC-TUI-config\%(Filename)%(Extension)</Link>
  </Content>
  <Content Include="MCC-TUI-config\lang\*.yml">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <Link>MCC-TUI-config\lang\%(Filename)%(Extension)</Link>
  </Content>
</ItemGroup>
```

### 3. 编写 Program.cs 关键点
- `Application.Init(driverName: "NetDriver")` —— 使用 NetDriver 避免 CJK 乱码
- `Console.OutputEncoding = UTF8` —— 设置控制台编码
- `Label.CanFocus = true` + `KeyDown` 事件 —— 实现标签可聚焦
- `Window.KeyDown` 拦截 `Key.Esc` —— **窗口层优先级分发**：① 输出视图可见 → 退出视图；② 文件列表可见 → 关闭列表；③ 否则阻止退出程序。**子控件不再各自处理 ESC**
- `ListView.KeyDown` 拦截 `Key.Backspace` —— 文件列表退出键
- `Process.Start` 使用位置参数 `"config/xxx.ini"`，**不传 `BasicIO`**
- `AddInstanceTab(name)` —— 动态在 `[管理]` 与 `[+]` 之间插入实例标签 `[文件名]`，返回 `InstanceInfo`
- `RebuildNavigation()` —— 每次新增/移除标签后重建左右方向键焦点导航链路
- **线程模型**：`System.Threading.Timer`（后台线程） + `Application.Invoke` 调度到 UI 线程，每 200ms 轮询 `ConcurrentQueue`，批量消费输出行并更新 UI
- `InstanceInfo` 类 —— 封装每个 MCC 实例的名称、标签、进程引用(`Process`)、输出队列(`ConcurrentQueue<string>`)、输出行缓冲(`ObservableCollection<string>`, 10000 行上限)、退出状态
- `SendInputToInstance(info, text)` —— 预留 stdin 写入接口，供未来 TUI→MCC 输入扩展
- `SanitizeOutput(line)` —— 剥离 ANSI 转义序列（`\x1b[...m`），保留 `§` 颜色码供渲染
- `EnterInstanceView(info)` / `ExitInstanceView()` —— 切换输出视图显示/隐藏

### 4. ColoredOutputView 关键设计
自定义 `View`，覆写 `OnDrawContent` 实现彩色文本渲染 + 文本选区：

- **颜色渲染**：解析 Minecraft `§` 格式码（`§0-§f`、`§r`），映射为 Terminal.Gui `Color`，调用 `ConsoleDriver.SetAttribute` + `AddStr` 分段绘制
- **选区管理**：`_selStart` / `_selEnd` 使用 `(Line, Col)` 可视列坐标（`§` 码不占视觉列宽），`_didDrag` 标志区分拖拽与单击
- **鼠标交互**：`OnMouseEvent` 使用**位掩码**检查 `MouseFlags`（`(flags & mask) != 0`），处理 `Button1Pressed` / `ReportMousePosition` / `Button1Released` / `Button1Clicked`
- **单击清选区**：`Button1Clicked` 时若 `!_didDrag`，调用 `ClearSelection()`
- **Ctrl+C**：`OnKeyDown` 中调用 `GetSelectedText()` 剥离 `§` 码 → `Clipboard.TrySetClipboardData()`
- **Lines setter**：拖拽期间跳过 `ClearSelection()` 和 `_scrollOffset` 重置，防止轮询刷新破坏选区

### 5. Debug 日志系统
- **开关**：`MCC-TUI.yml` 中 `debug: true/false`，默认 `false`，由 `LocalizationManager.IsDebugEnabled` 暴露
- **日志文件**：`MCC-TUI-{yyyy-MM-dd-HH-mm-ss}.log`，位于 exe 同目录，每次启动独立文件不覆盖
- **日志范围**：所有 `KeyDown` 入口（含窗口级和子控件级）、`OnMouseEvent` 入口及分支、`OnDrawContent`（节流每 20 帧）、`PollOutputQueues`（节流）、`RebuildNavigation`、`EnterInstanceView`/`ExitInstanceView`、`Lines` setter、`ClearSelection`、生命周期事件
- **实现**：`DebugLogger.Log(string)` 线程安全（`lock`），UTF-8 编码，每行带毫秒时间戳

### 6. 构建发布
构建时自动通过 CopyToTest target 将自包含单文件发布到 `test/` 目录，每次构建前先清理旧的 DLL/JSON/PDB 残留：

```bash
dotnet build -c Release
```

CopyToTest target 内部等价于：
```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none \
  -o ..\test
```

注意：target 通过 `-p:SkipCopyToTest=true` 属性防止 publish 内部触发的 build 递归调用 CopyToTest。
