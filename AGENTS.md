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
   - 光标移动至某实例标签页时，下方内容区域实时显示该 MCC 实例的 stdout/stderr 输出。
   - 输出行缓冲上限 10,000 行，超出时自动移除旧行。
   - 新输出到达时自动滚动到底部。
   - 进程退出后输出区域追加退出提示。
   - 按 `ESC` / `Backspace` 可从输出视图返回标签页焦点。

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
- **Headless 启动**：MCC 通过 `UseShellExecute = false, CreateNoWindow = true` 以 Headless 模式启动。stdout/stderr 通过 `RedirectStandardOutput` / `RedirectStandardError` 捕获，stdin 通过 `RedirectStandardInput` 保留写入接口供未来扩展。
- **线程模型**：`OutputDataReceived` / `ErrorDataReceived` / `Exited` 事件在 ThreadPool 线程触发。输出通过 `ConcurrentQueue` 暂存，由 `Application.AddTimeout` 在主线程批量消费并更新 UI，确保线程安全。
- **文件系统依赖**：假设启动器所在目录即为 MCC 根目录，包含 `MinecraftClient.exe` 和 `config` 子文件夹。
- **MCC 命令行接口**：MCC 通过 `args[0]` 接收配置文件路径作为第一个位置参数（`MinecraftClient.exe "config/xxx.ini"`），并非 `--config=` 选项。经查 MCC `Program.cs` 源码确认（`mcc\Minecraft-Console-Client-master\MinecraftClient\Program.cs:148`）。

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

## 文件结构
```
MCC-TUI/
├── AGENTS.md
├── .gitignore                              # 排除 bin/obj/publish/mcc/test/config
├── MCC-TUI/                                # 源代码目录
│   ├── MCC-TUI.csproj                      # .NET 8 项目文件
│   ├── Program.cs                          # 主入口 + TUI 界面逻辑
│   ├── LocalizationManager.cs              # 多语言管理器
│   └── MCC-TUI-config/                     # 语言配置目录
│       ├── MCC-TUI.yml                     # 语言配置（language: zh_cn / en_us）
│       └── lang/
│           ├── zh_cn.yml                   # 中文字符串
│           └── en_us.yml                   # 英文字符串
├── test/                                   # 部署测试目录（仅保留单文件 exe + 必要配置）
│   ├── MCC-TUI.exe                         # 自包含单文件（构建自动产出）
│   ├── MCC-TUI-config/                     # 部署用配置目录
│   │   ├── MCC-TUI.yml                     # 部署用语言配置
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
- `View.KeyDown` 拦截 `Key.Esc` —— 阻止 ESC 退出程序
- `ListView.KeyDown` 拦截 `Key.Esc` / `Key.Backspace` —— 返回主界面
- `Process.Start` 使用位置参数 `"config/xxx.ini"`
- `AddInstanceTab(name)` —— 动态在 `[管理]` 与 `[+]` 之间插入实例标签 `[文件名]`
- `RebuildNavigation()` —— 每次新增/移除标签后重建左右方向键焦点导航链路
- `Application.AddTimeout` —— 主线程定时轮询 `ConcurrentQueue`，批量消费输出行并更新 UI
- `InstanceInfo` 类 —— 封装每个 MCC 实例的名称、标签、进程引用、输出缓冲和退出状态
- `SendInputToInstance(info, text)` —— 预留 stdin 写入接口，供未来 TUI→MCC 输入扩展
- `Label.Enter` 事件 —— 聚焦实例标签时联动显示 `_instanceOutputFrame`；聚焦 `[管理]` 时隐藏

### 4. 构建发布
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
