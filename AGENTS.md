# AGENTS.md

## 项目目标
构建一个极简 TUI 启动器（最小可行模型），用于从 `config` 目录中选择 `.ini` 配置文件并启动 Minecraft-Console-Client（MCC）。该 TUI 为独立可执行文件，与 `MinecraftClient.exe` 置于同一目录，双击即可运行。启动 MCC 后 TUI 保持运行，不自动退出。

## 技术选型
- **语言**：C#（与 MCC 一致，可直接复用其配置逻辑，且团队学习成本最低）
- **TUI 框架**：[Terminal.Gui](https://github.com/gui-cs/Terminal.Gui)（类似 WinForms 的事件驱动模型，提供标签、按钮、列表等控件）
- **构建与发布**：.NET SDK 8.0 + `dotnet CLI`，最终通过 `dotnet publish` 生成单文件自包含可执行程序

## 功能需求
1. **主界面第一行**：显示 `[管理] [+]`。
   - `[管理]` 为静态标签。
   - `[+]` 为一个可获取焦点的按钮。
2. **交互流程**：
   - 用户通过 Tab/方向键将光标移至 `[+]`，按 `Enter` 键。
   - 在按钮下方动态显示一个文件列表，列出与 `MinecraftClient.exe` 同级 `config` 目录下的所有 `.ini` 文件。
   - 列表第一列为文件名称（不含路径），左对齐。
   - 用户可使用上下方向键移动光标选择文件，按 `Enter` 确认。
3. **启动 MCC**：
   - 根据选中的 `.ini` 文件，拼接启动参数作为第一个位置参数传入 `"config/选中文件.ini"`（MCC 通过 `args[0]` 作为配置文件路径，非 `--config=` 选项）。
   - 使用 `System.Diagnostics.Process.Start` 启动同目录下的 `MinecraftClient.exe`（或 Linux/macOS 下的 `MinecraftClient`）。
   - **启动后 TUI 保持运行，不关闭。用户可继续操作或手动关闭 TUI。**

## 架构约束
- **进程隔离**：TUI 启动器仅负责启动 MCC，不与其建立 IPC 通信，不捕获其输出。启动后 MCC 在独立窗口/终端中运行（由操作系统决定）。
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

## 实现步骤

### 1. 项目初始化
```bash
dotnet new console -n MCC-TUI
cd MCC-TUI
dotnet add package Terminal.Gui --version 2.0.0-pre.1802