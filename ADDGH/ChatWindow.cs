using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Net;
using System.Net.Http;
using System.IO;
using System.IO.Compression;
using System.Windows.Media.Imaging;
using System.Windows.Documents;
using WpfPath = System.Windows.Shapes.Path;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Windows.Markup;
using Grasshopper.Kernel;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI.Script;

namespace ADDGH
{
    public static partial class ChatWindow
    {
        private static Window _window;
        private static StackPanel _chatPanel;
        private static ScrollViewer _chatScroll;
        private static TextBox _txtInput;
        private static Button _btnSend;
        private static Grid _contextMeterHost;
        private static WpfPath _contextRingProgress;
        private static System.Windows.Threading.DispatcherTimer _scrollHideTimer;
        
        private static Grid _settingsOverlay;
        private static TextBox _txtApiKey;
        private static ComboBox _comboProvider;
        private static ComboBox _comboVisionProvider;
        private static TextBox _txtApiBaseUrl;
        private static TextBox _txtModel;
        private static bool _isLoadingProviderSettings = false;
        private static System.Threading.CancellationTokenSource _cts;
        private static List<AttachmentItem> _pendingAttachments = new List<AttachmentItem>();
        private static WrapPanel _attachmentPreviewPanel;
        private static Button _btnClearImage;

        private static Border _codeViewBorder;
        private static RichTextBox _richCodeView;
        private static Border _codeCanvasIssuesHost;
        private static TextBox _txtCanvasIssues;
        private static Border _inputAreaBorder;
        private static ColumnDefinition _codeCol;
        private static GH_Canvas _codeSurfaceHookedCanvas;
        private static GH_Document _codeSurfaceHookedDoc;
        private static System.Windows.Threading.DispatcherTimer _codeSurfaceDebounceTimer;
        private static bool _isCodeVisible = false;
        private static bool _isJsonMode = true;
        private static bool _isLibraryVisible = false;
        private static RowDefinition _libraryRow;
        private static StackPanel _libraryContent;
        private static TextBlock _txtLibCount;
        private static RowDefinition _skillRow;
        private static StackPanel _skillContent;
        private static TextBlock _txtSkillCount;
        private static bool _isSkillVisible = false;
        private static Window _referenceLibraryWindow;

        private static List<ChatHistoryConversation> _chatHistory = new List<ChatHistoryConversation>();
        private static string _activeHistoryId;
        private static bool _isHistorySidebarVisible = false;
        private static bool _isHistoryRestoring = false;
        private static Border _historySidebar;
        private static StackPanel _historyListPanel;
        private static TextBlock _historyCountText;
        private static Button _btnToggleHistory;
        
        private static Border _warningBar;
        private static TextBlock _txtWarning;
        private static Button _btnCloseWarning;
        private static Button _btnModeDropdown;
        private static Button _btnModeBattery;
        private static Button _btnModeCSharp;
        private static Button _btnModeMixed;
        private static MenuItem _menuModeBattery;
        private static MenuItem _menuModeCSharp;
        private static MenuItem _menuModeMixed;

        private enum LayoutMode
        {
            Battery,
            Mixed,
            CSharpFirst
        }

        private const string LayoutModeSettingKey = "ADDGH_LayoutMode";
        private static LayoutMode _layoutMode = LayoutMode.Mixed;

        private const string SYSTEM_PROMPT = @"你是 GH 参数化专家。

【建模逻辑】
1. 先对齐用户需求与约束，再落到具体步骤：数据流、关键电池、风险点；再动手改画布。
2. 风险等级：高风险操作需先用文字说明影响范围和关键风险；明确用户意图后再执行。
   - 🔴 高风险：删除 8 个以上电池、重构主干逻辑、连接可能引发长时间计算的组件（如复杂网格/物理模拟）。
   - 🟡 中风险：添加 5-8 个电池的功能分支、修改密集型交叉连线、替换现有逻辑块。
   - 🟢 低风险（直接操作）：修改 Slider/Panel 数值、添加单个辅助电池、电池对齐或整理分组。
3. 命名：Number Slider 必须设 label；普通电池严禁改 label。
4. 最终回复用结构化 Markdown（短标题、列表、重点加粗）；代码/JSON/表达式/关键参数放在 ``` 代码块中，勿把大段技术内容堆在普通段落里。
5. 参考画布（reference）：先完成建模思路与 GH 逻辑规划，再查阅 skills/reference_index.md；仅当条目与**已确定方案**明显相关时才调用 read_reference_json 读 JSON 做对照或局部复用，勿「先读参考再空想」。

【工具调用效率】
1. 需要当前拓扑、连线或实例 id 时再 get_gh_components，避免无目的重复拉全图。
2. 新增一整块逻辑时，**优先**用 create_component_graph **一次**提交 components 与 connections，把该块内的放置与连线同时做完；尽量少用多轮「少量 add_gh_component ↔ 少量 connect_gh_components」交替，除非必须等上一轮返回的 id/端口才能定案。
3. 单独 add_gh_component 仅限少数必要情形（如占位定位、必须先看清画布再决定下一步）；能并入同一张局部图时仍应合并为一次 create_component_graph。
4. **脚本与 catalog（克制）**：get_gh_components 可读脚本在 **script_bodies**（可能截断）；内置 C#/VB Script 用 **gh_native_script_editor**（**read_source**＝与 script_bodies 同源反射读取，**set_source_commit**＝只替换首个可编辑块，勿整文件顶替模板）；**Rhino GhPython / Python 3 Script 等可执行源码在实例的 `Text` 属性，不是 `Description`，勿把代码写进 Description。** 其它用 **set_gh_component_value**（可加 **property**，优先 `Text`）；未执行可 **recompute_gh_canvas**。仅必要时 search_gh_component_catalog；日常用 get_gh_components、search_component_library、create_component_graph。
5. 每次调用 function 须在参数中填 **summary**（一句中文说明本次在做什么，勿写函数名或 API）；可选 **summary_detail**（卡片右侧短语）。**例外**：show_reference_options 仅需 options（5 个字符串数组），可不填 summary。
6. 优先批量、直接行动。

【对用户表达】
对用户表达要直接说明改动内容、影响范围和需要确认的风险点，避免暴露内部函数名或 API 名。";

        private static string BuildSystemPrompt()
        {
            string prompt = SYSTEM_PROMPT + BuildModePrompt(_layoutMode);
            if (_layoutMode == LayoutMode.CSharpFirst)
                prompt += BuildCSharpDedicatedToolPrompt();
            return prompt;
        }

        private static string BuildCSharpDedicatedToolPrompt()
        {
            return @"

[C# Script dedicated tool rules]
1. In C# priority mode, new core modeling logic must use create_csharp_script_component, not create_script_component_graph.
2. Existing C# Script body edits must use edit_csharp_script_component, not gh_native_script_editor or set_gh_component_value.
3. The body field must contain only the RunScript method body. Do not include using statements, class declarations, the RunScript signature, or the default C# Script template.
4. The create tool first places a default C# Script component, waits for it to initialize, then applies the requested name, ports, and body.
5. Default C# outputs such as out/a are preserved. Output specs are business labels only; requested extra output variables start at b, c, d...; assign to those variables in the body. Do not assign to a in generated C# bodies because it is a default UI port.
6. Do not create unnecessary outputs. Prefer one or a few structured outputs; split into multiple script components only when the logic is genuinely clearer.
7. Do not declare local variables whose names collide with output variables currently in use, such as a, b, c.
8. Non-script helper components in this mode are limited to Params and Display categories for input, output, preview, and debugging.";
        }

        private static string BuildModePrompt(LayoutMode mode)
        {
            if (mode == LayoutMode.CSharpFirst)
            {
                return @"

【当前排布模式：C# 优先】
1. 强制使用一个或多个 C# Script 电池完成核心建模逻辑；逻辑复杂时可以拆成多个脚本电池组合，但优先保持数量少、数据流清晰。
2. 其它非脚本电池只能来自 Params 或 Display 分类；必要时可用 add_gh_component 单独补充这些辅助电池，但只能作为脚本逻辑的输入、输出、显示或调试辅助，不能用普通 GH 电池替代核心逻辑。
3. 新建 C# 脚本化逻辑必须调用 create_csharp_script_component 创建 C# Script、辅助电池、端口与方法体；不要调用 create_script_component_graph、read_skill_file、read_reference_json 或读取 reference。为避免 Grasshopper/Rhino 崩溃，C# Script 创建阶段不要同时连线，待组件稳定后再单独连接。
4. C# Script 的 RunScript 签名由 GH 根据当前输入/输出端口自动生成，不能在 body 中写自定义 RunScript 签名、using、class 或完整模板；body 只提供 RunScript 方法内部语句。
5. C# 输出端口由工具按 outputs 数量硬编码创建为 b, c, d...；outputs 里只写业务 label/type_hint。方法体里只给这些标准输出变量赋值，例如 b = curve; c = points;。不要给 a 赋值，因为部分 GH C# Script 在动态改端口后会显示 a 端口但签名中没有 ref object a。
6. 若需要表达输出业务含义，把含义连接到带 label 的 Panel，或在最终说明中解释；不要把业务名写成 C# 输出变量名，也不要依赖工具从源码里推断输出数量。
7. 修改已有 C# Script 的代码必须调用 edit_csharp_script_component，只替换 RunScript 方法体，保持原有 using、Script_Instance 类和签名模板。
8. 若端口变更后出现签名未同步或变量不存在，先 recompute_gh_canvas 再只修正方法体；不要通过重写完整源码解决。";
            }

            if (mode == LayoutMode.Battery)
            {
                return @"

【当前排布模式：电池模式】
1. 优先使用原生 Grasshopper 电池完成建模逻辑，新增逻辑优先用 create_component_graph 批量创建电池与连线。
2. 本模式禁止新建 C# Script 电池；不要把新功能写成新的 C# 脚本组件。
3. 如果画布上已有 C# Script，仍可查看、读取、编辑或修复已有脚本；该限制只针对“新建 C# 电池”。
4. 只有在用户明确要求或现有画布已经依赖脚本时，才编辑已有脚本；常规建模尽量用电池网络表达。";
            }

            return @"

【当前排布模式：混合模式】
1. 在原生 GH 电池和 C# Script 之间平衡选择：简单、可视化、参数化的数据流优先用电池；复杂循环、几何算法、批量数据处理或重复逻辑可用 C# Script。
2. 新建一整块原生电池逻辑时优先用 create_component_graph；新建 C# 逻辑时使用 create_csharp_script_component。
3. 不要为了很小的参数、面板或基础数学操作创建 C# Script；也不要为了复杂算法硬堆大量电池。
4. 需要修改已有 C# Script 时使用专用编辑工具，保持现有模板和端口约定。";
        }

        private static string BuildInitialSystemContent()
        {
            return _layoutMode != LayoutMode.CSharpFirst
                ? BuildSystemPrompt() + GetSkillsSummary()
                : BuildSystemPrompt();
        }

        private static List<object> BuildInitialSystemMessages()
        {
            string basePrompt = BuildSystemPrompt();
            string skills = _layoutMode != LayoutMode.CSharpFirst ? GetSkillsSummary() : "";
            var list = new List<object>();

            if (string.IsNullOrWhiteSpace(skills) || DeploymentOptions.MergeSkillsIntoSameSystemPromptAsLibraryIndex)
            {
                list.Add(new { role = "system", content = basePrompt + skills });
                return list;
            }

            list.Add(new { role = "system", content = basePrompt });
            list.Add(new { role = "system", content = skills.TrimStart() });
            return list;
        }

        private static LayoutMode ReadLayoutModeSetting()
        {
            try
            {
                string raw = Grasshopper.Instances.Settings.GetValue(LayoutModeSettingKey, LayoutMode.Mixed.ToString());
                if (string.Equals(raw, "Normal", StringComparison.OrdinalIgnoreCase)) return LayoutMode.Mixed;
                if (string.Equals(raw, "PythonFirst", StringComparison.OrdinalIgnoreCase)) return LayoutMode.Mixed;
                if (Enum.TryParse(raw, true, out LayoutMode mode)) return mode;
            }
            catch (Exception ex)
            {
                AddGhLog.Warn("Read layout mode failed: " + ex.Message);
            }
            return LayoutMode.Mixed;
        }

        private static void SaveLayoutModeSetting()
        {
            try { Grasshopper.Instances.Settings.SetValue(LayoutModeSettingKey, _layoutMode.ToString()); }
            catch (Exception ex) { AddGhLog.Warn("Save layout mode failed: " + ex.Message); }
        }

        private static void ReplaceCurrentSystemPrompt()
        {
            if (_messages == null) return;
            int leading = ChatMessageHelpers.CountLeadingSystemMessages(_messages);
            for (int i = leading - 1; i >= 0; i--)
                _messages.RemoveAt(i);
            _messages.InsertRange(0, BuildInitialSystemMessages());
            RefreshContextMeter();
        }

        private static void SetLayoutMode(LayoutMode mode)
        {
            if (_isGenerating) return;
            _layoutMode = mode;
            SaveLayoutModeSetting();
            UpdateLayoutModeButtons();
            ReplaceCurrentSystemPrompt();
        }

        private static void UpdateLayoutModeButtons()
        {
            string ModeLabel(LayoutMode mode)
            {
                switch (mode)
                {
                    case LayoutMode.Battery: return "电池模式";
                    case LayoutMode.CSharpFirst: return "C# 优先";
                    default: return "混合模式";
                }
            }

            if (_btnModeDropdown != null)
            {
                _btnModeDropdown.IsEnabled = !_isGenerating;
                _btnModeDropdown.Content = ModeLabel(_layoutMode) + " ▾";
                _btnModeDropdown.Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160));
            }

            if (_menuModeBattery != null) _menuModeBattery.Header = (_layoutMode == LayoutMode.Battery ? "✓ " : "   ") + "电池模式";
            if (_menuModeMixed != null) _menuModeMixed.Header = (_layoutMode == LayoutMode.Mixed ? "✓ " : "   ") + "混合模式";
            if (_menuModeCSharp != null) _menuModeCSharp.Header = (_layoutMode == LayoutMode.CSharpFirst ? "✓ " : "   ") + "C# 优先";

            void Paint(Button button, bool selected)
            {
                if (button == null) return;
                button.IsEnabled = !_isGenerating;
                button.Background = new SolidColorBrush(selected ? Color.FromRgb(238, 238, 238) : Color.FromRgb(30, 30, 30));
                button.Foreground = new SolidColorBrush(selected ? Color.FromRgb(18, 18, 18) : Color.FromRgb(160, 160, 160));
                button.BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(238, 238, 238) : Color.FromRgb(58, 58, 58));
                button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            }

            Paint(_btnModeBattery, _layoutMode == LayoutMode.Battery);
            Paint(_btnModeMixed, _layoutMode == LayoutMode.Mixed);
            Paint(_btnModeCSharp, _layoutMode == LayoutMode.CSharpFirst);
        }

        private static List<object> _messages = new List<object>();
        private static string _cachedCanvasState = null;  // 画布状态缓存
        private static string _cachedRhinoUnitSignature = null;
        private static bool _canvasChanged = true;  // 画布是否改变标记

        private static readonly HttpClient _httpClient = new HttpClient() { Timeout = TimeSpan.FromMinutes(5) };

        static ChatWindow()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch (Exception ex)
            {
                AddGhLog.Warn("Enable TLS 1.2 failed: " + ex.Message);
            }

            AppDomain.CurrentDomain.ProcessExit += (_, __) =>
            {
                try
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                }
                catch (Exception ex)
                {
                    AddGhLog.Warn("ProcessExit CTS cleanup: " + ex.Message);
                }
                _cts = null;
            };
        }

        private static void EnforceChatHistoryLimit()
        {
            ChatMessageHelpers.TrimMessageHistory(_messages, DeploymentOptions.MaxPersistedChatMessages);
            RefreshContextMeter();
        }

        /// <summary>Rhino/GH 退出或聊天窗口关闭时取消进行中的请求并释放计时器等资源。</summary>
        public static void ShutdownPlugin()
        {
            try { _cts?.Cancel(); }
            catch (Exception ex) { AddGhLog.Warn("ShutdownPlugin cancel: " + ex.Message); }
            try { _cts?.Dispose(); }
            catch (Exception ex) { AddGhLog.Warn("ShutdownPlugin dispose CTS: " + ex.Message); }
            _cts = null;
            _isGenerating = false;

            try
            {
                _scrollHideTimer?.Stop();
                _scrollHideTimer = null;
            }
            catch (Exception ex) { AddGhLog.Warn("ShutdownPlugin timer: " + ex.Message); }

            TeardownGrasshopperCodeSurfaceHooks();

            _pendingAttachments.Clear();
            _thinkingBubble = null;
        }

        private static Border _thinkingBubble;
        private static bool _isGenerating = false;

        private static void ShowThinkingAnimation(string status = "思考中...")
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                if (_thinkingBubble != null) {
                    var tb = _thinkingBubble.Child as TextBlock;
                    if (tb != null) tb.Text = status;
                    return;
                }
                
                var text = new TextBlock {
                    Text = status,
                    Foreground = new SolidColorBrush(Color.FromRgb(125, 125, 125)),
                    FontSize = 12,
                    Margin = new Thickness(5, 0, 0, 18),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.Normal
                };
                
                var breathingAnim = new DoubleAnimation {
                    From = 1.0, To = 0.3,
                    Duration = TimeSpan.FromSeconds(1),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };
                text.BeginAnimation(UIElement.OpacityProperty, breathingAnim);

                _thinkingBubble = new Border { Child = text, Opacity = 0.8, Margin = new Thickness(0, 2, 0, 2) };
                _chatPanel.Children.Add(_thinkingBubble);
                _chatScroll.ScrollToEnd();
            }));
        }

        private static void HideThinkingAnimation()
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                if (_thinkingBubble != null) {
                    _chatPanel.Children.Remove(_thinkingBubble);
                    _thinkingBubble = null;
                }
            }));
        }

        private static void InitializeFloatingScrollbars()
        {
            if (_window == null) return;

            _scrollHideTimer = new System.Windows.Threading.DispatcherTimer {
                Interval = TimeSpan.FromMilliseconds(700)
            };
            _scrollHideTimer.Tick += (s, e) => {
                _scrollHideTimer.Stop();
                HideFloatingScrollbars();
            };

            _window.Loaded += (s, e) => AttachFloatingScrollbarHandlers();
            _window.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((s, e) => {
                if (Math.Abs(e.VerticalChange) < 0.01 && Math.Abs(e.HorizontalChange) < 0.01) return;
                ShowFloatingScrollbars(e.OriginalSource as DependencyObject);
            }), true);
        }

        private static void AttachFloatingScrollbarHandlers()
        {
            if (_window == null) return;

            foreach (var viewer in FindVisualChildren<ScrollViewer>(_window)) {
                viewer.ScrollChanged -= ScrollViewer_ScrollChanged;
                viewer.ScrollChanged += ScrollViewer_ScrollChanged;
            }
        }

        private static void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (Math.Abs(e.VerticalChange) < 0.01 && Math.Abs(e.HorizontalChange) < 0.01) return;
            ShowFloatingScrollbars(sender as DependencyObject);
        }

        private static void ShowFloatingScrollbars(DependencyObject scope)
        {
            if (_window == null) return;
            var root = scope ?? _window;

            foreach (var bar in FindVisualChildren<ScrollBar>(root)) {
                bar.Opacity = 0.45;
            }

            _scrollHideTimer?.Stop();
            _scrollHideTimer?.Start();
        }

        private static void HideFloatingScrollbars()
        {
            if (_window == null) return;

            foreach (var bar in FindVisualChildren<ScrollBar>(_window)) {
                if (bar.IsMouseOver || bar.IsMouseCaptureWithin) continue;
                bar.ClearValue(UIElement.OpacityProperty);
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++) {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) yield return match;

                foreach (T descendant in FindVisualChildren<T>(child)) {
                    yield return descendant;
                }
            }
        }

        public static void Show()
        {
            if (_window != null)
            {
                _window.Show();
                _window.Activate();
                StartGrasshopperCodeSurfaceHooks();
                Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
                {
                    SyncCodeIssuesStripHeightToInputArea();
                    ScheduleCodeSurfaceRefreshFromCanvas();
                }));
                return;
            }

            string xaml = @"
<Window xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
        Title=""Magpie AI Agent"" Height=""850"" Width=""450""
        MinHeight=""850"" MinWidth=""450""
        MaxHeight=""850"" MaxWidth=""1200""
        ResizeMode=""NoResize""
        WindowStyle=""None"" AllowsTransparency=""True"" Background=""Transparent""
        Topmost=""True"" WindowStartupLocation=""CenterScreen"" x:Name=""MagpieWindow"">
    <Window.Resources>
        <Style TargetType=""ScrollBar"">
            <Setter Property=""Background"" Value=""Transparent""/>
            <Setter Property=""MinWidth"" Value=""0""/>
            <Setter Property=""MinHeight"" Value=""0""/>
            <Setter Property=""Opacity"" Value=""0""/>
            <Setter Property=""Template"">
                <Setter.Value>
                    <ControlTemplate TargetType=""ScrollBar"">
                        <Grid x:Name=""Bg"" Background=""Transparent"">
                            <Track x:Name=""PART_Track"" IsDirectionReversed=""true"">
                                <Track.DecreaseRepeatButton>
                                    <RepeatButton Command=""ScrollBar.PageUpCommand"" Opacity=""0""/>
                                </Track.DecreaseRepeatButton>
                                <Track.IncreaseRepeatButton>
                                    <RepeatButton Command=""ScrollBar.PageDownCommand"" Opacity=""0""/>
                                </Track.IncreaseRepeatButton>
                                <Track.Thumb>
                                    <Thumb MinWidth=""0"" MinHeight=""0"" Background=""Transparent"">
                                        <Thumb.Template>
                                            <ControlTemplate TargetType=""Thumb"">
                                                <Border Background=""#88FFFFFF"" Width=""6"" HorizontalAlignment=""Right"" CornerRadius=""3"" Margin=""0,2""/>
                                            </ControlTemplate>
                                        </Thumb.Template>
                                    </Thumb>
                                </Track.Thumb>
                            </Track>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
            <Style.Triggers>
                <Trigger Property=""Orientation"" Value=""Vertical"">
                    <Setter Property=""Width"" Value=""6""/>
                </Trigger>
                <Trigger Property=""Orientation"" Value=""Horizontal"">
                    <Setter Property=""Height"" Value=""6""/>
                </Trigger>
                <Trigger Property=""IsMouseOver"" Value=""True"">
                    <Setter Property=""Opacity"" Value=""0.45""/>
                </Trigger>
                <Trigger Property=""IsMouseCaptureWithin"" Value=""True"">
                    <Setter Property=""Opacity"" Value=""0.45""/>
                </Trigger>
            </Style.Triggers>
        </Style>
        <Style TargetType=""ScrollViewer"">
            <Setter Property=""Template"">
                <Setter.Value>
                    <ControlTemplate TargetType=""ScrollViewer"">
                        <Grid>
                            <ScrollContentPresenter x:Name=""PART_ScrollContentPresenter"" CanContentScroll=""{TemplateBinding CanContentScroll}""/>
                            <ScrollBar x:Name=""PART_VerticalScrollBar"" HorizontalAlignment=""Right"" Maximum=""{TemplateBinding ScrollableHeight}"" ViewportSize=""{TemplateBinding ViewportHeight}"" Value=""{Binding VerticalOffset, Mode=OneWay, RelativeSource={RelativeSource TemplatedParent}}"" Visibility=""{TemplateBinding ComputedVerticalScrollBarVisibility}""/>
                            <ScrollBar x:Name=""PART_HorizontalScrollBar"" VerticalAlignment=""Bottom"" Orientation=""Horizontal"" Maximum=""{TemplateBinding ScrollableWidth}"" ViewportSize=""{TemplateBinding ViewportWidth}"" Value=""{Binding HorizontalOffset, Mode=OneWay, RelativeSource={RelativeSource TemplatedParent}}"" Visibility=""{TemplateBinding ComputedHorizontalScrollBarVisibility}""/>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <Style TargetType=""Button"" x:Key=""IconButtonStyle"">
            <Setter Property=""Background"" Value=""Transparent""/>
            <Setter Property=""BorderThickness"" Value=""0""/>
            <Setter Property=""Cursor"" Value=""Hand""/>
            <Setter Property=""Template"">
                <Setter.Value>
                    <ControlTemplate TargetType=""Button"">
                        <Border Background=""{TemplateBinding Background}"" CornerRadius=""6"">
                            <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
                        </Border>
                        
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <Style TargetType=""ComboBox"" x:Key=""DarkComboBoxStyle"">
            <Setter Property=""Background"" Value=""#2A2A2A""/>
            <Setter Property=""Foreground"" Value=""#EDEDED""/>
            <Setter Property=""BorderBrush"" Value=""#3A3A3A""/>
            <Setter Property=""BorderThickness"" Value=""1""/>
            <Setter Property=""Padding"" Value=""10,6""/>
            <Setter Property=""Template"">
                <Setter.Value>
                    <ControlTemplate TargetType=""ComboBox"">
                        <Grid>
                            <ToggleButton x:Name=""ToggleButton"" Focusable=""False"" IsChecked=""{Binding Path=IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"">
                                <ToggleButton.Template>
                                    <ControlTemplate TargetType=""ToggleButton"">
                                        <Border Background=""#2A2A2A"" BorderBrush=""#3A3A3A"" BorderThickness=""1"" CornerRadius=""8"">
                                            <Grid>
                                                <TextBlock Margin=""10,0,30,0"" VerticalAlignment=""Center"" HorizontalAlignment=""Left"" Foreground=""#EDEDED"" TextTrimming=""CharacterEllipsis"" Text=""{Binding Path=SelectedItem.Content, RelativeSource={RelativeSource AncestorType=ComboBox}}""/>
                                                <TextBlock Text=""▼"" Foreground=""#888"" FontSize=""9"" HorizontalAlignment=""Right"" VerticalAlignment=""Center"" Margin=""0,0,10,0""/>
                                            </Grid>
                                        </Border>
                                    </ControlTemplate>
                                </ToggleButton.Template>
                            </ToggleButton>
                            <Popup x:Name=""PART_Popup"" IsOpen=""{TemplateBinding IsDropDownOpen}"" Placement=""Bottom"" AllowsTransparency=""True"" Focusable=""False"" PopupAnimation=""Fade"">
                                <Border Background=""#202020"" BorderBrush=""#3A3A3A"" BorderThickness=""1"" CornerRadius=""8"" Margin=""0,4,0,0"">
                                    <ScrollViewer MaxHeight=""220"">
                                        <ItemsPresenter/>
                                    </ScrollViewer>
                                </Border>
                            </Popup>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <Style TargetType=""ComboBoxItem"">
            <Setter Property=""Foreground"" Value=""#EDEDED""/>
            <Setter Property=""Background"" Value=""#202020""/>
            <Setter Property=""Padding"" Value=""10,8""/>
            <Setter Property=""Template"">
                <Setter.Value>
                    <ControlTemplate TargetType=""ComboBoxItem"">
                        <Border x:Name=""Bd"" Background=""{TemplateBinding Background}"" CornerRadius=""6"" Padding=""{TemplateBinding Padding}"">
                            <ContentPresenter TextElement.Foreground=""{TemplateBinding Foreground}""/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property=""IsHighlighted"" Value=""True"">
                                <Setter TargetName=""Bd"" Property=""Background"" Value=""#333333""/>
                            </Trigger>
                            <Trigger Property=""IsSelected"" Value=""True"">
                                <Setter TargetName=""Bd"" Property=""Background"" Value=""#3A3A3A""/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    
        <Style TargetType=""Expander"">
            <Setter Property=""Foreground"" Value=""#EEE""/>
            <Setter Property=""Background"" Value=""Transparent""/>
            <Setter Property=""Template"">
                <Setter.Value>
                    <ControlTemplate TargetType=""Expander"">
                        <Border Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}"">
                            <DockPanel>
                                <ToggleButton x:Name=""HeaderSite"" DockPanel.Dock=""Top"" IsChecked=""{Binding IsExpanded, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"" Content=""{TemplateBinding Header}"">
                                    <ToggleButton.Template>
                                        <ControlTemplate TargetType=""ToggleButton"">
                                            <Border Background=""Transparent"" Padding=""5"">
                                                <StackPanel Orientation=""Horizontal"">
                                                    <TextBlock x:Name=""Icon"" Text=""▶"" FontSize=""10"" Foreground=""#888"" Width=""15"" VerticalAlignment=""Center""/>
                                                    <ContentPresenter VerticalAlignment=""Center"" TextElement.Foreground=""#EEE""/>
                                                </StackPanel>
                                            </Border>
                                            <ControlTemplate.Triggers>
                                                <Trigger Property=""IsChecked"" Value=""True"">
                                                    <Setter TargetName=""Icon"" Property=""Text"" Value=""▼""/>
                                                </Trigger>
                                                <Trigger Property=""IsMouseOver"" Value=""True"">
                                                    <Setter TargetName=""Icon"" Property=""Foreground"" Value=""#FFF""/>
                                                </Trigger>
                                            </ControlTemplate.Triggers>
                                        </ControlTemplate>
                                    </ToggleButton.Template>
                                </ToggleButton>
                                <ContentPresenter x:Name=""ExpandSite"" Visibility=""Collapsed"" DockPanel.Dock=""Bottom""/>
                            </DockPanel>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property=""IsExpanded"" Value=""True"">
                                <Setter TargetName=""ExpandSite"" Property=""Visibility"" Value=""Visible""/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>
    
    
    <Border Background=""#141414"" CornerRadius=""16"" Margin=""20"">
        <Border.Effect>
            <DropShadowEffect BlurRadius=""30"" ShadowDepth=""10"" Opacity=""0.6"" Color=""Black""/>
        </Border.Effect>
        <Grid> <!-- Root Wrapper -->
            <Grid x:Name=""MainLayout"">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width=""*"" />
                    <ColumnDefinition Width=""0"" x:Name=""CodeCol""/>
                </Grid.ColumnDefinitions>

                <!-- Code View (Right) -->
                <Border Grid.Column=""1"" x:Name=""CodeViewBorder"" Background=""#141414"" CornerRadius=""0,16,16,0"" BorderBrush=""#2A2A2A"" BorderThickness=""1,0,0,0"">
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height=""60""/>
                            <RowDefinition Height=""*""/>
                            <RowDefinition Height=""Auto""/>
                        </Grid.RowDefinitions>
                        <Border Grid.Row=""0"" x:Name=""CodeViewHeaderBorder"" Background=""#1E1E1E"" CornerRadius=""0,16,0,0"" Padding=""20,0"">
                            <Grid>
                                <TextBlock Text=""GRAPH LOGIC"" Foreground=""#E0E0E0"" FontSize=""13"" FontWeight=""SemiBold"" VerticalAlignment=""Center""/>
                                <StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Right"" VerticalAlignment=""Center"">
                                    <Button x:Name=""BtnToggleViewMode"" Content=""JSON"" Foreground=""#B8B8B8"" Background=""Transparent"" BorderThickness=""1"" BorderBrush=""#333"" FontSize=""10"" Padding=""8,4"" Cursor=""Hand"">
                                        <Button.Template>
                                            <ControlTemplate TargetType=""Button"">
                                                <Border Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}"" CornerRadius=""4"">
                                                    <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
                                                </Border>
                                            </ControlTemplate>
                                        </Button.Template>
                                    </Button>
                                </StackPanel>
                            </Grid>
                        </Border>
                        <Border Grid.Row=""1"" Margin=""15,10,15,0"" Background=""Transparent"">
                            <RichTextBox x:Name=""RichCodeView"" Background=""Transparent"" Foreground=""#B8B8B8"" BorderThickness=""0""
                                 FontSize=""12"" FontFamily=""Consolas, Monaco, Courier New"" IsReadOnly=""True"" IsDocumentEnabled=""True""
                                 VerticalScrollBarVisibility=""Auto"" HorizontalScrollBarVisibility=""Disabled"" CaretBrush=""#888""
                                 Padding=""0""/>
                        </Border>
                        <Border Grid.Row=""2"" x:Name=""CodeCanvasIssuesHost"" Background=""#1E1E1E"" CornerRadius=""0,0,16,0"" BorderBrush=""#2A2A2A"" BorderThickness=""0,1,0,0"" MinHeight=""120"">
                            <DockPanel Margin=""15,10,15,12"" LastChildFill=""True"">
                                <TextBlock DockPanel.Dock=""Top"" Text=""画布诊断"" Foreground=""#888"" FontSize=""11"" FontWeight=""SemiBold"" Margin=""0,0,0,8""/>
                                <ScrollViewer VerticalScrollBarVisibility=""Auto"" HorizontalScrollBarVisibility=""Disabled"">
                                    <TextBox x:Name=""TxtCanvasIssues"" IsReadOnly=""True"" TextWrapping=""Wrap"" AcceptsReturn=""True""
                                        Background=""Transparent"" Foreground=""#C8C8C8"" BorderThickness=""0"" FontSize=""12"" Padding=""0"" CaretBrush=""#888""/>
                                </ScrollViewer>
                            </DockPanel>
                        </Border>
                    </Grid>
                </Border>

                <!-- Chat Area (Left) -->
                <Grid Grid.Column=""0"" x:Name=""ChatAreaGrid"">
                    <Grid.RowDefinitions>
                        <RowDefinition Height=""60""/>
                        <RowDefinition Height=""*"" />
                        <RowDefinition Height=""Auto""/> <!-- Warning Bar -->
                        <RowDefinition Height=""Auto""/> <!-- Input Area -->
                        <RowDefinition Height=""0"" x:Name=""LibraryRow""/> <!-- 电池库扩展行 -->
                    </Grid.RowDefinitions>
                
                <!-- Header -->
                <Border Grid.Row=""0"" Background=""#1E1E1E"" CornerRadius=""16,16,0,0"" x:Name=""HeaderBorder"">
                    <Grid Background=""Transparent"">
                        <TextBlock x:Name=""TxtHeaderTitle"" Text=""✨ Magpie"" Foreground=""#E0E0E0"" FontSize=""16"" FontWeight=""SemiBold"" VerticalAlignment=""Center"" Margin=""20,0,0,0"" Cursor=""Hand"" ToolTip=""双击缩小为悬浮球"" HorizontalAlignment=""Left""/>
                        <StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Right"" Margin=""0,0,15,0"">
                            <Button x:Name=""BtnToggleCode"" Foreground=""#FFFFFF"" Background=""Transparent"" BorderThickness=""0"" FontSize=""13"" Cursor=""Hand"" ToolTip=""切换代码视图"">
                                <Button.Template>
                                    <ControlTemplate TargetType=""Button"">
                                        <Border Background=""{TemplateBinding Background}"" CornerRadius=""6"" Padding=""8,5"">
                                        <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
                                        </Border>
                                        
                                    </ControlTemplate>
                                </Button.Template>
                            <Path Data=""M9.4,16.6L4.8,12l4.6-4.6L8,6l-6,6l6,6L9.4,16.6z M14.6,16.6l4.6-4.6l-4.6-4.6L16,6l6,6l-6,6L14.6,16.6z"" Fill=""White"" Width=""16"" Height=""16"" Stretch=""Uniform""/>
                            </Button>
                            <Button x:Name=""BtnNewChat"" Foreground=""#FFFFFF"" Background=""Transparent"" BorderThickness=""0"" FontSize=""18"" Cursor=""Hand"" ToolTip=""新对话"">
                                <Button.Template>
                                    <ControlTemplate TargetType=""Button"">
                                        <Border Background=""{TemplateBinding Background}"" CornerRadius=""6"" Padding=""8,5"">
                                        <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
                                        </Border>
                                        
                                    </ControlTemplate>
                                </Button.Template>
                            <TextBlock Text=""+"" Foreground=""White"" FontWeight=""Bold""/>
                            </Button>
                            <Button x:Name=""BtnToggleHistory"" Foreground=""#FFFFFF"" Background=""Transparent"" BorderThickness=""0"" FontSize=""13"" Cursor=""Hand"" ToolTip=""对话历史"">
                                <Button.Template>
                                    <ControlTemplate TargetType=""Button"">
                                        <Border Background=""{TemplateBinding Background}"" CornerRadius=""6"" Padding=""10,5"">
                                        <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
                                        </Border>
                                    </ControlTemplate>
                                </Button.Template>
                            <TextBlock Text=""历史"" Foreground=""White"" FontSize=""12"" FontWeight=""SemiBold""/>
                            </Button>
                            <Button x:Name=""BtnSettings"" Foreground=""#FFFFFF"" Background=""Transparent"" BorderThickness=""0"" FontSize=""14"" Cursor=""Hand"" ToolTip=""配置"">
                                <Button.Template>
                                    <ControlTemplate TargetType=""Button"">
                                        <Border Background=""{TemplateBinding Background}"" CornerRadius=""6"" Padding=""8,5"">
                                        <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
                                        </Border>
                                        
                                    </ControlTemplate>
                                </Button.Template>
                            <Path Data=""M11,2L11,3.07C11.68,3.12,12.34,3.28,12.95,3.54L13.72,2.77L15.15,4.22L14.4,4.98C14.73,5.54,14.95,6.15,15.03,6.79L16.07,6.93L16.07,8.93L15.03,9.07C14.95,9.71,14.73,10.32,14.4,10.88L15.15,11.64L13.72,13.09L12.95,12.32C12.34,12.58,11.68,12.74,11,12.79L11,14L9,14L9,12.79C8.32,12.74,7.66,12.58,7.05,12.32L6.28,13.09L4.85,11.64L5.6,10.88C5.27,10.32,5.05,9.71,4.97,9.07L3.93,8.93L3.93,6.93L4.97,6.79C5.05,6.15,5.27,5.54,5.6,4.98L4.85,4.22L6.28,2.77L7.05,3.54C7.66,3.28,8.32,3.12,9,3.07L9,2L11,2z M10,7C8.9,7,8,7.9,8,9C8,10.1,8.9,11,10,11C11.1,11,12,10.1,12,9C12,7.9,11.1,7,10,7z"" Fill=""White"" Width=""16"" Height=""16"" Stretch=""Uniform""/>
                            </Button>
                            <Button x:Name=""BtnClose"" Foreground=""#FFFFFF"" Background=""Transparent"" BorderThickness=""0"" FontSize=""14"" Margin=""5,0,0,0"" Cursor=""Hand"" ToolTip=""关闭"">
                                <Button.Template>
                                    <ControlTemplate TargetType=""Button"">
                                        <Border Background=""{TemplateBinding Background}"" CornerRadius=""6"" Padding=""8,5"">
                                        <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
                                        </Border>
                                        
                                    </ControlTemplate>
                                </Button.Template>
                            <Path Data=""M4,4L8,8M8,4L4,8"" Stroke=""White"" StrokeThickness=""2"" Width=""16"" Height=""16"" Stretch=""Uniform""/>
                            </Button>
                        </StackPanel>
                    </Grid>
                </Border>

            <!-- History -->
                <ScrollViewer Grid.Row=""1"" x:Name=""ChatScroll"" Margin=""5,10,5,0"" VerticalScrollBarVisibility=""Auto"" PanningMode=""VerticalOnly"">
                    <StackPanel x:Name=""ChatPanel"" Margin=""10""/>
                </ScrollViewer>

                <Border x:Name=""HistorySidebar"" Grid.Row=""1"" Panel.ZIndex=""9"" HorizontalAlignment=""Left"" VerticalAlignment=""Stretch"" Width=""0"" Visibility=""Collapsed"" Margin=""0,10,0,10"" Background=""#171717"" BorderBrush=""#2A2A2A"" BorderThickness=""0,1,1,1"" CornerRadius=""0,16,16,0"" ClipToBounds=""True"">
                    <Border.Effect>
                        <DropShadowEffect BlurRadius=""24"" ShadowDepth=""4"" Opacity=""0.35"" Color=""Black""/>
                    </Border.Effect>
                    <Grid Margin=""16,14,14,14"">
                        <Grid.RowDefinitions>
                            <RowDefinition Height=""Auto""/>
                            <RowDefinition Height=""Auto""/>
                            <RowDefinition Height=""*""/>
                        </Grid.RowDefinitions>
                        <Grid>
                            <TextBlock Text=""对话历史"" Foreground=""#EAEAEA"" FontSize=""15"" FontWeight=""SemiBold"" VerticalAlignment=""Center""/>
                            <TextBlock x:Name=""TxtHistoryCount"" Foreground=""#7C7C7C"" FontSize=""11"" Margin=""82,1,0,0"" VerticalAlignment=""Center""/>
                            <Button x:Name=""BtnCloseHistory"" Content=""✕"" HorizontalAlignment=""Right"" Background=""Transparent"" BorderThickness=""0"" Foreground=""#8E8E8E"" Cursor=""Hand"" FontSize=""11"" Width=""24"" Height=""24""/>
                        </Grid>
                        <TextBlock Grid.Row=""1"" Text=""本地保存，点击可恢复会话。"" Foreground=""#707070"" FontSize=""11"" Margin=""0,8,0,12""/>
                        <ScrollViewer Grid.Row=""2"" VerticalScrollBarVisibility=""Auto"" HorizontalScrollBarVisibility=""Disabled"">
                            <StackPanel x:Name=""HistoryListPanel""/>
                        </ScrollViewer>
                    </Grid>
                </Border>

                <!-- Input Area -->
                <Border Grid.Row=""2"" Background=""#1E1E1E"" CornerRadius=""0,0,16,16"" Padding=""15"" x:Name=""InputAreaBorder"">
                <StackPanel>
                    <!-- Warning Bar -->
                    <Border x:Name=""WarningBar"" Visibility=""Collapsed"" Background=""#33CC9900"" BorderBrush=""#66CC9900"" BorderThickness=""1"" CornerRadius=""8"" Padding=""12,8"" Margin=""0,0,0,10"">
                    <Grid>
                        <StackPanel Orientation=""Horizontal"">
                            <TextBlock Text=""⚠️"" Margin=""0,0,8,0"" VerticalAlignment=""Center""/>
                            <TextBlock x:Name=""TxtWarning"" Text=""正在执行复杂任务，已连续操作..."" Foreground=""#FFE0B2"" FontSize=""11"" VerticalAlignment=""Center""/>
                        </StackPanel>
                        <Button x:Name=""BtnCloseWarning"" Content=""✕"" HorizontalAlignment=""Right"" Background=""Transparent"" BorderThickness=""0"" Foreground=""#AAA"" Cursor=""Hand""/>
                    </Grid>
                </Border>
                
                        <WrapPanel x:Name=""AttachmentPreviewPanel"" Margin=""0,0,0,8"" Visibility=""Collapsed""/>
                        <Border Background=""#2A2A2A"" BorderBrush=""#333333"" BorderThickness=""1"" CornerRadius=""8"" Padding=""4"" Margin=""0,0,0,8"">
                            <TextBox x:Name=""TxtInput"" Background=""Transparent"" Foreground=""#FFF"" BorderThickness=""0"" Padding=""14,10,14,10"" FontSize=""14"" AcceptsReturn=""True"" VerticalScrollBarVisibility=""Auto"" TextWrapping=""Wrap"" MinHeight=""36"" MaxHeight=""116"" CaretBrush=""White"" ToolTip=""可在此处输入；Ctrl+V 粘贴文件或截图即可加入附件""/>
                        </Border>
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width=""Auto""/>
                                <ColumnDefinition Width=""Auto""/>
                                <ColumnDefinition Width=""Auto""/>
                                <ColumnDefinition Width=""Auto""/>
                                <ColumnDefinition Width=""Auto""/>
                                <ColumnDefinition Width=""*""/>
                                <ColumnDefinition Width=""Auto""/>
                                <ColumnDefinition Width=""Auto""/>
                            </Grid.ColumnDefinitions>
                            
                            <Button x:Name=""BtnUploadImage"" Grid.Column=""0"" Style=""{StaticResource IconButtonStyle}"" Content=""+"" Foreground=""#A0A0A0"" Background=""Transparent"" BorderThickness=""0"" FontSize=""22"" FontWeight=""Medium"" Cursor=""Hand"" ToolTip=""上传图片或文件"" Margin=""0,0,10,0""/>
                            <Button x:Name=""BtnStop"" Grid.Column=""1"" Content=""停止"" Visibility=""Collapsed"" Foreground=""#FF6B6B"" Background=""Transparent"" BorderThickness=""0"" FontSize=""16"" Cursor=""Hand"" ToolTip=""停止按钮"" Margin=""0,0,10,0""/>
                            <Button x:Name=""BtnContinue"" Grid.Column=""2"" Style=""{StaticResource IconButtonStyle}"" Content=""继续"" Foreground=""#A0A0A0"" Background=""Transparent"" BorderThickness=""0"" FontSize=""14"" Cursor=""Hand"" ToolTip=""继续生成""/>
                            <Button x:Name=""BtnToggleLibrary"" Grid.Column=""3"" Style=""{StaticResource IconButtonStyle}"" Content=""电池库"" Foreground=""#A0A0A0"" Background=""Transparent"" BorderThickness=""0"" FontSize=""14"" Cursor=""Hand"" ToolTip=""展开/收起电池库"" Margin=""8,0,0,0""/>
                            <Button x:Name=""BtnReference"" Grid.Column=""4"" Style=""{StaticResource IconButtonStyle}"" Content=""参考"" Foreground=""#A0A0A0"" Background=""Transparent"" BorderThickness=""0"" FontSize=""14"" Cursor=""Hand"" ToolTip=""参考菜单"" Margin=""8,0,0,0"">
                                <Button.ContextMenu>
                                    <ContextMenu Background=""#1E1E1E"" Foreground=""#E0E0E0"" BorderBrush=""#333"" BorderThickness=""1"" Padding=""4"">
                                        <ContextMenu.Template>
                                            <ControlTemplate TargetType=""ContextMenu"">
                                                <Border Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}"" CornerRadius=""4"" Padding=""{TemplateBinding Padding}"">
                                                    <ItemsPresenter/>
                                                </Border>
                                            </ControlTemplate>
                                        </ContextMenu.Template>
                                        <ContextMenu.Resources>
                                            <Style TargetType=""MenuItem"">
                                                <Setter Property=""Foreground"" Value=""#E0E0E0""/>
                                                <Setter Property=""Background"" Value=""Transparent""/>
                                                <Setter Property=""Padding"" Value=""12,8""/>
                                                <Setter Property=""Template"">
                                                    <Setter.Value>
                                                        <ControlTemplate TargetType=""MenuItem"">
                                                            <Border x:Name=""Bg"" Background=""{TemplateBinding Background}"" CornerRadius=""4"">
                                                                <ContentPresenter Content=""{TemplateBinding Header}"" Margin=""{TemplateBinding Padding}""/>
                                                            </Border>
                                                            <ControlTemplate.Triggers>
                                                                <Trigger Property=""IsHighlighted"" Value=""True"">
                                                                    <Setter TargetName=""Bg"" Property=""Background"" Value=""#333333""/>
                                                                </Trigger>
                                                            </ControlTemplate.Triggers>
                                                        </ControlTemplate>
                                                    </Setter.Value>
                                                </Setter>
                                            </Style>
                                        </ContextMenu.Resources>
                                        <MenuItem x:Name=""MenuCreateReference"" Header=""创建参考""/>
                                        <MenuItem x:Name=""MenuMyReferences"" Header=""我的参考""/>
                                    </ContextMenu>
                                </Button.ContextMenu>
                            </Button>

                            <Button x:Name=""BtnModeDropdown"" Grid.Column=""5"" Style=""{StaticResource IconButtonStyle}"" Content=""混合模式 ▾"" Foreground=""#A0A0A0"" Background=""Transparent"" BorderThickness=""0"" FontSize=""14"" Cursor=""Hand"" ToolTip=""排布模式"" HorizontalAlignment=""Left"" VerticalAlignment=""Center"" Margin=""10,0,0,0"">
                                <Button.ContextMenu>
                                    <ContextMenu Background=""#1E1E1E"" Foreground=""#E0E0E0"" BorderBrush=""#333"" BorderThickness=""1"" Padding=""4"">
                                        <ContextMenu.Template>
                                            <ControlTemplate TargetType=""ContextMenu"">
                                                <Border Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}"" CornerRadius=""4"" Padding=""{TemplateBinding Padding}"">
                                                    <ItemsPresenter/>
                                                </Border>
                                            </ControlTemplate>
                                        </ContextMenu.Template>
                                        <ContextMenu.Resources>
                                            <Style TargetType=""MenuItem"">
                                                <Setter Property=""Foreground"" Value=""#E0E0E0""/>
                                                <Setter Property=""Background"" Value=""Transparent""/>
                                                <Setter Property=""Padding"" Value=""12,8""/>
                                                <Setter Property=""Template"">
                                                    <Setter.Value>
                                                        <ControlTemplate TargetType=""MenuItem"">
                                                            <Border x:Name=""Bg"" Background=""{TemplateBinding Background}"" CornerRadius=""4"">
                                                                <ContentPresenter Content=""{TemplateBinding Header}"" Margin=""{TemplateBinding Padding}""/>
                                                            </Border>
                                                            <ControlTemplate.Triggers>
                                                                <Trigger Property=""IsHighlighted"" Value=""True"">
                                                                    <Setter TargetName=""Bg"" Property=""Background"" Value=""#333333""/>
                                                                </Trigger>
                                                            </ControlTemplate.Triggers>
                                                        </ControlTemplate>
                                                    </Setter.Value>
                                                </Setter>
                                            </Style>
                                        </ContextMenu.Resources>
                                        <MenuItem x:Name=""MenuModeBattery"" Header=""电池模式""/>
                                        <MenuItem x:Name=""MenuModeMixed"" Header=""混合模式""/>
                                        <MenuItem x:Name=""MenuModeCSharp"" Header=""C# 优先""/>
                                    </ContextMenu>
                                </Button.ContextMenu>
                            </Button>
                            
                            <Grid x:Name=""ContextMeterHost"" Grid.Column=""6"" Width=""17"" Height=""17"" Margin=""0,0,10,0"" VerticalAlignment=""Center"" ToolTip=""上下文使用情况"">
                                <Ellipse Stroke=""#4A4A4A"" StrokeThickness=""1.3"" Fill=""Transparent""/>
                                <Path x:Name=""ContextRingProgress"" Stroke=""#D8D8D8"" StrokeThickness=""1.3"" StrokeStartLineCap=""Round"" StrokeEndLineCap=""Round"" Fill=""Transparent""/>
                            </Grid>

                            <Button x:Name=""BtnSend"" Grid.Column=""7"" Content=""➤"" Foreground=""Black"" FontSize=""11"" Margin=""0"" Width=""22"" Height=""22"" Cursor=""Hand"" VerticalAlignment=""Center"">
                                <Button.Template>
                                    <ControlTemplate TargetType=""Button"">
                                        <Border x:Name=""bg"" Background=""White"" CornerRadius=""11"">
                                        <ContentPresenter x:Name=""cp"" HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Margin=""0""/>
                                        </Border>
                                        
                                    </ControlTemplate>
                                </Button.Template>
                            </Button>
                        </Grid>
                    </StackPanel>
                </Border>

            <!-- 电池库扩展区 -->
                <Border Grid.Row=""4"" Background=""#111111"" BorderBrush=""#333333"" BorderThickness=""0,1,0,0"" x:Name=""LibraryPanel"" CornerRadius=""0,0,16,16"">
                    <Grid Margin=""15"">
                        <Grid.RowDefinitions>
                            <RowDefinition Height=""Auto""/>
                            <RowDefinition Height=""*"" />
                        </Grid.RowDefinitions>
                        
                        <Grid Margin=""0,0,0,12"">
                            <StackPanel Orientation=""Horizontal"" VerticalAlignment=""Center"">
                                <TextBlock Text=""电池库"" Foreground=""#EEE"" FontSize=""15"" FontWeight=""Bold""/>
                                <TextBlock x:Name=""TxtLibCount"" Text="""" Foreground=""#555"" FontSize=""11"" Margin=""8,0,0,0"" VerticalAlignment=""Bottom""/>
                            </StackPanel>
                            <Button x:Name=""BtnRefreshLib"" Content=""同步"" HorizontalAlignment=""Right"" Foreground=""#A0A0A0"" Background=""Transparent"" BorderThickness=""0"" FontSize=""14"" Cursor=""Hand"" ToolTip=""重新同步电池库""/>
                        </Grid>

                        <ScrollViewer Grid.Row=""1"" VerticalScrollBarVisibility=""Auto"" Height=""350"">
                            <StackPanel x:Name=""LibraryContent"" />
                        </ScrollViewer>
                    </Grid>
                </Border>
        </Grid> <!-- End Chat Area Grid -->
    </Grid> <!-- End MainLayout Grid -->

    <!-- 配置悬浮层 -->
            <Grid x:Name=""SettingsOverlay"" Grid.Column=""0"" Panel.ZIndex=""20"" Margin=""0,60,0,0"" Background=""#A5000000"" Visibility=""Collapsed"">
            <Border Background=""#1E1E1E"" CornerRadius=""12"" Width=""380"" Height=""590"" HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Padding=""20"">
                <StackPanel>
                    <TextBlock Text=""配置 API"" Foreground=""White"" FontSize=""16"" FontWeight=""SemiBold"" Margin=""0,0,0,15""/>
                    
                    <TextBlock Text=""提供商 (Provider)"" Foreground=""#A0A0A0"" FontSize=""12"" Margin=""0,0,0,5""/>
                    <ComboBox x:Name=""ComboProvider"" Height=""36"" Margin=""0,0,0,10"" Style=""{StaticResource DarkComboBoxStyle}""/>

                    <TextBlock Text=""图片理解模型"" Foreground=""#A0A0A0"" FontSize=""12"" Margin=""0,0,0,5""/>
                    <ComboBox x:Name=""ComboVisionProvider"" Height=""36"" Margin=""0,0,0,10"" Style=""{StaticResource DarkComboBoxStyle}""/>

                    <TextBlock Text=""API Base URL"" Foreground=""#A0A0A0"" FontSize=""12"" Margin=""0,0,0,5""/>
                    <Border Background=""#2A2A2A"" CornerRadius=""8"" Padding=""5"" Margin=""0,0,0,10"">
                        <TextBox x:Name=""TxtApiBaseUrl"" Background=""Transparent"" Foreground=""White"" BorderThickness=""0"" FontSize=""13"" Padding=""5"" CaretBrush=""White""/>
                    </Border>

                    <TextBlock Text=""API Key"" Foreground=""#A0A0A0"" FontSize=""12"" Margin=""0,0,0,5""/>
                    <Border Background=""#2A2A2A"" CornerRadius=""8"" Padding=""5"" Margin=""0,0,0,10"">
                        <TextBox x:Name=""TxtApiKey"" Background=""Transparent"" Foreground=""White"" BorderThickness=""0"" FontSize=""13"" Padding=""5"" CaretBrush=""White""/>
                    </Border>

                    <TextBlock Text=""Model Name"" Foreground=""#A0A0A0"" FontSize=""12"" Margin=""0,0,0,5""/>
                    <Border Background=""#2A2A2A"" CornerRadius=""8"" Padding=""5"" Margin=""0,0,0,10"">
                        <TextBox x:Name=""TxtModel"" Background=""Transparent"" Foreground=""White"" BorderThickness=""0"" FontSize=""13"" Padding=""5"" CaretBrush=""White""/>
                    </Border>
                    
                    <TextBlock Text=""电池库存储路径"" Foreground=""#A0A0A0"" FontSize=""12"" Margin=""0,0,0,5""/>
                    <Grid Margin=""0,0,0,20"">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width=""*""/>
                            <ColumnDefinition Width=""10""/>
                            <ColumnDefinition Width=""Auto""/>
                        </Grid.ColumnDefinitions>
                        <Border Grid.Column=""0"" Background=""#2A2A2A"" CornerRadius=""8"" Padding=""5"">
                            <TextBox x:Name=""TxtLibraryPath"" Background=""Transparent"" Foreground=""White"" BorderThickness=""0"" FontSize=""13"" Padding=""5"" CaretBrush=""White"" IsReadOnly=""True""/>
                        </Border>
                        <Button x:Name=""BtnBrowseLibraryPath"" Grid.Column=""2"" Content=""浏览"" Background=""#444444"" Foreground=""White"" Width=""70"" Height=""32"" FontWeight=""SemiBold"">
                            <Button.Template>
                                <ControlTemplate TargetType=""Button"">
                                    <Border Background=""{TemplateBinding Background}"" CornerRadius=""8"">
                                        <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
                                    </Border>
                                </ControlTemplate>
                            </Button.Template>
                        </Button>
                    </Grid>

                    <Grid Margin=""0,10,0,0"">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width=""*""/>
                            <ColumnDefinition Width=""10""/>
                            <ColumnDefinition Width=""*""/>
                        </Grid.ColumnDefinitions>
                        <Button x:Name=""BtnCancelSettings"" Grid.Column=""0"" Content=""取消"" Background=""#333333"" Foreground=""White"" Height=""36"" FontWeight=""SemiBold"">
                            <Button.Template>
                                <ControlTemplate TargetType=""Button"">
                                    <Border Background=""{TemplateBinding Background}"" CornerRadius=""18"">
                                        <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
                                    </Border>
                                </ControlTemplate>
                            </Button.Template>
                        </Button>
                        <Button x:Name=""BtnSaveSettings"" Grid.Column=""2"" Content=""保存并关闭"" Background=""White"" Foreground=""Black"" Height=""36"" FontWeight=""SemiBold"">
                            <Button.Template>
                                <ControlTemplate TargetType=""Button"">
                                    <Border Background=""{TemplateBinding Background}"" CornerRadius=""18"">
                                        <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
                                    </Border>
                                </ControlTemplate>
                            </Button.Template>
                        </Button>
                    </Grid>
                </StackPanel>
            </Border>
            </Grid> <!-- End SettingsOverlay -->
        </Grid> <!-- End Root Wrapper -->
    </Border>
</Window>
";
            try 
            {
                _window = (Window)XamlReader.Parse(xaml);
            } 
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("界面加载失败: " + ex.Message);
                return;
            }

            _window.Closed += (s, e) =>
            {
                ShutdownPlugin();
                _window = null;
            };
            InitializeFloatingScrollbars();
            
            var headerBorder = (Border)_window.FindName("HeaderBorder");
            if (headerBorder != null) headerBorder.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed && e.ClickCount == 1) _window.DragMove(); };

            var codeViewHeaderBorder = (Border)_window.FindName("CodeViewHeaderBorder");
            if (codeViewHeaderBorder != null) codeViewHeaderBorder.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed && e.ClickCount == 1) _window.DragMove(); };

            var txtHeaderTitle = (TextBlock)_window.FindName("TxtHeaderTitle");
            if (txtHeaderTitle != null) txtHeaderTitle.MouseLeftButtonDown += (s, e) => { if (e.ClickCount >= 2) MinimizeToBall(); };

            var btnClose = (Button)_window.FindName("BtnClose");
            if (btnClose != null) {
                btnClose.Click += (s, e) => {
                    var sb = new Storyboard();
                    var anim = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.2));
                    anim.Completed += (s2, e2) => _window.Close();
                    Storyboard.SetTarget(anim, _window);
                    Storyboard.SetTargetProperty(anim, new PropertyPath(Window.OpacityProperty));
                    sb.Children.Add(anim);
                    sb.Begin();
                };
            }

            _chatPanel = (StackPanel)_window.FindName("ChatPanel");
            _chatScroll = (ScrollViewer)_window.FindName("ChatScroll");
            _txtInput = (TextBox)_window.FindName("TxtInput");
            _btnSend = (Button)_window.FindName("BtnSend");
            _btnModeDropdown = (Button)_window.FindName("BtnModeDropdown");
            _btnModeBattery = (Button)_window.FindName("BtnModeBattery");
            _btnModeCSharp = (Button)_window.FindName("BtnModeCSharp");
            _btnModeMixed = (Button)_window.FindName("BtnModeMixed");
            _menuModeBattery = (MenuItem)_window.FindName("MenuModeBattery");
            _menuModeCSharp = (MenuItem)_window.FindName("MenuModeCSharp");
            _menuModeMixed = (MenuItem)_window.FindName("MenuModeMixed");
            _layoutMode = ReadLayoutModeSetting();
            if (_btnModeDropdown != null) {
                _btnModeDropdown.Click += (s, e) => {
                    if (_btnModeDropdown.ContextMenu != null) {
                        _btnModeDropdown.ContextMenu.PlacementTarget = _btnModeDropdown;
                        _btnModeDropdown.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                        _btnModeDropdown.ContextMenu.IsOpen = true;
                    }
                };
            }
            if (_btnModeBattery != null) _btnModeBattery.Click += (s, e) => SetLayoutMode(LayoutMode.Battery);
            if (_btnModeMixed != null) _btnModeMixed.Click += (s, e) => SetLayoutMode(LayoutMode.Mixed);
            if (_btnModeCSharp != null) _btnModeCSharp.Click += (s, e) => SetLayoutMode(LayoutMode.CSharpFirst);
            if (_menuModeBattery != null) _menuModeBattery.Click += (s, e) => SetLayoutMode(LayoutMode.Battery);
            if (_menuModeMixed != null) _menuModeMixed.Click += (s, e) => SetLayoutMode(LayoutMode.Mixed);
            if (_menuModeCSharp != null) _menuModeCSharp.Click += (s, e) => SetLayoutMode(LayoutMode.CSharpFirst);
            UpdateLayoutModeButtons();
            _historySidebar = (Border)_window.FindName("HistorySidebar");
            _historyListPanel = (StackPanel)_window.FindName("HistoryListPanel");
            _historyCountText = (TextBlock)_window.FindName("TxtHistoryCount");
            _btnToggleHistory = (Button)_window.FindName("BtnToggleHistory");
            if (_btnToggleHistory != null)
            {
                _btnToggleHistory.Width = 34;
                _btnToggleHistory.Height = 30;
                _btnToggleHistory.Padding = new Thickness(0);
                _btnToggleHistory.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
                    <ControlTemplate TargetType=""Button"" xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                        <Border Background=""{TemplateBinding Background}"" CornerRadius=""6"" Padding=""4,3"">
                            <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
                        </Border>
                    </ControlTemplate>");
                _btnToggleHistory.Content = new TextBlock
                {
                    Text = "↻",
                    Foreground = Brushes.White,
                    FontSize = 16,
                    FontFamily = new FontFamily("Segoe UI Symbol"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            _contextMeterHost = (Grid)_window.FindName("ContextMeterHost");
            _contextRingProgress = (WpfPath)_window.FindName("ContextRingProgress");

            var btnCloseHistory = (Button)_window.FindName("BtnCloseHistory");
            if (_btnToggleHistory != null) _btnToggleHistory.Click += (s, e) => ToggleHistorySidebar();
            if (btnCloseHistory != null) btnCloseHistory.Click += (s, e) => SetHistorySidebarVisible(false);
            LoadChatHistoryStore();
            RefreshHistorySidebar();
            
            var btnContinue = (Button)_window.FindName("BtnContinue");
            if (btnContinue != null) {
            btnContinue.Click += (s, e) => {
                if (_isGenerating) { _cts?.Cancel(); return; }
                    if (_txtInput != null) _txtInput.Text = "继续";
                BtnSend_Click(null, null);
            };
            }

            _codeViewBorder = (Border)_window.FindName("CodeViewBorder");
            _codeCanvasIssuesHost = (Border)_window.FindName("CodeCanvasIssuesHost");
            _txtCanvasIssues = (TextBox)_window.FindName("TxtCanvasIssues");
            _richCodeView = (RichTextBox)_window.FindName("RichCodeView");
            if (_richCodeView != null)
                _richCodeView.SizeChanged += (s, ev) => SyncFlowDocumentPageWidthToViewport(_richCodeView);
            _codeCol = (ColumnDefinition)_window.FindName("CodeCol");
            var btnToggleCode = (Button)_window.FindName("BtnToggleCode");

            _inputAreaBorder = (Border)_window.FindName("InputAreaBorder");
            if (_inputAreaBorder != null)
                _inputAreaBorder.SizeChanged += (s, ev) => SyncCodeIssuesStripHeightToInputArea();

            if (btnToggleCode != null) {
            btnToggleCode.Click += (s, e) => {
                _isCodeVisible = !_isCodeVisible;
                if (_isCodeVisible) {
                        if (_codeCol != null) _codeCol.Width = new GridLength(750);
                    _window.Width = 1200;
                        if (headerBorder != null) headerBorder.CornerRadius = new CornerRadius(16, 0, 0, 0);
                        if (_inputAreaBorder != null) _inputAreaBorder.CornerRadius = new CornerRadius(0, 0, 0, 16);
                    StartGrasshopperCodeSurfaceHooks();
                    SyncCodeIssuesStripHeightToInputArea();
                    UpdateCodeView();
                } else {
                        if (_codeCol != null) _codeCol.Width = new GridLength(0);
                    _window.Width = 450;
                        if (headerBorder != null) headerBorder.CornerRadius = new CornerRadius(16, 16, 0, 0);
                        if (_inputAreaBorder != null) _inputAreaBorder.CornerRadius = new CornerRadius(0, 0, 16, 16);
                }
            };
            }

            _window.Loaded += (s, ev) =>
            {
                StartGrasshopperCodeSurfaceHooks();
                SyncCodeIssuesStripHeightToInputArea();
            };

            var btnToggleViewMode = (Button)_window.FindName("BtnToggleViewMode");
            if (btnToggleViewMode != null) {
            btnToggleViewMode.Click += (s, e) => {
                _isJsonMode = !_isJsonMode;
                btnToggleViewMode.Content = _isJsonMode ? "JSON" : "RAW";
                UpdateCodeView();
            };
            }

            var btnSettings = (Button)_window.FindName("BtnSettings");
            _settingsOverlay = (Grid)_window.FindName("SettingsOverlay");
            _txtApiKey = (TextBox)_window.FindName("TxtApiKey");
            _comboProvider = (ComboBox)_window.FindName("ComboProvider");
            _comboVisionProvider = (ComboBox)_window.FindName("ComboVisionProvider");
            _txtApiBaseUrl = (TextBox)_window.FindName("TxtApiBaseUrl");
            _txtModel = (TextBox)_window.FindName("TxtModel");
            _attachmentPreviewPanel = (WrapPanel)_window.FindName("AttachmentPreviewPanel");
            var txtLibraryPath = (TextBox)_window.FindName("TxtLibraryPath");
            PopulateProviderCombo();

            if (_comboProvider != null) {
                _comboProvider.SelectionChanged += (s, e) => {
                    if (_isLoadingProviderSettings) return;
                    LoadProviderSettingsToUI(GetSelectedProviderId());
                };
            }

            if (btnSettings != null) {
            btnSettings.Click += (s, e) => {
                    string providerId = GetCurrentProviderId();
                    SelectProviderComboItem(providerId);
                    SelectVisionProviderComboItem(GetCurrentVisionProviderId());
                    LoadProviderSettingsToUI(providerId);
                    if (txtLibraryPath != null) txtLibraryPath.Text = Grasshopper.Instances.Settings.GetValue("Library_Path", "");
                    SetSettingsOverlayVisible(true);
                };
            }

            var btnBrowseLibraryPath = (Button)_window.FindName("BtnBrowseLibraryPath");
            if (btnBrowseLibraryPath != null) {
            btnBrowseLibraryPath.Click += (s, e) => {
                var folderDialog = new System.Windows.Forms.FolderBrowserDialog {
                    Description = "选择电池库存储路径",
                    ShowNewFolderButton = true
                };
                if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                        if (txtLibraryPath != null) txtLibraryPath.Text = folderDialog.SelectedPath;
                    }
                };
            }

            var btnSaveSettings = (Button)_window.FindName("BtnSaveSettings");
            if (btnSaveSettings != null) {
                btnSaveSettings.Click += (s, e) => {
                    SaveSelectedProviderSettings();
                    SaveSelectedVisionProviderSetting();
                    if (txtLibraryPath != null) Grasshopper.Instances.Settings.SetValue("Library_Path", txtLibraryPath.Text);
                    SetSettingsOverlayVisible(false);
                };
            }

            var btnCancelSettings = (Button)_window.FindName("BtnCancelSettings");
            if (btnCancelSettings != null) {
                btnCancelSettings.Click += (s, e) => {
                    SetSettingsOverlayVisible(false);
                };
            }

            if (_btnSend != null) _btnSend.Click += BtnSend_Click;
            
            if (_txtInput != null) {
                _txtInput.TextChanged += (s, e) => UpdateInputHeight();
                _txtInput.PreviewKeyDown += (s, e) => {
                    if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) {
                        e.Handled = true;
                        int caret = _txtInput.CaretIndex;
                        _txtInput.SelectedText = Environment.NewLine;
                        _txtInput.CaretIndex = caret + Environment.NewLine.Length;
                        UpdateInputHeight();
                    }
                    else if (e.Key == Key.Enter) {
                        e.Handled = true;
                        if (!_isGenerating) BtnSend_Click(null, null);
                    }
                };
                UpdateInputHeight();

                DataObject.AddPastingHandler(_txtInput, TxtInput_OnPasting);
            }

            var btnUploadImage = (Button)_window.FindName("BtnUploadImage");
            _btnClearImage = (Button)_window.FindName("BtnClearImage");

            if (btnUploadImage != null) {
            btnUploadImage.Click += (s, e) => {
                var ofd = new Microsoft.Win32.OpenFileDialog {
                    Filter = "Supported Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.txt;*.md;*.json;*.csv;*.xml;*.ghx;*.pdf;*.docx;*.xlsx;*.pptx;*.doc;*.xls;*.ppt|All Files|*.*",
                    Multiselect = true
                };
                if (ofd.ShowDialog() == true) {
                    AddPendingAttachments(ofd.FileNames);
                }
            };
            }

            if (_btnClearImage != null) {
            _btnClearImage.Click += (s, e) => {
                _pendingAttachments.Clear();
                    RefreshAttachmentPreview();
                    if (_btnClearImage != null) _btnClearImage.Visibility = Visibility.Collapsed;
            };
            }

            var btnNewChat = (Button)_window.FindName("BtnNewChat");
            if (btnNewChat != null) {
            btnNewChat.Click += (s, e) => {
                _activeHistoryId = null;
                _messages.Clear();
                _messages.AddRange(BuildInitialSystemMessages());
                    if (_chatPanel != null) _chatPanel.Children.Clear();
                    _cachedCanvasState = null;
                    _canvasChanged = true;
                AppendSystemMessage("新对话已开启，当前会话已清空。");
                RefreshContextMeter();
                if (_isHistorySidebarVisible) RefreshHistorySidebar();
            };
            }

            // 同步电池库按钮
            var btnSyncLibrary = (Button)_window.FindName("BtnSyncLibrary");
            if (btnSyncLibrary != null) {
                btnSyncLibrary.Click += (s, e) => SyncComponentLibrary();
            }

            // 取消设置按钮
            var btnCancelSettings2 = (Button)_window.FindName("BtnCancelSettings");
            if (btnCancelSettings2 != null) {
                btnCancelSettings2.Click += (s, e) => {
                    SetSettingsOverlayVisible(false);
                };
            }

            // 初始化 UI 引用
            _warningBar = (Border)_window.FindName("WarningBar");
            _txtWarning = (TextBlock)_window.FindName("TxtWarning");
            _btnCloseWarning = (Button)_window.FindName("BtnCloseWarning");
            
            if (_btnCloseWarning != null) 
                _btnCloseWarning.Click += (s, e) => _warningBar.Visibility = Visibility.Collapsed;

            // 电池库逻辑
            _libraryRow = (RowDefinition)_window.FindName("LibraryRow");
            _libraryContent = (StackPanel)_window.FindName("LibraryContent");
            _txtLibCount = (TextBlock)_window.FindName("TxtLibCount");
            var btnToggleLibrary = (Button)_window.FindName("BtnToggleLibrary");
            var btnRefreshLib = (Button)_window.FindName("BtnRefreshLib");
            _skillRow = (RowDefinition)_window.FindName("SkillRow");
            _skillContent = (StackPanel)_window.FindName("SkillContent");
            _txtSkillCount = (TextBlock)_window.FindName("TxtSkillCount");
            var btnToggleSkill = (Button)_window.FindName("BtnToggleSkill");
            var btnRefreshSkill = (Button)_window.FindName("BtnRefreshSkill");
            var skillPanel = (Border)_window.FindName("SkillPanel");

            if (btnToggleSkill != null) {
                btnToggleSkill.Click += (s, e) => {
                    _isSkillVisible = !_isSkillVisible;
                    if (_skillRow != null) _skillRow.Height = _isSkillVisible ? new GridLength(400) : new GridLength(0);
                    if (skillPanel != null) skillPanel.Visibility = _isSkillVisible ? Visibility.Visible : Visibility.Collapsed;
                    if (_isSkillVisible) UpdateSkillLibraryUI();
                };
            }

            if (btnRefreshSkill != null) {
                btnRefreshSkill.Click += (s, e) => UpdateSkillLibraryUI();
            }

            if (btnToggleLibrary != null) {
                btnToggleLibrary.Click += (s, e) => {
                    _isLibraryVisible = !_isLibraryVisible;
                    _libraryRow.Height = _isLibraryVisible ? new GridLength(400) : new GridLength(0);
                    if (_isLibraryVisible) UpdateLibraryUI();
                };
            }

            if (btnRefreshLib != null) {
                btnRefreshLib.Click += (s, e) => SyncComponentLibrary();
            }

            var btnReference = (Button)_window.FindName("BtnReference");
            if (btnReference != null) {
                btnReference.Click += (s, e) => {
                    if (btnReference.ContextMenu != null) {
                        btnReference.ContextMenu.PlacementTarget = btnReference;
                        btnReference.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                        btnReference.ContextMenu.IsOpen = true;
                    }
                };
            }

            var menuCreateReference = (MenuItem)_window.FindName("MenuCreateReference");
            if (menuCreateReference != null) {
                menuCreateReference.Click += (s, e) => {
                    if (!ShowReferenceOptionsTool.TryEnsureCanvasReadyForCreateReference()) return;
                    string prompt = "请对当前画布内容进行总结，生成五个简短的画布描述（围绕当前画布什么典型建模操作，比如某种 gh 电池使用、基于某种建模逻辑的曲线生成方法等等），描述以卡片形式供我选择。\n" +
                        "【顺序限定】须先调用 get_gh_components 与 check_gh_errors：若画布无法读取、没有电池，或检查中含 Error，则只用文字提醒用户处理，**禁止**调用 " + ShowReferenceOptionsTool.FunctionName + "；仅当画布有内容且无 Error 时，再调用 " + ShowReferenceOptionsTool.FunctionName + "，且 arguments 仅需 JSON 数组 options（恰好 5 个字符串，勿传单个长字符串）。\n" +
                        "用户选择后，程序会把画布 JSON 保存到项目 reference 文件夹，并更新 skills/reference_index.md。";
                    SendHiddenPromptAsync("保存当前画布为参考", prompt);
                };
            }

            var menuMyReferences = (MenuItem)_window.FindName("MenuMyReferences");
            if (menuMyReferences != null) {
                menuMyReferences.Click += (s, e) => {
                    ShowReferenceLibraryUI();
                };
            }

            try {
                var helper = new System.Windows.Interop.WindowInteropHelper(_window);
                helper.Owner = Rhino.RhinoApp.MainWindowHandle();
            _window.Show();
            RefreshContextMeter();
            } catch (Exception ex) {
                System.Windows.Forms.MessageBox.Show("显示窗口时报错: " + ex.ToString());
            }
        }

        private static string GetTypeHint(IGH_Param param)
        {
            string baseType = "Any";
            try
            {
                string typeName = param.TypeName ?? "";
                if (typeName.Contains("Boolean") || typeName.Contains("Bool")) baseType = "Boolean";
                else if (typeName.Contains("Number") || typeName.Contains("Double") || typeName.Contains("Integer")) baseType = "Number";
                else if (typeName.Contains("Point")) baseType = "Point";
                else if (typeName.Contains("Vector")) baseType = "Vector";
                else if (typeName.Contains("Line")) baseType = "Line";
                else if (typeName.Contains("Curve")) baseType = "Curve";
                else if (typeName.Contains("Surface")) baseType = "Surface";
                else if (typeName.Contains("Brep")) baseType = "Brep";
                else if (typeName.Contains("Mesh")) baseType = "Mesh";
                else if (typeName.Contains("String") || typeName.Contains("Text")) baseType = "String";
            }
            catch (Exception ex) { AddGhLog.Debug("GetTypeHint TypeName: " + ex.Message); }
            
            try
            {
                if (param.Access == GH_ParamAccess.list) return baseType + "[]";
                if (param.Access == GH_ParamAccess.tree) return baseType + "[][]";
            }
            catch (Exception ex) { AddGhLog.Debug("GetTypeHint Access: " + ex.Message); }
            
            return baseType;
        }

        private static string NormalizeRequestedGhTypeHint(string raw)
        {
            string s = (raw ?? "").Trim();
            if (s.Length == 0) return "";

            while (s.EndsWith("[]", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 2).TrimEnd();

            switch (s.ToLowerInvariant())
            {
                case "bool":
                case "boolean":
                    return "Boolean";
                case "int":
                case "integer":
                    return "Integer";
                case "double":
                case "float":
                case "number":
                    return "Double";
                case "text":
                case "str":
                case "string":
                    return "String";
                case "point":
                case "point3d":
                    return "Point3d";
                case "vector":
                case "vector3d":
                    return "Vector3d";
                case "rect":
                case "rectangle":
                    return "Rectangle3d";
                default:
                    return s;
            }
        }

        private static Grasshopper.Kernel.Parameters.IGH_TypeHint TryCreateGhTypeHint(string raw)
        {
            string normalized = NormalizeRequestedGhTypeHint(raw);
            if (string.IsNullOrWhiteSpace(normalized)) return null;

            try
            {
                var found = Grasshopper.Kernel.Parameters.Hints.GH_TypeHintServer.FindHintByName(normalized);
                if (found != null) return found;
            }
            catch (Exception ex)
            {
                AddGhLog.Debug("TryCreateGhTypeHint FindHintByName " + normalized + ": " + ex.Message);
            }

            string[] typeCandidates =
            {
                "Grasshopper.Kernel.Parameters.Hints.GH_" + normalized + "Hint",
                "Grasshopper.Kernel.Parameters.Hints.GH_" + normalized + "Hint_CS",
                "Grasshopper.Kernel.Parameters.Hints.GH_" + normalized
            };

            foreach (string fullName in typeCandidates)
            {
                try
                {
                    Type t = typeof(Grasshopper.Kernel.Parameters.IGH_TypeHint).Assembly.GetType(fullName, false);
                    if (t == null) continue;
                    if (!typeof(Grasshopper.Kernel.Parameters.IGH_TypeHint).IsAssignableFrom(t)) continue;
                    return Activator.CreateInstance(t) as Grasshopper.Kernel.Parameters.IGH_TypeHint;
                }
                catch (Exception ex)
                {
                    AddGhLog.Debug("TryCreateGhTypeHint ctor " + fullName + ": " + ex.Message);
                }
            }

            return null;
        }

        private static bool TryApplyRuntimeTypeHint(Grasshopper.Kernel.IGH_Param param, string rawTypeHint, List<string> warnings = null)
        {
            var hint = TryCreateGhTypeHint(rawTypeHint);
            if (hint == null) return false;

            Type paramType = param?.GetType();
            if (paramType == null) return false;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var prop in paramType.GetProperties(flags))
            {
                if (!prop.CanWrite) continue;
                if (prop.Name.IndexOf("TypeHint", StringComparison.OrdinalIgnoreCase) < 0 &&
                    !string.Equals(prop.Name, "Hint", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!prop.PropertyType.IsAssignableFrom(hint.GetType()) &&
                    !prop.PropertyType.IsAssignableFrom(typeof(Grasshopper.Kernel.Parameters.IGH_TypeHint)))
                    continue;
                try
                {
                    prop.SetValue(param, hint, null);
                    return true;
                }
                catch (Exception ex)
                {
                    AddGhLog.Debug("TryApplyRuntimeTypeHint prop " + prop.Name + ": " + ex.Message);
                }
            }

            foreach (var method in paramType.GetMethods(flags))
            {
                if (method.Name.IndexOf("TypeHint", StringComparison.OrdinalIgnoreCase) < 0 &&
                    !string.Equals(method.Name, "SetHint", StringComparison.OrdinalIgnoreCase))
                    continue;
                var args = method.GetParameters();
                if (args.Length != 1) continue;
                if (!args[0].ParameterType.IsAssignableFrom(hint.GetType()) &&
                    !args[0].ParameterType.IsAssignableFrom(typeof(Grasshopper.Kernel.Parameters.IGH_TypeHint)))
                    continue;
                try
                {
                    method.Invoke(param, new object[] { hint });
                    return true;
                }
                catch (Exception ex)
                {
                    AddGhLog.Debug("TryApplyRuntimeTypeHint method " + method.Name + ": " + ex.Message);
                }
            }

            for (Type t = paramType; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var field in t.GetFields(flags))
                {
                    if (field.Name.IndexOf("TypeHint", StringComparison.OrdinalIgnoreCase) < 0 &&
                        !string.Equals(field.Name, "m_typeHint", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!field.FieldType.IsAssignableFrom(hint.GetType()) &&
                        !field.FieldType.IsAssignableFrom(typeof(Grasshopper.Kernel.Parameters.IGH_TypeHint)))
                        continue;
                    try
                    {
                        field.SetValue(param, hint);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        AddGhLog.Debug("TryApplyRuntimeTypeHint field " + field.Name + ": " + ex.Message);
                    }
                }
            }

            warnings?.Add("Type hint '" + rawTypeHint + "' was recognized but could not be applied to runtime parameter type " + paramType.Name + ".");
            return false;
        }

        private static void UpdateLibraryUI()
        {
            if (_libraryContent == null) return;
            
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                _libraryContent.Children.Clear();
                
                var groups = Grasshopper.Instances.ComponentServer.ObjectProxies
                    .Where(p => !p.Obsolete)
                    .GroupBy(p => p.Desc.Category)
                    .OrderBy(g => g.Key)
                    .ToList();
                
                int total = groups.Sum(g => g.Count());
                if (_txtLibCount != null) _txtLibCount.Text = $"({total} 个)";
                
                foreach (var group in groups)
                {
                    var expander = new Expander
                    {
                        Header = $"{group.Key}  ({group.Count()})",
                        Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                        Margin = new Thickness(0, 2, 0, 0),
                        IsExpanded = false
                    };
                    
                    var wrap = new WrapPanel { Margin = new Thickness(4, 4, 4, 8) };
                    foreach (var p in group.OrderBy(x => x.Desc.Name))
                    {
                        var card = new Border
                        {
                            Background = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
                            CornerRadius = new CornerRadius(6),
                            Width = 120,
                            Height = 58,
                            Margin = new Thickness(3),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                            BorderThickness = new Thickness(1),
                            Cursor = Cursors.Hand,
                            ToolTip = p.Desc.Description
                        };
                        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7) };
                        sp.Children.Add(new TextBlock
                        {
                            Text = p.Desc.Name,
                            Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                            FontSize = 11,
                            FontWeight = FontWeights.Bold,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        });
                        sp.Children.Add(new TextBlock
                        {
                            Text = p.Desc.NickName,
                            Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                            FontSize = 10
                        });
                        card.Child = sp;
                        wrap.Children.Add(card);
                    }
                    expander.Content = wrap;
                    _libraryContent.Children.Add(expander);
                }
            }));
        }

        private static void SyncComponentLibrary()
        {
            try
            {
                AppendSystemMessage("正在同步电池库...");

                string savePath = "";
                string customPath = Grasshopper.Instances.Settings.GetValue("Library_Path", "");
                
                // 如果用户设置了自定义路径，优先使用
                if (!string.IsNullOrEmpty(customPath) && System.IO.Directory.Exists(customPath))
                {
                    savePath = System.IO.Path.Combine(customPath, "grasshopper_library.json");
                }
                else
                {
                    // 使用默认路径逻辑
                    string skillsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Grasshopper", "Libraries", "skills");
                    
                    // 如果AppData里没有，尝试在当前工作目录找（这应该是源代码所在位置）
                    if (!System.IO.Directory.Exists(skillsPath))
                    {
                        skillsPath = System.IO.Path.Combine(Environment.CurrentDirectory, "skills");
                    }
                    if (!System.IO.Directory.Exists(skillsPath))
                    {
                        skillsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "skills");
                    }
                    if (!System.IO.Directory.Exists(skillsPath))
                    {
                        // 如果还是找不到，尝试在工作目录创建
                        skillsPath = System.IO.Path.Combine(Environment.CurrentDirectory, "skills");
                        if (!System.IO.Directory.Exists(skillsPath))
                        {
                            System.IO.Directory.CreateDirectory(skillsPath);
                        }
                    }
                    
                    // 在 skills 同级创建 reference 文件夹
                    string parentPath = System.IO.Path.GetDirectoryName(skillsPath);
                    string referencePath = System.IO.Path.Combine(parentPath, "reference");
                    
                    if (!System.IO.Directory.Exists(referencePath))
                    {
                        System.IO.Directory.CreateDirectory(referencePath);
                    }
                    
                    savePath = System.IO.Path.Combine(referencePath, "grasshopper_library.json");
                }

                // 按 Exposure 级别分类 - JSON
                System.Text.StringBuilder sbPrimary = new System.Text.StringBuilder();
                System.Text.StringBuilder sbSecondary = new System.Text.StringBuilder();
                System.Text.StringBuilder sbTertiary = new System.Text.StringBuilder();
                System.Text.StringBuilder sbHidden = new System.Text.StringBuilder();
                sbPrimary.AppendLine("[");
                sbSecondary.AppendLine("[");
                sbTertiary.AppendLine("[");
                sbHidden.AppendLine("[");
                bool firstPrimary = true, firstSecondary = true, firstTertiary = true, firstHidden = true;
                
                // 按 Exposure 级别分类 - CSV
                System.Text.StringBuilder sbCsvPrimary = new System.Text.StringBuilder();
                System.Text.StringBuilder sbCsvSecondary = new System.Text.StringBuilder();
                System.Text.StringBuilder sbCsvTertiary = new System.Text.StringBuilder();
                System.Text.StringBuilder sbCsvHidden = new System.Text.StringBuilder();
                
                // CSV表头
                string csvHeader = "name,nickname,description,category,subcategory,input_names,input_types,output_names,output_types";
                sbCsvPrimary.AppendLine(csvHeader);
                sbCsvSecondary.AppendLine(csvHeader);
                sbCsvTertiary.AppendLine(csvHeader);
                sbCsvHidden.AppendLine(csvHeader);
                
                int countPrimary = 0, countSecondary = 0, countTertiary = 0, countHidden = 0;

                foreach (var proxy in Grasshopper.Instances.ComponentServer.ObjectProxies)
                {
                    try
                    {
                        var comp = proxy.CreateInstance() as IGH_Component;
                        if (comp != null)
                        {
                            // 构建电池 JSON
                            string compJson = "";
                            compJson += "  {";
                            compJson += $"\"name\":\"{EscapeJsonString(comp.Name)}\",";
                            compJson += $"\"nickname\":\"{EscapeJsonString(comp.NickName)}\",";
                            compJson += $"\"description\":\"{EscapeJsonString(comp.Description)}\",";
                            compJson += $"\"category\":\"{EscapeJsonString(comp.Category)}\",";
                            compJson += $"\"subcategory\":\"{EscapeJsonString(comp.SubCategory)}\",";

                            // 输入端口
                            compJson += "\"inputs\":[";
                            List<string> inputNames = new List<string>();
                            List<string> inputTypes = new List<string>();
                            for (int i = 0; i < comp.Params.Input.Count; i++)
                            {
                                var param = comp.Params.Input[i];
                                if (i > 0) compJson += ",";
                                string typeHint = "Unknown";
                                try { typeHint = GetTypeHint(param); } catch (Exception ex) { AddGhLog.Debug("Sync lib input typeHint: " + ex.Message); }
                                
                                string desc = "";
                                try { desc = param.Description ?? ""; } catch (Exception ex) { AddGhLog.Debug("Sync lib input desc: " + ex.Message); }
                                
                                compJson += "{";
                                compJson += $"\"name\":\"{EscapeJsonString(param.Name)}\",";
                                compJson += $"\"description\":\"{EscapeJsonString(desc)}\",";
                                compJson += $"\"typeHint\":\"{EscapeJsonString(typeHint)}\"";
                                compJson += "}";
                                
                                inputNames.Add(param.Name);
                                inputTypes.Add(typeHint);
                            }
                            compJson += "],";

                            // 输出端口
                            compJson += "\"outputs\":[";
                            List<string> outputNames = new List<string>();
                            List<string> outputTypes = new List<string>();
                            for (int i = 0; i < comp.Params.Output.Count; i++)
                            {
                                var param = comp.Params.Output[i];
                                if (i > 0) compJson += ",";
                                string typeHint = "Unknown";
                                try { typeHint = GetTypeHint(param); } catch (Exception ex) { AddGhLog.Debug("Sync lib output typeHint: " + ex.Message); }
                                
                                string desc = "";
                                try { desc = param.Description ?? ""; } catch (Exception ex) { AddGhLog.Debug("Sync lib output desc: " + ex.Message); }
                                
                                compJson += "{";
                                compJson += $"\"name\":\"{EscapeJsonString(param.Name)}\",";
                                compJson += $"\"description\":\"{EscapeJsonString(desc)}\",";
                                compJson += $"\"typeHint\":\"{EscapeJsonString(typeHint)}\"";
                                compJson += "}";
                                
                                outputNames.Add(param.Name);
                                outputTypes.Add(typeHint);
                            }
                            compJson += "]";
                            compJson += "}";

                            // 构建 CSV 行
                            string csvLine = 
                                $"{EscapeCsvString(comp.Name)}," +
                                $"{EscapeCsvString(comp.NickName)}," +
                                $"{EscapeCsvString(comp.Description)}," +
                                $"{EscapeCsvString(comp.Category)}," +
                                $"{EscapeCsvString(comp.SubCategory)}," +
                                $"{EscapeCsvString(string.Join("|", inputNames))}," +
                                $"{EscapeCsvString(string.Join("|", inputTypes))}," +
                                $"{EscapeCsvString(string.Join("|", outputNames))}," +
                                $"{EscapeCsvString(string.Join("|", outputTypes))}";

                            // 按 Exposure 级别分配
                            if (proxy.Exposure == GH_Exposure.primary)
                            {
                                if (!firstPrimary) sbPrimary.AppendLine(",");
                                firstPrimary = false;
                                sbPrimary.AppendLine(compJson);
                                sbCsvPrimary.AppendLine(csvLine);
                                countPrimary++;
                            }
                            else if (proxy.Exposure == GH_Exposure.secondary)
                            {
                                if (!firstSecondary) sbSecondary.AppendLine(",");
                                firstSecondary = false;
                                sbSecondary.AppendLine(compJson);
                                sbCsvSecondary.AppendLine(csvLine);
                                countSecondary++;
                            }
                            else if (proxy.Exposure == GH_Exposure.tertiary)
                            {
                                if (!firstTertiary) sbTertiary.AppendLine(",");
                                firstTertiary = false;
                                sbTertiary.AppendLine(compJson);
                                sbCsvTertiary.AppendLine(csvLine);
                                countTertiary++;
                            }
                            else
                            {
                                if (!firstHidden) sbHidden.AppendLine(",");
                                firstHidden = false;
                                sbHidden.AppendLine(compJson);
                                sbCsvHidden.AppendLine(csvLine);
                                countHidden++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AddGhLog.Warn("Library sync skipped one proxy: " + ex.Message);
                    }
                }

                sbPrimary.AppendLine("\n]");
                sbSecondary.AppendLine("\n]");
                sbTertiary.AppendLine("\n]");
                sbHidden.AppendLine("\n]");

                // 保存文件
                string directory = System.IO.Path.GetDirectoryName(savePath);
                
                // JSON文件
                string primaryPath = System.IO.Path.Combine(directory, "library_primary.json");
                string secondaryPath = System.IO.Path.Combine(directory, "library_secondary.json");
                string tertiaryPath = System.IO.Path.Combine(directory, "library_tertiary.json");
                string hiddenPath = System.IO.Path.Combine(directory, "library_hidden.json");
                System.IO.File.WriteAllText(primaryPath, sbPrimary.ToString());
                System.IO.File.WriteAllText(secondaryPath, sbSecondary.ToString());
                System.IO.File.WriteAllText(tertiaryPath, sbTertiary.ToString());
                System.IO.File.WriteAllText(hiddenPath, sbHidden.ToString());
                
                // CSV文件
                string csvPrimaryPath = System.IO.Path.Combine(directory, "library_primary.csv");
                string csvSecondaryPath = System.IO.Path.Combine(directory, "library_secondary.csv");
                string csvTertiaryPath = System.IO.Path.Combine(directory, "library_tertiary.csv");
                string csvHiddenPath = System.IO.Path.Combine(directory, "library_hidden.csv");
                System.IO.File.WriteAllText(csvPrimaryPath, sbCsvPrimary.ToString(), System.Text.Encoding.UTF8);
                System.IO.File.WriteAllText(csvSecondaryPath, sbCsvSecondary.ToString(), System.Text.Encoding.UTF8);
                System.IO.File.WriteAllText(csvTertiaryPath, sbCsvTertiary.ToString(), System.Text.Encoding.UTF8);
                System.IO.File.WriteAllText(csvHiddenPath, sbCsvHidden.ToString(), System.Text.Encoding.UTF8);

                AppendSystemMessage($"电池库同步完成！已保存到：{directory}");
                AppendSystemMessage($"分类：primary({countPrimary})、secondary({countSecondary})、tertiary({countTertiary})、hidden({countHidden})");
                AppendSystemMessage("已同时导出 JSON 和 CSV 两种格式，CSV 格式可降低 token 消耗。");
            }
            catch (Exception ex)
            {
                AddGhLog.Error("SyncComponentLibrary failed", ex);
                AppendQuietDiagnosticCard("电池库同步", "未完成：" + ex.Message);
            }
        }

        private static string EscapeJsonString(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        private static string EscapeCsvString(string s)
        {
            if (s == null) return "";
            s = s.Replace("\"", "\"\"");
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r"))
            {
                s = "\"" + s + "\"";
            }
            return s;
        }

        private static void UpdateInputHeight()
        {
            if (_txtInput == null) return;

            _txtInput.UpdateLayout();
            int lineCount = Math.Max(1, _txtInput.LineCount);
            double lineHeight = Math.Max(20, _txtInput.FontSize * 1.45);
            double desiredHeight = 24 + (lineCount * lineHeight);
            _txtInput.Height = Math.Min(116, Math.Max(36, desiredHeight));
        }

        private static Border CreateStopSendGlyph()
        {
            var square = new Border {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(1),
                Background = Brushes.Black,
                SnapsToDevicePixels = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetEdgeMode(square, EdgeMode.Aliased);
            return square;
        }

        private static void ApplySendButtonGeneratingState()
        {
            UpdateLayoutModeButtons();
            if (_btnSend == null) return;
            _btnSend.Content = CreateStopSendGlyph();
            var bg = _btnSend.Template.FindName("bg", _btnSend) as Border;
            if (bg != null) bg.CornerRadius = new CornerRadius(11);
            var cp = _btnSend.Template.FindName("cp", _btnSend) as ContentPresenter;
            if (cp != null) cp.Margin = new Thickness(0);
        }

        private static void ApplySendButtonIdleState()
        {
            UpdateLayoutModeButtons();
            if (_btnSend == null) return;
            _btnSend.Content = "➤";
            var bg = _btnSend.Template.FindName("bg", _btnSend) as Border;
            if (bg != null) bg.CornerRadius = new CornerRadius(11);
            var cp = _btnSend.Template.FindName("cp", _btnSend) as ContentPresenter;
            if (cp != null) cp.Margin = new Thickness(0);
        }

        private static string BuildSimpleRollingSummaryBlock(IList<object> messages, int fromInclusive, int toExclusive, int maxChars)
        {
            if (messages == null || fromInclusive >= toExclusive) return "";

            var lines = new List<string>();
            for (int i = fromInclusive; i < toExclusive && i < messages.Count; i++)
            {
                string role = ChatMessageHelpers.TryGetRole(messages[i]) ?? "?";
                string content = ChatMessageHelpers.TryGetPlainTextContent(messages[i]) ?? "";
                content = content.Replace("\r", " ").Replace("\n", " ").Trim();
                if (content.Length > 220) content = content.Substring(0, 220) + "...";
                if (content.Length == 0) continue;
                lines.Add(role.ToUpperInvariant() + ": " + content);
            }

            string text = string.Join("\n", lines);
            if (text.Length > maxChars)
                text = text.Substring(0, maxChars) + "\n[...truncated]";
            return text;
        }

        private static bool TryApplyRollingSummaryInPlace()
        {
            if (_messages == null || _messages.Count == 0) return false;

            ChatMessageHelpers.GetTierBoundaries(_messages, out int tier0End, out int tier2Start, out bool hasTier1Summary);
            if (!ChatMessageHelpers.TryFindSummaryCutExclusive(_messages, tier2Start, DeploymentOptions.ContextVerbatimTailCount, out int cutExclusive))
                return false;

            string existingSummary = "";
            if (hasTier1Summary && tier0End < _messages.Count &&
                ChatMessageHelpers.IsRollingSummaryTier1Message(_messages[tier0End], out string body))
                existingSummary = body ?? "";

            int maxSummaryChars = Math.Max(1200, DeploymentOptions.Tier1SoftBudgetTokens * 3);
            string newBlock = BuildSimpleRollingSummaryBlock(_messages, tier2Start, cutExclusive, Math.Max(600, maxSummaryChars / 2));
            if (string.IsNullOrWhiteSpace(existingSummary) && string.IsNullOrWhiteSpace(newBlock))
                return false;

            string merged = string.IsNullOrWhiteSpace(existingSummary)
                ? newBlock
                : (string.IsNullOrWhiteSpace(newBlock) ? existingSummary : existingSummary + "\n" + newBlock);
            if (merged.Length > maxSummaryChars)
                merged = merged.Substring(0, maxSummaryChars) + "\n[...truncated]";

            for (int i = cutExclusive - 1; i >= tier2Start; i--)
                _messages.RemoveAt(i);

            var summaryMsg = new { role = "assistant", content = DeploymentOptions.RollingSummaryHeader + merged };
            if (hasTier1Summary && tier0End < _messages.Count)
                _messages[tier0End] = summaryMsg;
            else
                _messages.Insert(tier0End, summaryMsg);

            return true;
        }

        private static void ApplyMechanicalContextCompressionIfNeeded()
        {
            try
            {
                if (_messages == null || _messages.Count == 0) return;
                int projected = ChatMessageHelpers.EstimateProjectedMessageListTokens(_messages);
                int trigger = (int)Math.Round(DeploymentOptions.ContextBudgetTokens * DeploymentOptions.ContextCompressTriggerRatio);
                if (projected < trigger) return;

                TryApplyRollingSummaryInPlace();
                projected = ChatMessageHelpers.EstimateProjectedMessageListTokens(_messages);
                if (projected < trigger)
                {
                    ChatMessageHelpers.TrimMessageHistory(_messages, DeploymentOptions.MaxPersistedChatMessages);
                    return;
                }
                ChatMessageHelpers.ApplyMechanicalContextReductionInPlace(_messages);
                ChatMessageHelpers.TrimMessageHistory(_messages, DeploymentOptions.MaxPersistedChatMessages);
            }
            catch (Exception ex)
            {
                AddGhLog.Warn("ApplyMechanicalContextCompressionIfNeeded: " + ex.Message);
            }
        }

        private static Geometry BuildContextArcGeometry(double ratio, double size, double strokeThickness)
        {
            ratio = Math.Max(0, Math.Min(1, ratio));
            if (ratio <= 0) return Geometry.Empty;

            double radius = Math.Max(0.1, (size - strokeThickness) / 2.0);
            Point center = new Point(size / 2.0, size / 2.0);
            Point start = new Point(center.X, center.Y - radius);

            if (ratio >= 0.999)
            {
                return new EllipseGeometry(center, radius, radius);
            }

            double angle = (Math.PI * 2.0 * ratio) - (Math.PI / 2.0);
            Point end = new Point(
                center.X + (radius * Math.Cos(angle)),
                center.Y + (radius * Math.Sin(angle)));

            var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
            figure.Segments.Add(new ArcSegment
            {
                Point = end,
                Size = new Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = ratio >= 0.5
            });

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        private static void RefreshContextMeter()
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                if (_contextMeterHost == null || _contextRingProgress == null) return;

                int projected = 0;
                double ratio = 0;
                try
                {
                    projected = ChatMessageHelpers.EstimateProjectedMessageListTokens(_messages);
                    ratio = DeploymentOptions.ContextBudgetTokens <= 0
                        ? 0
                        : Math.Max(0, Math.Min(1, (double)projected / DeploymentOptions.ContextBudgetTokens));
                }
                catch (Exception ex)
                {
                    AddGhLog.Debug("RefreshContextMeter estimate: " + ex.Message);
                }

                double size = _contextMeterHost.Width > 0 ? _contextMeterHost.Width : Math.Max(17, _contextMeterHost.ActualWidth);
                double stroke = _contextRingProgress.StrokeThickness > 0 ? _contextRingProgress.StrokeThickness : 1.3;
                _contextRingProgress.Data = BuildContextArcGeometry(ratio, size, stroke);

                Color color = ratio >= 0.9
                    ? Color.FromRgb(231, 76, 60)
                    : ratio >= 0.72
                        ? Color.FromRgb(230, 184, 92)
                        : Color.FromRgb(216, 216, 216);
                _contextRingProgress.Stroke = new SolidColorBrush(color);
                _contextRingProgress.Visibility = ratio <= 0.001 ? Visibility.Collapsed : Visibility.Visible;
                _contextMeterHost.ToolTip = $"上下文约 {Math.Round(ratio * 100)}% ({projected}/{DeploymentOptions.ContextBudgetTokens})";
            }));
        }

        private static async void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            if (_isGenerating) { _cts?.Cancel(); return; }
            string input = _txtInput.Text.Trim();
            var attachmentsToSend = _pendingAttachments.ToList();
            if (string.IsNullOrEmpty(input) && attachmentsToSend.Count == 0) return;
            bool hasImageAttachments = attachmentsToSend.Any(a => a.Kind == AttachmentKind.Image && !string.IsNullOrEmpty(a.Base64));

            _isGenerating = true;
            ApplySendButtonGeneratingState();
            _txtInput.Text = "";

            if (_messages.Count == 0) {
                _messages.AddRange(BuildInitialSystemMessages());
            }

            if (attachmentsToSend.Count > 0) {
                if (!hasImageAttachments)
                {
                    var contentArr = BuildUserMessageContent(input, attachmentsToSend);
                    _messages.Add(new { role = "user", content = contentArr });
                }
                AppendUserMessageWithAttachments(input, attachmentsToSend);
            } else {
                _messages.Add(new { role = "user", content = input });
                AppendBubble(input, true);
            }

            SyncActiveHistoryConversation(string.IsNullOrWhiteSpace(input)
                ? (attachmentsToSend.FirstOrDefault()?.FileName ?? "附件对话")
                : input);

            EnforceChatHistoryLimit();

            _pendingAttachments.Clear();
            RefreshAttachmentPreview();
            if (_btnClearImage != null) _btnClearImage.Visibility = Visibility.Collapsed;

            try { _cts?.Dispose(); } catch (Exception ex) { AddGhLog.Warn("Dispose prior CTS: " + ex.Message); }
            _cts = new System.Threading.CancellationTokenSource();

            try {
                ShowThinkingAnimation();
                if (hasImageAttachments)
                {
                    string visionAnalysis = await PreprocessImageAttachmentsAsync(input, attachmentsToSend, _cts.Token);
                    if (string.IsNullOrWhiteSpace(visionAnalysis))
                        return;

                    _messages.Add(new { role = "user", content = BuildVisionExecutionUserText(input, attachmentsToSend, visionAnalysis) });
                    EnforceChatHistoryLimit();
                    SyncActiveHistoryConversation();
                }

                string apiKey = GetProviderRuntimeSettings().ApiKey;
                await CallLLMAPI(apiKey, 0, _cts.Token);
            } catch (OperationCanceledException) {
                AppendSystemMessage("已停止生成。");
            } catch (Exception ex) {
                AddGhLog.Error("CallLLMAPI failed", ex);
                AppendQuietDiagnosticCard("对话请求",
                    BuildProviderDiagnostic(GetProviderRuntimeSettings(), "出现异常：" + ex.GetType().Name, ex.Message));
            } finally {
                HideThinkingAnimation();
                _isGenerating = false;
                ApplySendButtonIdleState();
                try { _cts?.Dispose(); } catch (Exception ex) { AddGhLog.Warn("Dispose CTS after send: " + ex.Message); }
                _cts = null;
            }
        }

        private static void SetSettingsOverlayVisible(bool visible)
        {
            if (_settingsOverlay != null) _settingsOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

            // Keep the header available for dragging, but block actions that would mutate chat state.
            string[] headerActionNames = { "BtnNewChat", "BtnToggleCode", "BtnToggleHistory", "BtnSettings" };
            foreach (string name in headerActionNames)
            {
                if (_window?.FindName(name) is Button button) button.IsEnabled = !visible;
            }
        }

        private static string GetChatHistoryDirectory()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = System.IO.Path.Combine(root, "ADDGH", "history");
            try { System.IO.Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        private static string GetChatHistoryFilePath()
        {
            return System.IO.Path.Combine(GetChatHistoryDirectory(), "conversations.json");
        }

        private static string NormalizeConversationTitle(string text)
        {
            string s = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (string.IsNullOrWhiteSpace(s)) return "新对话";
            if (s.Length > 28) s = s.Substring(0, 28) + "…";
            return s;
        }

        private static string GetConversationPreview(ChatHistoryConversation conv)
        {
            if (conv?.Messages == null || conv.Messages.Count == 0) return "空白对话";
            for (int i = conv.Messages.Count - 1; i >= 0; i--)
            {
                var msg = conv.Messages[i];
                string role = ChatMessageHelpers.TryGetRole(msg);
                if (!string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
                    continue;
                string content = ChatMessageHelpers.TryGetPlainTextContent(msg);
                if (string.IsNullOrWhiteSpace(content)) continue;
                content = content.Replace("\r", " ").Replace("\n", " ").Trim();
                return content.Length > 64 ? content.Substring(0, 64) + "…" : content;
            }
            return "空白对话";
        }

        private static string FormatConversationTime(DateTime utcTime)
        {
            try
            {
                return utcTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
                return utcTime.ToString("yyyy-MM-dd HH:mm");
            }
        }

        private static ChatHistoryConversation FindHistoryConversation(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return _chatHistory.FirstOrDefault(c => string.Equals(c?.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private static ChatHistoryConversation GetOrCreateActiveHistoryConversation(string titleSeed = null)
        {
            var existing = FindHistoryConversation(_activeHistoryId);
            if (existing != null) return existing;

            var conv = new ChatHistoryConversation
            {
                Id = Guid.NewGuid().ToString("n"),
                Title = NormalizeConversationTitle(titleSeed),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Messages = new JArray()
            };
            _chatHistory.Insert(0, conv);
            _activeHistoryId = conv.Id;
            return conv;
        }

        private static void LoadChatHistoryStore()
        {
            _chatHistory = new List<ChatHistoryConversation>();
            try
            {
                string path = GetChatHistoryFilePath();
                if (!System.IO.File.Exists(path)) return;

                string json = System.IO.File.ReadAllText(path, Encoding.UTF8);
                JObject root = JObject.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
                JArray items = root["conversations"] as JArray ?? new JArray();
                foreach (var token in items)
                {
                    var conv = token.ToObject<ChatHistoryConversation>();
                    if (conv == null) continue;
                    conv.Id = string.IsNullOrWhiteSpace(conv.Id) ? Guid.NewGuid().ToString("n") : conv.Id;
                    conv.Title = NormalizeConversationTitle(conv.Title);
                    conv.Messages = conv.Messages ?? new JArray();
                    _chatHistory.Add(conv);
                }

                _chatHistory = _chatHistory
                    .OrderByDescending(c => c.UpdatedAtUtc)
                    .ThenByDescending(c => c.CreatedAtUtc)
                    .ToList();
            }
            catch (Exception ex)
            {
                AddGhLog.Warn("LoadChatHistoryStore failed: " + ex.Message);
            }
        }

        private static void SaveChatHistoryStore()
        {
            try
            {
                string path = GetChatHistoryFilePath();
                var root = new JObject
                {
                    ["conversations"] = JArray.FromObject(_chatHistory)
                };
                System.IO.File.WriteAllText(path, root.ToString(Formatting.Indented), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AddGhLog.Warn("SaveChatHistoryStore failed: " + ex.Message);
            }
        }

        private static void SyncActiveHistoryConversation(string titleSeed = null)
        {
            if (_isHistoryRestoring) return;
            if (_messages == null || _messages.Count == 0) return;

            var conv = GetOrCreateActiveHistoryConversation(titleSeed);
            if (conv == null) return;

            var payload = new JArray();
            foreach (var msg in _messages)
            {
                string role = ChatMessageHelpers.TryGetRole(msg);
                if (string.Equals(role, "system", StringComparison.OrdinalIgnoreCase)) continue;
                payload.Add(JToken.FromObject(msg));
            }

            conv.Messages = payload;
            if (string.IsNullOrWhiteSpace(conv.Title))
                conv.Title = NormalizeConversationTitle(titleSeed);
            if (string.Equals(conv.Title, "新对话", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(titleSeed))
                conv.Title = NormalizeConversationTitle(titleSeed);
            conv.UpdatedAtUtc = DateTime.UtcNow;

            _chatHistory = _chatHistory
                .OrderByDescending(c => c.UpdatedAtUtc)
                .ThenByDescending(c => c.CreatedAtUtc)
                .ToList();

            SaveChatHistoryStore();
            if (_isHistorySidebarVisible) RefreshHistorySidebar();
        }

        private static void OpenHistoryConversation(string conversationId)
        {
            var conv = FindHistoryConversation(conversationId);
            if (conv == null) return;

            _isHistoryRestoring = true;
            try
            {
                _activeHistoryId = conv.Id;
                _messages = new List<object>(BuildInitialSystemMessages());
                foreach (var token in conv.Messages ?? new JArray())
                {
                    if (token is JObject jo) _messages.Add(jo.DeepClone());
                    else _messages.Add(token);
                }

                if (_chatPanel != null) _chatPanel.Children.Clear();
                RefreshUI();
                if (_txtInput != null) _txtInput.Text = "";
                RefreshContextMeter();
                UpdateHistorySidebarSelection();
            }
            finally
            {
                _isHistoryRestoring = false;
            }
        }

        private static void DeleteHistoryConversation(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return;
            var removed = _chatHistory.FirstOrDefault(c => string.Equals(c.Id, conversationId, StringComparison.OrdinalIgnoreCase));
            if (removed == null) return;

            _chatHistory.Remove(removed);
            if (string.Equals(_activeHistoryId, conversationId, StringComparison.OrdinalIgnoreCase))
            {
                _activeHistoryId = null;
                if (_messages != null)
                {
                    _messages.Clear();
                    _messages.AddRange(BuildInitialSystemMessages());
                    if (_chatPanel != null) _chatPanel.Children.Clear();
                    AppendSystemMessage("当前对话已删除，已切换到新会话。");
                    RefreshContextMeter();
                }
            }

            SaveChatHistoryStore();
            RefreshHistorySidebar();
        }

        private static void UpdateHistorySidebarSelection()
        {
            if (_historyListPanel == null) return;
            foreach (var child in _historyListPanel.Children.OfType<Border>())
            {
                string id = child.Tag as string;
                bool active = !string.IsNullOrWhiteSpace(id)
                    && string.Equals(id, _activeHistoryId, StringComparison.OrdinalIgnoreCase);
                child.BorderBrush = new SolidColorBrush(active ? Color.FromRgb(58, 58, 58) : Color.FromRgb(40, 40, 40));
                child.Background = new SolidColorBrush(active ? Color.FromRgb(26, 26, 26) : Color.FromRgb(23, 23, 23));
            }
        }

        private static Button CreateHistoryActionButton(string text, bool danger = false)
        {
            var button = new Button
            {
                Content = text,
                Foreground = new SolidColorBrush(danger ? Color.FromRgb(190, 190, 190) : Color.FromRgb(208, 208, 208)),
                Background = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
                BorderBrush = new SolidColorBrush(danger ? Color.FromRgb(52, 52, 52) : Color.FromRgb(44, 44, 44)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 0, 0),
                Cursor = Cursors.Hand,
                FontSize = 10.5
            };
            button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
                <ControlTemplate TargetType=""Button"" xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                    <Border Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}"" CornerRadius=""10"">
                        <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Margin=""{TemplateBinding Padding}""/>
                    </Border>
                </ControlTemplate>");
            return button;
        }

        private static void RefreshHistorySidebar()
        {
            if (_historySidebar == null || _historyListPanel == null) return;

            _historyListPanel.Children.Clear();
            if (_historyCountText != null)
                _historyCountText.Text = _chatHistory.Count.ToString() + " 条";

            if (_chatHistory.Count == 0)
            {
                _historyListPanel.Children.Add(new TextBlock
                {
                    Text = "暂无本地对话",
                    Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 110)),
                    FontSize = 12,
                    Margin = new Thickness(2, 10, 2, 0)
                });
                UpdateHistorySidebarSelection();
                return;
            }

            foreach (var conv in _chatHistory.OrderByDescending(c => c.UpdatedAtUtc).ThenByDescending(c => c.CreatedAtUtc))
            {
                var card = new Border
                {
                    Tag = conv.Id,
                    Background = new SolidColorBrush(Color.FromRgb(23, 23, 23)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(11),
                    Margin = new Thickness(0, 0, 0, 10),
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    MaxWidth = 292
                };

                var cardGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel { Orientation = Orientation.Vertical };
                info.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(conv.Title) ? "新对话" : conv.Title,
                    Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                    FontSize = 13,
                    FontWeight = FontWeights.Medium,
                    TextWrapping = TextWrapping.Wrap
                });
                info.Children.Add(new TextBlock
                {
                    Text = GetConversationPreview(conv),
                    Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 5, 0, 0)
                });
                info.Children.Add(new TextBlock
                {
                    Text = FormatConversationTime(conv.UpdatedAtUtc),
                    Foreground = new SolidColorBrush(Color.FromRgb(98, 98, 98)),
                    FontSize = 10,
                    Margin = new Thickness(0, 8, 0, 0)
                });

                Grid.SetColumn(info, 0);
                cardGrid.Children.Add(info);

                var actions = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };

                var openBtn = CreateHistoryActionButton("打开");
                openBtn.Margin = new Thickness(0, 0, 0, 6);
                openBtn.Click += (s, e) => OpenHistoryConversation(conv.Id);

                var deleteBtn = CreateHistoryActionButton("删除", true);
                deleteBtn.Click += (s, e) => DeleteHistoryConversation(conv.Id);

                actions.Children.Add(openBtn);
                actions.Children.Add(deleteBtn);

                Grid.SetColumn(actions, 1);
                cardGrid.Children.Add(actions);

                card.Child = cardGrid;
                card.MouseLeftButtonUp += (s, e) =>
                {
                    if (e.Handled) return;
                    OpenHistoryConversation(conv.Id);
                };
                _historyListPanel.Children.Add(card);
            }

            UpdateHistorySidebarSelection();
        }

        private static void SetHistorySidebarVisible(bool visible)
        {
            if (_historySidebar == null) return;
            _isHistorySidebarVisible = visible;

            if (visible)
            {
                _historySidebar.Visibility = Visibility.Visible;
                _historySidebar.BeginAnimation(FrameworkElement.WidthProperty, null);
                _historySidebar.Width = 320;
                _historySidebar.Height = double.NaN;
                _historySidebar.VerticalAlignment = VerticalAlignment.Stretch;
                RefreshHistorySidebar();
            }
            else
            {
                _historySidebar.BeginAnimation(FrameworkElement.WidthProperty, null);
                _historySidebar.Width = 0;
                _historySidebar.Visibility = Visibility.Collapsed;
            }
        }

        private static void ToggleHistorySidebar()
        {
            SetHistorySidebarVisible(!_isHistorySidebarVisible);
        }

        private static void TxtInput_OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            try
            {
                if (TryConsumePasteAsAttachments(e))
                    e.Handled = true;
            }
            catch (Exception ex)
            {
                AddGhLog.Warn("Paste into input: " + ex.Message);
            }
        }

        /// <summary>
        /// WPF 粘贴时 <see cref="DataObjectPastingEventArgs.SourceDataObject"/> 有时与系统剪贴板不一致（例如资源管理器复制文件后 Ctrl+V），
        /// 故依次尝试事件源与 <see cref="Clipboard.GetDataObject"/>。
        /// </summary>
        private static IEnumerable<IDataObject> EnumeratePasteDataSources(DataObjectPastingEventArgs e)
        {
            if (e?.SourceDataObject != null)
                yield return e.SourceDataObject;
            IDataObject clip = null;
            try { clip = Clipboard.GetDataObject(); }
            catch (Exception ex) { AddGhLog.Debug("Clipboard.GetDataObject paste: " + ex.Message); yield break; }
            if (clip != null && !ReferenceEquals(clip, e?.SourceDataObject))
                yield return clip;
        }

        /// <summary>
        /// 将剪贴板中的文件路径或图片转为待发送附件；返回 true 时已 CancelCommand，不再往输入框插入文本。
        /// </summary>
        private static bool TryConsumePasteAsAttachments(DataObjectPastingEventArgs e)
        {
            foreach (IDataObject data in EnumeratePasteDataSources(e))
            {
                if (data == null) continue;

                if (data.GetDataPresent(DataFormats.FileDrop, true))
                {
                    var paths = data.GetData(DataFormats.FileDrop, true) as string[];
                    if (paths != null && paths.Length > 0)
                    {
                        string[] files = paths.Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                        if (files.Length > 0)
                        {
                            e.CancelCommand();
                            AddPendingAttachments(files);
                            if (_btnClearImage != null) _btnClearImage.Visibility = Visibility.Visible;
                            return true;
                        }
                    }
                }
            }

            foreach (IDataObject data in EnumeratePasteDataSources(e))
            {
                if (data == null) continue;
                try
                {
                    foreach (string fmt in data.GetFormats())
                    {
                        if (!string.Equals(fmt, "PNG", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(fmt, "image/png", StringComparison.OrdinalIgnoreCase))
                            continue;

                        object payload = data.GetData(fmt, false);
                        byte[] pngBytes = null;
                        if (payload is MemoryStream ms)
                            pngBytes = ms.ToArray();
                        else if (payload is byte[] barr)
                            pngBytes = barr;

                        if (pngBytes != null && pngBytes.Length > 16)
                        {
                            string tmpPath = Path.Combine(Path.GetTempPath(), "ADDGH_paste_" + DateTime.UtcNow.Ticks + "_" + Guid.NewGuid().ToString("n").Substring(0, 8) + ".png");
                            File.WriteAllBytes(tmpPath, pngBytes);
                            e.CancelCommand();
                            AddPendingAttachments(new[] { tmpPath });
                            if (_btnClearImage != null) _btnClearImage.Visibility = Visibility.Visible;
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AddGhLog.Debug("Paste PNG format scan: " + ex.Message);
                }
            }

            try
            {
                if (Clipboard.ContainsImage())
                {
                    BitmapSource img = Clipboard.GetImage();
                    if (img != null)
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(img));
                        string tmpPath = Path.Combine(Path.GetTempPath(), "ADDGH_paste_" + DateTime.UtcNow.Ticks + "_" + Guid.NewGuid().ToString("n").Substring(0, 8) + ".png");
                        using (var fs = new FileStream(tmpPath, FileMode.CreateNew))
                            encoder.Save(fs);
                        e.CancelCommand();
                        AddPendingAttachments(new[] { tmpPath });
                        if (_btnClearImage != null) _btnClearImage.Visibility = Visibility.Visible;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                AddGhLog.Warn("Paste Clipboard.GetImage: " + ex.Message);
            }

            return false;
        }

        private static StackPanel BuildToolOperationCardsPanel(List<(string primary, string secondary)> entries)
        {
            if (entries == null || entries.Count == 0) return null;
            var stack = new StackPanel {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            foreach (var tup in entries) {
                string primary = tup.primary ?? "";
                string secondary = tup.secondary ?? "";
                if (string.IsNullOrWhiteSpace(primary)) continue;

                var row = new Border {
                    Background = new SolidColorBrush(Color.FromRgb(22, 22, 22)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(48, 48, 48)),
                    BorderThickness = new Thickness(0.5),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(0, 0, 0, 4),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    MinHeight = 22,
                    ToolTip = string.IsNullOrWhiteSpace(secondary) ? primary : (primary + " · " + secondary)
                };

                var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var primaryTb = new TextBlock {
                    Text = primary,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 148, 148)),
                    FontSize = 11,
                    FontWeight = FontWeights.Normal,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Left
                };
                Grid.SetColumn(primaryTb, 0);

                var secondaryTb = new TextBlock {
                    Text = secondary,
                    Foreground = new SolidColorBrush(Color.FromRgb(105, 105, 105)),
                    FontSize = 9.5,
                    FontWeight = FontWeights.Normal,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 120,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Right,
                    Visibility = string.IsNullOrWhiteSpace(secondary) ? Visibility.Collapsed : Visibility.Visible
                };
                Grid.SetColumn(secondaryTb, 1);

                grid.Children.Add(primaryTb);
                grid.Children.Add(secondaryTb);
                row.Child = grid;
                stack.Children.Add(row);
            }

            return stack.Children.Count > 0 ? stack : null;
        }

        private static void InsertChatElementBeforeThinking(FrameworkElement element)
        {
            if (element == null || _chatPanel == null) return;
            if (_thinkingBubble != null) {
                _chatPanel.Children.Remove(_thinkingBubble);
                _chatPanel.Children.Add(element);
                _chatPanel.Children.Add(_thinkingBubble);
            } else {
                _chatPanel.Children.Add(element);
            }
            if (_chatScroll != null) _chatScroll.ScrollToEnd();
        }

        private static void AppendToolOperationCards(List<(string primary, string secondary)> entries)
        {
            StackPanel stack = BuildToolOperationCardsPanel(entries);
            if (stack == null) return;
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => InsertChatElementBeforeThinking(stack)));
        }

        private static object GetCreateScriptComponentGraphToolDefinition()
        {
            var portSchema = new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", description = "Port name; also applied to Name and NickName." },
                    type_hint = new { type = "string", description = "可选：端口类型提示，仅写入 Description，不做强类型约束。" }
                },
                required = new[] { "name" }
            };

            return new
            {
                type = "function",
                function = new
                {
                    name = "create_script_component_graph",
                    description = "Create one or more Python 3 Script components with ports, source, helper components, connections, and group. Use this in mixed mode only when Python scripting is clearly better than native GH components. Python uses Rhino 8 Python 3 Script only; do not fall back to GhPython. For C# Script use create_csharp_script_component instead.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            mode = new { type = "string", description = "csharp | python。csharp 创建 C# Script；python 创建 Python 3 Script。" },
                            scripts = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        alias_id = new { type = "string", description = "脚本电池临时代号，供 connections 引用，必须唯一。" },
                                        label = new { type = "string", description = "可选：脚本电池 NickName。" },
                                        x = new { type = "number", description = "画布 X 坐标。" },
                                        y = new { type = "number", description = "画布 Y 坐标。" },
                                        source = new { type = "string", description = "脚本源码。C# 只提供 RunScript 方法内部语句，不要包含 using、Script_Instance 类或 RunScript 签名；Python 3 写入 Text。" },
                                        inputs = new { type = "array", items = portSchema },
                                        output_count = new { type = "integer", description = "已弃用：C# Script 请改用 create_csharp_script_component。" },
                                        outputs = new { type = "array", items = portSchema, description = "输出端口。C# 模式下不要填写，会被忽略；Python 模式可填写。" }
                                    },
                                    required = new[] { "alias_id", "x", "y", "source" }
                                }
                            },
                            components = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        alias_id = new { type = "string", description = "辅助电池临时代号，供 connections 引用，必须唯一。" },
                                        name = new { type = "string", description = "Helper component standard name; alternative to component_guid." },
                                        component_guid = new { type = "string", description = "Optional type GUID; alternative to name." },
                                        label = new { type = "string", description = "仅限 Slider/Panel 的显示标签。" },
                                        x = new { type = "number", description = "画布 X 坐标。" },
                                        y = new { type = "number", description = "画布 Y 坐标。" },
                                        value = new { type = "string", description = "Slider/Panel 初值。" },
                                        min = new { type = "number", description = "Slider 最小值。" },
                                        max = new { type = "number", description = "Slider 最大值。" },
                                        decimals = new { type = "integer", description = "Slider 小数位数。" }
                                    },
                                    required = new[] { "alias_id", "x", "y" }
                                }
                            },
                            connections = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        from_alias = new { type = "string", description = "源电池临时代号。" },
                                        from_index = new { type = "integer", description = "源输出端口索引，从 0 开始。" },
                                        to_alias = new { type = "string", description = "目标电池临时代号。" },
                                        to_index = new { type = "integer", description = "目标输入端口索引，从 0 开始。" }
                                    },
                                    required = new[] { "from_alias", "from_index", "to_alias", "to_index" }
                                }
                            },
                            group_name = new { type = "string", description = "可选：自动为这些电池打组。" },
                            summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                            summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                        },
                        required = new[] { "mode", "scripts", "connections", "summary" }
                    }
                }
            };
        }

        private static object GetCreateCSharpScriptComponentToolDefinition()
        {
            var inputPortSchema = new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", description = "C# input variable name. Must be a valid identifier and must not collide with reserved/output variables a,b,c..." },
                    type_hint = new { type = "string", description = "Optional type hint written to the port description only. No strong type is forced." }
                },
                required = new[] { "name" }
            };

            var outputPortSchema = new
            {
                type = "object",
                properties = new
                {
                    label = new { type = "string", description = "Business label for this output. The actual C# variable name is forced to b,c,d... and this label is written to the port description." },
                    name = new { type = "string", description = "Optional alias for label. It is not used as the C# variable name." },
                    type_hint = new { type = "string", description = "Optional output type hint written to the port description only." }
                }
            };

            var helperComponentSchema = new
            {
                type = "object",
                properties = new
                {
                    alias_id = new { type = "string", description = "Temporary alias used by connections." },
                    name = new { type = "string", description = "Grasshopper component name. In C# priority mode only Params and Display categories are allowed." },
                    component_guid = new { type = "string", description = "Optional component type GUID. In C# priority mode only Params and Display categories are allowed." },
                    label = new { type = "string", description = "Optional label, mainly for Slider/Panel." },
                    x = new { type = "number", description = "Canvas X coordinate." },
                    y = new { type = "number", description = "Canvas Y coordinate." },
                    value = new { type = "string", description = "Initial Slider/Panel value." },
                    min = new { type = "number", description = "Slider minimum." },
                    max = new { type = "number", description = "Slider maximum." },
                    decimals = new { type = "integer", description = "Slider decimal places." }
                },
                required = new[] { "alias_id", "x", "y" }
            };

            var connectionSchema = new
            {
                type = "object",
                properties = new
                {
                    from_alias = new { type = "string", description = "Source alias." },
                    from_index = new { type = "integer", description = "Source output index, zero based." },
                    to_alias = new { type = "string", description = "Target alias." },
                    to_index = new { type = "integer", description = "Target input index, zero based." }
                },
                required = new[] { "from_alias", "from_index", "to_alias", "to_index" }
            };

            return new
            {
                type = "function",
                function = new
                {
                    name = "create_csharp_script_component",
                    description = "Dedicated C# Script layout tool. It first creates a default C# Script component, waits briefly for Grasshopper/Rhino 8 to finish initializing it, then applies the requested component name, input ports, extra output ports, and RunScript body. Default C# outputs such as out/a are preserved; requested business outputs are added as b,c,d... and those are the variables to assign. It intentionally skips connections during creation; connect components later after the script component is stable. Use this instead of create_script_component_graph for C# priority modeling.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            alias_id = new { type = "string", description = "Temporary alias for the C# Script component. Defaults to core when omitted." },
                            name = new { type = "string", description = "Optional C# Script component nickname applied after the default component has initialized." },
                            label = new { type = "string", description = "Deprecated alias for name. Prefer name." },
                            x = new { type = "number", description = "Canvas X coordinate." },
                            y = new { type = "number", description = "Canvas Y coordinate." },
                            inputs = new { type = "array", items = inputPortSchema },
                            outputs = new { type = "array", items = outputPortSchema, description = "Business output labels. Default out/a ports are preserved; requested outputs are added as b,c,d... in this order. Do not assign to a." },
                            body = new { type = "string", description = "Only the RunScript method body. No using statements, no class declaration, no RunScript signature, no template." },
                            components = new { type = "array", items = helperComponentSchema },
                            connections = new { type = "array", items = connectionSchema, description = "Optional. Currently skipped during C# creation for stability; use a later connection tool call after creation." },
                            group_name = new { type = "string", description = "Optional group name." },
                            summary = new { type = "string", description = "Required short Chinese summary for the UI operation card. Do not write the function name." },
                            summary_detail = new { type = "string", description = "Optional short secondary phrase for the UI operation card." }
                        },
                        required = new[] { "x", "y", "body", "summary" }
                    }
                }
            };
        }

        private static object GetEditCSharpScriptComponentToolDefinition()
        {
            return new
            {
                type = "function",
                function = new
                {
                    name = "edit_csharp_script_component",
                    description = "Dedicated safe editor for existing Grasshopper C# Script components. read_body returns only the editable RunScript body when available. set_body replaces only the editable RunScript body while preserving the built-in C# Script template, using statements, class declaration, and GH-managed RunScript signature. Never pass a full C# file, class, using block, or RunScript signature.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            id = new { type = "string", description = "C# Script component InstanceGuid." },
                            mode = new { type = "string", description = "read_body | set_body" },
                            body = new { type = "string", description = "Required for set_body: only the RunScript method body. No using statements, no class declaration, no RunScript signature, no template." },
                            summary = new { type = "string", description = "Required short Chinese summary for the UI operation card. Do not write the function name." },
                            summary_detail = new { type = "string", description = "Optional short secondary phrase for the UI operation card." }
                        },
                        required = new[] { "id", "mode", "summary" }
                    }
                }
            };
        }

        private static string GetToolDefinitionName(object toolDefinition)
        {
            try
            {
                JObject jo = JObject.FromObject(toolDefinition);
                return jo["function"]?["name"]?.ToString();
            }
            catch (Exception ex)
            {
                AddGhLog.Debug("GetToolDefinitionName failed: " + ex.Message);
                return null;
            }
        }

        private static object[] FilterToolsForLayoutMode(object[] toolDefinitions)
        {
            if (toolDefinitions == null) return toolDefinitions;

            var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_layoutMode == LayoutMode.Battery)
            {
                blocked.Add("create_csharp_script_component");
            }
            else if (_layoutMode == LayoutMode.CSharpFirst)
            {
                blocked.Add("create_component_graph");
                blocked.Add("create_script_component_graph");
                blocked.Add("gh_native_script_editor");
                blocked.Add("read_skill_file");
                blocked.Add("read_reference_json");
                blocked.Add("create_gh_skill");
                blocked.Add(ShowReferenceOptionsTool.FunctionName);
            }

            return toolDefinitions
                .Where(t => !blocked.Contains(GetToolDefinitionName(t) ?? ""))
                .Select(RestrictAddComponentToolForScriptMode)
                .ToArray();
        }

        private static object RestrictAddComponentToolForScriptMode(object toolDefinition)
        {
            string name = GetToolDefinitionName(toolDefinition);
            if (_layoutMode != LayoutMode.CSharpFirst || !string.Equals(name, "add_gh_component", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(name, "set_gh_component_value", StringComparison.OrdinalIgnoreCase) && _layoutMode == LayoutMode.CSharpFirst)
                {
                    JObject setJo = JObject.FromObject(toolDefinition);
                    var setFn = setJo["function"] as JObject;
                    if (setFn != null)
                    {
                        setFn["description"] = "C# 优先模式下的受限辅助值设置工具：仅用于 Slider、Panel 等非脚本辅助电池的数值或显示文本。严禁用它写入 C# Script 源码；修改 C# Script 方法体必须使用 edit_csharp_script_component。";
                    }
                    return setJo;
                }
                return toolDefinition;
            }

            JObject jo = JObject.FromObject(toolDefinition);
            var fn = jo["function"] as JObject;
            if (fn != null)
            {
                fn["description"] = "C# priority mode helper tool: only add Params or Display components as inputs, outputs, preview, or debugging helpers for C# Script. Do not create core modeling components here; put core logic in create_csharp_script_component.";
            }
            return jo;
        }

        private static async Task<ApiResponse> CallLLMAPI(string apiKey, int depth = 0, System.Threading.CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            const int MAX_DEPTH = 50;
            if (depth >= MAX_DEPTH) 
            {
                AppendQuietDiagnosticCard("对话请求", "已达对话轮数安全上限（50 轮）。如需继续，请点击“继续”。");
                return new ApiResponse { 
                    Content = "已达对话轮数安全上限 (50轮)。如需继续，请点击‘继续’。" 
                };
            }

            // 警告模式：当连续操作较多时弹出提醒
            if (depth >= 30)
            {
                Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                    if (_txtWarning != null) _txtWarning.Text = $"长序列任务处理中 (第 {depth} 步)...";
                    if (_warningBar != null) _warningBar.Visibility = Visibility.Visible;
                }));
            }
            else if (depth == 0)
            {
                // 新请求开始，隐藏警告
                Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                    if (_warningBar != null) _warningBar.Visibility = Visibility.Collapsed;
                }));
            }

            var providerSettings = GetProviderRuntimeSettings();
            if (string.IsNullOrWhiteSpace(apiKey)) apiKey = providerSettings.ApiKey;
            providerSettings.ApiKey = apiKey;
            if (string.IsNullOrWhiteSpace(providerSettings.ApiKey))
            {
                return ReturnProviderError(providerSettings, "LLM 配置错误",
                    $"请先配置 {providerSettings.Config.DisplayName} 的 API Key。");
            }

            ApplyMechanicalContextCompressionIfNeeded();
            RefreshContextMeter();
            var messagesToSend = ChatMessageHelpers.ProjectMessagesForSend(_messages);

                    object[] toolDefinitions = new object[]
                    {
                        new {
                            type = "function",
                            function = new {
                                name = "ensure_gh_canvas",
                                description = "确保当前存在可用的 Grasshopper 画布。若未检测到可用画布，则新建一个空白 GH 画布并设为当前画布。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "summary" }
                                }
                            }
                        },
                        GetCreateCSharpScriptComponentToolDefinition(),
                        GetEditCSharpScriptComponentToolDefinition(),
                        GetCreateScriptComponentGraphToolDefinition(),
                        new {
                            type = "function",
                            function = new {
                                name = "get_gh_components",
                                description = "获取当前 Grasshopper 画布的完整 JSON：电池、端口、连线、运行时错误；对脚本/表达式类实例尽可能附带 script_bodies（截断后文本，含属性/字段名）。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "add_gh_component",
                                description = "在画布上创建**单个** Grasshopper 电池。适合只落一颗、占位/定位，或必须先看清画布反馈再决定下一步；**多颗电池且要带连线请优先用 create_component_graph 一次完成**。必须提供 name 或 component_guid 之一。默认只用 **name**。**不要**为普通电池先查 catalog；仅当已确认同名歧义或必须放置脚本/表达式类且 name 无法区分时，再用 search_gh_component_catalog 的 **component_guid**。Slider/Panel 必须提供 label。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        name = new { type = "string", description = "电池标准名称（与 component_guid 二选一）" },
                                        component_guid = new { type = "string", description = "可选：组件库**类型** GUID（与 name 二选一）。多数情况不必填；同名冲突或脚本电池时再取自 search_gh_component_catalog 的 guid。" },
                                        x = new { type = "number", description = "画布 X 坐标" },
                                        y = new { type = "number", description = "画布 Y 坐标" },
                                        label = new { type = "string", description = "仅限 Slider/Panel 的显示标签。普通电池严禁使用。" },
                                        graph_mapper_type = new { type = "string", description = "可选：Graph Mapper 曲线类型。Graph Mapper 未指定时默认 Bezier；可填 Bezier、Linear、Parabola、Sine、Gaussian、Power、Square Root 等。" },
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "x", "y", "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "connect_gh_components",
                                description = "在两个**已有**电池的端口之间建立一条连接；常用于补连、改线或接入已有实例 id。若在同一次任务里**新建多颗电池及它们之间的多条连线**，优先在同一次 create_component_graph 的 connections 里完成，避免多轮少量 add 再少量 connect。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        from_id = new { type = "string", description = "源电池的 GUID" },
                                        from_index = new { type = "integer", description = "源电池输出端口索引 (从0开始)" },
                                        to_id = new { type = "string", description = "目标电池的 GUID" },
                                        to_index = new { type = "integer", description = "目标电池输入端口索引 (从0开始)" },
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "from_id", "from_index", "to_id", "to_index", "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "remove_gh_component",
                                description = "从画布上删除指定的电池。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        id = new { type = "string", description = "要删除的电池 GUID" },
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "id", "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "set_gh_component_value",
                                description = "用于： Slider/Panel 的数值或显示文本； **仅当**用户明确要求或方案必需时，向 Evaluate/Expression/C#/Python/VB 等**脚本或表达式电池实例**写入代码/公式。**GhPython、Rhino Python 3 Script：可执行源码在 `Text`，严禁用 `Description`（摘要/元数据）；未指定 property 时会优先匹配 `Text`。** 默认按成员名启发式匹配可写 string 属性或字段；若失败，错误信息会列出候选名。可用 property 精确指定成员名。写完后会触发求解与延迟再算以尽量使脚本执行。**读代码**请用 get_gh_components（含 script_bodies 字段）。Slider 可同时设置 min/max/decimals。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        id = new { type = "string", description = "电池 GUID" },
                                        value = new { type = "string", description = "脚本/表达式/Panel：要写入的完整文本或代码（多行可用 \\n）。Panel 必填；Slider 填数字字符串。" },
                                        property = new { type = "string", description = "可选：精确写入的成员名（属性或字段，大小写不敏感）。Python/GhPython/Python 3 Script 填 **Text**；勿填 Description。其它如 Code、Script、PythonCode 等以 get_gh_components 提示为准；不填则启发式自动选（会优先 Text）。" },
                                        graph_mapper_type = new { type = "string", description = "可选：当 id 是 Graph Mapper 时设置曲线类型；未指定时默认 Bezier。也可把类型写在 value 中。" },
                                        min = new { type = "number", description = "可选：Slider 最小值" },
                                        max = new { type = "number", description = "可选：Slider 最大值" },
                                        decimals = new { type = "integer", description = "可选：Slider 小数位数（0-10）" },
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "id", "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "remove_gh_connection",
                                description = "断开两个电池之间的连线。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        from_id = new { type = "string", description = "源电池 GUID" },
                                        from_index = new { type = "integer", description = "源电池输出端口索引" },
                                        to_id = new { type = "string", description = "目标电池 GUID" },
                                        to_index = new { type = "integer", description = "目标电池输入端口索引" },
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "from_id", "from_index", "to_id", "to_index", "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "create_component_graph",
                                description = "【推荐】**新建一整块逻辑时优先**：一次性提交多个电池与它们之间的连线，优于多轮「少量 add_gh_component ↔ 少量 connect_gh_components」。适合构建复杂或局部的几何逻辑。每个 component 须提供 alias_id、x、y，以及 **name**（常用）；**不要**默认填 component_guid。仅同名或脚本类 name 无法区分时才用 guid。示例：{\"components\":[{\"alias_id\":\"pt1\",\"name\":\"Construct Point\",\"x\":0,\"y\":0},{\"alias_id\":\"crv1\",\"name\":\"Circle CNR\",\"x\":200,\"y\":0,\"value\":\"5\"}],\"connections\":[{\"from_alias\":\"pt1\",\"from_index\":0,\"to_alias\":\"crv1\",\"to_index\":0}]}",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        components = new {
                                            type = "array",
                                            items = new {
                                                type = "object",
                                                properties = new {
                                                    alias_id = new { type = "string", description = "临时代号(如 'pt1', 'crv1')，用于连线引用，必须唯一" },
                                                    name = new { type = "string", description = "电池标准名称（与 component_guid 二选一）" },
                                                    component_guid = new { type = "string", description = "可选：类型 GUID（与 name 二选一）。一般只写 name；同名或脚本电池再填 guid。" },
                                                    label = new { type = "string", description = "仅限 Slider/Panel 的显示标签。普通电池严禁使用。" },
                                                    x = new { type = "number", description = "画布 X 坐标" },
                                                    y = new { type = "number", description = "画布 Y 坐标" },
                                                    value = new { type = "string", description = "可选：Slider/Panel 初值；**脚本类电池（含 Python 3 Script）的源码**（写入 **Text**，勿当 Description）。" },
                                                    graph_mapper_type = new { type = "string", description = "可选：Graph Mapper 曲线类型。未指定时默认 Bezier；也可把类型写在 value 中。" },
                                                    min = new { type = "number", description = "可选：如果是 Slider，设置最小值" },
                                                    max = new { type = "number", description = "可选：如果是 Slider，设置最大值" },
                                                    decimals = new { type = "integer", description = "可选：如果是 Slider，设置小数位数" }
                                                },
                                                required = new[] { "alias_id", "x", "y" }
                                            }
                                        },
                                        connections = new {
                                            type = "array",
                                            items = new {
                                                type = "object",
                                                properties = new {
                                                    from_alias = new { type = "string", description = "源电池的临时代号" },
                                                    from_index = new { type = "integer", description = "源电池输出端口索引，从0开始" },
                                                    to_alias = new { type = "string", description = "目标电池的临时代号" },
                                                    to_index = new { type = "integer", description = "目标电池输入端口索引，从0开始" }
                                                },
                                                required = new[] { "from_alias", "from_index", "to_alias", "to_index" }
                                            }
                                        },
                                        group_name = new { type = "string", description = "可选：提供名称将自动为这些电池打组" },
                                        auto_group = new { type = "boolean", description = "可选：是否自动根据任务成组 (默认 false)" },
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "components", "connections", "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "recompute_gh_canvas",
                                description = "手动触发当前 Grasshopper 文档重新求解（写脚本/公式后若未执行或预览未更新可先试），不修改任何电池。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "gh_native_script_editor",
                                description = "针对 **Grasshopper 内置 C#/VB Script**：`open_focus` 打开/聚焦 GH_ScriptEditor；`read_source` **只从电池实例反射可读脚本正文**（与 get_gh_components.script_bodies 同源，**不调用**编辑器 GetSourceCode，避免读取崩溃）；`set_source_commit` 走编辑器并仅替换首个可编辑块。**read_source→改返回的 primary_for_edit→set_source_commit** 为推荐流程。**GhPython / Python 3 Script** 请用 set_gh_component_value，源码属性为 **Text**（勿写 Description）。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        id = new { type = "string", description = "电池 InstanceGuid" },
                                        mode = new { type = "string", description = "open_focus | read_source（反射读，非编辑器） | set_source_commit" },
                                        code = new { type = "string", description = "仅在 set_source_commit 必填：要写入**首个可编辑区**的正文（多行可）；通常对应 RunScript 内逻辑。**不要**默认为整文件含 using/模板，除非已与 read_source 对照确认。" },
                                        language = new { type = "string", description = "可选：auto（默认）| cs | vb。仅在无法从电池反射到 GH_ScriptLanguage 时使用。" },
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "id", "mode", "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "check_gh_errors",
                                description = "检查当前画布是否存在运行时错误或警告。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "set_gh_component_status",
                                description = "控制电池的显示(Preview)和启用(Enabled)状态。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        id = new { type = "string", description = "电池 GUID" },
                                        preview = new { type = "boolean", description = "是否显示预览 (true为显示, false为隐藏)" },
                                        enabled = new { type = "boolean", description = "是否启用电池 (true为启用, false为禁用)" },
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "id", "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "modify_gh_component_ports",
                                description = "针对支持动态端口的电池增加或删除端口。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        id = new { type = "string", description = "电池 GUID" },
                                        is_input = new { type = "boolean", description = "true 为输入端口，false 为输出端口" },
                                        action = new { type = "string", description = "'add' 或 'remove'" },
                                        port_name = new { type = "string", description = "remove 时优先按此名称删除端口，匹配 Name / NickName（忽略大小写）" },
                                        index = new { type = "integer", description = "remove 的可选兜底索引；未提供 port_name 时使用" },
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "id", "is_input", "action", "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "manage_gh_groups",
                                description = "管理画布上的电池组(Group)。action 说明：'create'=创建新组（需要 ids 和 name），'ungroup'=解散组（需要 group_id），'add_to_group'=添加电池到组（需要 group_id 和 ids），'remove_from_group'=从组中移除电池（需要 group_id 和 ids）。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        action = new { type = "string", description = "'create', 'ungroup', 'add_to_group', 'remove_from_group'" },
                                        ids = new { type = "array", items = new { type = "string" }, description = "涉及的电池 GUID 列表，create/add_to_group/remove_from_group 时需要" },
                                        group_id = new { type = "string", description = "操作已有组时的组 GUID，ungroup/add_to_group/remove_from_group 时需要" },
                                        name = new { type = "string", description = "创建组时的显示名称，create 时需要" },
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "action", "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "modify_gh_port_data",
                                description = "修改电池端口的数据处理方式（如：拍平 Flatten, 成组 Graft, 精简 Simplify, 反转 Reverse）。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        id = new { type = "string", description = "电池 GUID" },
                                        is_input = new { type = "boolean", description = "true为输入端口，false为输出端口" },
                                        index = new { type = "integer", description = "端口索引 (从0开始)" },
                                        operation = new { type = "string", description = "操作类型: 'Flatten', 'Graft', 'Simplify', 'Reverse', 'None'" },
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "id", "is_input", "index", "operation", "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "search_component_library",
                                description = "根据关键词在 Grasshopper 电池库中搜索可用插件和电池名称（简短文本列表，最多约 15 条）。**日常找电池名首选**；不要用 search_gh_component_catalog 代替本接口做常规检索。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        keyword = new { type = "string", description = "搜索关键词" },
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "keyword", "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "search_gh_component_catalog",
                                description = "【非必要勿调用】仅在：① 要放置/核对**脚本或表达式类**电池类型；② 已确认 **add_gh_component 因同名失败** 必须用 guid 时。日常找电池名请优先 **search_component_library**（更轻）。返回 JSON（含 guid）；**不要**把本接口当作每次建模的前置步骤。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        query = new { type = "string", description = "匹配 name、nickname、category 或 subcategory 的关键词；脚本场景才用 Evaluate、C# 等，日常用 Point、Curve 等标准名即可" },
                                        max_results = new { type = "integer", description = "最大条数，默认 30，上限 200" },
                                        category_contains = new { type = "string", description = "可选：仅保留 category 或 subcategory 含此子串的项（如 Script）" },
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "query", "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "query_gh_components",
                                description = "按 id、组件名、报错状态、脚本组件、是否有连线、端口名等条件搜索当前 Grasshopper 画布，并仅返回命中的组件片段；可选附带一阶或二阶邻居摘要，适合先搜索再局部展开，不必每次读取整张画布 JSON。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        id = new { type = "string", description = "可选：组件 GUID，精确匹配。" },
                                        name_contains = new { type = "string", description = "可选：组件 Name 或 NickName 包含的关键字。" },
                                        has_errors = new { type = "boolean", description = "可选：是否只看有 Error/Warning 的组件。" },
                                        is_script = new { type = "boolean", description = "可选：是否只看脚本/表达式类组件。" },
                                        has_connections = new { type = "boolean", description = "可选：是否只看存在输入来源或输出接收者的组件。" },
                                        port_name_contains = new { type = "string", description = "可选：端口 Name/NickName 包含的关键字。" },
                                        max_results = new { type = "integer", description = "可选：最多返回多少个命中，默认 8，上限 50。" },
                                        neighbor_depth = new { type = "integer", description = "可选：返回命中组件邻居摘要的层数，0/1/2，默认 1。" },
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "get_component_context",
                                description = "按组件 id 读取该组件及其邻居的完整局部上下文。默认不展开脚本体，只返回结构、端口、连线、运行时消息；需要脚本正文时再单独调用 read_component_script。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        id = new { type = "string", description = "组件 GUID。" },
                                        depth = new { type = "integer", description = "可选：邻居层数，默认 1。" },
                                        include_script_bodies = new { type = "boolean", description = "可选：是否在局部上下文中直接附带 script_bodies；默认 false。" },
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "id", "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "read_component_script",
                                description = "单独读取某个脚本/表达式类组件可反射到的脚本正文。适合在 query_gh_components 或 get_component_context 定位到目标后，再递进展开脚本体。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        id = new { type = "string", description = "组件 GUID。" },
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "id", "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "create_gh_skill",
                                description = "将当前画布内容或特定逻辑总结为一个可复用的技能(Skill)并保存。文件开头必须包含 YAML Frontmatter 格式的 name 和 description。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        file_name = new { type = "string", description = "保存的文件名（英文，以 .md 结尾，如 'connect_points.md'）" },
                                        name = new { type = "string", description = "技能名称（如 '连点成线'）" },
                                        description = new { type = "string", description = "简短的技能描述" },
                                        content = new { type = "string", description = "技能的详细内容，包括使用的电池、连线逻辑、注意事项等（Markdown 格式）" },
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "file_name", "name", "description", "content", "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "read_skill_file",
                                description = "读取 skills 目录中的 skill 文件，获取详细的操作指南和最佳实践。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        file_name = new { type = "string", description = "要读取的 skill 文件名，如 'general_modeling.md' 或 'workflow_optimization.md'" },
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "file_name", "summary" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "read_reference_json",
                                description = "读取 reference 目录中的参考画布 JSON。应在建模/连线逻辑已规划清楚之后再调用；可先 read_skill_file 读取 reference_index.md 选定 file_name。不要在尚未形成方案时抢先读参考。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        file_name = new { type = "string", description = "reference 目录下的 JSON 文件名，如 'ref_20260503123000.json'" },
                                        summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                        summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                                    },
                                    required = new[] { "file_name", "summary" }
                                }
                            }
                        },
                        ShowReferenceOptionsTool.GetApiToolDefinition()
                    };
                    toolDefinitions = FilterToolsForLayoutMode(toolDefinitions);

                ShowThinkingAnimation("载入中...");
                DateTime startTime = DateTime.Now;
                
                HttpResponseMessage response;
                string usedEndpoint = null;
                string lastEndpointError = null;
                try
                {
                    response = null;
                    foreach (var endpoint in BuildEndpointCandidates(providerSettings.BaseUrl))
                    {
                        ct.ThrowIfCancellationRequested();
                        usedEndpoint = endpoint.Url;
                        JObject requestBody = BuildChatRequestBody(providerSettings, messagesToSend, toolDefinitions);
                        AddGhLog.Info("Trying LLM endpoint: " + endpoint.Url + ", model=" + providerSettings.ModelName);

                        response = await SendProviderRequestAsync(providerSettings, requestBody, endpoint.Url, ct);
                        if (response.IsSuccessStatusCode)
                            break;

                        string errPreview = "";
                        try { errPreview = await response.Content.ReadAsStringAsync(); }
                        catch (Exception readEx) { errPreview = "无法读取错误响应体：" + readEx.Message; }

                        lastEndpointError = "HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase + "\n" + ClampDiagDetail(errPreview, 900);
                        AddGhLog.Warn("LLM endpoint failed: " + endpoint.Url + " | " + lastEndpointError.Replace("\r", " ").Replace("\n", " | "));

                        if (!ShouldTryNextEndpoint(response.StatusCode))
                        {
                            return ReturnProviderError(providerSettings, "LLM 连接错误",
                                "模型服务返回 HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase,
                                errPreview,
                                endpoint.Url);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return ReturnProviderError(providerSettings, "LLM 连接错误",
                        "请求未能发送到模型服务：" + ex.GetType().Name,
                        FormatExceptionChain(ex),
                        usedEndpoint);
                }
                
                ShowThinkingAnimation("思考中...");

                if (!response.IsSuccessStatusCode)
                {
                    return ReturnProviderError(providerSettings, "LLM 连接错误",
                        "模型服务返回 HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase,
                        lastEndpointError,
                        usedEndpoint);
                }

                // 使用流读取以支持即时取消
                string responseJson = "";
                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var reader = new System.IO.StreamReader(stream))
                {
                    var task = reader.ReadToEndAsync();
                    while (!task.IsCompleted) {
                        if (ct.IsCancellationRequested) {
                            ct.ThrowIfCancellationRequested();
                        }
                        await Task.Delay(50, ct);
                    }
                    responseJson = task.Result;
                }
                double durationSeconds = (DateTime.Now - startTime).TotalSeconds;

                if (!TryParseAssistantMessageFromResponse(responseJson, out JObject messageNode, out string parseError))
                {
                    return ReturnProviderError(providerSettings, "LLM 响应错误",
                        "模型服务返回的内容不是可解析的 OpenAI 聊天响应：" + parseError,
                        responseJson,
                        usedEndpoint);
                }

                string fullContent = messageNode["content"]?.ToString() ?? "";
                string fullReasoning = messageNode["reasoning_content"]?.ToString() ?? "";
                var fullToolCalls = messageNode["tool_calls"] as JArray ?? new JArray();

                if (string.IsNullOrWhiteSpace(fullContent) && string.IsNullOrWhiteSpace(fullReasoning) && fullToolCalls.Count == 0)
                {
                    return ReturnProviderError(providerSettings, "LLM 响应错误",
                        "模型服务返回成功，但消息内容、思考内容和工具调用都为空。",
                        responseJson,
                        usedEndpoint);
                }

                await _window.Dispatcher.InvokeAsync(() => {
                    if (!string.IsNullOrEmpty(fullReasoning))
                    {
                        AppendCollapsibleBubble(fullReasoning, "已思考 " + Math.Round(durationSeconds, 1) + "s", "💭");
                    }
                    if (!string.IsNullOrEmpty(fullContent))
                    {
                        AppendBubble(fullContent, false, depth == 0);
                    }
                });

                _messages.Add(messageNode);
                EnforceChatHistoryLimit();

                int addComp = 0, delComp = 0, addConn = 0, delConn = 0;

                if (fullToolCalls.Count > 0)
                {
                    ShowThinkingAnimation("工作中...");
                    var operationCards = new List<(string primary, string secondary)>();

                    foreach (var toolCall in fullToolCalls)
                    {
                        ct.ThrowIfCancellationRequested();
                        string funcName = toolCall["function"]?["name"]?.ToString();
                        string argsJson = toolCall["function"]?["arguments"]?.ToString();
                        string callId = toolCall["id"]?.ToString();

                        JObject argsObj = ChatMessageHelpers.ParseToolArgumentsForExecution(argsJson, out string cardSum, out string cardDet);
                        if (!string.IsNullOrWhiteSpace(cardSum))
                            operationCards.Add((cardSum, string.IsNullOrWhiteSpace(cardDet) ? "" : cardDet));

                        string toolResult = "";
                        try
                        {
                            if (funcName == "ensure_gh_canvas") toolResult = ExecuteEnsureGhCanvas();
                            else if (funcName == "get_gh_components") toolResult = ExecuteGetGhComponents();
                            else if (funcName == "recompute_gh_canvas") toolResult = ExecuteRecomputeGhCanvas();
                            else if (funcName == "gh_native_script_editor") {
                                toolResult = ExecuteGhNativeScriptEditor(
                                    argsObj["id"]?.ToString(),
                                    argsObj["mode"]?.ToString(),
                                    argsObj["code"]?.ToString(),
                                    argsObj["language"]?.ToString());
                            }
                            else if (funcName == "add_gh_component") {
                                string label = argsObj["label"]?.ToString();
                                string name = argsObj["name"]?.ToString();
                                string cguid = argsObj["component_guid"]?.ToString();
                                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(cguid))
                                    toolResult = "Error: 必须提供 name 或 component_guid。";
                                else {
                                    float x = argsObj["x"]?.ToObject<float>() ?? 0f;
                                    float y = argsObj["y"]?.ToObject<float>() ?? 0f;
                                    toolResult = ExecuteAddGhComponent(
                                        name ?? "",
                                        x,
                                        y,
                                        label,
                                        cguid,
                                        argsObj["graph_mapper_type"]?.ToString() ?? argsObj["graph_type"]?.ToString());
                                    if (!toolResult.StartsWith("Error:")) addComp++;
                                }
                            }
                            else if (funcName == "connect_gh_components") {
                                toolResult = ExecuteConnectGhComponents(
                                    argsObj["from_id"]?.ToString(),
                                    argsObj["from_index"]?.ToObject<int>() ?? 0,
                                    argsObj["to_id"]?.ToString(),
                                    argsObj["to_index"]?.ToObject<int>() ?? 0);
                                addConn++;
                            }
                            else if (funcName == "remove_gh_component") {
                                toolResult = ExecuteRemoveGhComponent(argsObj["id"]?.ToString());
                                delComp++;
                            }
                            else if (funcName == "set_gh_component_value") {
                                string val = argsObj["value"]?.ToString();
                                double? min = argsObj["min"] == null || argsObj["min"].Type == JTokenType.Null ? (double?)null : argsObj["min"].ToObject<double>();
                                double? max = argsObj["max"] == null || argsObj["max"].Type == JTokenType.Null ? (double?)null : argsObj["max"].ToObject<double>();
                                int? decimals = argsObj["decimals"] == null || argsObj["decimals"].Type == JTokenType.Null ? (int?)null : argsObj["decimals"].ToObject<int>();
                                toolResult = ExecuteSetGhComponentValue(
                                    argsObj["id"]?.ToString(),
                                    val,
                                    min,
                                    max,
                                    decimals,
                                    argsObj["property"]?.ToString(),
                                    argsObj["graph_mapper_type"]?.ToString() ?? argsObj["graph_type"]?.ToString());
                            }
                            else if (funcName == "remove_gh_connection") {
                                toolResult = ExecuteRemoveGhConnection(
                                    argsObj["from_id"]?.ToString(),
                                    argsObj["from_index"]?.ToObject<int>() ?? 0,
                                    argsObj["to_id"]?.ToString(),
                                    argsObj["to_index"]?.ToObject<int>() ?? 0);
                                delConn++;
                            }
                            else if (funcName == "create_component_graph") {
                                bool autoG = argsObj["auto_group"]?.ToObject<bool>() ?? false;
                                string gName = argsObj["group_name"]?.ToString();
                                if (string.IsNullOrEmpty(gName))
                                    gName = autoG ? "AI Generated" : null;
                                toolResult = ExecuteCreateComponentGraph(
                                    argsObj["components"] as JArray,
                                    argsObj["connections"] as JArray,
                                    gName);
                                if (argsObj["components"] is JArray comps) addComp += comps.Count;
                                if (argsObj["connections"] is JArray conns) addConn += conns.Count;
                            }
                            else if (funcName == "create_csharp_script_component") {
                                string csharpName = argsObj["name"]?.ToString();
                                if (string.IsNullOrWhiteSpace(csharpName)) csharpName = argsObj["label"]?.ToString();
                                toolResult = ExecuteCreateCSharpScriptComponent(
                                    argsObj["alias_id"]?.ToString(),
                                    csharpName,
                                    argsObj["x"]?.ToObject<float>() ?? 0f,
                                    argsObj["y"]?.ToObject<float>() ?? 0f,
                                    argsObj["inputs"] as JArray,
                                    argsObj["outputs"] as JArray,
                                    argsObj["body"]?.ToString(),
                                    argsObj["components"] as JArray,
                                    argsObj["connections"] as JArray,
                                    argsObj["group_name"]?.ToString());
                                if (!toolResult.StartsWith("Error:")) {
                                    addComp += 1;
                                    if (argsObj["components"] is JArray compsArr) addComp += compsArr.Count;
                                }
                            }
                            else if (funcName == "edit_csharp_script_component") {
                                toolResult = ExecuteEditCSharpScriptComponent(
                                    argsObj["id"]?.ToString(),
                                    argsObj["mode"]?.ToString(),
                                    argsObj["body"]?.ToString());
                            }
                            else if (funcName == "create_script_component_graph") {
                                toolResult = ExecuteCreateScriptComponentGraph(
                                    argsObj["mode"]?.ToString(),
                                    argsObj["scripts"] as JArray,
                                    argsObj["components"] as JArray,
                                    argsObj["connections"] as JArray,
                                    argsObj["group_name"]?.ToString());
                                if (!toolResult.StartsWith("Error:")) {
                                    if (argsObj["scripts"] is JArray scriptsArr) addComp += scriptsArr.Count;
                                    if (argsObj["components"] is JArray compsArr) addComp += compsArr.Count;
                                    if (argsObj["connections"] is JArray connsArr) addConn += connsArr.Count;
                                }
                            }
                            else if (funcName == "check_gh_errors") toolResult = ExecuteCheckGhErrors();
                            else if (funcName == "search_component_library")
                                toolResult = ExecuteSearchComponentLibrary(argsObj["keyword"]?.ToString());
                            else if (funcName == "search_gh_component_catalog") {
                                int maxR = argsObj["max_results"]?.ToObject<int?>() ?? 30;
                                string catc = argsObj["category_contains"]?.ToString();
                                toolResult = ExecuteSearchGhComponentCatalog(argsObj["query"]?.ToString(), maxR, catc);
                            }
                            else if (funcName == "query_gh_components") {
                                bool? hasErrors = argsObj["has_errors"] == null || argsObj["has_errors"].Type == JTokenType.Null
                                    ? (bool?)null : argsObj["has_errors"].ToObject<bool>();
                                bool? isScript = argsObj["is_script"] == null || argsObj["is_script"].Type == JTokenType.Null
                                    ? (bool?)null : argsObj["is_script"].ToObject<bool>();
                                bool? hasConnections = argsObj["has_connections"] == null || argsObj["has_connections"].Type == JTokenType.Null
                                    ? (bool?)null : argsObj["has_connections"].ToObject<bool>();
                                toolResult = ExecuteQueryGhComponents(
                                    argsObj["id"]?.ToString(),
                                    argsObj["name_contains"]?.ToString(),
                                    hasErrors,
                                    isScript,
                                    hasConnections,
                                    argsObj["port_name_contains"]?.ToString(),
                                    argsObj["max_results"]?.ToObject<int?>() ?? 8,
                                    argsObj["neighbor_depth"]?.ToObject<int?>() ?? 1);
                            }
                            else if (funcName == "get_component_context") {
                                bool includeScriptBodies = argsObj["include_script_bodies"]?.ToObject<bool?>() ?? false;
                                toolResult = ExecuteGetComponentContext(
                                    argsObj["id"]?.ToString(),
                                    argsObj["depth"]?.ToObject<int?>() ?? 1,
                                    includeScriptBodies);
                            }
                            else if (funcName == "read_component_script") {
                                toolResult = ExecuteReadComponentScript(argsObj["id"]?.ToString());
                            }
                            else if (funcName == "set_gh_component_status") {
                                bool? preview = argsObj["preview"] == null || argsObj["preview"].Type == JTokenType.Null
                                    ? (bool?)null : argsObj["preview"].ToObject<bool>();
                                bool? enabled = argsObj["enabled"] == null || argsObj["enabled"].Type == JTokenType.Null
                                    ? (bool?)null : argsObj["enabled"].ToObject<bool>();
                                toolResult = ExecuteSetGhComponentStatus(argsObj["id"]?.ToString(), preview, enabled);
                            }
                            else if (funcName == "modify_gh_component_ports") {
                                toolResult = ExecuteModifyGhComponentPorts(
                                    argsObj["id"]?.ToString(),
                                    argsObj["is_input"]?.ToObject<bool>() ?? false,
                                    argsObj["action"]?.ToString(),
                                    argsObj["port_name"]?.ToString(),
                                    argsObj["index"]?.ToObject<int?>());
                            }
                            else if (funcName == "modify_gh_port_data") {
                                toolResult = ExecuteModifyGhPortData(
                                    argsObj["id"]?.ToString(),
                                    argsObj["is_input"]?.ToObject<bool>() ?? false,
                                    argsObj["index"]?.ToObject<int>() ?? 0,
                                    argsObj["operation"]?.ToString());
                            }
                            else if (funcName == "manage_gh_groups") {
                                string gId = argsObj["group_id"]?.ToString();
                                string gName = argsObj["name"]?.ToString();
                                JArray idsArray = argsObj["ids"] as JArray;
                                List<string> idsList = idsArray?.Select(v => v.ToString()).ToList();
                                toolResult = ExecuteManageGhGroups(argsObj["action"]?.ToString(), idsList, gId, gName);
                            }
                            else if (funcName == "read_skill_file")
                                toolResult = ExecuteReadSkillFile(argsObj["file_name"]?.ToString());
                            else if (funcName == "read_reference_json")
                                toolResult = ExecuteReadReferenceJson(argsObj["file_name"]?.ToString());
                            else if (funcName == "create_gh_skill") {
                                toolResult = ExecuteCreateGhSkill(
                                    argsObj["file_name"]?.ToString(),
                                    argsObj["name"]?.ToString(),
                                    argsObj["description"]?.ToString(),
                                    argsObj["content"]?.ToString());
                            }
                            else if (funcName == ShowReferenceOptionsTool.FunctionName) {
                                var (refToolMsg, refEndRound) = ShowReferenceOptionsTool.Run(argsObj, argsJson, operationCards);
                                toolResult = refToolMsg;
                                if (refEndRound)
                                {
                                    _messages.Add(new { role = "tool", tool_call_id = callId, name = funcName, content = toolResult });
                                    EnforceChatHistoryLimit();
                                    SyncActiveHistoryConversation();
                                    return new ApiResponse { Content = fullContent, Reasoning = fullReasoning };
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            toolResult = "Error: " + ex.Message;
                            AddGhLog.Error("工具执行失败: " + (funcName ?? "?"), ex);
                        }

                        _messages.Add(new { role = "tool", tool_call_id = callId, name = funcName, content = toolResult });
                    }

                    EnforceChatHistoryLimit();

                    if (operationCards.Count > 0)
                        AppendToolOperationCards(operationCards);

                    if (addComp > 0 || delComp > 0 || addConn > 0 || delConn > 0) {
                        AppendColoredStatsMessage(addComp, delComp, addConn, delConn);
                    }

                    SyncActiveHistoryConversation();
                    ct.ThrowIfCancellationRequested();
                    return await CallLLMAPI(apiKey, depth + 1, ct);
                }

                SyncActiveHistoryConversation();
                return new ApiResponse { 
                    Content = fullContent, 
                    Reasoning = fullReasoning 
                };
        }

        private static string ExecuteEnsureGhCanvas()
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                try
                {
                    var currentDoc = Grasshopper.Instances.ActiveCanvas?.Document;
                    if (currentDoc != null)
                    {
                        result = "当前已存在可用 Grasshopper 画布。";
                        return;
                    }

                    try
                    {
                        var editor = Grasshopper.Instances.DocumentEditor;
                        if (editor != null)
                        {
                            var showMethod = editor.GetType().GetMethod("Show", Type.EmptyTypes);
                            showMethod?.Invoke(editor, null);
                        }
                    }
                    catch (Exception ex)
                    {
                        AddGhLog.Debug("DocumentEditor.Show fallback: " + ex.Message);
                    }

                    var doc = new Grasshopper.Kernel.GH_Document();
                    bool addedToServer = false;

                    var server = Grasshopper.Instances.DocumentServer;
                    if (server != null)
                    {
                        foreach (var method in server.GetType().GetMethods().Where(m => m.Name == "AddDocument"))
                        {
                            var parameters = method.GetParameters();
                            if (parameters.Length == 0 || !parameters[0].ParameterType.IsAssignableFrom(typeof(Grasshopper.Kernel.GH_Document))) continue;

                            object[] callArgs = new object[parameters.Length];
                            callArgs[0] = doc;
                            for (int i = 1; i < parameters.Length; i++)
                            {
                                callArgs[i] = parameters[i].ParameterType == typeof(bool) ? (object)true : Type.Missing;
                            }

                            method.Invoke(server, callArgs);
                            addedToServer = true;
                            break;
                        }
                    }

                    var canvas = Grasshopper.Instances.ActiveCanvas;
                    if (canvas != null)
                    {
                        var docProp = canvas.GetType().GetProperty("Document");
                        if (docProp != null && docProp.CanWrite)
                        {
                            docProp.SetValue(canvas, doc, null);
                        }
                        canvas.Refresh();
                    }

                    _canvasChanged = true;
                    _cachedCanvasState = null;
                    result = addedToServer
                        ? "未检测到可用画布，已新建空白 Grasshopper 画布。"
                        : "未检测到可用画布，已创建空白 Grasshopper 文档，但未能加入文档服务器。";
                }
                catch (Exception ex)
                {
                    result = "Error: 新建 Grasshopper 画布失败 - " + ex.Message;
                }
            }));
            return result;
        }

        private static string GetRhinoUnitSignature()
        {
            var rhinoDoc = Rhino.RhinoDoc.ActiveDoc;
            if (rhinoDoc == null) return "no-rhino-doc";
            return string.Join("|",
                rhinoDoc.ModelUnitSystem,
                rhinoDoc.PageUnitSystem,
                rhinoDoc.ModelAbsoluteTolerance.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                rhinoDoc.ModelRelativeTolerance.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                rhinoDoc.ModelAngleToleranceDegrees.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                rhinoDoc.PageAbsoluteTolerance.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                rhinoDoc.PageRelativeTolerance.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                rhinoDoc.PageAngleToleranceDegrees.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        private static JObject BuildRhinoUnitsJson()
        {
            var rhinoDoc = Rhino.RhinoDoc.ActiveDoc;
            if (rhinoDoc == null)
            {
                return new JObject { ["available"] = false };
            }

            return new JObject
            {
                ["available"] = true,
                ["model_unit_system"] = rhinoDoc.ModelUnitSystem.ToString(),
                ["model_unit_system_value"] = (int)rhinoDoc.ModelUnitSystem,
                ["page_unit_system"] = rhinoDoc.PageUnitSystem.ToString(),
                ["page_unit_system_value"] = (int)rhinoDoc.PageUnitSystem,
                ["model_absolute_tolerance"] = rhinoDoc.ModelAbsoluteTolerance,
                ["model_relative_tolerance"] = rhinoDoc.ModelRelativeTolerance,
                ["model_angle_tolerance_degrees"] = rhinoDoc.ModelAngleToleranceDegrees,
                ["page_absolute_tolerance"] = rhinoDoc.PageAbsoluteTolerance,
                ["page_relative_tolerance"] = rhinoDoc.PageRelativeTolerance,
                ["page_angle_tolerance_degrees"] = rhinoDoc.PageAngleToleranceDegrees
            };
        }

        private static string ExecuteGetGhComponents()
        {
            string currentUnitSignature = GetRhinoUnitSignature();
            if (!_canvasChanged && _cachedCanvasState != null && string.Equals(_cachedRhinoUnitSignature, currentUnitSignature, StringComparison.Ordinal)) {
                return _cachedCanvasState;
            }

            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }

                var graph = new JObject();
                if (DeploymentOptions.IncludeCanvasExportTimestamp)
                    graph["timestamp"] = DateTime.Now.ToString("HH:mm:ss");
                graph["rhino_units"] = BuildRhinoUnitsJson();
                
                var globalErrors = new JArray();
                var components = new JArray();
                var groups = new JArray(); // 存储组信息
                
                foreach (var obj in doc.Objects)
                {
                    if (obj is Grasshopper.Kernel.Special.GH_Group group)
                    {
                        var groupJson = new JObject();
                        groupJson["id"] = group.InstanceGuid.ToString();
                        groupJson["name"] = group.NickName;
                        var members = new JArray();
                        foreach (var memberId in group.Objects()) members.Add(memberId.ToString());
                        groupJson["members"] = members;
                        groups.Add(groupJson);
                        continue;
                    }

                    var compJson = new JObject();
                    compJson["name"] = obj.Name;
                    compJson["nickname"] = obj.NickName;
                    compJson["id"] = obj.InstanceGuid.ToString();
                    compJson["pivot"] = new JObject { { "x", Math.Round(obj.Attributes.Pivot.X) }, { "y", Math.Round(obj.Attributes.Pivot.Y) } };

                    // 检查报错
                    if (obj is IGH_ActiveObject ao && ao.RuntimeMessageLevel != GH_RuntimeMessageLevel.Blank)
                    {
                        var msgs = new JArray();
                        foreach (string m in ao.RuntimeMessages(GH_RuntimeMessageLevel.Error)) {
                            msgs.Add("Error: " + m);
                            globalErrors.Add(new JObject { { "id", obj.InstanceGuid.ToString() }, { "name", obj.Name }, { "level", "Error" }, { "message", m } });
                        }
                        foreach (string m in ao.RuntimeMessages(GH_RuntimeMessageLevel.Warning)) {
                            msgs.Add("Warning: " + m);
                            globalErrors.Add(new JObject { { "id", obj.InstanceGuid.ToString() }, { "name", obj.Name }, { "level", "Warning" }, { "message", m } });
                        }
                        compJson["runtime_messages"] = msgs;
                    }

                    if (obj is Grasshopper.Kernel.IGH_Component comp)
                    {
                        var inputs = new JArray();
                        for (int i = 0; i < comp.Params.Input.Count; i++)
                        {
                            var param = comp.Params.Input[i];
                            var paramJson = new JObject();
                            paramJson["index"] = i;
                            paramJson["name"] = param.Name;
                            paramJson["type"] = param.TypeName;
                            
                            // 增加数据结构概况
                            if (param.VolatileDataCount > 0) {
                                var tree = param.VolatileData;
                                paramJson["data_structure"] = $"Tree ({tree.PathCount} branches, {tree.DataCount} items total)";
                            } else {
                                paramJson["data_structure"] = "Empty";
                            }

                            // 增加数据操作状态
                            if (param.DataMapping == Grasshopper.Kernel.GH_DataMapping.Flatten) paramJson["is_flattened"] = true;
                            if (param.DataMapping == Grasshopper.Kernel.GH_DataMapping.Graft) paramJson["is_grafted"] = true;
                            if (param.Reverse) paramJson["is_reversed"] = true;
                            if (param.Simplify) paramJson["is_simplified"] = true;
                            
                            var sources = new JArray();
                            foreach (var source in param.Sources)
                            {
                                var srcObj = source.Attributes.GetTopLevel.DocObject;
                                int srcIdx = (srcObj is Grasshopper.Kernel.IGH_Component srcC) ? srcC.Params.Output.IndexOf(source) : 0;
                                sources.Add(new JObject { { "id", srcObj.InstanceGuid.ToString() }, { "output_index", srcIdx }, { "name", srcObj.Name } });
                            }
                            paramJson["sources"] = sources;
                            if (param.SourceCount == 0 && param.VolatileDataCount > 0) paramJson["has_internal_data"] = true;
                            inputs.Add(paramJson);
                        }
                        compJson["inputs"] = inputs;

                        var outputs = new JArray();
                        for (int i = 0; i < comp.Params.Output.Count; i++)
                        {
                            var param = comp.Params.Output[i];
                            var portJson = new JObject { { "index", i }, { "name", param.Name }, { "type", param.TypeName } };
                            if (param.VolatileDataCount > 0) {
                                portJson["data_structure"] = $"Tree ({param.VolatileData.PathCount} branches, {param.VolatileData.DataCount} items total)";
                            }
                            if (param.DataMapping == Grasshopper.Kernel.GH_DataMapping.Flatten) portJson["is_flattened"] = true;
                            if (param.DataMapping == Grasshopper.Kernel.GH_DataMapping.Graft) portJson["is_grafted"] = true;
                            if (param.Reverse) portJson["is_reversed"] = true;
                            if (param.Simplify) portJson["is_simplified"] = true;
                            outputs.Add(portJson);
                        }
                        compJson["outputs"] = outputs;
                    }
                    else if (obj is Grasshopper.Kernel.IGH_Param param)
                    {
                        compJson["type"] = param.TypeName;
                        if (param.VolatileDataCount > 0) {
                            compJson["data_structure"] = $"Tree ({param.VolatileData.PathCount} branches, {param.VolatileData.DataCount} items total)";
                        }
                        if (param.DataMapping == Grasshopper.Kernel.GH_DataMapping.Flatten) compJson["is_flattened"] = true;
                        if (param.DataMapping == Grasshopper.Kernel.GH_DataMapping.Graft) compJson["is_grafted"] = true;
                        if (param.Reverse) compJson["is_reversed"] = true;
                        if (param.Simplify) compJson["is_simplified"] = true;

                        var sources = new JArray();
                        foreach (var source in param.Sources)
                        {
                            var srcObj = source.Attributes.GetTopLevel.DocObject;
                            int srcIdx = (srcObj is Grasshopper.Kernel.IGH_Component srcC) ? srcC.Params.Output.IndexOf(source) : 0;
                            sources.Add(new JObject { { "id", srcObj.InstanceGuid.ToString() }, { "output_index", srcIdx }, { "name", srcObj.Name } });
                        }
                        compJson["sources"] = sources;
                    }
                    AppendScriptBodiesToComponentJson(compJson, obj);
                    components.Add(compJson);
                }
                graph["canvas_errors"] = globalErrors;
                graph["components"] = components;
                graph["groups"] = groups;
                
                result = graph.ToString(Formatting.None); // 使用压缩格式节省 Token
                _cachedCanvasState = result;
                _cachedRhinoUnitSignature = currentUnitSignature;
                _canvasChanged = false;
                UpdateCodeView();
            }));
            return result;
        }

        // ── 共享序列化 helper（不改变任何字段结构）──────────────────────────
        private static JObject BuildComponentJson(Grasshopper.Kernel.IGH_DocumentObject obj)
        {
            return BuildComponentJson(obj, true);
        }

        private static JObject BuildComponentJson(Grasshopper.Kernel.IGH_DocumentObject obj, bool includeScriptBodies)
        {
            var j = new JObject();
            j["name"]     = obj.Name;
            j["nickname"] = obj.NickName;
            j["id"]       = obj.InstanceGuid.ToString();
            j["pivot"]    = new JObject { { "x", Math.Round(obj.Attributes.Pivot.X) }, { "y", Math.Round(obj.Attributes.Pivot.Y) } };
            if (IsGraphMapperObject(obj)) j["graph_mapper_type"] = CurrentGraphMapperTypeName(obj) ?? "";
            if (obj is IGH_ActiveObject ao && ao.RuntimeMessageLevel != GH_RuntimeMessageLevel.Blank)
            {
                var msgs = new JArray();
                foreach (string m in ao.RuntimeMessages(GH_RuntimeMessageLevel.Error))   msgs.Add("Error: " + m);
                foreach (string m in ao.RuntimeMessages(GH_RuntimeMessageLevel.Warning)) msgs.Add("Warning: " + m);
                j["runtime_messages"] = msgs;
            }
            if (obj is Grasshopper.Kernel.IGH_Component comp)
            {
                var inputs = new JArray();
                for (int i = 0; i < comp.Params.Input.Count; i++)
                {
                    var param = comp.Params.Input[i];
                    var pj = new JObject { ["index"] = i, ["name"] = param.Name, ["type"] = param.TypeName };
                    if (param.VolatileDataCount > 0) pj["data_structure"] = $"Tree ({param.VolatileData.PathCount} branches, {param.VolatileData.DataCount} items total)";
                    else pj["data_structure"] = "Empty";
                    if (param.DataMapping == Grasshopper.Kernel.GH_DataMapping.Flatten) pj["is_flattened"] = true;
                    if (param.DataMapping == Grasshopper.Kernel.GH_DataMapping.Graft)   pj["is_grafted"]  = true;
                    if (param.Reverse)  pj["is_reversed"]  = true;
                    if (param.Simplify) pj["is_simplified"] = true;
                    var srcs = new JArray();
                    foreach (var src in param.Sources) {
                        var so = src.Attributes.GetTopLevel.DocObject;
                        srcs.Add(new JObject { { "id", so.InstanceGuid.ToString() }, { "output_index", (so is Grasshopper.Kernel.IGH_Component sc) ? sc.Params.Output.IndexOf(src) : 0 }, { "name", so.Name } });
                    }
                    pj["sources"] = srcs;
                    if (param.SourceCount == 0 && param.VolatileDataCount > 0) pj["has_internal_data"] = true;
                    inputs.Add(pj);
                }
                j["inputs"] = inputs;
                var outputs = new JArray();
                for (int i = 0; i < comp.Params.Output.Count; i++)
                {
                    var param = comp.Params.Output[i];
                    var pj = new JObject { { "index", i }, { "name", param.Name }, { "type", param.TypeName } };
                    if (param.VolatileDataCount > 0) pj["data_structure"] = $"Tree ({param.VolatileData.PathCount} branches, {param.VolatileData.DataCount} items total)";
                    if (param.DataMapping == Grasshopper.Kernel.GH_DataMapping.Flatten) pj["is_flattened"] = true;
                    if (param.DataMapping == Grasshopper.Kernel.GH_DataMapping.Graft)   pj["is_grafted"]  = true;
                    if (param.Reverse)  pj["is_reversed"]  = true;
                    if (param.Simplify) pj["is_simplified"] = true;
                    outputs.Add(pj);
                }
                j["outputs"] = outputs;
            }
            else if (obj is Grasshopper.Kernel.IGH_Param pm)
            {
                j["type"] = pm.TypeName;
                if (pm.VolatileDataCount > 0) j["data_structure"] = $"Tree ({pm.VolatileData.PathCount} branches, {pm.VolatileData.DataCount} items total)";
                if (pm.DataMapping == Grasshopper.Kernel.GH_DataMapping.Flatten) j["is_flattened"] = true;
                if (pm.DataMapping == Grasshopper.Kernel.GH_DataMapping.Graft)   j["is_grafted"]  = true;
                if (pm.Reverse)  j["is_reversed"]  = true;
                if (pm.Simplify) j["is_simplified"] = true;
                var srcs = new JArray();
                foreach (var src in pm.Sources) {
                    var so = src.Attributes.GetTopLevel.DocObject;
                    srcs.Add(new JObject { { "id", so.InstanceGuid.ToString() }, { "output_index", (so is Grasshopper.Kernel.IGH_Component sc) ? sc.Params.Output.IndexOf(src) : 0 }, { "name", so.Name } });
                }
                j["sources"] = srcs;
            }
            if (includeScriptBodies)
                AppendScriptBodiesToComponentJson(j, obj);
            return j;
        }

        // ── 摘要：仅 id/name/pivot + 首条报错，不含端口 ──────────────────────
        private static void GetComponentIssueCounts(Grasshopper.Kernel.IGH_DocumentObject obj, out int errorCount, out int warningCount, out string firstIssue)
        {
            errorCount = 0;
            warningCount = 0;
            firstIssue = null;
            if (!(obj is IGH_ActiveObject ao) || ao.RuntimeMessageLevel == GH_RuntimeMessageLevel.Blank) return;

            var errs = ao.RuntimeMessages(GH_RuntimeMessageLevel.Error);
            var warns = ao.RuntimeMessages(GH_RuntimeMessageLevel.Warning);
            errorCount = errs?.Count ?? 0;
            warningCount = warns?.Count ?? 0;

            if (errorCount > 0) firstIssue = errs[0];
            else if (warningCount > 0) firstIssue = warns[0];
        }

        private static bool ComponentHasConnections(Grasshopper.Kernel.IGH_DocumentObject obj)
        {
            if (obj is Grasshopper.Kernel.IGH_Component comp)
            {
                foreach (var p in comp.Params.Input) if (p.SourceCount > 0) return true;
                foreach (var p in comp.Params.Output) if (p.Recipients.Count > 0) return true;
                return false;
            }
            if (obj is Grasshopper.Kernel.IGH_Param param)
                return param.SourceCount > 0 || param.Recipients.Count > 0;
            return false;
        }

        private static bool ComponentHasPortName(Grasshopper.Kernel.IGH_DocumentObject obj, string portNameContains)
        {
            string needle = (portNameContains ?? "").Trim();
            if (needle.Length == 0) return true;

            bool HasName(string a, string b)
            {
                return (!string.IsNullOrEmpty(a) && a.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (!string.IsNullOrEmpty(b) && b.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (obj is Grasshopper.Kernel.IGH_Component comp)
            {
                foreach (var p in comp.Params.Input)
                    if (HasName(p.Name, p.NickName)) return true;
                foreach (var p in comp.Params.Output)
                    if (HasName(p.Name, p.NickName)) return true;
                return false;
            }

            if (obj is Grasshopper.Kernel.IGH_Param param)
                return HasName(param.Name, param.NickName);

            return false;
        }

        private static bool ComponentLooksLikeScript(Grasshopper.Kernel.IGH_DocumentObject obj)
        {
            if (obj == null) return false;
            if (IsCSharpScriptComponent(obj)) return true;

            string[] probes =
            {
                obj.Name ?? "",
                obj.NickName ?? "",
                obj.GetType()?.Name ?? ""
            };
            foreach (string probe in probes)
            {
                if (probe.IndexOf("script", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (probe.IndexOf("python", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (probe.IndexOf("ghpython", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (probe.IndexOf("evaluate", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (probe.IndexOf("expression", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (probe.IndexOf("c#", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (probe.IndexOf("vb", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            try { return GhEnumerateScriptPayloadStrings(obj).Count > 0; }
            catch { return false; }
        }

        private static JObject BuildComponentQuerySummary(Grasshopper.Kernel.IGH_DocumentObject obj)
        {
            GetComponentIssueCounts(obj, out int errorCount, out int warningCount, out string firstIssue);
            var jo = new JObject
            {
                ["id"] = obj.InstanceGuid.ToString(),
                ["name"] = obj.Name,
                ["nickname"] = obj.NickName,
                ["pivot"] = new JObject { { "x", Math.Round(obj.Attributes.Pivot.X) }, { "y", Math.Round(obj.Attributes.Pivot.Y) } },
                ["is_script"] = ComponentLooksLikeScript(obj),
                ["has_connections"] = ComponentHasConnections(obj)
            };

            if (obj is Grasshopper.Kernel.IGH_Component comp)
            {
                jo["kind"] = "component";
                jo["input_count"] = comp.Params.Input.Count;
                jo["output_count"] = comp.Params.Output.Count;
            }
            else if (obj is Grasshopper.Kernel.IGH_Param param)
            {
                jo["kind"] = "param";
                jo["type"] = param.TypeName;
                jo["source_count"] = param.SourceCount;
                jo["recipient_count"] = param.Recipients.Count;
            }
            else
            {
                jo["kind"] = "object";
            }

            if (errorCount > 0) jo["error_count"] = errorCount;
            if (warningCount > 0) jo["warning_count"] = warningCount;
            if (!string.IsNullOrWhiteSpace(firstIssue)) jo["first_issue"] = firstIssue;
            return jo;
        }

        private static List<Grasshopper.Kernel.IGH_DocumentObject> CollectComponentContextObjects(
            GH_Document doc,
            Grasshopper.Kernel.IGH_DocumentObject target,
            int depth)
        {
            var orderedIds = new List<Guid>();
            var visited = new HashSet<Guid>();

            void Traverse(Grasshopper.Kernel.IGH_DocumentObject obj, int remaining)
            {
                if (obj == null || remaining <= 0) return;
                if (obj is Grasshopper.Kernel.IGH_Component comp)
                {
                    foreach (var p in comp.Params.Input)
                    {
                        foreach (var s in p.Sources)
                        {
                            var nb = s.Attributes?.GetTopLevel?.DocObject;
                            if (nb == null || !visited.Add(nb.InstanceGuid)) continue;
                            orderedIds.Add(nb.InstanceGuid);
                            Traverse(nb, remaining - 1);
                        }
                    }
                    foreach (var p in comp.Params.Output)
                    {
                        foreach (var r in p.Recipients)
                        {
                            var nb = r.Attributes?.GetTopLevel?.DocObject;
                            if (nb == null || !visited.Add(nb.InstanceGuid)) continue;
                            orderedIds.Add(nb.InstanceGuid);
                            Traverse(nb, remaining - 1);
                        }
                    }
                }
                else if (obj is Grasshopper.Kernel.IGH_Param param)
                {
                    foreach (var s in param.Sources)
                    {
                        var nb = s.Attributes?.GetTopLevel?.DocObject;
                        if (nb == null || !visited.Add(nb.InstanceGuid)) continue;
                        orderedIds.Add(nb.InstanceGuid);
                        Traverse(nb, remaining - 1);
                    }
                    foreach (var r in param.Recipients)
                    {
                        var nb = r.Attributes?.GetTopLevel?.DocObject;
                        if (nb == null || !visited.Add(nb.InstanceGuid)) continue;
                        orderedIds.Add(nb.InstanceGuid);
                        Traverse(nb, remaining - 1);
                    }
                }
            }

            visited.Add(target.InstanceGuid);
            orderedIds.Add(target.InstanceGuid);
            Traverse(target, Math.Max(0, depth));

            var result = new List<Grasshopper.Kernel.IGH_DocumentObject>();
            foreach (var guid in orderedIds)
            {
                var obj = doc.FindObject(guid, true);
                if (obj != null) result.Add(obj);
            }
            return result;
        }

        private static string ExecuteQueryGhComponents(
            string id = null,
            string nameContains = null,
            bool? hasErrors = null,
            bool? isScript = null,
            bool? hasConnections = null,
            string portNameContains = null,
            int maxResults = 8,
            int neighborDepth = 1)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }

                string idNeedle = (id ?? "").Trim();
                string nameNeedle = (nameContains ?? "").Trim();
                string portNeedle = (portNameContains ?? "").Trim();
                maxResults = Math.Max(1, Math.Min(50, maxResults));
                neighborDepth = Math.Max(0, Math.Min(2, neighborDepth));

                var matched = new List<Grasshopper.Kernel.IGH_DocumentObject>();
                foreach (var obj in doc.Objects)
                {
                    if (obj is Grasshopper.Kernel.Special.GH_Group) continue;

                    if (idNeedle.Length > 0 && !obj.InstanceGuid.ToString().Equals(idNeedle, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (nameNeedle.Length > 0)
                    {
                        bool nameMatch =
                            (!string.IsNullOrEmpty(obj.Name) && obj.Name.IndexOf(nameNeedle, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrEmpty(obj.NickName) && obj.NickName.IndexOf(nameNeedle, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (!nameMatch) continue;
                    }

                    if (hasErrors.HasValue)
                    {
                        GetComponentIssueCounts(obj, out int errorCount, out int warningCount, out _);
                        bool objHasErrors = errorCount > 0 || warningCount > 0;
                        if (objHasErrors != hasErrors.Value) continue;
                    }

                    if (isScript.HasValue && ComponentLooksLikeScript(obj) != isScript.Value)
                        continue;

                    if (hasConnections.HasValue && ComponentHasConnections(obj) != hasConnections.Value)
                        continue;

                    if (portNeedle.Length > 0 && !ComponentHasPortName(obj, portNeedle))
                        continue;

                    matched.Add(obj);
                }

                var hits = new JArray();
                foreach (var obj in matched.Take(maxResults))
                {
                    var hit = new JObject
                    {
                        ["summary"] = BuildComponentQuerySummary(obj),
                        ["component"] = BuildComponentJson(obj, false)
                    };

                    if (neighborDepth > 0)
                    {
                        var neighbors = new JArray();
                        foreach (var ctxObj in CollectComponentContextObjects(doc, obj, neighborDepth))
                        {
                            if (ctxObj.InstanceGuid == obj.InstanceGuid) continue;
                            neighbors.Add(BuildComponentQuerySummary(ctxObj));
                        }
                        hit["neighbors"] = neighbors;
                    }

                    hits.Add(hit);
                }

                result = new JObject
                {
                    ["query"] = new JObject
                    {
                        ["id"] = idNeedle,
                        ["name_contains"] = nameNeedle,
                        ["has_errors"] = hasErrors.HasValue ? JToken.FromObject(hasErrors.Value) : JValue.CreateNull(),
                        ["is_script"] = isScript.HasValue ? JToken.FromObject(isScript.Value) : JValue.CreateNull(),
                        ["has_connections"] = hasConnections.HasValue ? JToken.FromObject(hasConnections.Value) : JValue.CreateNull(),
                        ["port_name_contains"] = portNeedle,
                        ["neighbor_depth"] = neighborDepth
                    },
                    ["total_hits"] = matched.Count,
                    ["returned_hits"] = hits.Count,
                    ["hits"] = hits
                }.ToString(Formatting.None);
            }));
            return result;
        }

        private static string ExecuteGetCanvasSummary()
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }
                var arr = new JArray();
                foreach (var obj in doc.Objects)
                {
                    if (obj is Grasshopper.Kernel.Special.GH_Group) continue;
                    var j = new JObject {
                        ["id"]    = obj.InstanceGuid.ToString(),
                        ["name"]  = obj.Name,
                        ["pivot"] = new JObject { { "x", Math.Round(obj.Attributes.Pivot.X) }, { "y", Math.Round(obj.Attributes.Pivot.Y) } }
                    };
                    if (obj is IGH_ActiveObject ao && ao.RuntimeMessageLevel != GH_RuntimeMessageLevel.Blank)
                    {
                        var errs = ao.RuntimeMessages(GH_RuntimeMessageLevel.Error);
                        var warn = ao.RuntimeMessages(GH_RuntimeMessageLevel.Warning);
                        if (errs.Count > 0)      j["error"] = "❌ " + errs[0];
                        else if (warn.Count > 0) j["error"] = "⚠️ " + warn[0];
                    }
                    arr.Add(j);
                }
                var groups = new JArray();
                foreach (var g in doc.Objects.OfType<Grasshopper.Kernel.Special.GH_Group>()) {
                    var members = new JArray();
                    foreach (var mid in g.Objects()) members.Add(mid.ToString());
                    groups.Add(new JObject { ["id"] = g.InstanceGuid.ToString(), ["name"] = g.NickName, ["members"] = members });
                }
                result = new JObject
                {
                    ["rhino_units"] = BuildRhinoUnitsJson(),
                    ["components"] = arr,
                    ["groups"] = groups
                }.ToString(Formatting.None);
            }));
            return result;
        }

        // ── 上下文：目标 + 前后各 depth 层邻居（完整详情）───────────────────
        private static string ExecuteGetComponentContext(string id, int depth = 1, bool includeScriptBodies = false)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }
                if (!Guid.TryParse(id, out Guid guid)) { result = "Error: ID 格式错误。"; return; }
                var target = doc.FindObject(guid, true);
                if (target == null) { result = "Error: 找不到该电池。"; return; }

                var arr = new JArray();
                foreach (var obj in CollectComponentContextObjects(doc, target, depth))
                    arr.Add(BuildComponentJson(obj, includeScriptBodies));
                result = new JObject { ["context_components"] = arr }.ToString(Formatting.None);
            }));
            return result;
        }

        private static string ExecuteReadComponentScript(string id)
        {
            const int readCap = 150000;
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }
                if (!Guid.TryParse(id, out Guid guid)) { result = "Error: ID 格式错误。"; return; }
                var obj = doc.FindObject(guid, true);
                if (obj == null) { result = "Error: 找不到该电池。"; return; }
                result = GhReadScriptSourceViaReflection(obj, readCap, Math.Min(readCap, 120000));
            }));
            return result;
        }

        private static void SyncCodeIssuesStripHeightToInputArea()
        {
            if (_codeCanvasIssuesHost == null || _inputAreaBorder == null) return;
            double h = _inputAreaBorder.ActualHeight;
            if (double.IsNaN(h) || h < 1) return;
            _codeCanvasIssuesHost.Height = h;
        }

        private static void ScheduleCodeSurfaceRefreshFromCanvas()
        {
            _canvasChanged = true;
            if (_window?.Dispatcher == null) return;

            Action armTimer = () =>
            {
                if (_codeSurfaceDebounceTimer != null)
                {
                    _codeSurfaceDebounceTimer.Stop();
                    _codeSurfaceDebounceTimer = null;
                }
                _codeSurfaceDebounceTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
                _codeSurfaceDebounceTimer.Tick += (_, __) =>
                {
                    if (_codeSurfaceDebounceTimer != null)
                    {
                        _codeSurfaceDebounceTimer.Stop();
                        _codeSurfaceDebounceTimer = null;
                    }
                    Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
                    {
                        _canvasChanged = true;
                        UpdateCodeView();
                    }));
                };
                _codeSurfaceDebounceTimer.Start();
            };

            if (_window.Dispatcher.CheckAccess())
                armTimer();
            else
                _window.Dispatcher.Invoke(armTimer);
        }

        private static void OnGhDocObjectsChanged(object sender, GH_DocObjectEventArgs e)
        {
            ScheduleCodeSurfaceRefreshFromCanvas();
        }

        private static void OnGhDocSolutionEnd(object sender, GH_SolutionEventArgs e)
        {
            ScheduleCodeSurfaceRefreshFromCanvas();
        }

        private static void OnGhCanvasDocumentChanged(object sender, GH_CanvasDocumentChangedEventArgs e)
        {
            AttachGrasshopperDocumentForCodeRefresh(e?.NewDocument);
            ScheduleCodeSurfaceRefreshFromCanvas();
        }

        private static void DetachGrasshopperDocumentForCodeRefresh()
        {
            if (_codeSurfaceHookedDoc == null) return;
            try {
                _codeSurfaceHookedDoc.ObjectsAdded -= OnGhDocObjectsChanged;
                _codeSurfaceHookedDoc.ObjectsDeleted -= OnGhDocObjectsChanged;
                _codeSurfaceHookedDoc.SolutionEnd -= OnGhDocSolutionEnd;
            } catch (Exception ex) {
                AddGhLog.Warn("DetachGrasshopperDocumentForCodeRefresh: " + ex.Message);
            }
            _codeSurfaceHookedDoc = null;
        }

        private static void AttachGrasshopperDocumentForCodeRefresh(GH_Document doc)
        {
            if (doc == _codeSurfaceHookedDoc) return;
            DetachGrasshopperDocumentForCodeRefresh();
            _codeSurfaceHookedDoc = doc;
            if (doc == null) return;
            doc.ObjectsAdded += OnGhDocObjectsChanged;
            doc.ObjectsDeleted += OnGhDocObjectsChanged;
            doc.SolutionEnd += OnGhDocSolutionEnd;
        }

        private static void DetachCodeSurfaceCanvasHookOnly()
        {
            if (_codeSurfaceHookedCanvas == null) return;
            try { _codeSurfaceHookedCanvas.DocumentChanged -= OnGhCanvasDocumentChanged; }
            catch (Exception ex) { AddGhLog.Warn("Detach canvas hook: " + ex.Message); }
            _codeSurfaceHookedCanvas = null;
        }

        private static void AttachCodeSurfaceCanvasHook()
        {
            var canvas = Grasshopper.Instances.ActiveCanvas as GH_Canvas;
            if (canvas == null) return;
            if (canvas == _codeSurfaceHookedCanvas) return;
            DetachCodeSurfaceCanvasHookOnly();
            _codeSurfaceHookedCanvas = canvas;
            _codeSurfaceHookedCanvas.DocumentChanged += OnGhCanvasDocumentChanged;
        }

        private static void TeardownGrasshopperCodeSurfaceHooks()
        {
            try {
                DetachCodeSurfaceCanvasHookOnly();
                DetachGrasshopperDocumentForCodeRefresh();
                if (_codeSurfaceDebounceTimer != null)
                {
                    _codeSurfaceDebounceTimer.Stop();
                    _codeSurfaceDebounceTimer = null;
                }
            } catch (Exception ex) {
                AddGhLog.Warn("TeardownGrasshopperCodeSurfaceHooks: " + ex.Message);
            }
        }

        private static void StartGrasshopperCodeSurfaceHooks()
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                try {
                    AttachCodeSurfaceCanvasHook();
                    AttachGrasshopperDocumentForCodeRefresh(Grasshopper.Instances.ActiveCanvas?.Document);
                } catch (Exception ex) {
                    AddGhLog.Warn("StartGrasshopperCodeSurfaceHooks: " + ex.Message);
                }
            }));
        }

        private static void UpdateCodePanelCanvasIssues()
        {
            if (_txtCanvasIssues == null) return;
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                if (!_isCodeVisible) return;

                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) {
                    _txtCanvasIssues.Text = "当前无激活的 Grasshopper 文档。";
                    return;
                }

                string err = GetCanvasErrors(doc)?.Trim();
                if (string.IsNullOrEmpty(err))
                    _txtCanvasIssues.Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140));
                else
                    _txtCanvasIssues.Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200));
                _txtCanvasIssues.Text = string.IsNullOrEmpty(err)
                    ? "画布暂无组件级 Error / Warning 运行时提示。"
                    : err;
            }));
        }

        private static void UpdateCodeView()
        {
            if (!_isCodeVisible || _richCodeView == null) return;

            if (!_isJsonMode)
            {
                string raw = ExecuteGetGhComponents();
                Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
                {
                    try {
                        // 尝试在 UI 上进行格式化展示，即使 AI 接收的是压缩版
                        var obj = JsonConvert.DeserializeObject(raw);
                        SetRichCodeViewContent(_richCodeView, JsonConvert.SerializeObject(obj, Formatting.Indented));
                    } catch (Exception ex) {
                        AddGhLog.Debug("UpdateCodeView JSON indent failed: " + ex.Message);
                        SetRichCodeViewContent(_richCodeView, raw);
                    }
                    UpdateCodePanelCanvasIssues();
                }));
                return;
            }

            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                try {
                    var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                    if (doc == null) {
                        SetRichCodeViewContent(_richCodeView, "// 没有激活的画布", asPlainComment: true);
                        return;
                    }

                    var graph = new JObject();
                    if (DeploymentOptions.IncludeCanvasExportTimestamp)
                        graph["timestamp"] = DateTime.Now.ToString("HH:mm:ss");
                    graph["object_count"] = doc.ObjectCount;

                    var components = new JArray();
                    foreach (var obj in doc.Objects)
                    {
                        var compJson = new JObject();
                        compJson["name"] = obj.Name;
                        compJson["nickname"] = obj.NickName;
                        compJson["id"] = obj.InstanceGuid.ToString();
                        compJson["pivot"] = new JObject { { "x", Math.Round(obj.Attributes.Pivot.X) }, { "y", Math.Round(obj.Attributes.Pivot.Y) } };

                        if (obj is Grasshopper.Kernel.IGH_Component comp)
                        {
                            var inputs = new JArray();
                            foreach (var param in comp.Params.Input)
                            {
                                var paramJson = new JObject();
                                paramJson["name"] = param.Name;
                                paramJson["nickname"] = param.NickName;
                            
                                var sources = new JArray();
                                foreach (var source in param.Sources)
                                {
                                    sources.Add(source.Attributes.GetTopLevel.DocObject.InstanceGuid.ToString());
                                }
                                paramJson["sources"] = sources;
                                inputs.Add(paramJson);
                            }
                            compJson["inputs"] = inputs;

                            var outputs = new JArray();
                            foreach (var param in comp.Params.Output)
                            {
                                var paramJson = new JObject();
                                paramJson["name"] = param.Name;
                                paramJson["nickname"] = param.NickName;
                                outputs.Add(paramJson);
                            }
                            compJson["outputs"] = outputs;
                        }
                        else if (obj is Grasshopper.Kernel.IGH_Param param)
                        {
                            var sources = new JArray();
                            foreach (var source in param.Sources)
                            {
                                sources.Add(source.Attributes.GetTopLevel.DocObject.InstanceGuid.ToString());
                            }
                            compJson["sources"] = sources;
                        }

                        components.Add(compJson);
                    }
                    graph["components"] = components;

                    SetRichCodeViewContent(_richCodeView, graph.ToString(Formatting.Indented));
                } finally {
                    UpdateCodePanelCanvasIssues();
                }
            }));
        }

        /// <summary>
        /// 优先按名称从组件库创建实例；若提供合法 component_guid 则按类型 GUID 创建（用于同名或脚本类）。
        /// </summary>
        private static Grasshopper.Kernel.IGH_DocumentObject InstantiateDocumentObjectFromLibrary(string name, string componentGuid)
        {
            if (!string.IsNullOrWhiteSpace(componentGuid) && Guid.TryParse(componentGuid.Trim(), out Guid cid)) {
                var emitted = Grasshopper.Instances.ComponentServer.EmitObject(cid) as Grasshopper.Kernel.IGH_DocumentObject;
                if (emitted != null) return emitted;
            }
            if (string.IsNullOrWhiteSpace(name)) return null;
            var proxy = FindComponentProxy(name);
            return proxy?.CreateInstance() as Grasshopper.Kernel.IGH_DocumentObject;
        }

        private const string DefaultGraphMapperType = "Bezier";

        private static bool IsGraphMapperObject(Grasshopper.Kernel.IGH_DocumentObject obj)
        {
            return obj is Grasshopper.Kernel.Special.GH_GraphMapper;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return null;
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return null;
        }

        private static string GetGraphMapperTypeRequest(JToken token, string valueFallback = null)
        {
            if (token == null) return FirstNonEmpty(valueFallback, DefaultGraphMapperType);
            return FirstNonEmpty(
                token["graph_mapper_type"]?.ToString(),
                token["graph_type"]?.ToString(),
                token["mapper_type"]?.ToString(),
                valueFallback,
                DefaultGraphMapperType);
        }

        private static string CurrentGraphMapperTypeName(Grasshopper.Kernel.IGH_DocumentObject obj)
        {
            var mapper = obj as Grasshopper.Kernel.Special.GH_GraphMapper;
            return mapper?.Graph?.Name;
        }

        private static Grasshopper.Kernel.GH_GraphProxy FindGraphMapperProxy(string keyword)
        {
            var proxies = Grasshopper.Instances.ComponentServer?.GraphProxies;
            if (proxies == null) return null;

            string wanted = (keyword ?? DefaultGraphMapperType).Trim();
            if (wanted.Length == 0) wanted = DefaultGraphMapperType;

            var list = proxies.ToList();
            var exact = list.FirstOrDefault(p => string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            exact = list.FirstOrDefault(p => string.Equals(p.Type?.Name, wanted, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            return list.FirstOrDefault(p =>
                (!string.IsNullOrWhiteSpace(p.Name) && p.Name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrWhiteSpace(p.Description) && p.Description.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrWhiteSpace(p.Type?.Name) && p.Type.Name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static string DescribeGraphMapperTypes(int maxNames = 20)
        {
            var proxies = Grasshopper.Instances.ComponentServer?.GraphProxies;
            if (proxies == null || proxies.Count == 0) return "";
            return " 可用类型：" + string.Join(", ", proxies.Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Take(maxNames));
        }

        private static bool TrySetGraphMapperType(Grasshopper.Kernel.IGH_DocumentObject obj, string graphType, out string detail)
        {
            detail = "";
            var mapper = obj as Grasshopper.Kernel.Special.GH_GraphMapper;
            if (mapper == null)
            {
                detail = "Error: 该电池不是 Graph Mapper。";
                return false;
            }

            string requested = FirstNonEmpty(graphType, DefaultGraphMapperType);
            var proxy = FindGraphMapperProxy(requested);
            if (proxy == null)
            {
                detail = "Error: 找不到 Graph Mapper 类型 '" + requested + "'。" + DescribeGraphMapperTypes();
                return false;
            }

            var graph = Grasshopper.Instances.ComponentServer.EmitGraph(proxy.GUID);
            if (graph == null)
            {
                detail = "Error: 无法创建 Graph Mapper 类型 '" + proxy.Name + "'。";
                return false;
            }

            try { graph.PrepareForUse(); } catch { }

            if (mapper.Container == null)
                mapper.Container = new Grasshopper.Kernel.Graphs.GH_GraphContainer(graph, 0.0, 1.0, 0.0, 1.0);
            else
                mapper.Container.Graph = graph;

            try { mapper.Container.PrepareForUse(); } catch { }
            mapper.ExpireSolution(true);
            try { mapper.Attributes?.ExpireLayout(); } catch { }

            detail = "Graph Mapper 类型=" + proxy.Name;
            return true;
        }

        private static bool IsScriptModeAuxiliaryComponentAllowed(Grasshopper.Kernel.IGH_DocumentObject obj)
        {
            if (_layoutMode != LayoutMode.CSharpFirst) return true;
            string category = obj?.Category ?? "";
            return string.Equals(category, "Params", StringComparison.OrdinalIgnoreCase)
                || string.Equals(category, "Display", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildScriptModeAuxiliaryComponentError(Grasshopper.Kernel.IGH_DocumentObject obj, string requestedName)
        {
            string displayName = !string.IsNullOrWhiteSpace(requestedName) ? requestedName : (obj?.Name ?? "该电池");
            string category = string.IsNullOrWhiteSpace(obj?.Category) ? "未知" : obj.Category;
            return "Error: C# 优先模式下，非脚本辅助电池只允许使用 Params 或 Display 分类；"
                + displayName + " 属于 " + category + "，已拒绝创建。核心建模逻辑请写入 C# Script 电池。";
        }

        private static string ExecuteAddGhComponent(string name, float x, float y, string label = null, string componentGuid = null, string graphMapperType = null)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }

                var obj = InstantiateDocumentObjectFromLibrary(name, componentGuid);

                if (obj == null) {
                    result = !string.IsNullOrWhiteSpace(componentGuid)
                        ? "Error: component_guid 无效或未加载对应的电池类型。"
                        : "Error: 找不到电池 '" + name + "'。";
                    return;
                }

                if (!IsScriptModeAuxiliaryComponentAllowed(obj))
                {
                    result = BuildScriptModeAuxiliaryComponentError(obj, name);
                    return;
                }

                obj.CreateAttributes();
                obj.Attributes.Pivot = new System.Drawing.PointF(x, y);
                if (!string.IsNullOrEmpty(label)) obj.NickName = label;
                obj.Attributes.ExpireLayout();

                string graphMapperDetail = null;
                if (IsGraphMapperObject(obj) && !TrySetGraphMapperType(obj, FirstNonEmpty(graphMapperType, DefaultGraphMapperType), out graphMapperDetail))
                {
                    result = graphMapperDetail;
                    return;
                }

                doc.AddObject(obj, false);
                _canvasChanged = true;
                try { doc.ScheduleSolution(150); } 
                catch (Exception ex) { AddGhLog.Warn("ExecuteAddGhComponent Schedule failed: " + ex.Message); }
                string displayName = !string.IsNullOrWhiteSpace(name) ? name : (obj.Name ?? "组件");
                result = "已添加 " + displayName + " (ID: " + obj.InstanceGuid + ").";
                if (!string.IsNullOrWhiteSpace(graphMapperDetail)) result += " " + graphMapperDetail + "。";
            }));
            return result;
        }

        private static string ExecuteConnectGhComponents(string fromId, int fromIndex, string toId, int toIndex)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }
                if (!Guid.TryParse(fromId, out Guid guidFrom) || !Guid.TryParse(toId, out Guid guidTo)) { result = "Error: ID 格式错误。"; return; }

                var objFrom = doc.FindObject(guidFrom, true);
                var objTo = doc.FindObject(guidTo, true);
                if (objFrom == null || objTo == null) { result = "Error: 找不到电池。"; return; }

                Grasshopper.Kernel.IGH_Param sourceParam = (objFrom is Grasshopper.Kernel.IGH_Component cF) ? (fromIndex < cF.Params.Output.Count ? cF.Params.Output[fromIndex] : null) : (objFrom as Grasshopper.Kernel.IGH_Param);
                Grasshopper.Kernel.IGH_Param targetParam = (objTo is Grasshopper.Kernel.IGH_Component cT) ? (toIndex < cT.Params.Input.Count ? cT.Params.Input[toIndex] : null) : (objTo as Grasshopper.Kernel.IGH_Param);

                if (sourceParam == null || targetParam == null) { result = "Error: 端口越界。"; return; }

                targetParam.AddSource(sourceParam);
                _canvasChanged = true;
                try { doc.ScheduleSolution(150); } 
                catch (Exception ex) { AddGhLog.Warn("ExecuteConnectGhComponents Schedule failed: " + ex.Message); }
                result = "连线成功。";
                result += GetCanvasErrors(doc);
            }));
            return result;
        }

        private static string ExecuteRemoveGhComponent(string id)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }
                if (!Guid.TryParse(id, out Guid guid)) { result = "Error: ID 格式错误。"; return; }
                var obj = doc.FindObject(guid, true);
                if (obj == null) { result = "Error: 找不到电池。"; return; }

                doc.RemoveObject(obj, false);
                result = "删除成功。";
                _canvasChanged = true;
                try { doc.ScheduleSolution(150); } 
                catch (Exception ex) { AddGhLog.Warn("ExecuteRemoveGhComponent Schedule failed: " + ex.Message); }
            }));
            return result;
        }

        private static bool GhScriptMetaExcludedName(string pn)
        {
            if (string.IsNullOrEmpty(pn)) return true;
            foreach (var ex in new[] {
                "NickName", "Category", "SubCategory", "Description", "Keywords", "InstanceDescription",
                "Path", "FileName", "Url", "Message", "ToolTip", "IconDisplayName", "LanguageName"
            })
                if (pn.Equals(ex, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool GhScriptNameLooksLikePayload(string pn)
        {
            if (GhScriptMetaExcludedName(pn)) return false;
            // 支持完整接口名如 RhinoCodePlatform.GH.IScriptComponent.Text
            string shortName = pn.Contains('.') ? pn.Substring(pn.LastIndexOf('.') + 1) : pn;
            // GhPython / Rhino「Python 3 Script」等：可执行正文在 Text，不用子串「Text」以免误匹配如 Texture。
            if (string.Equals(shortName, "Text", StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var part in new[] {
                "Code", "Script", "Formula", "Expression", "Source", "Snippet", "Program", "Definition",
                "Logic", "Statement", "Body", "Python", "CSharp", "Csharp", "VB", "VBA", "IronPython", "Compile",
                "ScriptSource", "Editor", "UserCode", "RawCode", "TextBody", "Document", "PyCode", "Roslyn"
            })
                if (shortName.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static int GhScriptMemberPreference(string pn)
        {
            string sn = pn.Contains('.') ? pn.Substring(pn.LastIndexOf('.') + 1) : pn;
            if (string.Equals(sn, "Code", StringComparison.OrdinalIgnoreCase)) return 500;
            if (string.Equals(sn, "Script", StringComparison.OrdinalIgnoreCase)) return 490;
            if (string.Equals(sn, "Text", StringComparison.OrdinalIgnoreCase)) return 485;
            if (sn.IndexOf("Formula", StringComparison.OrdinalIgnoreCase) >= 0) return 480;
            if (sn.IndexOf("Expression", StringComparison.OrdinalIgnoreCase) >= 0) return 470;
            if (sn.IndexOf("ScriptSource", StringComparison.OrdinalIgnoreCase) >= 0) return 460;
            if (sn.IndexOf("Editor", StringComparison.OrdinalIgnoreCase) >= 0) return 450;
            if (sn.IndexOf("Python", StringComparison.OrdinalIgnoreCase) >= 0) return 440;
            if (sn.IndexOf("CSharp", StringComparison.OrdinalIgnoreCase) >= 0 || sn.IndexOf("Csharp", StringComparison.OrdinalIgnoreCase) >= 0) return 430;
            if (sn.IndexOf("VB", StringComparison.OrdinalIgnoreCase) >= 0) return 420;
            if (sn.IndexOf("Source", StringComparison.OrdinalIgnoreCase) >= 0) return 400;
            if (sn.IndexOf("Content", StringComparison.OrdinalIgnoreCase) >= 0) return 350;
            if (sn.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0) return 340;
            return 100;
        }

        private static string GhTruncateScriptSnippet(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.Length <= maxChars) return text;
            return text.Substring(0, maxChars) + "\n...[truncated " + (text.Length - maxChars) + " chars]";
        }

        /// <summary> 枚举电池实例上「像脚本正文」的可读 string 属性/字段（顺序已按启发式偏好排好）。 </summary>
        private static List<(string label, string text, int pref)> GhEnumerateScriptPayloadStrings(Grasshopper.Kernel.IGH_DocumentObject obj)
        {
            var results = new List<(string label, string text, int pref)>();
            if (obj == null) return results;

            var propCandidates = new List<System.Reflection.PropertyInfo>();
            var fieldCandidates = new List<System.Reflection.FieldInfo>();
            var seenProp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenFieldSig = new HashSet<string>();

            for (Type t = obj.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var prop in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (prop.PropertyType != typeof(string)) continue;
                    if (prop.GetIndexParameters().Length != 0) continue;
                    if (!GhScriptNameLooksLikePayload(prop.Name)) continue;
                    if (!seenProp.Add(prop.Name)) continue;
                    propCandidates.Add(prop);
                }

                foreach (var fld in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (fld.FieldType != typeof(string)) continue;
                    if (!GhScriptNameLooksLikePayload(fld.Name)) continue;
                    string sig = (t.FullName ?? t.Name) + "::" + fld.Name;
                    if (!seenFieldSig.Add(sig)) continue;
                    fieldCandidates.Add(fld);
                }
            }

            foreach (var prop in propCandidates.OrderByDescending(p => GhScriptMemberPreference(p.Name)).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    object v = prop.GetGetMethod(true)?.Invoke(obj, null);
                    if (v is string sv && !string.IsNullOrEmpty(sv))
                        results.Add((prop.Name + " (prop)", sv, GhScriptMemberPreference(prop.Name)));
                }
                catch (Exception ex) { AddGhLog.Debug("GhEnumerateScriptPayloadStrings prop " + prop.Name + ": " + ex.Message); }
            }

            foreach (var fld in fieldCandidates.OrderByDescending(f => GhScriptMemberPreference(f.Name)).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    object v = fld.GetValue(obj);
                    if (v is string sv && !string.IsNullOrEmpty(sv))
                        results.Add((fld.Name + " (field)", sv, GhScriptMemberPreference(fld.Name)));
                }
                catch (Exception ex) { AddGhLog.Debug("GhEnumerateScriptPayloadStrings field " + fld.Name + ": " + ex.Message); }
            }

            return results.OrderByDescending(x => x.pref).ThenBy(x => x.label, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>read_source：不打开 GH_ScriptEditor（GetSourceCode 易在未就绪时崩溃），与 get_gh_components.script_bodies 同源反射读取。 </summary>
        private static string GhReadScriptSourceViaReflection(Grasshopper.Kernel.IGH_DocumentObject obj, int readCap, int maxPerMember)
        {
            var items = GhEnumerateScriptPayloadStrings(obj);
            var jo = new JObject();
            jo["via"] = "component_reflection";
            jo["runtime_type_hint"] = obj?.GetType()?.Name ?? "";

            if (items.Count == 0)
            {
                jo["script_bodies"] = new JObject();
                jo["primary_key"] = "";
                jo["primary_for_edit"] = "";
                jo["truncated"] = false;
                jo["hint"] = "未反射到脚本类 string 成员；若为内置 C# Script 仍可用 open_focus 人工查看，或换 get_gh_components/correct property。";
                return jo.ToString(Formatting.None);
            }

            bool truncated = false;
            var bag = new JObject();
            int approxTotal = 0;
            foreach (var (label, text, _) in items)
            {
                string s = text;
                if (s.Length > maxPerMember)
                {
                    s = GhTruncateScriptSnippet(s, maxPerMember);
                    truncated = true;
                }

                int bump = (label?.Length ?? 0) + s.Length + 40;
                if (approxTotal + bump > readCap)
                {
                    truncated = true;
                    break;
                }

                bag[label] = s;
                approxTotal += bump;
            }

            var best = items[0];
            string primary = best.text;
            if (primary.Length > maxPerMember)
            {
                primary = GhTruncateScriptSnippet(primary, maxPerMember);
                truncated = true;
            }

            jo["script_bodies"] = bag;
            jo["primary_key"] = best.label;
            jo["primary_for_edit"] = primary;
            jo["truncated"] = truncated;
            jo["hint"] = "与 get_gh_components 的 script_bodies 同源；不调用 GH_ScriptEditor。内置 C# 改代码仍用 set_source_commit（只替换首个可编辑块）或 property 精确写入。";
            return jo.ToString(Formatting.None);
        }

        /// <summary>尝试通过反射直接写入脚本电池的内容，优先 m_codeBlocks，其次按启发式匹配 string 属性/字段。</summary>
        private static bool TrySetNativeScriptContentViaReflection(Grasshopper.Kernel.IGH_DocumentObject obj, string newCode)
        {
            if (obj == null || newCode == null) return false;
            Type t = obj.GetType();
            if (t == null) return false;

            try
            {
                // 先尝试找到并修改 GH_CodeBlocks 相关的属性/字段
                var codeBlocksField = FindInstanceFieldInHierarchy(t, "m_codeBlocks");
                if (codeBlocksField != null)
                {
                    var currentBlocks = codeBlocksField.GetValue(obj);
                    if (currentBlocks != null)
                    {
                        try
                        {
                            GH_CodeBlocks blocks = currentBlocks as GH_CodeBlocks;
                            if (blocks != null)
                            {
                                GH_CodeBlocks merged = GhBuildCodeBlocksReplacingFirstMutable(blocks, newCode);
                                codeBlocksField.SetValue(obj, merged);
                                AddGhLog.Debug("Successfully set native script via m_codeBlocks field");
                                return true;
                            }
                        }
                        catch (Exception ex)
                        {
                            AddGhLog.Debug("Failed to set via m_codeBlocks: " + ex.Message);
                        }
                    }
                }

                // 备选：按启发式枚举所有 string 属性/字段（支持完整接口名如 RhinoCodePlatform.GH.IScriptComponent.Text）
                var propCandidates = new List<System.Reflection.PropertyInfo>();
                var fieldCandidates = new List<System.Reflection.FieldInfo>();
                var seenProp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seenFieldSig = new HashSet<string>();

                for (Type tt = t; tt != null && tt != typeof(object); tt = tt.BaseType)
                {
                    foreach (var prop in tt.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (prop.PropertyType != typeof(string)) continue;
                        if (!seenProp.Add(prop.Name)) continue;
                        if (prop.GetIndexParameters().Length != 0) continue;
                        if (prop.GetSetMethod(true) == null) continue;
                        if (!GhScriptNameLooksLikePayload(prop.Name)) continue;
                        propCandidates.Add(prop);
                    }
                    foreach (var fld in tt.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (fld.FieldType != typeof(string)) continue;
                        string sig = (tt.FullName ?? tt.Name) + "::" + fld.Name;
                        if (!seenFieldSig.Add(sig)) continue;
                        if (!GhScriptNameLooksLikePayload(fld.Name)) continue;
                        fieldCandidates.Add(fld);
                    }
                }

                foreach (var prop in propCandidates.OrderByDescending(p => GhScriptMemberPreference(p.Name)).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        prop.GetSetMethod(true).Invoke(obj, new object[] { newCode });
                        AddGhLog.Debug("Successfully set script via property: " + prop.Name);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        AddGhLog.Debug("Failed to set via property " + prop.Name + ": " + ex.Message);
                    }
                }

                foreach (var fld in fieldCandidates.OrderByDescending(f => GhScriptMemberPreference(f.Name)).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        fld.SetValue(obj, newCode);
                        AddGhLog.Debug("Successfully set script via field: " + fld.Name);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        AddGhLog.Debug("Failed to set via field " + fld.Name + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                AddGhLog.Warn("TrySetNativeScriptContentViaReflection failed: " + ex.Message);
            }

            return false;
        }

        /// <summary>把脚本/表达式类电池中能读到的 string 成员填入 script_bodies（截断以适应 token）。</summary>
        private static void AppendScriptBodiesToComponentJson(JObject compJson, Grasshopper.Kernel.IGH_DocumentObject obj)
        {
            if (obj == null || compJson == null) return;

            const int maxTotalApprox = 24000;
            const int maxPerMember = 12000;
            var bag = new JObject();
            int used = 0;

            bool TryPut(string logicalName, string raw)
            {
                if (string.IsNullOrEmpty(raw)) return false;
                string s = GhTruncateScriptSnippet(raw, maxPerMember);
                int bump = logicalName.Length + s.Length + 40;
                if (used + bump > maxTotalApprox) return false;
                bag[logicalName] = s;
                used += bump;
                return true;
            }

            foreach (var entry in GhEnumerateScriptPayloadStrings(obj))
                TryPut(entry.label, entry.text);

            if (bag.Count > 0) {
                compJson["script_bodies"] = bag;
                compJson["runtime_type_hint"] = obj.GetType()?.Name ?? "";
            }
        }

        private static void FinalizeGrasshopperScriptMutation(GH_Document doc, Grasshopper.Kernel.IGH_DocumentObject obj)
        {
            if (doc == null) return;
            obj?.ExpireSolution(true);
            _canvasChanged = true;
            try { doc.ScheduleSolution(150); } 
            catch (Exception ex) { AddGhLog.Warn("FinalizeGrasshopperScriptMutation Schedule failed: " + ex.Message); }
            try { Grasshopper.Instances.ActiveCanvas?.Refresh(); } 
            catch (Exception ex) { AddGhLog.Debug("FinalizeGrasshopperScriptMutation Refresh failed: " + ex.Message); }
        }

        private static void WaitForUiResponsiveDelay(int milliseconds)
        {
            if (milliseconds <= 0) return;
            var dispatcher = _window?.Dispatcher;
            if (dispatcher != null && dispatcher.CheckAccess())
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                var timer = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Background,
                    dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(milliseconds)
                };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    frame.Continue = false;
                };
                timer.Start();
                System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
            else
            {
                System.Threading.Thread.Sleep(milliseconds);
            }
        }

        private static FieldInfo FindInstanceFieldInHierarchy(Type type, string fieldName)
        {
            if (type == null || string.IsNullOrWhiteSpace(fieldName)) return null;
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var field = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        private static bool GhHasWritableStringMember(Grasshopper.Kernel.IGH_DocumentObject obj, string name)
        {
            if (obj == null || string.IsNullOrWhiteSpace(name)) return false;
            for (Type t = obj.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var prop in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (prop.PropertyType != typeof(string)) continue;
                    if (prop.GetIndexParameters().Length != 0) continue;
                    if (!prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (prop.GetSetMethod(true) != null) return true;
                }
                foreach (var fld in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (fld.FieldType != typeof(string)) continue;
                    if (!fld.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                    return true;
                }
            }
            return false;
        }

        private static bool TrySetScriptMemberExact(Grasshopper.Kernel.IGH_DocumentObject obj, string member, string text, out string detail)
        {
            detail = null;
            if (obj == null || string.IsNullOrWhiteSpace(member) || text == null) return false;
            member = member.Trim();
            member = member.Replace("[prop]", "").Replace("[field]", "").Trim();

            // 模型常误把 Python 3 Script / GhPython 正文写进 Description；可执行源码在 Text。
            if (member.Equals("Description", StringComparison.OrdinalIgnoreCase) && GhHasWritableStringMember(obj, "Text"))
                member = "Text";

            for (Type t = obj.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var prop in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (prop.PropertyType != typeof(string)) continue;
                    if (prop.GetIndexParameters().Length != 0) continue;
                    if (!prop.Name.Equals(member, StringComparison.OrdinalIgnoreCase)) continue;
                    var setter = prop.GetSetMethod(true);
                    if (setter == null) continue;
                    try {
                        setter.Invoke(obj, new object[] { text });
                        detail = prop.Name + " (prop)";
                        return true;
                    } catch (Exception ex) {
                        AddGhLog.Debug("TrySetScriptMemberExact prop " + prop.Name + ": " + ex.Message);
                        return false;
                    }
                }

                foreach (var fld in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (fld.FieldType != typeof(string)) continue;
                    if (!fld.Name.Equals(member, StringComparison.OrdinalIgnoreCase)) continue;
                    try {
                        fld.SetValue(obj, text);
                        detail = fld.Name + " (field)";
                        return true;
                    } catch (Exception ex) {
                        AddGhLog.Debug("TrySetScriptMemberExact field " + fld.Name + ": " + ex.Message);
                        return false;
                    }
                }
            }
            return false;
        }

        /// <summary> 脚本/表达式类电池：按 string 属性/字段名启发式写入。 </summary>
        private static bool TrySetGrasshopperScriptOrFormula(Grasshopper.Kernel.IGH_DocumentObject obj, string text, out string detail)
        {
            detail = null;
            if (obj == null || text == null) return false;

            // 跳过内置 C#/VB 脚本电池（有 m_codeBlocks 字段），避免破坏内部结构
            Type tt = obj.GetType();
            if (tt != null && FindInstanceFieldInHierarchy(tt, "m_codeBlocks") != null)
                return false;

            var propCandidates = new List<System.Reflection.PropertyInfo>();
            var fieldCandidates = new List<System.Reflection.FieldInfo>();
            var seenProp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenFieldSig = new HashSet<string>();

            for (Type t = obj.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var prop in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (prop.PropertyType != typeof(string)) continue;
                    if (!seenProp.Add(prop.Name)) continue;
                    if (prop.GetIndexParameters().Length != 0) continue;
                    if (prop.GetSetMethod(true) == null) continue;
                    if (!GhScriptNameLooksLikePayload(prop.Name)) continue;
                    propCandidates.Add(prop);
                }

                foreach (var fld in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (fld.FieldType != typeof(string)) continue;
                    string sig = (t.FullName ?? t.Name) + "::" + fld.Name;
                    if (!seenFieldSig.Add(sig)) continue;
                    if (!GhScriptNameLooksLikePayload(fld.Name)) continue;
                    fieldCandidates.Add(fld);
                }
            }

            foreach (var prop in propCandidates.OrderByDescending(p => GhScriptMemberPreference(p.Name)).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                try {
                    prop.GetSetMethod(true).Invoke(obj, new object[] { text });
                    detail = prop.Name + " (prop)";
                    return true;
                } catch (Exception ex) {
                    AddGhLog.Debug("TrySetGrasshopperScriptOrFormula prop " + prop?.Name + ": " + ex.Message);
                }
            }

            foreach (var fld in fieldCandidates.OrderByDescending(f => GhScriptMemberPreference(f.Name)).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                try {
                    fld.SetValue(obj, text);
                    detail = fld.Name + " (field)";
                    return true;
                } catch (Exception ex) {
                    AddGhLog.Debug("TrySetGrasshopperScriptOrFormula field " + fld?.Name + ": " + ex.Message);
                }
            }

            return false;
        }

        /// <summary> 用于错误提示：列出实例上可写的 string 属性与字段名。 </summary>
        private static string DescribeWritableStringProperties(Grasshopper.Kernel.IGH_DocumentObject obj, int maxNames = 20)
        {
            if (obj == null) return "";
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (Type t = obj.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var prop in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (prop.PropertyType != typeof(string)) continue;
                    if (prop.GetIndexParameters().Length != 0) continue;
                    if (prop.GetSetMethod(true) == null) continue;
                    if (!seen.Add(prop.Name)) continue;
                    names.Add(prop.Name + "[prop]");
                    if (names.Count >= maxNames) break;
                }
                if (names.Count >= maxNames) break;
            }
            for (Type t = obj.GetType(); t != null && t != typeof(object) && names.Count < maxNames; t = t.BaseType)
            {
                foreach (var fld in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (fld.FieldType != typeof(string)) continue;
                    if (!seen.Add(fld.Name)) continue;
                    names.Add(fld.Name + "[field]");
                    if (names.Count >= maxNames) break;
                }
            }
            return names.Count == 0 ? "" : " 可写 string 成员：" + string.Join(", ", names);
        }

        private static string ExecuteSetGhComponentValue(string id, string value, double? min, double? max, int? decimals, string exactMember = null, string graphMapperType = null)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }
                if (!Guid.TryParse(id, out Guid guid)) { result = "Error: ID 格式错误。"; return; }
                var obj = doc.FindObject(guid, true);
                if (obj == null) { result = "Error: 找不到电池。"; return; }

                if (_layoutMode == LayoutMode.CSharpFirst && IsCSharpScriptComponent(obj))
                {
                    result = "Error: C# priority mode does not allow set_gh_component_value to edit C# Script source. Use edit_csharp_script_component with mode=set_body.";
                    return;
                }

                if (IsGraphMapperObject(obj)) {
                    string requestedGraphType = FirstNonEmpty(
                        graphMapperType,
                        string.Equals(exactMember, "graph_mapper_type", StringComparison.OrdinalIgnoreCase) ? value : null,
                        string.Equals(exactMember, "graph_type", StringComparison.OrdinalIgnoreCase) ? value : null,
                        value,
                        DefaultGraphMapperType);
                    if (!TrySetGraphMapperType(obj, requestedGraphType, out string graphMapperDetail))
                    {
                        result = graphMapperDetail;
                        return;
                    }
                    _canvasChanged = true;
                    try { doc.ScheduleSolution(150); }
                    catch (Exception ex) { AddGhLog.Warn("ExecuteSetGhComponentValue (Graph Mapper) Schedule failed: " + ex.Message); }
                    result = graphMapperDetail + "。";
                } else if (obj is Grasshopper.Kernel.Special.GH_NumberSlider slider) {
                    List<string> changes = new List<string>();
                    
                    if (min.HasValue) {
                        slider.Slider.Minimum = (decimal)min.Value;
                        changes.Add("最小值=" + min.Value);
                    }
                    if (max.HasValue) {
                        slider.Slider.Maximum = (decimal)max.Value;
                        changes.Add("最大值=" + max.Value);
                    }
                    if (decimals.HasValue) {
                        int dec = Math.Max(0, Math.Min(10, decimals.Value));
                        slider.Slider.DecimalPlaces = dec;
                        changes.Add("小数位=" + dec);
                    }
                    
                    if (value != null) {
                        if (decimal.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal decVal)) {
                            slider.Slider.Value = decVal;
                            changes.Add("值=" + decVal);
                        } else { result = "Error: 数值解析失败。"; return; }
                    }
                    
                    if (changes.Count > 0) {
                        _canvasChanged = true;
                        try { doc.ScheduleSolution(150); } 
                        catch (Exception ex) { AddGhLog.Warn("ExecuteSetGhComponentValue (Slider) Schedule failed: " + ex.Message); }
                        result = "Slider 设置成功：" + string.Join("，", changes);
                    } else {
                        result = "未指定任何属性更改。";
                    }
                } else if (obj is Grasshopper.Kernel.Special.GH_Panel panel) {
                    if (value == null) {
                        result = "Error: Panel 必须提供 value 参数。"; return;
                    }
                    panel.UserText = value;
                    _canvasChanged = true;
                    try { doc.ScheduleSolution(150); } 
                    catch (Exception ex) { AddGhLog.Warn("ExecuteSetGhComponentValue (Panel) Schedule failed: " + ex.Message); }
                    result = "Panel 设置成功。";
                } else if (value == null) {
                    result = "Error: 修改脚本/表达式电池必须在 value 中提供完整代码或公式文本。";
                } else if (!string.IsNullOrWhiteSpace(exactMember) && TrySetScriptMemberExact(obj, exactMember, value, out string byName)) {
                    FinalizeGrasshopperScriptMutation(doc, obj);
                    result = "已按指定成员写入脚本/表达式（" + byName + "）。";
                    _canvasChanged = true;
                } else if (TrySetNativeScriptContentViaReflection(obj, value)) {
                    FinalizeGrasshopperScriptMutation(doc, obj);
                    result = "已写入内置脚本内容（反射 m_codeBlocks）。";
                    _canvasChanged = true;
                } else if (TrySetGrasshopperScriptOrFormula(obj, value, out string propName)) {
                    FinalizeGrasshopperScriptMutation(doc, obj);
                    result = "已写入脚本/表达式内容（" + propName + "）。";
                    _canvasChanged = true;
                } else {
                    string hint = DescribeWritableStringProperties(obj);
                    result = "Error: 未能自动写入代码/公式（未找到合适的 string 成员或写入被拒绝）。"
                        + hint
                        + " 可在 set_gh_component_value 中传 property 指定成员名；或根据 get_gh_components 中的 runtime_type_hint 反馈插件作者扩展。";
                }
                result += GetCanvasErrors(doc);
            }));
            return result;
        }
        
        private static string ExecuteModifyGhPortData(string id, bool isInput, int index, string operation)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }
                if (!Guid.TryParse(id, out Guid guid)) { result = "Error: ID 格式错误。"; return; }
                var obj = doc.FindObject(guid, true);
                if (obj == null) { result = "Error: 找不到电池。"; return; }

                Grasshopper.Kernel.IGH_Param param = null;
                if (obj is Grasshopper.Kernel.IGH_Component comp) {
                    var list = isInput ? comp.Params.Input : comp.Params.Output;
                    if (index >= 0 && index < list.Count) param = list[index];
                } else if (obj is Grasshopper.Kernel.IGH_Param p) {
                    param = p;
                }

                if (param == null) { result = "Error: 端口越界或不支持。"; return; }

                switch (operation.ToLower())
                {
                    case "flatten":
                        param.DataMapping = Grasshopper.Kernel.GH_DataMapping.Flatten;
                        break;
                    case "graft":
                        param.DataMapping = Grasshopper.Kernel.GH_DataMapping.Graft;
                        break;
                    case "simplify":
                        param.Simplify = !param.Simplify; 
                        break;
                    case "reverse":
                        param.Reverse = !param.Reverse; 
                        break;
                    case "none":
                        param.DataMapping = Grasshopper.Kernel.GH_DataMapping.None;
                        break;
                }

                param.ExpireSolution(true);
                _canvasChanged = true;
                try { doc.ScheduleSolution(150); } 
                catch (Exception ex) { AddGhLog.Warn("ExecuteModifyGhPortData Schedule failed: " + ex.Message); }
                result = "端口数据操作成功。";
            }));
            return result;
        }

        private static string ExecuteRemoveGhConnection(string fromId, int fromIndex, string toId, int toIndex)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }
                if (!Guid.TryParse(fromId, out Guid guidFrom) || !Guid.TryParse(toId, out Guid guidTo)) { result = "Error: ID 格式错误。"; return; }
                var objFrom = doc.FindObject(guidFrom, true);
                var objTo = doc.FindObject(guidTo, true);
                if (objFrom == null || objTo == null) { result = "Error: 找不到电池。"; return; }
                Grasshopper.Kernel.IGH_Param sourceParam = (objFrom is Grasshopper.Kernel.IGH_Component cF) ? (fromIndex < cF.Params.Output.Count ? cF.Params.Output[fromIndex] : null) : (objFrom as Grasshopper.Kernel.IGH_Param);
                Grasshopper.Kernel.IGH_Param targetParam = (objTo is Grasshopper.Kernel.IGH_Component cT) ? (toIndex < cT.Params.Input.Count ? cT.Params.Input[toIndex] : null) : (objTo as Grasshopper.Kernel.IGH_Param);
                if (sourceParam == null || targetParam == null) { result = "Error: 端口越界。"; return; }
                targetParam.RemoveSource(sourceParam);
                _canvasChanged = true;
                try { doc.ScheduleSolution(150); } 
                catch (Exception ex) { AddGhLog.Warn("ExecuteRemoveGhConnection Schedule failed: " + ex.Message); }
                result = "连线已断开。";
                result += GetCanvasErrors(doc);
            }));
            return result;
        }

        private static Grasshopper.Kernel.IGH_ObjectProxy FindComponentProxy(string name)
        {
            List<Grasshopper.Kernel.IGH_ObjectProxy> exactMatches = new List<Grasshopper.Kernel.IGH_ObjectProxy>();
            foreach (var p in Grasshopper.Instances.ComponentServer.ObjectProxies)
            {
                if (p.Obsolete) continue;
                // 第一优先级：完整名称精确匹配
                if (p.Desc.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { 
                    exactMatches.Add(p);
                }
            }

            // 如果没找到名称匹配，再尝试昵称匹配
            if (exactMatches.Count == 0)
            {
                foreach (var p in Grasshopper.Instances.ComponentServer.ObjectProxies)
                {
                    if (p.Obsolete) continue;
                    if (p.Desc.NickName.Equals(name, StringComparison.OrdinalIgnoreCase)) { 
                        exactMatches.Add(p);
                    }
                }
            }

            Grasshopper.Kernel.IGH_ObjectProxy proxy = null;
            if (exactMatches.Count > 0)
            {
                // 优先选择 Grasshopper 原生电池：检查描述里的分类和作者
                foreach (var p in exactMatches)
                {
                    string desc = p.Desc.ToString() ?? "";
                    string category = p.Desc.Category ?? "";
                    string subCategory = p.Desc.SubCategory ?? "";
                    
                    // 原生 Grasshopper 的常见分类
                    bool isNative = category.StartsWith("Math") || category.StartsWith("Sets") || 
                                    category.StartsWith("Vector") || category.StartsWith("Curve") ||
                                    category.StartsWith("Surface") || category.StartsWith("Mesh") ||
                                    category.StartsWith("Intersect") || category.StartsWith("Transform") ||
                                    category.StartsWith("Display") || category.StartsWith("Params") ||
                                    desc.Contains("McNeel") || desc.Contains("David Rutten");
                    
                    if (isNative)
                    {
                        proxy = p;
                        break;
                    }
                }
                // 如果没找到原生的，就用第一个
                if (proxy == null) proxy = exactMatches[0];
            }
            else
            {
                // 模糊匹配
                foreach (var p in Grasshopper.Instances.ComponentServer.ObjectProxies) {
                    if (p.Obsolete) continue;
                    if (p.Desc.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) { proxy = p; break; }
                }
            }
            return proxy;
        }

        private static Grasshopper.Kernel.IGH_ObjectProxy FindExactComponentProxyByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            foreach (var p in Grasshopper.Instances.ComponentServer.ObjectProxies)
            {
                if (p.Obsolete) continue;
                if (string.Equals(p.Desc.Name, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Desc.NickName, name, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return null;
        }

        private static string ResolveScriptComponentName(string mode)
        {
            string m = (mode ?? "").Trim().ToLowerInvariant();
            if (m == "csharp" || m == "cs" || m == "c#") return "C# Script";
            if (m == "python" || m == "py") return "Python 3 Script";
            return null;
        }

        private static string GetCSharpOutputPortName(int index)
        {
            if (index < 0) return "b";
            const string letters = "abcdefghijklmnopqrstuvwxyz";
            int shifted = index + 1;
            if (shifted < letters.Length) return letters[shifted].ToString();
            return "out" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static JArray BuildCSharpOutputPortsFromCount(int count)
        {
            count = Math.Max(1, Math.Min(26, count));
            var outputs = new JArray();
            for (int i = 0; i < count; i++)
            {
                outputs.Add(new JObject
                {
                    ["name"] = GetCSharpOutputPortName(i),
                    ["type_hint"] = "Auto-inferred C# output"
                });
            }
            return outputs;
        }

        private static JArray BuildCSharpOutputPortsFromLabels(JArray outputLabels)
        {
            int count = outputLabels == null ? 0 : Math.Min(26, outputLabels.Count);
            var outputs = new JArray();
            for (int i = 0; i < count; i++)
            {
                var spec = outputLabels != null && i < outputLabels.Count ? outputLabels[i] as JObject : null;
                string label = spec?["label"]?.ToString();
                if (string.IsNullOrWhiteSpace(label)) label = spec?["name"]?.ToString();
                string typeHint = spec?["type_hint"]?.ToString();
                outputs.Add(new JObject
                {
                    ["name"] = string.IsNullOrWhiteSpace(label) ? GetCSharpOutputPortName(i) : label.Trim(),
                    ["type_hint"] = typeHint ?? ""
                });
            }
            return outputs;
        }

        private static bool IsValidCSharpIdentifier(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            name = name.Trim();
            if (!(char.IsLetter(name[0]) || name[0] == '_')) return false;
            for (int i = 1; i < name.Length; i++)
            {
                if (!(char.IsLetterOrDigit(name[i]) || name[i] == '_')) return false;
            }

            var keywords = new HashSet<string>(StringComparer.Ordinal)
            {
                "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const",
                "continue","decimal","default","delegate","do","double","else","enum","event","explicit","extern",
                "false","finally","fixed","float","for","foreach","goto","if","implicit","in","int","interface",
                "internal","is","lock","long","namespace","new","null","object","operator","out","override","params",
                "private","protected","public","readonly","ref","return","sbyte","sealed","short","sizeof","stackalloc",
                "static","string","struct","switch","this","throw","true","try","typeof","uint","ulong","unchecked",
                "unsafe","ushort","using","virtual","void","volatile","while"
            };
            return !keywords.Contains(name);
        }

        private static void ApplyPortMetadata(Grasshopper.Kernel.IGH_Param param, JToken specToken, bool forceCSharpOutputName = false, int portIndex = 0, List<string> warnings = null)
        {
            if (param == null || specToken == null) return;
            string name = specToken["name"]?.ToString();
            string typeHint = specToken["type_hint"]?.ToString();
            bool appliedRuntimeTypeHint = false;
            if (!string.IsNullOrWhiteSpace(typeHint))
                appliedRuntimeTypeHint = TryApplyRuntimeTypeHint(param, typeHint, warnings);
            if (forceCSharpOutputName)
            {
                string forced = GetCSharpOutputPortName(portIndex);
                if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name.Trim(), forced, StringComparison.Ordinal))
                    warnings?.Add("C# 输出端口 " + name.Trim() + " 已规范为 " + forced + "；原名称写入 Description。");
                param.Name = forced;
                param.NickName = forced;
                var descParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(name)) descParts.Add("label: " + name.Trim());
                if (!string.IsNullOrWhiteSpace(typeHint)) descParts.Add("type: " + typeHint.Trim());
                if (descParts.Count > 0) param.Description = string.Join("; ", descParts);
            }
            else if (!string.IsNullOrWhiteSpace(name))
            {
                param.Name = name.Trim();
                param.NickName = name.Trim();
                if (!string.IsNullOrWhiteSpace(typeHint))
                    param.Description = appliedRuntimeTypeHint ? "[type_hint] " + typeHint.Trim() : typeHint.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(typeHint))
            {
                param.Description = appliedRuntimeTypeHint ? "[type_hint] " + typeHint.Trim() : typeHint.Trim();
            }
            param.Attributes?.ExpireLayout();
        }

        private static string NormalizeCSharpScriptSourceForMutableBlock(string source, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(source)) return source ?? "";
            string text = source.Replace("\r\n", "\n").Replace('\r', '\n');
            int runIdx = text.IndexOf("RunScript", StringComparison.Ordinal);
            if (runIdx < 0 || text.IndexOf("Script_Instance", StringComparison.Ordinal) < 0)
                return source;

            int open = text.IndexOf('{', runIdx);
            if (open < 0) return source;

            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        string body = text.Substring(open + 1, i - open - 1).Trim('\n', '\r');
                        warnings?.Add("检测到 C# 完整模板，已仅保留 RunScript 方法内部逻辑，默认 using/class/签名模板未替换。");
                        return body;
                    }
                }
            }

            return source;
        }

        private static bool TryFindRunScriptBodyBounds(string source, out int bodyStart, out int bodyEnd)
        {
            bodyStart = -1;
            bodyEnd = -1;
            if (string.IsNullOrEmpty(source)) return false;

            int runIdx = source.IndexOf("RunScript", StringComparison.Ordinal);
            if (runIdx < 0) return false;

            int open = source.IndexOf('{', runIdx);
            if (open < 0) return false;

            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                char ch = source[i];
                if (ch == '{') depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        bodyStart = open + 1;
                        bodyEnd = i;
                        return bodyEnd >= bodyStart;
                    }
                }
            }

            return false;
        }

        private static string IndentCSharpBodyForTemplate(string body, string indent)
        {
            string norm = (body ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n', '\r');
            if (string.IsNullOrEmpty(norm)) return "";

            var lines = norm.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length > 0) lines[i] = indent + lines[i];
                else lines[i] = indent;
            }
            return string.Join(Environment.NewLine, lines);
        }

        private static bool TrySetCSharpBodyByReplacingRunScriptInStringMembers(Grasshopper.Kernel.IGH_DocumentObject obj, string body, out string detail)
        {
            detail = null;
            if (obj == null || body == null) return false;

            for (Type t = obj.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var prop in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0 && p.GetSetMethod(true) != null)
                    .OrderByDescending(p => GhScriptMemberPreference(p.Name)))
                {
                    if (!GhScriptNameLooksLikePayload(prop.Name)) continue;
                    try
                    {
                        string current = prop.GetGetMethod(true)?.Invoke(obj, null) as string;
                        if (!TryReplaceRunScriptBodyInSource(current, body, out string updated)) continue;
                        prop.GetSetMethod(true).Invoke(obj, new object[] { updated });
                        detail = prop.Name + " (prop RunScript body)";
                        return true;
                    }
                    catch (Exception ex) { AddGhLog.Debug("TrySetCSharpBodyByReplacingRunScript prop " + prop.Name + ": " + ex.Message); }
                }

                foreach (var fld in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(f => f.FieldType == typeof(string))
                    .OrderByDescending(f => GhScriptMemberPreference(f.Name)))
                {
                    if (!GhScriptNameLooksLikePayload(fld.Name)) continue;
                    try
                    {
                        string current = fld.GetValue(obj) as string;
                        if (!TryReplaceRunScriptBodyInSource(current, body, out string updated)) continue;
                        fld.SetValue(obj, updated);
                        detail = fld.Name + " (field RunScript body)";
                        return true;
                    }
                    catch (Exception ex) { AddGhLog.Debug("TrySetCSharpBodyByReplacingRunScript field " + fld.Name + ": " + ex.Message); }
                }
            }

            return false;
        }

        private static bool TryReplaceRunScriptBodyInSource(string source, string body, out string updated)
        {
            updated = null;
            if (!TryFindRunScriptBodyBounds(source, out int bodyStart, out int bodyEnd)) return false;

            int lineStart = source.LastIndexOf('\n', Math.Max(0, bodyStart - 1));
            string newline = source.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            string indent = "        ";
            if (lineStart >= 0)
            {
                int i = lineStart + 1;
                var sb = new StringBuilder();
                while (i < source.Length && (source[i] == ' ' || source[i] == '\t'))
                {
                    sb.Append(source[i]);
                    i++;
                }
                if (sb.Length > 0) indent = sb.ToString();
            }

            string replacement = newline + IndentCSharpBodyForTemplate(body, indent) + newline + indent.Substring(0, Math.Max(0, indent.Length - 4));
            updated = source.Substring(0, bodyStart) + replacement + source.Substring(bodyEnd);
            return true;
        }

        private static bool TrySetCSharpScriptBodyIntoTemplate(Grasshopper.Kernel.IGH_DocumentObject obj, string source, List<string> warnings)
        {
            if (TrySetCSharpScriptBodyPreservingTemplate(obj, source, warnings))
                return true;

            if (TrySetCSharpBodyByReplacingRunScriptInStringMembers(obj, source, out string detail))
            {
                warnings?.Add("C# Script body was written by replacing the existing RunScript body in " + detail + ".");
                return true;
            }

            warnings?.Add("C# Script editable code block or full RunScript template was not found; refused unsafe full-template replacement.");
            return false;
        }

        private static bool TrySetCSharpScriptBodyPreservingTemplate(Grasshopper.Kernel.IGH_DocumentObject obj, string source, List<string> warnings)
        {
            string body = NormalizeCSharpScriptSourceForMutableBlock(source, warnings);
            Type t = obj?.GetType();
            if (t == null) return false;

            try
            {
                var codeBlocksField = FindInstanceFieldInHierarchy(t, "m_codeBlocks");
                if (codeBlocksField != null && codeBlocksField.GetValue(obj) is GH_CodeBlocks blocks)
                {
                    codeBlocksField.SetValue(obj, GhBuildCodeBlocksReplacingFirstMutable(blocks, body));
                    return true;
                }
            }
            catch (Exception ex)
            {
                AddGhLog.Debug("TrySetCSharpScriptBodyPreservingTemplate m_codeBlocks: " + ex.Message);
            }

            warnings?.Add("C# Script editable code block was not found; refused full-template replacement.");
            return false;
        }

        private static bool TryReadCSharpScriptBodyPreservingTemplate(Grasshopper.Kernel.IGH_DocumentObject obj, out string body, out string detail)
        {
            body = "";
            detail = "";
            Type t = obj?.GetType();
            if (t == null) return false;

            try
            {
                var codeBlocksField = FindInstanceFieldInHierarchy(t, "m_codeBlocks");
                if (codeBlocksField != null && codeBlocksField.GetValue(obj) is GH_CodeBlocks blocks)
                {
                    for (int i = 0; i < blocks.Count; i++)
                    {
                        GH_CodeBlock block = blocks[i];
                        if (block == null || block.ReadOnly) continue;
                        body = string.Join(Environment.NewLine, block.Lines ?? Array.Empty<string>());
                        detail = "m_codeBlocks[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
                        return true;
                    }
                    detail = "m_codeBlocks has no editable block.";
                }
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                AddGhLog.Debug("TryReadCSharpScriptBodyPreservingTemplate m_codeBlocks: " + ex.Message);
            }

            return false;
        }

        private static bool TryConfigureScriptPorts(Grasshopper.Kernel.IGH_DocumentObject obj, JArray inputs, JArray outputs, bool csharpMode, List<string> warnings)
        {
            if (!(obj is Grasshopper.Kernel.IGH_Component comp))
            {
                warnings?.Add((obj?.NickName ?? obj?.Name ?? "脚本电池") + " 不是可配置端口的组件。");
                return false;
            }

            if (!(obj is Grasshopper.Kernel.IGH_VariableParameterComponent vpc))
            {
                warnings?.Add((obj.NickName ?? obj.Name ?? "脚本电池") + " 不支持动态端口，已保留默认端口。");
            }
            else
            {
                bool Resize(IList<Grasshopper.Kernel.IGH_Param> list, Grasshopper.Kernel.GH_ParameterSide side, int target)
                {
                    while (list.Count < target)
                    {
                        var created = vpc.CreateParameter(side, list.Count);
                        if (created == null) return false;
                        if (side == Grasshopper.Kernel.GH_ParameterSide.Input) comp.Params.RegisterInputParam(created);
                        else comp.Params.RegisterOutputParam(created);
                    }
                    while (list.Count > target)
                    {
                        int last = list.Count - 1;
                        if (!vpc.CanRemoveParameter(side, last)) return false;
                        comp.Params.UnregisterParameter(list[last]);
                    }
                    return true;
                }

                int inputTarget = inputs?.Count ?? comp.Params.Input.Count;
                int outputTarget = outputs?.Count ?? comp.Params.Output.Count;
                if (!Resize(comp.Params.Input, Grasshopper.Kernel.GH_ParameterSide.Input, inputTarget))
                    warnings?.Add((obj.NickName ?? obj.Name ?? "脚本电池") + " 输入端口数量未能完全调整。");
                if (!Resize(comp.Params.Output, Grasshopper.Kernel.GH_ParameterSide.Output, outputTarget))
                    warnings?.Add((obj.NickName ?? obj.Name ?? "脚本电池") + " 输出端口数量未能完全调整。");

                try { vpc.VariableParameterMaintenance(); } catch (Exception ex) { warnings?.Add("端口维护失败：" + ex.Message); }
                try { comp.Params.OnParametersChanged(); } catch (Exception ex) { warnings?.Add("端口刷新失败：" + ex.Message); }
            }

            for (int i = 0; inputs != null && i < inputs.Count && i < comp.Params.Input.Count; i++)
                ApplyPortMetadata(comp.Params.Input[i], inputs[i], false, i, warnings);
            for (int i = 0; outputs != null && i < outputs.Count && i < comp.Params.Output.Count; i++)
                ApplyPortMetadata(comp.Params.Output[i], outputs[i], csharpMode, i, warnings);

            return true;
        }

        private static bool TryConfigureCSharpScriptPortsAfterDefaultCreate(Grasshopper.Kernel.IGH_DocumentObject obj, JArray inputs, JArray requestedOutputs, List<string> warnings)
        {
            if (!(obj is Grasshopper.Kernel.IGH_Component comp))
            {
                warnings?.Add((obj?.NickName ?? obj?.Name ?? "C# Script") + " is not a configurable component.");
                return false;
            }

            if (!(obj is Grasshopper.Kernel.IGH_VariableParameterComponent vpc))
            {
                warnings?.Add((obj.NickName ?? obj.Name ?? "C# Script") + " does not support dynamic ports; default ports were preserved.");
                return false;
            }

            bool changed = false;

            bool AddPort(Grasshopper.Kernel.GH_ParameterSide side, out Grasshopper.Kernel.IGH_Param created)
            {
                created = null;
                int index = side == Grasshopper.Kernel.GH_ParameterSide.Input ? comp.Params.Input.Count : comp.Params.Output.Count;
                created = vpc.CreateParameter(side, index);
                if (created == null) return false;
                if (side == Grasshopper.Kernel.GH_ParameterSide.Input) comp.Params.RegisterInputParam(created);
                else comp.Params.RegisterOutputParam(created);
                changed = true;
                return true;
            }

            if (inputs != null)
            {
                while (comp.Params.Input.Count < inputs.Count)
                {
                    if (!AddPort(Grasshopper.Kernel.GH_ParameterSide.Input, out _))
                    {
                        warnings?.Add("Failed to add one or more C# input ports; existing default inputs were preserved.");
                        break;
                    }
                }
            }

            var outputTargets = requestedOutputs ?? new JArray();
            var requestedOutputParams = new List<Grasshopper.Kernel.IGH_Param>();
            for (int i = 0; i < outputTargets.Count; i++)
            {
                string forcedName = GetCSharpOutputPortName(i);
                var existing = comp.Params.Output.FirstOrDefault(p =>
                    string.Equals(p.Name, forcedName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.NickName, forcedName, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    if (!AddPort(Grasshopper.Kernel.GH_ParameterSide.Output, out existing))
                    {
                        warnings?.Add("Failed to add C# output port " + forcedName + "; default out/a outputs were preserved.");
                        continue;
                    }
                }

                requestedOutputParams.Add(existing);
            }

            if (changed)
            {
                try { vpc.VariableParameterMaintenance(); } catch (Exception ex) { warnings?.Add("C# port maintenance failed: " + ex.Message); }
                try { comp.Params.OnParametersChanged(); } catch (Exception ex) { warnings?.Add("C# port refresh failed: " + ex.Message); }
            }

            for (int i = 0; inputs != null && i < inputs.Count && i < comp.Params.Input.Count; i++)
                ApplyPortMetadata(comp.Params.Input[i], inputs[i], false, i, warnings);

            for (int i = 0; i < outputTargets.Count && i < requestedOutputParams.Count; i++)
                ApplyPortMetadata(requestedOutputParams[i], outputTargets[i], true, i, warnings);

            return true;
        }

        private static bool IsCSharpScriptComponent(Grasshopper.Kernel.IGH_DocumentObject obj)
        {
            if (obj == null) return false;
            string name = obj.Name ?? "";
            string nick = obj.NickName ?? "";
            if (name.IndexOf("C#", StringComparison.OrdinalIgnoreCase) >= 0 && name.IndexOf("Script", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (nick.IndexOf("C#", StringComparison.OrdinalIgnoreCase) >= 0 && nick.IndexOf("Script", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (TryReflectGhScriptLanguage(obj, out GH_ScriptLanguage lang, out _) && lang == GH_ScriptLanguage.CS)
                return true;
            return obj.GetType()?.GetField("m_codeBlocks", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
        }

        private static string ExecuteEditCSharpScriptComponent(string id, string mode, string body)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: no active Grasshopper canvas."; return; }
                if (!Guid.TryParse(id, out Guid guid)) { result = "Error: invalid component id."; return; }
                var obj = doc.FindObject(guid, true);
                if (obj == null) { result = "Error: component not found."; return; }
                if (!IsCSharpScriptComponent(obj))
                {
                    result = "Error: target is not a Grasshopper C# Script component.";
                    return;
                }

                string m = (mode ?? "").Trim();
                if (m.Equals("read_body", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryReadCSharpScriptBodyPreservingTemplate(obj, out string currentBody, out string detail))
                    {
                        var payload = new JObject
                        {
                            ["status"] = "ok",
                            ["mode"] = "read_body",
                            ["id"] = obj.InstanceGuid.ToString(),
                            ["body"] = currentBody,
                            ["source"] = detail,
                            ["warning"] = "This is only the editable RunScript body. Do not add using/class/signature when writing it back."
                        };
                        result = payload.ToString(Formatting.None);
                    }
                    else
                    {
                        string fallback = GhReadScriptSourceViaReflection(obj, 150000, 120000);
                        try
                        {
                            var jo = JObject.Parse(fallback);
                            var payload = new JObject
                            {
                                ["status"] = "ok",
                                ["mode"] = "read_body",
                                ["id"] = obj.InstanceGuid.ToString(),
                                ["body"] = jo["primary_for_edit"]?.ToString() ?? "",
                                ["source"] = jo["primary_key"]?.ToString() ?? "reflection_fallback",
                                ["runtime_type_hint"] = jo["runtime_type_hint"]?.ToString() ?? "",
                                ["warning"] = "Editable code block structure was not recognized; returned reflection-based fallback text."
                            };
                            result = payload.ToString(Formatting.None);
                        }
                        catch (Exception ex)
                        {
                            result = "Error: could not read the editable C# Script body without touching the template. " + ex.Message;
                        }
                    }
                    return;
                }

                if (!m.Equals("set_body", StringComparison.OrdinalIgnoreCase))
                {
                    result = "Error: mode must be read_body or set_body.";
                    return;
                }
                if (body == null)
                {
                    result = "Error: set_body requires body.";
                    return;
                }

                var warnings = new List<string>();
                bool wrote = TrySetCSharpScriptBodyIntoTemplate(obj, body, warnings);
                if (!wrote)
                {
                    result = "Error: could not write C# Script body safely. The editable block structure was not recognized.";
                    if (warnings.Count > 0) result += " " + string.Join(" ", warnings);
                    return;
                }

                FinalizeGrasshopperScriptMutation(doc, obj);
                var payloadSet = new JObject
                {
                    ["status"] = "ok",
                    ["mode"] = "set_body",
                    ["id"] = obj.InstanceGuid.ToString(),
                    ["template_preserved"] = true,
                    ["warnings"] = new JArray(warnings)
                };
                string errors = GetCanvasErrors(doc);
                if (!string.IsNullOrWhiteSpace(errors)) payloadSet["canvas_errors"] = errors;
                result = payloadSet.ToString(Formatting.None);
            }));
            return result;
        }

        private static string ExecuteCreateCSharpScriptComponent(string aliasId, string label, float x, float y, JArray inputs, JArray outputs, string body, JArray components, JArray connections, string groupName = null)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: no active Grasshopper canvas."; return; }

                aliasId = string.IsNullOrWhiteSpace(aliasId) ? "core" : aliasId.Trim();
                var outputSpecs = BuildCSharpOutputPortsFromLabels(outputs);
                var outputNames = new HashSet<string>(Enumerable.Range(0, outputSpecs.Count).Select(GetCSharpOutputPortName), StringComparer.Ordinal);
                outputNames.Add("a");

                for (int i = 0; inputs != null && i < inputs.Count; i++)
                {
                    string inputName = inputs[i]?["name"]?.ToString()?.Trim();
                    if (!IsValidCSharpIdentifier(inputName))
                    {
                        result = "Error: C# input port name must be a valid identifier: " + (inputName ?? "");
                        return;
                    }
                    if (outputNames.Contains(inputName))
                    {
                        result = "Error: C# input port name '" + inputName + "' collides with reserved/output variable names. Rename the input.";
                        return;
                    }
                }

                var aliasSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                aliasSet.Add(aliasId);

                var scriptProxy = FindExactComponentProxyByName("C# Script");
                if (scriptProxy == null)
                {
                    result = "Error: cannot find C# Script component. Confirm the Grasshopper script component is loaded.";
                    return;
                }

                if (components != null)
                {
                    foreach (var c in components)
                    {
                        string alias = c["alias_id"]?.ToString();
                        if (string.IsNullOrWhiteSpace(alias)) { result = "Error: every helper component must provide alias_id."; return; }
                        if (!aliasSet.Add(alias)) { result = "Error: duplicate alias_id: " + alias; return; }

                        string name = c["name"]?.ToString();
                        string cguid = c["component_guid"]?.ToString();
                        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(cguid))
                        {
                            result = "Error: helper component " + alias + " must provide name or component_guid.";
                            return;
                        }

                        var probe = InstantiateDocumentObjectFromLibrary(name ?? "", cguid);
                        if (probe == null)
                        {
                            result = "Error: cannot instantiate helper component " + alias + ".";
                            return;
                        }
                        if (!IsScriptModeAuxiliaryComponentAllowed(probe))
                        {
                            result = BuildScriptModeAuxiliaryComponentError(probe, name ?? alias);
                            return;
                        }
                    }
                }

                var createdObjs = new Dictionary<string, Grasshopper.Kernel.IGH_DocumentObject>(StringComparer.OrdinalIgnoreCase);
                var aliasMap = new JObject();
                var warnings = new List<string>();

                var scriptObj = scriptProxy.CreateInstance() as Grasshopper.Kernel.IGH_DocumentObject;
                if (!(scriptObj is Grasshopper.Kernel.IGH_Component))
                {
                    result = "Error: C# Script component cannot be instantiated as a connectable component.";
                    return;
                }
                scriptObj.CreateAttributes();
                scriptObj.Attributes.Pivot = new System.Drawing.PointF(x, y);
                doc.AddObject(scriptObj, false);

                ShowThinkingAnimation("正在稳定 C# 电池...");
                WaitForUiResponsiveDelay(500);
                ShowThinkingAnimation("正在配置 C# 电池...");

                if (!string.IsNullOrWhiteSpace(label))
                {
                    scriptObj.NickName = label.Trim();
                    scriptObj.Attributes?.ExpireLayout();
                }

                TryConfigureCSharpScriptPortsAfterDefaultCreate(scriptObj, inputs, outputSpecs, warnings);

                bool wrote = TrySetCSharpScriptBodyIntoTemplate(scriptObj, body ?? "", warnings);
                if (wrote)
                {
                    try { scriptObj.ExpireSolution(false); }
                    catch (Exception ex) { warnings.Add("C# Script expire failed: " + ex.Message); }
                }
                else warnings.Add("C# Script body was not written.");

                createdObjs[aliasId] = scriptObj;
                aliasMap[aliasId] = scriptObj.InstanceGuid.ToString();

                if (components != null)
                {
                    foreach (var c in components)
                    {
                        string name = c["name"]?.ToString();
                        string cguid = c["component_guid"]?.ToString();
                        string helperLabel = c["label"]?.ToString();
                        float hx = c["x"]?.ToObject<float>() ?? 0;
                        float hy = c["y"]?.ToObject<float>() ?? 0;
                        string val = c["value"]?.ToString();
                        string graphMapperType = GetGraphMapperTypeRequest(c, val);
                        double? min = c["min"]?.ToObject<double>();
                        double? max = c["max"]?.ToObject<double>();
                        int? decimals = c["decimals"]?.ToObject<int>();
                        string alias = c["alias_id"]?.ToString();

                        var obj = InstantiateDocumentObjectFromLibrary(name ?? "", cguid);
                        obj.CreateAttributes();
                        obj.Attributes.Pivot = new System.Drawing.PointF(hx, hy);
                        if (!string.IsNullOrEmpty(helperLabel)) obj.NickName = helperLabel;
                        bool isGraphMapper = IsGraphMapperObject(obj);
                        if (isGraphMapper && !TrySetGraphMapperType(obj, graphMapperType, out string graphMapperDetail))
                        {
                            result = graphMapperDetail;
                            return;
                        }
                        doc.AddObject(obj, false);

                        if (obj is Grasshopper.Kernel.Special.GH_NumberSlider slider)
                        {
                            if (min.HasValue) slider.Slider.Minimum = (decimal)min.Value;
                            if (max.HasValue) slider.Slider.Maximum = (decimal)max.Value;
                            if (decimals.HasValue) slider.Slider.DecimalPlaces = Math.Max(0, Math.Min(10, decimals.Value));
                            if (!string.IsNullOrEmpty(val) && decimal.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal d))
                                slider.Slider.Value = d;
                        }
                        else if (obj is Grasshopper.Kernel.Special.GH_Panel panel && !string.IsNullOrEmpty(val))
                        {
                            panel.UserText = val;
                        }
                        else if (isGraphMapper)
                        {
                        }

                        createdObjs[alias] = obj;
                        aliasMap[alias] = obj.InstanceGuid.ToString();
                    }
                }

                int connected = 0;
                if (connections != null)
                {
                    warnings.Add("C# Script connections were skipped during creation to avoid Grasshopper/Rhino crashes. Use connect_gh_components in a later step after the C# Script component is stable.");
                }

                if (!string.IsNullOrWhiteSpace(groupName) && createdObjs.Count > 0)
                {
                    var group = new Grasshopper.Kernel.Special.GH_Group();
                    group.NickName = groupName;
                    group.Colour = System.Drawing.Color.FromArgb(80, 100, 150, 250);
                    foreach (var obj in createdObjs.Values) group.AddObject(obj.InstanceGuid);
                    doc.AddObject(group, false);
                    try { group.ExpireSolution(false); } catch { }
                }

                _canvasChanged = true;
                try { doc.ScheduleSolution(150); }
                catch (Exception ex) { AddGhLog.Warn("ExecuteCreateCSharpScriptComponent Schedule failed: " + ex.Message); }

                var payload = new JObject
                {
                    ["status"] = "ok",
                    ["mode"] = "csharp",
                    ["created_scripts"] = 1,
                    ["created_components"] = components?.Count ?? 0,
                    ["created_connections"] = connected,
                    ["skipped_connections"] = connections?.Count ?? 0,
                    ["script_write_ok"] = wrote ? 1 : 0,
                    ["forced_output_variables"] = new JArray(Enumerable.Range(0, outputSpecs.Count).Select(GetCSharpOutputPortName)),
                    ["aliases"] = aliasMap,
                    ["warnings"] = new JArray(warnings)
                };
                string errors = GetCanvasErrors(doc);
                if (!string.IsNullOrWhiteSpace(errors)) payload["canvas_errors"] = errors;
                result = payload.ToString(Formatting.None);
            }));
            return result;
        }

        private static string ExecuteCreateScriptComponentGraph(string mode, JArray scripts, JArray components, JArray connections, string groupName = null)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }

                string scriptComponentName = ResolveScriptComponentName(mode);
                if (string.IsNullOrWhiteSpace(scriptComponentName))
                {
                    result = "Error: mode 必须是 csharp 或 python。";
                    return;
                }

                if (scriptComponentName == "C# Script")
                {
                    result = "Error: C# Script must be created with create_csharp_script_component so ports are configured before only the RunScript body is written.";
                    return;
                }

                if (scripts == null || scripts.Count == 0)
                {
                    result = "Error: scripts 至少需要一个脚本电池定义。";
                    return;
                }

                var aliasSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var scriptProxy = FindExactComponentProxyByName(scriptComponentName);
                if (scriptProxy == null)
                {
                    result = scriptComponentName == "Python 3 Script"
                        ? "Error: 找不到 Python 3 Script 电池。混合模式只适配 Rhino 8 Python 3 Script，请确认已安装并启用该组件。"
                        : "Error: 找不到 C# Script 电池。请确认 Grasshopper 脚本组件已加载。";
                    return;
                }

                foreach (var s in scripts)
                {
                    string alias = s["alias_id"]?.ToString();
                    if (string.IsNullOrWhiteSpace(alias)) { result = "Error: 每个脚本电池都必须提供 alias_id。"; return; }
                    if (!aliasSet.Add(alias)) { result = "Error: alias_id 重复：" + alias; return; }
                    var probe = scriptProxy.CreateInstance() as Grasshopper.Kernel.IGH_DocumentObject;
                    if (probe == null) { result = "Error: 无法实例化 " + scriptComponentName + "。"; return; }
                    if (!(probe is Grasshopper.Kernel.IGH_Component)) { result = "Error: " + scriptComponentName + " 不是可连线组件。"; return; }
                }

                if (components != null)
                {
                    foreach (var c in components)
                    {
                        string alias = c["alias_id"]?.ToString();
                        if (string.IsNullOrWhiteSpace(alias)) { result = "Error: 每个辅助电池都必须提供 alias_id。"; return; }
                        if (!aliasSet.Add(alias)) { result = "Error: alias_id 重复：" + alias; return; }
                        string name = c["name"]?.ToString();
                        string cguid = c["component_guid"]?.ToString();
                        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(cguid))
                        {
                            result = "Error: 辅助电池 " + alias + " 必须提供 name 或 component_guid。";
                            return;
                        }
                        var probe = InstantiateDocumentObjectFromLibrary(name ?? "", cguid);
                        if (probe == null)
                        {
                            result = "Error: 无法实例化辅助电池 " + alias + "。";
                            return;
                        }
                        if (!IsScriptModeAuxiliaryComponentAllowed(probe))
                        {
                            result = BuildScriptModeAuxiliaryComponentError(probe, name ?? alias);
                            return;
                        }
                    }
                }

                var createdObjs = new Dictionary<string, Grasshopper.Kernel.IGH_DocumentObject>(StringComparer.OrdinalIgnoreCase);
                var aliasMap = new JObject();
                var warnings = new List<string>();
                int scriptWriteOk = 0;

                foreach (var s in scripts)
                {
                    string alias = s["alias_id"]?.ToString();
                    string label = s["label"]?.ToString();
                    string source = s["source"]?.ToString() ?? "";
                    float x = s["x"]?.ToObject<float>() ?? 0f;
                    float y = s["y"]?.ToObject<float>() ?? 0f;

                    var obj = scriptProxy.CreateInstance() as Grasshopper.Kernel.IGH_DocumentObject;
                    obj.CreateAttributes();
                    obj.Attributes.Pivot = new System.Drawing.PointF(x, y);
                    if (!string.IsNullOrWhiteSpace(label)) obj.NickName = label;
                    doc.AddObject(obj, false);

                    JArray outputSpecs = s["outputs"] as JArray;
                    if (scriptComponentName == "C# Script")
                    {
                        if (outputSpecs != null && outputSpecs.Count > 0)
                            warnings.Add("C# mode is no longer handled by create_script_component_graph; use create_csharp_script_component.");
                        int outputCount = s["output_count"]?.ToObject<int?>() ?? 1;
                        outputSpecs = BuildCSharpOutputPortsFromCount(outputCount);
                    }

                    TryConfigureScriptPorts(obj, s["inputs"] as JArray, outputSpecs, scriptComponentName == "C# Script", warnings);

                    bool wrote = false;
                    if (scriptComponentName == "C# Script")
                    {
                        wrote = TrySetCSharpScriptBodyIntoTemplate(obj, source, warnings);
                    }
                    else
                    {
                        wrote = TrySetScriptMemberExact(obj, "Text", source, out _);
                        if (!wrote) wrote = TrySetGrasshopperScriptOrFormula(obj, source, out _);
                    }

                    if (wrote)
                    {
                        scriptWriteOk++;
                        FinalizeGrasshopperScriptMutation(doc, obj);
                    }
                    else
                    {
                        warnings.Add("脚本源码未能写入：" + alias);
                    }

                    createdObjs[alias] = obj;
                    aliasMap[alias] = obj.InstanceGuid.ToString();
                }

                if (components != null)
                {
                    foreach (var c in components)
                    {
                        string name = c["name"]?.ToString();
                        string cguid = c["component_guid"]?.ToString();
                        string label = c["label"]?.ToString();
                        float x = c["x"]?.ToObject<float>() ?? 0;
                        float y = c["y"]?.ToObject<float>() ?? 0;
                        string val = c["value"]?.ToString();
                        string graphMapperType = GetGraphMapperTypeRequest(c, val);
                        double? min = c["min"]?.ToObject<double>();
                        double? max = c["max"]?.ToObject<double>();
                        int? decimals = c["decimals"]?.ToObject<int>();
                        string alias = c["alias_id"]?.ToString();

                        var obj = InstantiateDocumentObjectFromLibrary(name ?? "", cguid);
                        obj.CreateAttributes();
                        obj.Attributes.Pivot = new System.Drawing.PointF(x, y);
                        if (!string.IsNullOrEmpty(label)) obj.NickName = label;
                        bool isGraphMapper = IsGraphMapperObject(obj);
                        if (isGraphMapper && !TrySetGraphMapperType(obj, graphMapperType, out string graphMapperDetail))
                        {
                            result = graphMapperDetail;
                            return;
                        }
                        doc.AddObject(obj, false);

                        if (obj is Grasshopper.Kernel.Special.GH_NumberSlider slider)
                        {
                            if (min.HasValue) slider.Slider.Minimum = (decimal)min.Value;
                            if (max.HasValue) slider.Slider.Maximum = (decimal)max.Value;
                            if (decimals.HasValue) slider.Slider.DecimalPlaces = Math.Max(0, Math.Min(10, decimals.Value));
                            if (!string.IsNullOrEmpty(val) && decimal.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal d))
                                slider.Slider.Value = d;
                        }
                        else if (obj is Grasshopper.Kernel.Special.GH_Panel panel && !string.IsNullOrEmpty(val))
                        {
                            panel.UserText = val;
                        }
                        else if (isGraphMapper)
                        {
                        }

                        createdObjs[alias] = obj;
                        aliasMap[alias] = obj.InstanceGuid.ToString();
                    }
                }

                int connected = 0;
                if (connections != null)
                {
                    foreach (var conn in connections)
                    {
                        if (createdObjs.TryGetValue(conn["from_alias"]?.ToString(), out var f) && createdObjs.TryGetValue(conn["to_alias"]?.ToString(), out var t))
                        {
                            int fIdx = conn["from_index"]?.ToObject<int>() ?? 0;
                            int tIdx = conn["to_index"]?.ToObject<int>() ?? 0;
                            var sP = (f is Grasshopper.Kernel.IGH_Component cF) ? (fIdx >= 0 && fIdx < cF.Params.Output.Count ? cF.Params.Output[fIdx] : null) : (f as Grasshopper.Kernel.IGH_Param);
                            var tP = (t is Grasshopper.Kernel.IGH_Component cT) ? (tIdx >= 0 && tIdx < cT.Params.Input.Count ? cT.Params.Input[tIdx] : null) : (t as Grasshopper.Kernel.IGH_Param);
                            if (sP != null && tP != null)
                            {
                                tP.AddSource(sP);
                                connected++;
                            }
                            else
                            {
                                warnings.Add("连线端口越界：" + conn["from_alias"] + " -> " + conn["to_alias"]);
                            }
                        }
                        else
                        {
                            warnings.Add("连线引用了不存在的 alias：" + conn["from_alias"] + " -> " + conn["to_alias"]);
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(groupName) && createdObjs.Count > 0)
                {
                    var group = new Grasshopper.Kernel.Special.GH_Group();
                    group.NickName = groupName;
                    group.Colour = System.Drawing.Color.FromArgb(80, 100, 150, 250);
                    foreach (var obj in createdObjs.Values) group.AddObject(obj.InstanceGuid);
                    doc.AddObject(group, false);
                    group.ExpireSolution(true);
                }

                _canvasChanged = true;
                try { doc.ScheduleSolution(150); }
                catch (Exception ex) { AddGhLog.Warn("ExecuteCreateScriptComponentGraph Schedule failed: " + ex.Message); }

                var payload = new JObject
                {
                    ["status"] = "ok",
                    ["mode"] = scriptComponentName == "C# Script" ? "csharp" : "python",
                    ["created_scripts"] = scripts.Count,
                    ["created_components"] = components?.Count ?? 0,
                    ["created_connections"] = connected,
                    ["script_write_ok"] = scriptWriteOk,
                    ["aliases"] = aliasMap,
                    ["warnings"] = new JArray(warnings)
                };
                string errors = GetCanvasErrors(doc);
                if (!string.IsNullOrWhiteSpace(errors)) payload["canvas_errors"] = errors;
                result = payload.ToString(Formatting.None);
            }));
            return result;
        }

        private static string ExecuteCreateComponentGraph(JArray components, JArray connections, string groupName = null)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }

                Dictionary<string, Grasshopper.Kernel.IGH_DocumentObject> createdObjs = new Dictionary<string, Grasshopper.Kernel.IGH_DocumentObject>();

                if (components != null) {
                    foreach (var c in components) {
                        string name = c["name"]?.ToString();
                        string cguid = c["component_guid"]?.ToString();
                        string label = c["label"]?.ToString();
                        float x = c["x"]?.ToObject<float>() ?? 0;
                        float y = c["y"]?.ToObject<float>() ?? 0;
                        string val = c["value"]?.ToString();
                        string graphMapperType = GetGraphMapperTypeRequest(c, val);
                        double? min = c["min"]?.ToObject<double>();
                        double? max = c["max"]?.ToObject<double>();
                        int? decimals = c["decimals"]?.ToObject<int>();
                        string alias = c["alias_id"]?.ToString();

                        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(cguid))
                            continue;

                        var obj = InstantiateDocumentObjectFromLibrary(name ?? "", cguid);

                        if (obj != null) {
                            obj.CreateAttributes();
                            obj.Attributes.Pivot = new System.Drawing.PointF(x, y);
                            if (!string.IsNullOrEmpty(label)) obj.NickName = label;
                            bool isGraphMapper = IsGraphMapperObject(obj);
                            if (isGraphMapper && !TrySetGraphMapperType(obj, graphMapperType, out string graphMapperDetail)) {
                                result = graphMapperDetail;
                                return;
                            }
                            doc.AddObject(obj, false);

                            if (obj is Grasshopper.Kernel.Special.GH_NumberSlider s) {
                                if (min.HasValue) s.Slider.Minimum = (decimal)min.Value;
                                if (max.HasValue) s.Slider.Maximum = (decimal)max.Value;
                                if (decimals.HasValue) s.Slider.DecimalPlaces = Math.Max(0, Math.Min(10, decimals.Value));
                                if (!string.IsNullOrEmpty(val) && decimal.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal d))
                                    s.Slider.Value = d;
                            }
                            else if (isGraphMapper) {
                            }
                            else if (obj is Grasshopper.Kernel.Special.GH_Panel p && !string.IsNullOrEmpty(val)) {
                                p.UserText = val;
                            }
                            else if (!string.IsNullOrEmpty(val) && TrySetGrasshopperScriptOrFormula(obj, val, out _)) {
                                obj.ExpireSolution(true);
                            }

                            if (!string.IsNullOrEmpty(alias)) createdObjs[alias] = obj;
                        }
                    }
                }

                if (connections != null) {
                    foreach (var conn in connections) {
                        if (createdObjs.TryGetValue(conn["from_alias"]?.ToString(), out var f) && createdObjs.TryGetValue(conn["to_alias"]?.ToString(), out var t)) {
                            int fIdx = conn["from_index"]?.ToObject<int>() ?? 0;
                            int tIdx = conn["to_index"]?.ToObject<int>() ?? 0;
                            var sP = (f is Grasshopper.Kernel.IGH_Component cF) ? (fIdx < cF.Params.Output.Count ? cF.Params.Output[fIdx] : null) : (f as Grasshopper.Kernel.IGH_Param);
                            var tP = (t is Grasshopper.Kernel.IGH_Component cT) ? (tIdx < cT.Params.Input.Count ? cT.Params.Input[tIdx] : null) : (t as Grasshopper.Kernel.IGH_Param);
                            if (sP != null && tP != null) tP.AddSource(sP);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(groupName) && createdObjs.Count > 0)
                {
                    var group = new Grasshopper.Kernel.Special.GH_Group();
                    group.NickName = groupName;
                    group.Colour = System.Drawing.Color.FromArgb(80, 100, 150, 250);
                    foreach (var obj in createdObjs.Values) group.AddObject(obj.InstanceGuid);
                    doc.AddObject(group, false);
                    group.ExpireSolution(true);
                }

                _canvasChanged = true;
                try { doc.ScheduleSolution(150); } 
                catch (Exception ex) { AddGhLog.Warn("ExecuteCreateComponentGraph Schedule failed: " + ex.Message); }
                result = "图谱构建完成。";
                result += GetCanvasErrors(doc);
            }));
            return result;
        }

        private static string ExecuteCheckGhErrors()
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }
                result = GetCanvasErrors(doc);
                if (string.IsNullOrEmpty(result)) result = "一切正常。";
            }));
            return result;
        }

        private static string ExecuteRecomputeGhCanvas()
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }
                _canvasChanged = true;
                try { doc.ScheduleSolution(150); } 
                catch (Exception ex) { AddGhLog.Warn("ExecuteRecomputeGhCanvas Schedule failed: " + ex.Message); }
                try { Grasshopper.Instances.ActiveCanvas?.Refresh(); } 
                catch (Exception ex) { AddGhLog.Debug("ExecuteRecomputeGhCanvas Refresh failed: " + ex.Message); }
                result = "已触发画布重新求解（含延迟再算）。";
            }));
            return result;
        }

        /// <summary>
        /// 内置 C#/VB 脚本编辑器使用多块源码（只读模板 + RunScript 等可编辑段）。整块替换成单个 block 会破坏结构导致 Rhino 崩溃。
        /// </summary>
        private static GH_CodeBlocks GhBuildCodeBlocksReplacingFirstMutable(GH_CodeBlocks baseline, string text)
        {
            string norm = text == null ? "" : text.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] newLines = norm.Length == 0 ? Array.Empty<string>() : norm.Split('\n');

            if (baseline == null || baseline.Count == 0)
            {
                var fb = new GH_CodeBlocks();
                fb.Add(new GH_CodeBlock(newLines, false));
                fb.MergeConsecutiveBlocks();
                return fb;
            }

            var merged = new GH_CodeBlocks();
            bool replacedFirstMutable = false;

            for (int i = 0; i < baseline.Count; i++)
            {
                GH_CodeBlock b = baseline[i];
                bool ro = b.ReadOnly;
                string[] copyLines = (b.Lines ?? Enumerable.Empty<string>()).ToArray();

                if (!ro && !replacedFirstMutable)
                {
                    merged.Add(new GH_CodeBlock(newLines, false));
                    replacedFirstMutable = true;
                }
                else
                    merged.Add(new GH_CodeBlock(copyLines, ro));
            }

            if (!replacedFirstMutable)
                merged.Add(new GH_CodeBlock(newLines, false));

            merged.MergeConsecutiveBlocks();
            return merged;
        }

        private static GH_ScriptLanguage ParseGhNativeScriptLanguageHint(string hint)
        {
            if (string.IsNullOrWhiteSpace(hint) || string.Equals(hint, "auto", StringComparison.OrdinalIgnoreCase))
                return GH_ScriptLanguage.CS;
            string h = hint.Trim();
            if (h.StartsWith("vb", StringComparison.OrdinalIgnoreCase)) return GH_ScriptLanguage.VB;
            return GH_ScriptLanguage.CS;
        }

        private static bool TryReflectGhScriptLanguage(Grasshopper.Kernel.IGH_DocumentObject obj, out GH_ScriptLanguage lang, out string fromMember)
        {
            fromMember = null;
            lang = GH_ScriptLanguage.CS;
            if (obj == null) return false;
            Type tEnum = typeof(GH_ScriptLanguage);
            for (Type t = obj.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (p.PropertyType != tEnum) continue;
                    try
                    {
                        object v = p.GetValue(obj);
                        if (v is GH_ScriptLanguage sl && sl != GH_ScriptLanguage.None)
                        {
                            lang = sl;
                            fromMember = p.Name;
                            return true;
                        }
                    }
                    catch (Exception ex) { AddGhLog.Debug("TryReflectGhScriptLanguage: " + ex.Message); }
                }
            }
            return false;
        }

        private static GH_ScriptLanguage ResolveGhNativeScriptLanguage(Grasshopper.Kernel.IGH_DocumentObject obj, string hint)
        {
            if (TryReflectGhScriptLanguage(obj, out GH_ScriptLanguage refl, out _))
                return refl;

            if (obj is Grasshopper.Kernel.IGH_ActiveObject act)
            {
                string nick = act.NickName ?? "";
                if (nick.IndexOf("vb", StringComparison.OrdinalIgnoreCase) >= 0)
                    return GH_ScriptLanguage.VB;
            }

            return ParseGhNativeScriptLanguageHint(hint);
        }

        private static bool TryPerformGhScriptEditorOk(GH_ScriptEditor editor)
        {
            if (editor == null) return false;
            try
            {
                PropertyInfo pi = typeof(GH_ScriptEditor).GetProperty("OKButton", BindingFlags.Instance | BindingFlags.NonPublic);
                if (pi?.GetValue(editor) is System.Windows.Forms.Button ok)
                {
                    ok.PerformClick();
                    return true;
                }
            }
            catch (Exception ex) { AddGhLog.Debug("TryPerformGhScriptEditorOk: " + ex.Message); }
            return false;
        }

        private static bool IsGhScriptEditorDisposed(GH_ScriptEditor editor)
        {
            if (editor == null) return true;
            try
            {
                var pi = editor.GetType().GetProperty("IsDisposed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi?.GetValue(editor) is bool disposed) return disposed;
            }
            catch (Exception ex) { AddGhLog.Debug("IsGhScriptEditorDisposed: " + ex.Message); }
            return false;
        }

        private static bool TrySetGhScriptEditorProperty(GH_ScriptEditor editor, string name, object value)
        {
            if (editor == null || string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                var pi = editor.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi == null || !pi.CanWrite) return false;
                pi.SetValue(editor, value);
                return true;
            }
            catch (Exception ex) { AddGhLog.Debug("TrySetGhScriptEditorProperty " + name + ": " + ex.Message); }
            return false;
        }

        private static bool TryInvokeGhScriptEditorMethod(GH_ScriptEditor editor, string name)
        {
            if (editor == null || string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                var mi = editor.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (mi == null) return false;
                mi.Invoke(editor, null);
                return true;
            }
            catch (Exception ex) { AddGhLog.Debug("TryInvokeGhScriptEditorMethod " + name + ": " + ex.Message); }
            return false;
        }

        /// <summary>
        /// 在脚本编辑器 UI 线程上同步执行（避免 Show 后立刻改控件与点 OK 时序错乱）。
        /// </summary>
        private static void GhScriptEditorRunOnUi(GH_ScriptEditor editor, Action work)
        {
            if (editor == null || work == null) return;
            if (IsGhScriptEditorDisposed(editor)) return;
            void Do() { if (!IsGhScriptEditorDisposed(editor)) work(); }

            try
            {
                var invokeRequired = editor.GetType().GetProperty("InvokeRequired", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (invokeRequired?.GetValue(editor) is bool required && required)
                {
                    var invoke = editor.GetType().GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Delegate) }, null);
                    if (invoke != null)
                    {
                        invoke.Invoke(editor, new object[] { (Action)Do });
                        return;
                    }
                }
            }
            catch (Exception ex) { AddGhLog.Debug("GhScriptEditorRunOnUi invoke: " + ex.Message); }

            Do();
        }

        /// <summary> OK 已把脚本写回电池并会自行触发求解；此处仅轻量排队，避免与编辑器内部 NewSolution 重入。 </summary>
        private static void AfterNativeScriptEditorCommit(GH_Document doc)
        {
            _canvasChanged = true;
            try { doc?.ScheduleSolution(80); } catch (Exception ex) { AddGhLog.Debug("AfterNativeScriptEditorCommit Schedule: " + ex.Message); }
            try { Grasshopper.Instances.ActiveCanvas?.Refresh(); } catch { }
        }

        private static string ExecuteGhNativeScriptEditor(string id, string mode, string code, string languageHint)
        {
            const int readCap = 150000;
            string result = "";
            string mraw = mode?.Trim() ?? "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                try
                {
                    var canvas = Grasshopper.Instances.ActiveCanvas;
                    var doc = canvas?.Document;
                    if (canvas == null || doc == null) { result = "Error: 没有打开的画布。"; return; }

                    bool isOpen = mraw.Equals("open_focus", StringComparison.OrdinalIgnoreCase);
                    bool isRead = mraw.Equals("read_source", StringComparison.OrdinalIgnoreCase);
                    bool isSet = mraw.Equals("set_source_commit", StringComparison.OrdinalIgnoreCase);
                    if (!isOpen && !isRead && !isSet)
                    {
                        result = "Error: mode 必须是 open_focus、read_source 或 set_source_commit。";
                        return;
                    }

                    if (!Guid.TryParse(id, out Guid guid)) { result = "Error: ID 格式错误。"; return; }
                    var obj = doc.FindObject(guid, true);
                    if (obj == null) { result = "Error: 找不到电池。"; return; }

                    if (isSet && code == null) { result = "Error: set_source_commit 需要 code 参数。"; return; }

                    if (isRead)
                    {
                        int perMember = Math.Min(readCap, 120000);
                        result = GhReadScriptSourceViaReflection(obj, readCap, perMember);
                        return;
                    }

                    GH_ScriptEditor existing = GH_ScriptEditor.FindScriptEditor(obj);
                    GH_ScriptLanguage lang = ResolveGhNativeScriptLanguage(obj, languageHint);

                    GH_ScriptEditor editor = existing;
                    if (editor == null)
                    {
                        try
                        {
                            editor = new GH_ScriptEditor(lang, obj);
                        }
                        catch (Exception ex)
                        {
                            result = "Error: 无法创建原生 GH_ScriptEditor（该宿主可能不是内置 C#/VB Script，例如 GhPython 或 RhinoCode；请改用 set_gh_component_value）：" + ex.Message;
                            return;
                        }
                    }

                    if (isOpen)
                    {
                        TrySetGhScriptEditorProperty(editor, "WindowState", System.Windows.Forms.FormWindowState.Normal);
                        TrySetGhScriptEditorProperty(editor, "StartPosition", System.Windows.Forms.FormStartPosition.CenterParent);
                        if (!editor.Visible)
                            editor.Show(Grasshopper.Instances.DocumentEditor);
                        TryInvokeGhScriptEditorMethod(editor, "BringToFront");
                        TryInvokeGhScriptEditorMethod(editor, "Activate");
                        result = "已打开或聚焦原生脚本编辑器。" + GetCanvasErrors(doc);
                        return;
                    }

                    // 对于 set_source_commit，先尝试使用反射直接修改电池，避免打开编辑器窗口（这是崩溃的主要原因）
                    bool directSetSuccess = false;
                    try
                    {
                        directSetSuccess = TrySetNativeScriptContentViaReflection(obj, code);
                        if (directSetSuccess)
                        {
                            obj.ExpireSolution(true);
                            _canvasChanged = true;
                            try { doc.ScheduleSolution(150); } 
                            catch (Exception ex) { AddGhLog.Warn("SetNativeScript Schedule failed: " + ex.Message); }
                            try { Grasshopper.Instances.ActiveCanvas?.Refresh(); } 
                            catch (Exception ex) { AddGhLog.Debug("SetNativeScript Refresh failed: " + ex.Message); }
                            result = "已直接写入脚本内容（避免了编辑器窗口）。" + GetCanvasErrors(doc);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        AddGhLog.Warn("Direct set native script failed, falling back to editor: " + ex.Message);
                    }

                    // 如果反射失败，作为备选方案使用编辑器（但要更安全）
                    if (isSet && !editor.Visible)
                    {
                        try
                        {
                            TrySetGhScriptEditorProperty(editor, "StartPosition", System.Windows.Forms.FormStartPosition.Manual);
                            TrySetGhScriptEditorProperty(editor, "ShowInTaskbar", false);
                            TrySetGhScriptEditorProperty(editor, "Location", new System.Drawing.Point(-10000, -10000));
                            editor.Show(Grasshopper.Instances.DocumentEditor);
                            // 给一点时间让窗口初始化
                            System.Threading.Thread.Sleep(50);
                        }
                        catch (Exception ex)
                        {
                            AddGhLog.Warn("Failed to show editor offscreen: " + ex.Message);
                        }
                    }

                    bool okClicked = false;
                    try
                    {
                        GhScriptEditorRunOnUi(editor, () =>
                        {
                            GH_CodeBlocks baseline = editor.GetSourceCode();
                            GH_CodeBlocks merged = GhBuildCodeBlocksReplacingFirstMutable(baseline, code);
                            editor.SetSourceCode(merged);
                            okClicked = TryPerformGhScriptEditorOk(editor);
                        });
                    }
                    catch (Exception ex)
                    {
                        AddGhLog.Warn("Editor OK click failed: " + ex.Message);
                        okClicked = false;
                    }

                    // 尝试安全关闭编辑器窗口
                    try
                    {
                        if (editor.Visible && existing == null)
                        {
                            TryInvokeGhScriptEditorMethod(editor, "Hide");
                            editor.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        AddGhLog.Debug("Editor close failed: " + ex.Message);
                    }

                    if (!okClicked)
                    {
                        result = "Error: 无法通过原生编辑器提交脚本（Grasshopper 版本可能受限）。";
                        return;
                    }

                    AfterNativeScriptEditorCommit(doc);
                    result = "已通过原生脚本编辑器写入并提交。" + GetCanvasErrors(doc);
                }
                catch (Exception ex)
                {
                    result = "Error: gh_native_script_editor — " + ex.Message;
                    AddGhLog.Warn("ExecuteGhNativeScriptEditor: " + ex.Message);
                }
            }));

            return result;
        }

        private static string ExecuteSetGhComponentStatus(string id, bool? preview, bool? enabled)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }
                if (!Guid.TryParse(id, out Guid guid)) { result = "Error: ID 格式错误。"; return; }
                var obj = doc.FindObject(guid, true);
                if (obj == null) { result = "Error: 找不到电池。"; return; }

                if (preview.HasValue && obj is Grasshopper.Kernel.IGH_PreviewObject po) po.Hidden = !preview.Value;
                if (enabled.HasValue && obj is Grasshopper.Kernel.IGH_ActiveObject ao) ao.Locked = !enabled.Value;
                
                obj.ExpireSolution(true);
                _canvasChanged = true;
                try { doc.ScheduleSolution(150); } 
                catch (Exception ex) { AddGhLog.Warn("ExecuteSetGhComponentStatus Schedule failed: " + ex.Message); }
                result = "状态更新成功。";
                _canvasChanged = true;
            }));
            return result;
        }

        private static string ExecuteModifyGhComponentPorts(string id, bool isInput, string action, string portName = null, int? index = null)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }
                if (!Guid.TryParse(id, out Guid guid)) { result = "Error: ID 格式错误。"; return; }
                var obj = doc.FindObject(guid, true);
                if (!(obj is Grasshopper.Kernel.IGH_VariableParameterComponent vpc)) { result = "Error: 该电池不支持动态端口。"; return; }

                var comp = obj as Grasshopper.Kernel.IGH_Component;
                if (comp == null) { result = "Error: 无法作为组件处理。"; return; }

                if (action == "add") {
                    if (isInput) {
                        var newParam = vpc.CreateParameter(Grasshopper.Kernel.GH_ParameterSide.Input, comp.Params.Input.Count);
                        comp.Params.RegisterInputParam(newParam);
                    } else {
                        var newParam = vpc.CreateParameter(Grasshopper.Kernel.GH_ParameterSide.Output, comp.Params.Output.Count);
                        comp.Params.RegisterOutputParam(newParam);
                    }
                } else if (action == "remove") {
                    var list = isInput ? comp.Params.Input : comp.Params.Output;
                    if (list.Count > 0) {
                        int removeIndex = -1;
                        Grasshopper.Kernel.IGH_Param param = null;
                        string targetName = string.IsNullOrWhiteSpace(portName) ? null : portName.Trim();

                        if (!string.IsNullOrWhiteSpace(targetName)) {
                            for (int i = 0; i < list.Count; i++) {
                                var candidate = list[i];
                                if (candidate == null) continue;

                                bool match = (!string.IsNullOrWhiteSpace(candidate.Name) && candidate.Name.Trim().Equals(targetName, StringComparison.OrdinalIgnoreCase))
                                    || (!string.IsNullOrWhiteSpace(candidate.NickName) && candidate.NickName.Trim().Equals(targetName, StringComparison.OrdinalIgnoreCase));
                                if (match) {
                                    if (removeIndex >= 0) {
                                        result = "Error: 端口名称不唯一，请改用 index。";
                                        return;
                                    }
                                    removeIndex = i;
                                    param = candidate;
                                }
                            }

                            if (removeIndex < 0) {
                                result = "Error: 未找到名称为 '" + targetName + "' 的端口。";
                                return;
                            }
                        } else if (index.HasValue) {
                            removeIndex = index.Value;
                            if (removeIndex < 0 || removeIndex >= list.Count) {
                                result = "Error: 端口索引超出范围。";
                                return;
                            }
                            param = list[removeIndex];
                        } else {
                            removeIndex = list.Count - 1;
                            param = list[removeIndex];
                        }

                        if (vpc.CanRemoveParameter(isInput ? Grasshopper.Kernel.GH_ParameterSide.Input : Grasshopper.Kernel.GH_ParameterSide.Output, removeIndex)) {
                            comp.Params.UnregisterParameter(param);
                        } else { result = "Error: 无法删除该端口。"; return; }
                    }
                }
                
                vpc.VariableParameterMaintenance();
                comp.Params.OnParametersChanged();
                obj.ExpireSolution(true);
                _canvasChanged = true;
                try { doc.ScheduleSolution(150); } 
                catch (Exception ex) { AddGhLog.Warn("ExecuteModifyGhComponentPorts Schedule failed: " + ex.Message); }
                result = "端口修改成功。";
            }));
            return result;
        }

        private static string ExecuteManageGhGroups(string action, List<string> ids, string groupId, string name)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }

                if (action == "create") {
                    var group = new Grasshopper.Kernel.Special.GH_Group();
                    group.NickName = name ?? "Group";
                    group.Colour = System.Drawing.Color.FromArgb(80, 250, 150, 100); // 默认橘色
                    if (ids != null) {
                        foreach (var id in ids) if (Guid.TryParse(id, out Guid g)) group.AddObject(g);
                    }
                    doc.AddObject(group, false);
                    group.ExpireSolution(true);
                    result = "已创建组 '" + group.NickName + "' (ID: " + group.InstanceGuid + ")。";
                } else if (action == "ungroup") {
                    if (Guid.TryParse(groupId, out Guid gId)) {
                        var obj = doc.FindObject(gId, true);
                        if (obj is Grasshopper.Kernel.Special.GH_Group) {
                            doc.RemoveObject(obj, false);
                            result = "已解散组。";
                        } else result = "Error: 找不到该组。";
                    }
                } else if (action == "add_to_group" || action == "remove_from_group") {
                    if (Guid.TryParse(groupId, out Guid gId)) {
                        var obj = doc.FindObject(gId, true);
                        if (obj is Grasshopper.Kernel.Special.GH_Group group) {
                            if (ids != null) {
                                foreach (var id in ids) {
                                    if (Guid.TryParse(id, out Guid g)) {
                                        if (action == "add_to_group") group.AddObject(g);
                                        else group.RemoveObject(g);
                                    }
                                }
                            }
                            group.ExpireSolution(true);
                            result = "组员已更新。";
                        } else result = "Error: 找不到该组。";
                    }
                }
                _canvasChanged = true;
                try { doc.ScheduleSolution(150); } 
                catch (Exception ex) { AddGhLog.Warn("ExecuteManageGhGroups Schedule failed: " + ex.Message); }
            }));
            return result;
        }

        private static string ExecuteSearchComponentLibrary(string keyword)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                List<string> ms = new List<string>();
                foreach (var p in Grasshopper.Instances.ComponentServer.ObjectProxies) {
                    if (p.Obsolete) continue;
                    if (p.Desc.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) {
                        ms.Add("- " + p.Desc.Name + " (" + p.Desc.NickName + ")");
                        if (ms.Count > 15) break;
                    }
                }
                result = ms.Count > 0 ? string.Join("\n", ms) : "未找到匹配电池。";
            }));
            return result;
        }

        private static string ExecuteSearchGhComponentCatalog(string query, int maxResults, string categoryContains = null)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                if (string.IsNullOrWhiteSpace(query)) {
                    result = "Error: query 不能为空。";
                    return;
                }
                if (maxResults <= 0) maxResults = 30;
                if (maxResults > 200) maxResults = 200;

                string q = query.Trim();
                string catFilter = categoryContains?.Trim();
                var matches = new JArray();

                foreach (var p in Grasshopper.Instances.ComponentServer.ObjectProxies) {
                    if (p.Obsolete) continue;
                    string name = p.Desc?.Name ?? "";
                    string nick = p.Desc?.NickName ?? "";
                    string cat = p.Desc?.Category ?? "";
                    string sub = p.Desc?.SubCategory ?? "";

                    if (!string.IsNullOrEmpty(catFilter)) {
                        bool inCat = (cat.IndexOf(catFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                            || (sub.IndexOf(catFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (!inCat) continue;
                    }

                    bool hit = name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || nick.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || cat.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || sub.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

                    if (!hit) continue;

                    matches.Add(new JObject {
                        ["name"] = name,
                        ["nickname"] = nick,
                        ["guid"] = p.Guid.ToString(),
                        ["category"] = cat,
                        ["subcategory"] = sub
                    });

                    if (matches.Count >= maxResults) break;
                }

                var wrap = new JObject {
                    ["count"] = matches.Count,
                    ["items"] = matches
                };
                result = wrap.ToString(Formatting.None);
            }));
            return result;
        }

        private static string GetCanvasErrors(Grasshopper.Kernel.GH_Document doc)
        {
            List<string> errs = new List<string>();
            foreach (var obj in doc.Objects) {
                if (obj is Grasshopper.Kernel.IGH_ActiveObject ao && (ao.RuntimeMessageLevel == GH_RuntimeMessageLevel.Error || ao.RuntimeMessageLevel == GH_RuntimeMessageLevel.Warning)) {
                    foreach (string m in ao.RuntimeMessages(GH_RuntimeMessageLevel.Error)) errs.Add("Error(" + obj.Name + "): " + m);
                    foreach (string m in ao.RuntimeMessages(GH_RuntimeMessageLevel.Warning)) errs.Add("Warning(" + obj.Name + "): " + m);
                }
            }
            return errs.Count > 0 ? "检测到报错:\n" + string.Join("\n", errs) : "";
        }
        private static void RefreshUI()
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                _chatPanel.Children.Clear();
                foreach (var msg in _messages)
                {
                    var m = msg as JObject;
                    if (m == null) continue;
                    
                    string role = m["role"]?.ToString();
                    if (role == "system") continue;
                    
                    if (role == "user")
                    {
                        AppendBubble(m["content"]?.ToString(), true);
                    }
                    else if (role == "assistant")
                    {
                        string reasoning = m["reasoning_content"]?.ToString();
                        string content = m["content"]?.ToString();
                        
                        if (!string.IsNullOrEmpty(reasoning))
                            AppendCollapsibleBubble(reasoning, "已思考", "💭");
                        if (!string.IsNullOrEmpty(content))
                            AppendBubble(content, false, false); 
                    }
                }
                RefreshContextMeter();
            }));
        }

        private static void AppendBubble(string text, bool isUser, bool showHeader = true)
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var container = new StackPanel {
                    Margin = new Thickness(0, 0, 0, 20),
                    HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left
                };
                
                if (showHeader && isUser)
                {
                    var header = new TextBlock {
                        Text = "YOU",
                        Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 0, 0, 6),
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    container.Children.Add(header);
                }

                var bubble = new Border {
                    Padding = new Thickness(0, 5, 0, 10),
                    MaxWidth = 380,
                    HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left
                };

                bubble.Child = BuildMarkdownPanel(text, isUser);
                container.Children.Add(bubble);
                if (_thinkingBubble != null) {
                    _chatPanel.Children.Remove(_thinkingBubble);
                    _chatPanel.Children.Add(container);
                    _chatPanel.Children.Add(_thinkingBubble);
                } else {
                    _chatPanel.Children.Add(container);
                }

                var anim = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3));
                container.BeginAnimation(UIElement.OpacityProperty, anim);
                _chatScroll.ScrollToEnd();
            }));
        }

        private static void AppendUserMessageWithAttachments(string text, List<AttachmentItem> attachments)
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var container = new StackPanel {
                    Margin = new Thickness(0, 0, 0, 20),
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                container.Children.Add(new TextBlock
                {
                    Text = "YOU",
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 6),
                    HorizontalAlignment = HorizontalAlignment.Right
                });

                var bubbleContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 380 };
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var bubble = new Border {
                        Padding = new Thickness(0, 5, 0, 10),
                        MaxWidth = 380,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    bubble.Child = BuildMarkdownPanel(text, true);
                    bubbleContent.Children.Add(bubble);
                }

                var cards = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 380 };
                foreach (var attachment in attachments)
                {
                    cards.Children.Add(CreateAttachmentCard(attachment, false));
                }
                bubbleContent.Children.Add(cards);
                container.Children.Add(bubbleContent);

                if (_thinkingBubble != null) {
                    _chatPanel.Children.Remove(_thinkingBubble);
                    _chatPanel.Children.Add(container);
                    _chatPanel.Children.Add(_thinkingBubble);
                } else {
                    _chatPanel.Children.Add(container);
                }

                container.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));
                _chatScroll.ScrollToEnd();
            }));
        }

        private static void AppendCollapsibleBubble(string text, string title, string icon)
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var groupPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 15), HorizontalAlignment = HorizontalAlignment.Left };
                
                var headerGrid = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent, Margin = new Thickness(0,0,0,5) };
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var statusIcon = new TextBlock { Text = icon, Foreground = Brushes.Gray, FontSize = 12, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                var headerText = new TextBlock { Text = title, Foreground = Brushes.Gray, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                var toggleIcon = new TextBlock { Text = "▼", Foreground = Brushes.DimGray, FontSize = 9, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

                Grid.SetColumn(statusIcon, 0);
                Grid.SetColumn(headerText, 1);
                Grid.SetColumn(toggleIcon, 2);
                headerGrid.Children.Add(statusIcon);
                headerGrid.Children.Add(headerText);
                headerGrid.Children.Add(toggleIcon);

                var logPanel = new StackPanel { Margin = new Thickness(22, 4, 0, 0), Visibility = Visibility.Collapsed };
                
                var contentBorder = new Border {
                    Background = new SolidColorBrush(Color.FromRgb(22, 22, 22)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(38, 38, 38)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10)
                };

                var content = BuildMarkdownPanel(text, false, true);
                content.MaxHeight = 260;
                contentBorder.Child = content;
                logPanel.Children.Add(contentBorder);

                headerGrid.MouseLeftButtonDown += (s, e) => {
                    if (logPanel.Visibility == Visibility.Visible) {
                        logPanel.Visibility = Visibility.Collapsed;
                        toggleIcon.Text = "▼";
                    } else {
                        logPanel.Visibility = Visibility.Visible;
                        toggleIcon.Text = "▲";
                    }
                };

                groupPanel.Children.Add(headerGrid);
                groupPanel.Children.Add(logPanel);
                
                if (_thinkingBubble != null) { _chatPanel.Children.Remove(_thinkingBubble); _chatPanel.Children.Add(groupPanel); _chatPanel.Children.Add(_thinkingBubble); }
                else _chatPanel.Children.Add(groupPanel);

                groupPanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5)));
                _chatScroll.ScrollToEnd();
            }));
        }

        private static void AppendMarkdownInlines(InlineCollection inlines, string text)
        {
            string[] parts = Regex.Split(text ?? "", @"(\*\*.*?\*\*|`.*?`|\*.*?\*)");
            foreach (var part in parts) {
                if (string.IsNullOrEmpty(part)) continue;
                if (part.StartsWith("**") && part.EndsWith("**") && part.Length >= 4) {
                    inlines.Add(new Bold(new Run(part.Substring(2, part.Length - 4))));
                } else if (part.StartsWith("*") && part.EndsWith("*") && part.Length >= 2) {
                    inlines.Add(new Italic(new Run(part.Substring(1, part.Length - 2))));
                } else if (part.StartsWith("`") && part.EndsWith("`") && part.Length >= 2) {
                    inlines.Add(new Run(part.Substring(1, part.Length - 2)) {
                        FontFamily = new FontFamily("Consolas, Courier New"),
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 100)),
                        Background = new SolidColorBrush(Color.FromRgb(60, 60, 60))
                    });
                } else {
                    inlines.Add(new Run(part));
                }
            }
        }

        private static bool IsMarkdownHorizontalRule(string trimmed)
        {
            if (trimmed.Length < 3) return false;
            char c = trimmed[0];
            if (c != '-' && c != '*' && c != '_') return false;
            for (int k = 0; k < trimmed.Length; k++)
                if (trimmed[k] != c) return false;
            return true;
        }

        private static string[] SplitMarkdownTableRow(string line)
        {
            string t = line.Trim();
            if (!t.Contains("|")) return null;
            string inner = t;
            if (inner.StartsWith("|")) inner = inner.Substring(1);
            if (inner.EndsWith("|")) inner = inner.Substring(0, inner.Length - 1);
            string[] parts = inner.Split('|');
            if (parts.Length < 2) return null;
            return parts.Select(p => p.Trim()).ToArray();
        }

        private static bool IsMarkdownTableSeparatorRow(string[] cells)
        {
            if (cells == null || cells.Length == 0) return false;
            foreach (string cell in cells) {
                string s = cell.Replace(" ", "");
                if (!Regex.IsMatch(s, @"^:?-{3,}:?$")) return false;
            }
            return true;
        }

        private static void AppendMarkdownTable(FlowDocument doc, List<string[]> rows)
        {
            int cols = rows.Max(r => r.Length);
            for (int r = 0; r < rows.Count; r++)
                while (rows[r].Length < cols) {
                    var list = rows[r].ToList();
                    list.Add("");
                    rows[r] = list.ToArray();
                }

            var table = new Table {
                CellSpacing = 0,
                Margin = new Thickness(0, 6, 0, 10)
            };
            for (int c = 0; c < cols; c++)
                table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

            var borderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            var headerBg = new SolidColorBrush(Color.FromRgb(40, 40, 40));

            var rowGroup = new TableRowGroup();
            for (int r = 0; r < rows.Count; r++) {
                var tableRow = new TableRow();
                for (int c = 0; c < cols; c++) {
                    var paragraph = new Paragraph {
                        Margin = new Thickness(0),
                        FontSize = 14,
                        LineHeight = 22,
                        Foreground = new SolidColorBrush(Color.FromRgb(235, 235, 235)),
                        FontWeight = r == 0 ? FontWeights.SemiBold : FontWeights.Normal
                    };
                    AppendMarkdownInlines(paragraph.Inlines, rows[r][c]);
                    var cell = new TableCell(paragraph) {
                        BorderBrush = borderBrush,
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(8, 6, 8, 6),
                        Background = r == 0 ? headerBg : Brushes.Transparent
                    };
                    tableRow.Cells.Add(cell);
                }
                rowGroup.Rows.Add(tableRow);
            }

            table.RowGroups.Add(rowGroup);
            doc.Blocks.Add(table);
        }

        private static bool TryConsumeMarkdownTable(string[] lines, ref int i, FlowDocument doc)
        {
            int start = i;
            if (start >= lines.Length) return false;
            string firstTrim = lines[start].Trim();
            if (string.IsNullOrEmpty(firstTrim) || firstTrim.StartsWith("```") || !firstTrim.Contains("|")) return false;

            var rows = new List<string[]>();
            int j = start;
            while (j < lines.Length) {
                string raw = lines[j];
                string t = raw.Trim();
                if (string.IsNullOrWhiteSpace(t)) break;
                if (t.StartsWith("```")) break;
                if (!t.Contains("|")) break;
                string[] cells = SplitMarkdownTableRow(lines[j]);
                if (cells == null || cells.Length < 2) break;
                rows.Add(cells);
                j++;
            }

            if (rows.Count < 2 || !IsMarkdownTableSeparatorRow(rows[1])) return false;

            var bodyRows = new List<string[]>();
            bodyRows.Add(rows[0]);
            for (int k = 2; k < rows.Count; k++)
                bodyRows.Add(rows[k]);

            AppendMarkdownTable(doc, bodyRows);
            i = j - 1;
            return true;
        }

        private static string TrimMessageForDisplay(string text)
        {
            return (text ?? "").TrimEnd(' ', '\t', '\r', '\n', '\u00A0');
        }

        private static RichTextBox BuildMarkdownPanel(string text, bool alignRight = false, bool subdued = false)
        {
            Color bodyColor = subdued ? Color.FromRgb(205, 205, 205) : Color.FromRgb(235, 235, 235);
            Color codeBodyColor = subdued ? Color.FromRgb(220, 220, 220) : Color.FromRgb(230, 230, 230);
            Color codeHeaderColor = subdued ? Color.FromRgb(190, 190, 190) : Color.FromRgb(224, 224, 224);
            Color codeGutterColor = subdued ? Color.FromRgb(88, 88, 88) : Color.FromRgb(100, 100, 100);
            double bodyFontSize = subdued ? 12 : 14;
            double bodyLineHeight = subdued ? 19 : 22;

            var doc = new FlowDocument {
                PagePadding = new Thickness(0),
                Background = Brushes.Transparent,
                FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                FontSize = bodyFontSize,
                TextAlignment = alignRight ? TextAlignment.Right : TextAlignment.Left
            };

            var viewer = new RichTextBox {
                Document = doc,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(bodyColor),
                Padding = new Thickness(0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                IsDocumentEnabled = true
            };
            text = TrimMessageForDisplay(text);
            if (string.IsNullOrEmpty(text)) return viewer;

            var lines = text.Replace("\r\n", "\n").Split('\n');
            bool inCode = false;
            string codeLang = "";
            var code = new StringBuilder();

            Action flushCode = () => {
                var codeText = code.ToString().TrimEnd('\n');
                code.Clear();

                string langDisplay = string.IsNullOrWhiteSpace(codeLang) ? "CODE" : codeLang.ToUpperInvariant();
                var header = new TextBlock {
                    Text = langDisplay,
                    Foreground = new SolidColorBrush(codeHeaderColor),
                    FontSize = subdued ? 11 : 12,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8)
                };

                string normalized = codeText.Replace("\r\n", "\n");
                string[] parts = normalized.Length == 0 ? new[] { "" } : normalized.Split('\n');
                int lineCountForGutter = Math.Max(1, parts.Length);
                int gutterDigits = Math.Max(2, (int)Math.Floor(Math.Log10(lineCountForGutter)) + 1);

                string lineNumText = string.Join(Environment.NewLine,
                    Enumerable.Range(1, lineCountForGutter).Select(i => i.ToString().PadLeft(gutterDigits)));

                var lineNumColumn = new TextBlock {
                    Text = lineNumText,
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    FontSize = subdued ? 11 : 12,
                    Foreground = new SolidColorBrush(codeGutterColor),
                    TextAlignment = TextAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 3, 12, 0)
                };

                var codeBlock = new TextBox {
                    Text = codeText,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.NoWrap,
                    AcceptsReturn = true,
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    FontSize = subdued ? 11 : 12,
                    Foreground = new SolidColorBrush(codeBodyColor),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0, 3, 0, 0),
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 280
                };

                var codeRow = new Grid();
                codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(lineNumColumn, 0);
                Grid.SetColumn(codeBlock, 1);
                codeRow.Children.Add(lineNumColumn);
                codeRow.Children.Add(codeBlock);

                var inner = new StackPanel();
                inner.Children.Add(header);
                inner.Children.Add(codeRow);

                doc.Blocks.Add(new BlockUIContainer(new Border {
                    Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(18, 16, 20, 18),
                    Margin = new Thickness(0, 8, 0, 10),
                    Child = inner
                }));
            };

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++) {
                string line = lines[lineIndex];
                if (line.TrimStart().StartsWith("```")) {
                    if (!inCode) {
                        inCode = true;
                        codeLang = line.Trim().Trim('`').Trim();
                    } else {
                        inCode = false;
                        flushCode();
                        codeLang = "";
                    }
                    continue;
                }

                if (inCode) {
                    code.AppendLine(line);
                    continue;
                }

                string trimmed = line.Trim();
                if (IsMarkdownHorizontalRule(trimmed)) {
                    doc.Blocks.Add(new BlockUIContainer(new Border {
                        Height = 1,
                        Background = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                        Margin = new Thickness(0, 10, 0, 10)
                    }));
                    continue;
                }

                int idxForTable = lineIndex;
                if (TryConsumeMarkdownTable(lines, ref idxForTable, doc)) {
                    lineIndex = idxForTable;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(trimmed)) {
                    doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 0, 0, 6), LineHeight = 6 });
                    continue;
                }

                var paragraph = new Paragraph {
                    Foreground = new SolidColorBrush(bodyColor),
                    FontSize = bodyFontSize,
                    LineHeight = bodyLineHeight,
                    Margin = new Thickness(0, 2, 0, 2),
                    TextAlignment = alignRight ? TextAlignment.Right : TextAlignment.Left
                };

                if (trimmed.StartsWith("### ")) {
                    paragraph.FontSize = 15;
                    paragraph.FontWeight = FontWeights.SemiBold;
                    paragraph.Foreground = new SolidColorBrush(subdued ? Color.FromRgb(205, 205, 205) : Color.FromRgb(255, 220, 150));
                    paragraph.TextAlignment = alignRight ? TextAlignment.Right : TextAlignment.Left;
                    AppendMarkdownInlines(paragraph.Inlines, trimmed.Substring(4));
                } else if (trimmed.StartsWith("## ")) {
                    paragraph.FontSize = subdued ? 13 : 16;
                    paragraph.FontWeight = FontWeights.SemiBold;
                    paragraph.Foreground = new SolidColorBrush(subdued ? Color.FromRgb(205, 205, 205) : Color.FromRgb(255, 220, 150));
                    paragraph.Margin = new Thickness(0, 8, 0, 4);
                    paragraph.TextAlignment = alignRight ? TextAlignment.Right : TextAlignment.Left;
                    AppendMarkdownInlines(paragraph.Inlines, trimmed.Substring(3));
                } else if (trimmed.StartsWith("# ")) {
                    paragraph.FontSize = subdued ? 14 : 17;
                    paragraph.FontWeight = FontWeights.Bold;
                    paragraph.Foreground = new SolidColorBrush(subdued ? Color.FromRgb(205, 205, 205) : Color.FromRgb(255, 220, 150));
                    paragraph.Margin = new Thickness(0, 8, 0, 4);
                    paragraph.TextAlignment = alignRight ? TextAlignment.Right : TextAlignment.Left;
                    AppendMarkdownInlines(paragraph.Inlines, trimmed.Substring(2));
                } else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ")) {
                    paragraph.Inlines.Add(new Run("• ") { Foreground = new SolidColorBrush(subdued ? Color.FromRgb(170, 170, 170) : Color.FromRgb(255, 200, 100)) });
                    AppendMarkdownInlines(paragraph.Inlines, trimmed.Substring(2));
                } else if (trimmed.StartsWith("> ")) {
                    paragraph.Foreground = new SolidColorBrush(subdued ? Color.FromRgb(175, 175, 175) : Color.FromRgb(190, 190, 190));
                    paragraph.Margin = new Thickness(10, 4, 0, 4);
                    paragraph.Inlines.Add(new Run("│ ") { Foreground = new SolidColorBrush(subdued ? Color.FromRgb(75, 75, 75) : Color.FromRgb(70, 70, 70)) });
                    AppendMarkdownInlines(paragraph.Inlines, trimmed.Substring(2));
                } else {
                    AppendMarkdownInlines(paragraph.Inlines, line);
                }

                doc.Blocks.Add(paragraph);
            }

            if (inCode) flushCode();
            return viewer;
        }

        private static void SaveReference(string description)
        {
            string canvasJson = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                canvasJson = ExecuteGetGhComponents();
            }));

            System.Threading.Tasks.Task.Run(() => {
                try {
                    if (string.IsNullOrWhiteSpace(canvasJson) || canvasJson.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)) {
                        string hint = "无法读取有效画布 JSON（无文档、画布为空或返回错误）。请打开 Grasshopper 文档并确认有电池后再试。";
                        if (!string.IsNullOrWhiteSpace(canvasJson))
                            hint += "\n" + ClampDiagDetail(canvasJson, 320);
                        AppendQuietDiagnosticCard("保存参考", hint);
                        return;
                    }

                    string refPath = GetReferenceDirectory();
                    string indexPath = GetReferenceIndexPath();
                    if (!System.IO.Directory.Exists(refPath)) System.IO.Directory.CreateDirectory(refPath);
                    string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                    string fileName = "ref_" + timestamp + ".json";
                    string filePath = System.IO.Path.Combine(refPath, fileName);
                    System.IO.File.WriteAllText(filePath, canvasJson, System.Text.Encoding.UTF8);

                    string result = UpdateReferenceIndexSkill(description, fileName);

                    AppendSystemMessage($"参考已保存：{fileName}\n{result}\nJSON：{filePath}\n索引：{indexPath}");
                } catch (Exception ex) {
                    AddGhLog.Error("SaveReference failed", ex);
                    AppendQuietDiagnosticCard("保存参考", "出现异常：" + ex.Message);
                }
            });
        }

        /// <summary> 仓库根（含 skills/、reference/）。插件在 Rhino 中加载时 BaseDirectory 常在 bin 下，需向上查找；也可设环境变量 ADDGH_PROJECT_ROOT。 </summary>
        private static string GetProjectRootDirectory()
        {
            string env = Environment.GetEnvironmentVariable("ADDGH_PROJECT_ROOT");
            if (!string.IsNullOrWhiteSpace(env))
            {
                string full = System.IO.Path.GetFullPath(env.Trim());
                if (System.IO.Directory.Exists(full)) return full;
            }

            bool HasRepoSkills(string d) =>
                !string.IsNullOrEmpty(d)
                && System.IO.Directory.Exists(System.IO.Path.Combine(d, "skills"))
                && System.IO.File.Exists(System.IO.Path.Combine(d, "skills", "reference_index.md"));

            bool HasAddGhSubfolder(string d) =>
                !string.IsNullOrEmpty(d)
                && System.IO.File.Exists(System.IO.Path.Combine(d, "ADDGH", "ADDGH.csproj"));

            bool HasAddGhProject(string d) =>
                !string.IsNullOrEmpty(d)
                && System.IO.File.Exists(System.IO.Path.Combine(d, "ADDGH.csproj"));

            string TryWalk(string start, int maxSteps)
            {
                string dir = start;
                for (int i = 0; i < maxSteps && !string.IsNullOrEmpty(dir); i++)
                {
                    if (HasAddGhSubfolder(dir)) return dir;
                    if (HasRepoSkills(dir)) return dir;
                    if (HasAddGhProject(dir))
                        return System.IO.Directory.GetParent(dir)?.FullName ?? dir;
                    dir = System.IO.Directory.GetParent(dir)?.FullName;
                }
                return null;
            }

            string found = TryWalk(AppDomain.CurrentDomain.BaseDirectory, 22);
            if (!string.IsNullOrEmpty(found)) return found;

            found = TryWalk(Environment.CurrentDirectory, 18);
            if (!string.IsNullOrEmpty(found)) return found;

            try
            {
                string asm = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(asm))
                {
                    found = TryWalk(System.IO.Path.GetDirectoryName(asm), 22);
                    if (!string.IsNullOrEmpty(found)) return found;
                }
            }
            catch (Exception ex)
            {
                AddGhLog.Warn("GetProjectRootDirectory assembly path walk failed: " + ex.Message);
            }

            return Environment.CurrentDirectory;
        }

        private static string GetSkillsDirectory()
        {
            return System.IO.Path.Combine(GetProjectRootDirectory(), "skills");
        }

        private static string GetReferenceDirectory()
        {
            return System.IO.Path.Combine(GetProjectRootDirectory(), "reference");
        }

        private class ReferenceEntry
        {
            public string Description { get; set; }
            public string FileName { get; set; }
            public bool JsonExists { get; set; }
        }

        private static string GetReferenceIndexTemplate()
        {
            return "---\n" +
                "name: reference-index\n" +
                "description: 在完成初步 GH 建模逻辑规划之后查阅；仅当条目与已定方案相关时，再调用 read_reference_json 读取 JSON 对照实现。\n" +
                "---\n\n" +
                "# Reference Index\n\n" +
                "使用流程：\n" +
                "1. 先规划：用简短步骤说明本任务的 GH 逻辑（数据流、关键电池、风险点等）。\n" +
                "2. 再浏览：查阅下列参考条目，看是否与**已定方案**高度相关。\n" +
                "3. 后读取：若相关，调用 `read_reference_json` 并传入对应 `file_name`，用 JSON 对齐细节、补充或改造实现。\n\n" +
                "## References\n";
        }

        private static string GetReferenceIndexPath()
        {
            return System.IO.Path.Combine(GetSkillsDirectory(), "reference_index.md");
        }

        private static void EnsureReferenceIndexSkill()
        {
            string skillsPath = GetSkillsDirectory();
            if (!System.IO.Directory.Exists(skillsPath)) System.IO.Directory.CreateDirectory(skillsPath);

            string indexPath = GetReferenceIndexPath();
            if (!System.IO.File.Exists(indexPath))
            {
                System.IO.File.WriteAllText(indexPath, GetReferenceIndexTemplate(), Encoding.UTF8);
            }
        }

        private static string FormatReferenceEntry(string description, string jsonFileName)
        {
            string safeDescription = (description ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (string.IsNullOrWhiteSpace(safeDescription)) safeDescription = "未命名参考画布";
            string safeFileName = System.IO.Path.GetFileName(jsonFileName ?? "");

            return $"- 描述：{safeDescription}\n" +
                $"  文件：reference/{safeFileName}\n" +
                $"  调用：read_reference_json(file_name=\"{safeFileName}\")\n";
        }

        private static List<ReferenceEntry> ReadReferenceIndexEntries()
        {
            EnsureReferenceIndexSkill();

            string content = System.IO.File.ReadAllText(GetReferenceIndexPath(), Encoding.UTF8);
            var entries = new List<ReferenceEntry>();
            var matches = System.Text.RegularExpressions.Regex.Matches(
                content,
                @"-\s*描述：(?<desc>.*?)\r?\n\s*文件：reference/(?<file>[^\r\n]+)\r?\n\s*调用：read_reference_json\(file_name=""(?<call>[^""]+)""\)",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            string referencePath = GetReferenceDirectory();
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                string fileName = System.IO.Path.GetFileName(match.Groups["file"].Value.Trim());
                if (string.IsNullOrWhiteSpace(fileName)) continue;

                entries.Add(new ReferenceEntry
                {
                    Description = match.Groups["desc"].Value.Trim(),
                    FileName = fileName,
                    JsonExists = System.IO.File.Exists(System.IO.Path.Combine(referencePath, fileName))
                });
            }

            return entries;
        }

        private static void WriteReferenceIndexEntries(IEnumerable<ReferenceEntry> entries)
        {
            EnsureReferenceIndexSkill();
            var sb = new StringBuilder(GetReferenceIndexTemplate());

            foreach (var entry in entries)
            {
                sb.Append(FormatReferenceEntry(entry.Description, entry.FileName));
            }

            System.IO.File.WriteAllText(GetReferenceIndexPath(), sb.ToString(), Encoding.UTF8);
        }

        private static string UpdateReferenceIndexSkill(string description, string jsonFileName)
        {
            EnsureReferenceIndexSkill();
            string indexPath = GetReferenceIndexPath();
            string safeFileName = System.IO.Path.GetFileName(jsonFileName ?? "");
            if (string.IsNullOrWhiteSpace(safeFileName))
                return "Error: 参考文件名为空，未写入索引。";

            string content = System.IO.File.Exists(indexPath)
                ? System.IO.File.ReadAllText(indexPath, Encoding.UTF8)
                : GetReferenceIndexTemplate();

            if (content.IndexOf("reference/" + safeFileName, StringComparison.OrdinalIgnoreCase) >= 0
                || content.IndexOf("file_name=\"" + safeFileName + "\"", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Rhino.RhinoApp.InvokeOnUiThread((Action)(() => { UpdateSkillLibraryUI(); }));
                return "索引中已包含该文件，未重复追加。";
            }

            if (content.IndexOf("## References", StringComparison.Ordinal) < 0)
            {
                if (!content.EndsWith("\n")) content += "\n";
                content += "\n## References\n";
            }
            if (!content.EndsWith("\n")) content += "\n";
            content += FormatReferenceEntry(description, safeFileName);
            System.IO.File.WriteAllText(indexPath, content, Encoding.UTF8);

            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => { UpdateSkillLibraryUI(); }));

            return "已更新统一参考索引 skills/reference_index.md。";
        }

        private static void ShowReferenceLibraryUI()
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                EnsureReferenceIndexSkill();

                if (_referenceLibraryWindow != null)
                {
                    _referenceLibraryWindow.Close();
                    _referenceLibraryWindow = null;
                }

                var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(16, 16, 16)), Margin = new Thickness(0) };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var header = new Grid { Margin = new Thickness(18, 16, 18, 10) };
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var titlePanel = new StackPanel { Orientation = Orientation.Vertical };
                titlePanel.Children.Add(new TextBlock
                {
                    Text = "我的参考",
                    Foreground = Brushes.White,
                    FontSize = 18,
                    FontWeight = FontWeights.SemiBold
                });
                titlePanel.Children.Add(new TextBlock
                {
                    Text = "从 reference_index.md 管理已保存的画布参考",
                    Foreground = new SolidColorBrush(Color.FromRgb(145, 145, 145)),
                    FontSize = 11,
                    Margin = new Thickness(0, 4, 0, 0)
                });
                Grid.SetColumn(titlePanel, 0);
                header.Children.Add(titlePanel);

                var refreshButton = CreateReferenceLibraryButton("刷新", false);
                refreshButton.Click += (s, e) => ShowReferenceLibraryUI();
                Grid.SetColumn(refreshButton, 1);
                header.Children.Add(refreshButton);

                var closeButton = CreateReferenceLibraryButton("关闭", false);
                closeButton.Margin = new Thickness(8, 0, 0, 0);
                closeButton.Click += (s, e) => _referenceLibraryWindow?.Close();
                Grid.SetColumn(closeButton, 2);
                header.Children.Add(closeButton);

                Grid.SetRow(header, 0);
                root.Children.Add(header);

                var entries = ReadReferenceIndexEntries();
                var content = new StackPanel { Margin = new Thickness(18, 0, 18, 18) };

                if (entries.Count == 0)
                {
                    content.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(44, 44, 44)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(18),
                        Child = new TextBlock
                        {
                            Text = "还没有保存的参考。点击“创建参考”后，这里会显示对应 JSON 和描述。",
                            Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                            FontSize = 13,
                            TextWrapping = TextWrapping.Wrap
                        }
                    });
                }
                else
                {
                    foreach (var entry in entries)
                    {
                        content.Children.Add(CreateReferenceCard(entry));
                    }
                }

                var scroll = new ScrollViewer
                {
                    Content = content,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Padding = new Thickness(0)
                };
                Grid.SetRow(scroll, 1);
                root.Children.Add(scroll);

                _referenceLibraryWindow = new Window
                {
                    Title = "我的参考",
                    Width = 560,
                    Height = 520,
                    MinWidth = 460,
                    MinHeight = 360,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = new SolidColorBrush(Color.FromRgb(16, 16, 16)),
                    Content = root,
                    Owner = _window
                };
                _referenceLibraryWindow.Closed += (s, e) => _referenceLibraryWindow = null;
                _referenceLibraryWindow.Show();
            }));
        }

        private static Button CreateReferenceLibraryButton(string text, bool danger)
        {
            var button = new Button
            {
                Content = text,
                Background = new SolidColorBrush(danger ? Color.FromRgb(60, 28, 28) : Color.FromRgb(34, 34, 34)),
                Foreground = new SolidColorBrush(danger ? Color.FromRgb(255, 170, 170) : Color.FromRgb(230, 230, 230)),
                BorderBrush = new SolidColorBrush(danger ? Color.FromRgb(95, 42, 42) : Color.FromRgb(56, 56, 56)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 6, 12, 6),
                Cursor = Cursors.Hand,
                FontSize = 12
            };

            button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
                <ControlTemplate TargetType=""Button"" xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                    <Border Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}"" CornerRadius=""8"">
                        <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Margin=""{TemplateBinding Padding}""/>
                    </Border>
                </ControlTemplate>");

            return button;
        }

        private static Border CreateReferenceCard(ReferenceEntry entry)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(46, 46, 46)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel { Orientation = Orientation.Vertical };
            info.Children.Add(new TextBlock
            {
                Text = entry.Description,
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            info.Children.Add(new TextBlock
            {
                Text = $"reference/{entry.FileName}",
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (!entry.JsonExists)
            {
                info.Children.Add(new TextBlock
                {
                    Text = "JSON 文件缺失，删除会清理索引条目",
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 90)),
                    FontSize = 11,
                    Margin = new Thickness(0, 5, 0, 0)
                });
            }

            Grid.SetColumn(info, 0);
            grid.Children.Add(info);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var useButton = CreateReferenceLibraryButton("使用", false);
            useButton.Click += (s, e) => {
                if (_txtInput != null)
                {
                    _txtInput.Text = $"请先说明本任务的 GH 建模规划（步骤与关键电池）。方案确定后查阅 skills/reference_index.md；若与条目「{entry.FileName}」相关，再调用 read_reference_json 读取该 JSON 并对照实现。";
                    _txtInput.Focus();
                }
                _referenceLibraryWindow?.Close();
            };
            actions.Children.Add(useButton);

            var deleteButton = CreateReferenceLibraryButton("删除", true);
            deleteButton.Margin = new Thickness(8, 0, 0, 0);
            deleteButton.Click += (s, e) => DeleteReferenceEntryWithConfirmation(entry);
            actions.Children.Add(deleteButton);

            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);

            card.Child = grid;
            return card;
        }

        private static void DeleteReferenceEntryWithConfirmation(ReferenceEntry entry)
        {
            var result = System.Windows.MessageBox.Show(
                $"确定删除参考“{entry.Description}”？\n\n将同时删除 reference/{entry.FileName} 并清理 reference_index.md 中的对应条目。",
                "删除参考",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                DeleteReferenceEntry(entry.FileName);
                ShowReferenceLibraryUI();
                AppendSystemMessage($"已删除参考：{entry.FileName}");
            }
            catch (Exception ex)
            {
                AddGhLog.Error("DeleteReferenceEntryWithConfirmation failed", ex);
                AppendQuietDiagnosticCard("删除参考", "出现异常：" + ex.Message);
            }
        }

        private static void DeleteReferenceEntry(string fileName)
        {
            string safeFileName = System.IO.Path.GetFileName(fileName ?? "");
            if (string.IsNullOrWhiteSpace(safeFileName)) throw new InvalidOperationException("参考文件名为空。");

            string referencePath = GetReferenceDirectory();
            string jsonPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(referencePath, safeFileName));
            string referenceFullPath = System.IO.Path.GetFullPath(referencePath);

            if (!jsonPath.StartsWith(referenceFullPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("非法 reference 文件路径。");

            if (System.IO.File.Exists(jsonPath)) System.IO.File.Delete(jsonPath);

            var remaining = ReadReferenceIndexEntries()
                .Where(entry => !entry.FileName.Equals(safeFileName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            WriteReferenceIndexEntries(remaining);

            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                UpdateSkillLibraryUI();
            }));
        }

        private static async void SendHiddenPromptAsync(string displayText, string actualPrompt)
        {
            if (_isGenerating) { _cts?.Cancel(); return; }

            _isGenerating = true;
            ApplySendButtonGeneratingState();
            _txtInput.Text = "";

            if (_messages.Count == 0) {
                _messages.AddRange(BuildInitialSystemMessages());
            }

            _messages.Add(new { role = "user", content = actualPrompt });
            AppendBubble(displayText, true);

            SyncActiveHistoryConversation(string.IsNullOrWhiteSpace(displayText) ? actualPrompt : displayText);

            EnforceChatHistoryLimit();

            _pendingAttachments.Clear();
            RefreshAttachmentPreview();
            if (_btnClearImage != null) _btnClearImage.Visibility = Visibility.Collapsed;

            try { _cts?.Dispose(); } catch (Exception ex) { AddGhLog.Warn("Dispose prior CTS: " + ex.Message); }
            _cts = new System.Threading.CancellationTokenSource();
            string apiKey = GetProviderRuntimeSettings().ApiKey;

            try {
                ShowThinkingAnimation();
                await CallLLMAPI(apiKey, 0, _cts.Token);
            } catch (OperationCanceledException) {
                AppendSystemMessage("已停止生成。");
            } catch (Exception ex) {
                AddGhLog.Error("SendHiddenPrompt CallLLMAPI failed", ex);
                AppendQuietDiagnosticCard("后台任务",
                    BuildProviderDiagnostic(GetProviderRuntimeSettings(), "出现异常：" + ex.GetType().Name, ex.Message));
            } finally {
                HideThinkingAnimation();
                _isGenerating = false;
                ApplySendButtonIdleState();
                try { _cts?.Dispose(); } catch (Exception ex) { AddGhLog.Warn("Dispose CTS after hidden prompt: " + ex.Message); }
                _cts = null;
            }
        }

        private static void AppendColoredStatsMessage(int addComp, int delComp, int addConn, int delConn)
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var card = new Border {
                    Background = new SolidColorBrush(Color.FromRgb(35, 35, 35)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 15),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                    BorderThickness = new Thickness(1)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                titleStack.Children.Add(new TextBlock {
                    Text = "操作统计",
                    Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                });
                Grid.SetColumn(titleStack, 0);
                grid.Children.Add(titleStack);

                var stack = new StackPanel {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };

                if (addComp > 0) stack.Children.Add(CreateStatBadge("电池", $"+{addComp}", Color.FromRgb(46, 204, 113)));
                if (delComp > 0) stack.Children.Add(CreateStatBadge("电池", $"-{delComp}", Color.FromRgb(231, 76, 60)));
                if (addConn > 0) stack.Children.Add(CreateStatBadge("连线", $"+{addConn}", Color.FromRgb(46, 204, 113)));
                if (delConn > 0) stack.Children.Add(CreateStatBadge("连线", $"-{delConn}", Color.FromRgb(231, 76, 60)));

                Grid.SetColumn(stack, 1);
                grid.Children.Add(stack);
                card.Child = grid;

                if (_thinkingBubble != null) { _chatPanel.Children.Remove(_thinkingBubble); _chatPanel.Children.Add(card); _chatPanel.Children.Add(_thinkingBubble); }
                else _chatPanel.Children.Add(card);
                _chatScroll.ScrollToEnd();
            }));
        }

        private static Border CreateStatBadge(string label, string value, Color color)
        {
            var badge = new Border {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(6, 0, 0, 0)
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)), FontSize = 11, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = value, Foreground = new SolidColorBrush(color), FontSize = 12, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            badge.Child = sp;
            return badge;
        }

        private static string ClampDiagDetail(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Trim();
            if (text.Length <= maxChars) return text;
            return text.Substring(0, maxChars) + "…";
        }

        private static TextBox CreateSelectableTextBox(string text, Brush foreground, double fontSize, Thickness margin, TextAlignment alignment = TextAlignment.Left)
        {
            return new TextBox
            {
                Text = text ?? "",
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = foreground,
                FontSize = fontSize,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(0),
                Margin = margin,
                TextAlignment = alignment,
                Cursor = Cursors.IBeam
            };
        }

        /// <summary> 对话区低调诊断卡片（灰阶小字，左侧对齐）；完整栈仍写入 AddGhLog。 </summary>
        private static void AppendQuietDiagnosticCard(string categoryLabel, string detail)
        {
            string cat = string.IsNullOrWhiteSpace(categoryLabel) ? "诊断" : categoryLabel.Trim();
            string body = ClampDiagDetail(detail ?? "", 1400);

            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                if (_chatPanel == null) return;

                var stack = new StackPanel
                {
                    Margin = new Thickness(0, 0, 0, 12),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    MaxWidth = 380
                };

                stack.Children.Add(new TextBlock
                {
                    Text = cat,
                    Foreground = new SolidColorBrush(Color.FromRgb(82, 82, 82)),
                    FontSize = 10,
                    FontWeight = FontWeights.Normal,
                    Margin = new Thickness(2, 0, 0, 4)
                });

                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 8, 10, 8)
                };

                card.Child = CreateSelectableTextBox(
                    string.IsNullOrEmpty(body) ? "（无详情）" : body,
                    new SolidColorBrush(Color.FromRgb(130, 130, 130)),
                    11,
                    new Thickness(0));

                stack.Children.Add(card);

                if (_thinkingBubble != null)
                {
                    _chatPanel.Children.Remove(_thinkingBubble);
                    _chatPanel.Children.Add(stack);
                    _chatPanel.Children.Add(_thinkingBubble);
                }
                else
                {
                    _chatPanel.Children.Add(stack);
                }

                stack.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.18)));
                if (_chatScroll != null) _chatScroll.ScrollToEnd();
            }));
        }

        private static void AppendSystemMessage(string text, bool isError = false)
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var tb = CreateSelectableTextBox(
                    text,
                    isError ? Brushes.Tomato : Brushes.Gray,
                    12,
                    new Thickness(0, 0, 0, 15),
                    TextAlignment.Center);
                tb.HorizontalAlignment = HorizontalAlignment.Center;
                tb.MaxWidth = 380;
                if (_thinkingBubble != null) {
                    _chatPanel.Children.Remove(_thinkingBubble);
                    _chatPanel.Children.Add(tb);
                    _chatPanel.Children.Add(_thinkingBubble);
                } else _chatPanel.Children.Add(tb);
                _chatScroll.ScrollToEnd();
            }));
        }
        private static Window _ballWindow;
        private static void MinimizeToBall()
        {
            if (_window == null) return;
            _window.Hide();

            if (_ballWindow != null) { _ballWindow.Show(); return; }

            _ballWindow = new Window {
                Width = 50, Height = 50,
                MinWidth = 50, MaxWidth = 50,
                MinHeight = 50, MaxHeight = 50,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                Cursor = Cursors.Hand,
                Left = _window.Left + _window.Width - 60,
                Top = _window.Top + 20
            };

            var border = new Border {
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                CornerRadius = new CornerRadius(25),
                BorderThickness = new Thickness(0),
                Child = new TextBlock { 
                Text = "✨",
                FontSize = 24,
                    Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
                }
            };
            
            border.MouseLeftButtonDown += (s, e) => {
                if (e.LeftButton == MouseButtonState.Pressed) {
                    if (e.ClickCount >= 2) {
                        _ballWindow.Hide();
                        _window.Show();
                        _window.Activate();
                    } else {
                        _ballWindow.DragMove();
                    }
                }
            };

            _ballWindow.Content = border;
            _ballWindow.Show();
        }

        private static string ExecuteReadSkillFile(string fileName)
        {
            try {
                string skillsPath = GetSkillsDirectory();
                if (!fileName.EndsWith(".md")) fileName += ".md";
                string filePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(skillsPath, System.IO.Path.GetFileName(fileName)));
                
                if (System.IO.File.Exists(filePath)) {
                    return System.IO.File.ReadAllText(filePath);
                }
                return $"Error: 找不到技能文件 {fileName}";
            } catch (Exception ex) {
                return "Error: " + ex.Message;
            }
        }

        private static string ExecuteReadReferenceJson(string fileName)
        {
            try {
                if (string.IsNullOrWhiteSpace(fileName)) return "Error: file_name 不能为空。";
                if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) fileName += ".json";

                string referencePath = GetReferenceDirectory();
                string safeName = System.IO.Path.GetFileName(fileName);
                string filePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(referencePath, safeName));
                string referenceFullPath = System.IO.Path.GetFullPath(referencePath);

                if (!filePath.StartsWith(referenceFullPath, StringComparison.OrdinalIgnoreCase))
                    return "Error: 非法 reference 文件路径。";

                if (!System.IO.File.Exists(filePath))
                    return $"Error: 找不到参考 JSON 文件 {safeName}";

                return System.IO.File.ReadAllText(filePath, Encoding.UTF8);
            } catch (Exception ex) {
                return "Error: " + ex.Message;
            }
        }

        private static string ExecuteCreateGhSkill(string fileName, string name, string description, string content)
        {
            try {
                string skillsPath = GetSkillsDirectory();
                if (!System.IO.Directory.Exists(skillsPath)) System.IO.Directory.CreateDirectory(skillsPath);
                if (!fileName.EndsWith(".md")) fileName += ".md";
                string filePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(skillsPath, System.IO.Path.GetFileName(fileName)));
                
                string fileContent = $"---\nname: {name}\ndescription: {description}\n---\n\n{content}";
                System.IO.File.WriteAllText(filePath, fileContent, Encoding.UTF8);
                
                Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                    UpdateSkillLibraryUI();
                }));
                
                return $"技能 '{name}' 已成功保存至 {fileName}。";
            } catch (Exception ex) {
                return "Error: " + ex.Message;
            }
        }

        private static void UpdateSkillLibraryUI()
        {
            if (_skillContent == null) return;
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                _skillContent.Children.Clear();
                string skillsPath = GetSkillsDirectory();
                if (!System.IO.Directory.Exists(skillsPath)) return;

                var files = System.IO.Directory.GetFiles(skillsPath, "*.md");
                if (_txtSkillCount != null) _txtSkillCount.Text = $"({files.Length} 个)";

                var wrap = new WrapPanel { Margin = new Thickness(4, 4, 4, 8) };
                foreach (var file in files) {
                    string fileName = System.IO.Path.GetFileName(file);
                    if (fileName.Equals("index.md", StringComparison.OrdinalIgnoreCase)) continue;

                    string content = System.IO.File.ReadAllText(file);
                    var match = System.Text.RegularExpressions.Regex.Match(content, @"---\s*name:\s*(.*?)\s*description:\s*(.*?)\s*---", System.Text.RegularExpressions.RegexOptions.Singleline);
                    
                    string name = fileName;
                    string desc = "";
                    if (match.Success) {
                        name = match.Groups[1].Value.Trim();
                        desc = match.Groups[2].Value.Trim();
                    }

                    var card = new Border {
                        Background = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
                        CornerRadius = new CornerRadius(6),
                        Width = 160,
                        Height = 70,
                        Margin = new Thickness(3),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                        BorderThickness = new Thickness(1),
                        Cursor = Cursors.Hand,
                        ToolTip = desc
                    };
                    
                    var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7) };
                    sp.Children.Add(new TextBlock { Text = name, Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)), FontSize = 12, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
                    sp.Children.Add(new TextBlock { Text = desc, Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)), FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 30, TextWrapping = TextWrapping.Wrap });
                    card.Child = sp;
                    
                    card.MouseLeftButtonDown += (s, e) => {
                        if (_txtInput != null) {
                            _txtInput.Text = $"请参考技能：{name} ({fileName})";
                        }
                    };
                    
                    wrap.Children.Add(card);
                }
                _skillContent.Children.Add(wrap);
            }));
        }

        private static string GetSkillsSummary()
        {
            try {
                string skillsPath = GetSkillsDirectory();
                if (!System.IO.Directory.Exists(skillsPath)) return "";

                var summaries = new List<string>();
                foreach (var file in System.IO.Directory.GetFiles(skillsPath, "*.md")) {
                    string fileName = System.IO.Path.GetFileName(file);
                    if (fileName.Equals("index.md", StringComparison.OrdinalIgnoreCase)) continue;

                    string content = System.IO.File.ReadAllText(file);
                    // 匹配 YAML Frontmatter: --- name: xxx description: xxx ---
                    var match = System.Text.RegularExpressions.Regex.Match(content, @"---\s*name:\s*(.*?)\s*description:\s*(.*?)\s*---", System.Text.RegularExpressions.RegexOptions.Singleline);
                    
                    if (match.Success) {
                        string name = match.Groups[1].Value.Trim();
                        string desc = match.Groups[2].Value.Trim();
                        summaries.Add($"- [{name}]: {desc} (文件: {fileName})");
                    }
                }

                if (summaries.Count > 0) {
                    return "\n\n【当前项目可用技能库】:\n" + string.Join("\n", summaries) + "\n(你可以随时调用工具阅读上述文件以获取详细操作技能。)";
                }
            } catch (Exception ex) {
                AddGhLog.Warn("GetSkillsSummary failed: " + ex.Message);
            }
            return "";
        }
    }
}
