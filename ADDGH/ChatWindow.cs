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
using System.Windows.Media.Effects;
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
using System.Windows.Interop;
using Grasshopper.Kernel;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI.Script;

namespace ADDGH
{
    public static partial class ChatWindow
    {
        private static Window _window;
        private const double DefaultWindowWidth = 450;
        private const double WindowChromeMargin = 20;
        private const double ResizeHitTestThickness = 8;
        private const double PaneMinWidth = DefaultWindowWidth - (WindowChromeMargin * 2);
        private const double CodeViewColumnWidth = 750;
        private static double _widthBeforeCodeView = double.NaN;
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
        private static Border _rootChromeBorder;
        private static TextBlock _codeHeaderTitle;
        private static Button _btnToggleViewMode;
        private static ColumnDefinition _historyCol;
        private static ColumnDefinition _chatCol;
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

        private static void ActivateMainWindow()
        {
            if (_window == null) return;

            if (_ballWindow != null && _ballWindow.IsVisible)
                _ballWindow.Hide();

            if (_window.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;

            _window.Show();
            _window.Activate();
        }

        private static void AttachWindowResizeHitTest()
        {
            if (_window == null) return;
            var source = PresentationSource.FromVisual(_window) as HwndSource;
            if (source != null) source.AddHook(WindowResizeHook);
        }

        private static IntPtr WindowResizeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTLEFT = 10;
            const int HTRIGHT = 11;
            const int HTTOP = 12;
            const int HTTOPLEFT = 13;
            const int HTTOPRIGHT = 14;
            const int HTBOTTOM = 15;
            const int HTBOTTOMLEFT = 16;
            const int HTBOTTOMRIGHT = 17;

            if (msg != WM_NCHITTEST || _window == null || _window.ResizeMode == ResizeMode.NoResize || _window.WindowState != WindowState.Normal)
                return IntPtr.Zero;

            long lp = lParam.ToInt64();
            var screenPoint = new Point(unchecked((short)(lp & 0xFFFF)), unchecked((short)((lp >> 16) & 0xFFFF)));
            Point p = _window.PointFromScreen(screenPoint);
            double width = _window.ActualWidth;
            double height = _window.ActualHeight;

            bool left = IsNearResizeEdge(p.X, 0, width) || IsNearResizeEdge(p.X, WindowChromeMargin, width);
            bool right = IsNearResizeEdge(p.X, width, width) || IsNearResizeEdge(p.X, width - WindowChromeMargin, width);
            bool top = IsNearResizeEdge(p.Y, 0, height) || IsNearResizeEdge(p.Y, WindowChromeMargin, height);
            bool bottom = IsNearResizeEdge(p.Y, height, height) || IsNearResizeEdge(p.Y, height - WindowChromeMargin, height);

            int hit = 0;
            if (left && top) hit = HTTOPLEFT;
            else if (right && top) hit = HTTOPRIGHT;
            else if (left && bottom) hit = HTBOTTOMLEFT;
            else if (right && bottom) hit = HTBOTTOMRIGHT;
            else if (left) hit = HTLEFT;
            else if (right) hit = HTRIGHT;
            else if (top) hit = HTTOP;
            else if (bottom) hit = HTBOTTOM;

            if (hit == 0) return IntPtr.Zero;
            handled = true;
            return new IntPtr(hit);
        }

        private static bool IsNearResizeEdge(double value, double edge, double extent)
        {
            return value >= 0
                && value <= extent
                && Math.Abs(value - edge) <= ResizeHitTestThickness;
        }

        private static void UpdateWindowChromeForState()
        {
            if (_rootChromeBorder == null || _window == null) return;

            if (_window.WindowState == WindowState.Maximized)
            {
                _rootChromeBorder.Margin = new Thickness(0);
                _rootChromeBorder.CornerRadius = new CornerRadius(0);
                _rootChromeBorder.Effect = null;
                return;
            }

            _rootChromeBorder.Margin = new Thickness(WindowChromeMargin);
            _rootChromeBorder.CornerRadius = new CornerRadius(16);
            _rootChromeBorder.Effect = new DropShadowEffect
            {
                BlurRadius = 30,
                ShadowDepth = 10,
                Opacity = 0.6,
                Color = Colors.Black
            };
        }

        private static void BeginWindowHeaderDrag(MouseButtonEventArgs e)
        {
            if (_window == null || e.LeftButton != MouseButtonState.Pressed || e.ClickCount != 1) return;

            if (_window.WindowState == WindowState.Maximized)
            {
                Point screenPoint = _window.PointToScreen(e.GetPosition(_window));
                double restoreWidth = Math.Max(_window.RestoreBounds.Width, _window.MinWidth);
                _window.WindowState = WindowState.Normal;
                _window.Left = screenPoint.X - Math.Min(restoreWidth * 0.5, Math.Max(80, e.GetPosition(_window).X));
                _window.Top = Math.Max(0, screenPoint.Y - 20);
                UpdateWindowChromeForState();
            }

            try { _window.DragMove(); }
            catch (InvalidOperationException) { }
        }

        private static void UpdateWindowMinWidthForVisiblePanes()
        {
            if (_window == null) return;

            double minWidth = DefaultWindowWidth;
            if (_isCodeVisible)
                minWidth = Math.Max(minWidth, (PaneMinWidth * 2) + (WindowChromeMargin * 2));
            if (_isHistorySidebarVisible)
                minWidth += 320;

            _window.MinWidth = minWidth;
            if (_window.Width < minWidth)
                _window.Width = minWidth;
        }

        public static void Show()
        {
            if (_window != null)
            {
                ActivateMainWindow();
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
        MinHeight=""520"" MinWidth=""450""
        ResizeMode=""CanResizeWithGrip""
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


    <Border x:Name=""RootChromeBorder"" Background=""#141414"" CornerRadius=""16"" Margin=""20"">
        <Border.Effect>
            <DropShadowEffect BlurRadius=""30"" ShadowDepth=""10"" Opacity=""0.6"" Color=""Black""/>
        </Border.Effect>
        <Grid> <!-- Root Wrapper -->
            <Grid x:Name=""MainLayout"">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width=""0"" x:Name=""HistoryCol""/>
                    <ColumnDefinition Width=""*"" MinWidth=""410"" x:Name=""ChatCol""/>
                    <ColumnDefinition Width=""0"" x:Name=""CodeCol""/>
                </Grid.ColumnDefinitions>
                <Grid.RowDefinitions>
                    <RowDefinition Height=""50""/>
                    <RowDefinition Height=""Auto""/>
                    <RowDefinition Height=""*""/>
                    <RowDefinition Height=""Auto""/>
                    <RowDefinition Height=""0"" x:Name=""LibraryRow""/>
                </Grid.RowDefinitions>

                <Border Grid.Row=""0"" Grid.Column=""0"" Grid.ColumnSpan=""3"" Background=""#1E1E1E"" CornerRadius=""16,16,0,0"" x:Name=""HeaderBorder""/>
                <TextBlock Grid.Row=""0"" Grid.Column=""1"" x:Name=""TxtHeaderTitle"" Text=""✨ Magpie"" Foreground=""#E0E0E0"" FontSize=""16"" FontWeight=""SemiBold"" VerticalAlignment=""Center"" Margin=""20,0,0,0"" Cursor=""Hand"" ToolTip=""双击缩小为悬浮球"" HorizontalAlignment=""Left""/>
                <TextBlock Grid.Row=""0"" Grid.Column=""2"" x:Name=""CodeHeaderTitle"" Text=""GRAPH LOGIC"" Foreground=""#E0E0E0"" FontSize=""18"" FontWeight=""SemiBold"" VerticalAlignment=""Center"" Margin=""28,0,110,0"" TextTrimming=""CharacterEllipsis"" Visibility=""Collapsed""/>
                <StackPanel Grid.Row=""0"" Grid.Column=""0"" Grid.ColumnSpan=""3"" Orientation=""Horizontal"" HorizontalAlignment=""Right"" VerticalAlignment=""Center"" Margin=""0,0,18,0"">
                    <Button x:Name=""BtnMinimize"" Content=""−"" Foreground=""#FFFFFF"" Background=""Transparent"" BorderThickness=""0"" FontSize=""18"" Width=""34"" Height=""30"" Cursor=""Hand"" ToolTip=""最小化""/>
                    <Button x:Name=""BtnMaxRestore"" Content=""□"" Foreground=""#FFFFFF"" Background=""Transparent"" BorderThickness=""0"" FontSize=""14"" Width=""34"" Height=""30"" Cursor=""Hand"" ToolTip=""最大化/还原""/>
                    <Button x:Name=""BtnClose"" Foreground=""#FFFFFF"" Background=""Transparent"" BorderThickness=""0"" FontSize=""14"" Width=""34"" Height=""30"" Cursor=""Hand"" ToolTip=""关闭"">
                        <Button.Template>
                            <ControlTemplate TargetType=""Button"">
                                <Border Background=""{TemplateBinding Background}"" CornerRadius=""6"" Padding=""8,5""><ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/></Border>
                            </ControlTemplate>
                        </Button.Template>
                        <Path Data=""M4,4L8,8M8,4L4,8"" Stroke=""White"" StrokeThickness=""2"" Width=""16"" Height=""16"" Stretch=""Uniform""/>
                    </Button>
                </StackPanel>

                <Border Grid.Row=""1"" Grid.Column=""0"" Grid.ColumnSpan=""3"" Background=""#181818"" BorderBrush=""#252525"" BorderThickness=""0,1,0,1""/>
                <StackPanel Grid.Row=""1"" Grid.Column=""1"" Orientation=""Horizontal"" HorizontalAlignment=""Left"" Margin=""14,5"">
                    <Button x:Name=""BtnToggleCode"" Style=""{StaticResource IconButtonStyle}"" Foreground=""#FFFFFF"" Background=""Transparent"" BorderThickness=""0"" FontSize=""13"" Cursor=""Hand"" ToolTip=""切换代码视图"" Margin=""0,0,8,0""><Path Data=""M9.4,16.6L4.8,12l4.6-4.6L8,6l-6,6l6,6L9.4,16.6z M14.6,16.6l4.6-4.6l-4.6-4.6L16,6l6,6l-6,6L14.6,16.6z"" Fill=""White"" Width=""16"" Height=""16"" Stretch=""Uniform""/></Button>
                    <Button x:Name=""BtnNewChat"" Style=""{StaticResource IconButtonStyle}"" Foreground=""#FFFFFF"" Background=""Transparent"" BorderThickness=""0"" FontSize=""18"" Cursor=""Hand"" ToolTip=""新对话"" Margin=""0,0,8,0""><TextBlock Text=""+"" Foreground=""White"" FontWeight=""Bold""/></Button>
                    <Button x:Name=""BtnToggleHistory"" Style=""{StaticResource IconButtonStyle}"" Foreground=""#FFFFFF"" Background=""Transparent"" BorderThickness=""0"" FontSize=""13"" Cursor=""Hand"" ToolTip=""对话历史"" Margin=""0,0,8,0""><TextBlock Text=""历史"" Foreground=""White"" FontSize=""12"" FontWeight=""SemiBold""/></Button>
                    <Button x:Name=""BtnSettings"" Style=""{StaticResource IconButtonStyle}"" Foreground=""#FFFFFF"" Background=""Transparent"" BorderThickness=""0"" FontSize=""14"" Cursor=""Hand"" ToolTip=""配置""><Path Data=""M11,2L11,3.07C11.68,3.12,12.34,3.28,12.95,3.54L13.72,2.77L15.15,4.22L14.4,4.98C14.73,5.54,14.95,6.15,15.03,6.79L16.07,6.93L16.07,8.93L15.03,9.07C14.95,9.71,14.73,10.32,14.4,10.88L15.15,11.64L13.72,13.09L12.95,12.32C12.34,12.58,11.68,12.74,11,12.79L11,14L9,14L9,12.79C8.32,12.74,7.66,12.58,7.05,12.32L6.28,13.09L4.85,11.64L5.6,10.88C5.27,10.32,5.05,9.71,4.97,9.07L3.93,8.93L3.93,6.93L4.97,6.79C5.05,6.15,5.27,5.54,5.6,4.98L4.85,4.22L6.28,2.77L7.05,3.54C7.66,3.28,8.32,3.12,9,3.07L9,2L11,2z M10,7C8.9,7,8,7.9,8,9C8,10.1,8.9,11,10,11C11.1,11,12,10.1,12,9C12,7.9,11.1,7,10,7z"" Fill=""White"" Width=""16"" Height=""16"" Stretch=""Uniform""/></Button>
                </StackPanel>
                <Button Grid.Row=""1"" Grid.Column=""2"" x:Name=""BtnToggleViewMode"" Content=""JSON"" Foreground=""#B8B8B8"" Background=""Transparent"" BorderThickness=""1"" BorderBrush=""#333"" FontSize=""10"" Padding=""8,4"" Cursor=""Hand"" HorizontalAlignment=""Right"" VerticalAlignment=""Center"" Margin=""0,0,20,0"" Visibility=""Collapsed"">
                    <Button.Template><ControlTemplate TargetType=""Button""><Border Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}"" CornerRadius=""4""><ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/></Border></ControlTemplate></Button.Template>
                </Button>

                <Border x:Name=""HistorySidebar"" Grid.Row=""0"" Grid.Column=""0"" Grid.RowSpan=""5"" Panel.ZIndex=""9"" HorizontalAlignment=""Stretch"" VerticalAlignment=""Stretch"" Visibility=""Collapsed"" Margin=""0"" Background=""#171717"" BorderBrush=""#2A2A2A"" BorderThickness=""0,1,1,1"" CornerRadius=""16,0,0,16"" ClipToBounds=""True"">
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

                <ScrollViewer Grid.Row=""2"" Grid.Column=""1"" x:Name=""ChatScroll"" Margin=""5,10,5,0"" VerticalScrollBarVisibility=""Auto"" PanningMode=""VerticalOnly"">
                    <StackPanel x:Name=""ChatPanel"" Margin=""10""/>
                </ScrollViewer>

                <Border Grid.Row=""3"" Grid.Column=""1"" Background=""#1E1E1E"" CornerRadius=""0,0,16,16"" Padding=""15"" x:Name=""InputAreaBorder"">
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
                <Border Grid.Row=""4"" Grid.Column=""1"" Background=""#111111"" BorderBrush=""#333333"" BorderThickness=""0,1,0,0"" x:Name=""LibraryPanel"" CornerRadius=""0,0,16,16"">
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

                <Border Grid.Row=""2"" Grid.Column=""2"" Grid.RowSpan=""2"" x:Name=""CodeViewBorder"" Background=""#141414"" CornerRadius=""0,0,16,0"" BorderBrush=""#2A2A2A"" BorderThickness=""1,0,0,0"">
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height=""*""/>
                            <RowDefinition Height=""Auto""/>
                        </Grid.RowDefinitions>
                        <Border Grid.Row=""0"" Margin=""15,10,15,0"" Background=""Transparent""><RichTextBox x:Name=""RichCodeView"" Background=""Transparent"" Foreground=""#B8B8B8"" BorderThickness=""0"" FontSize=""12"" FontFamily=""Consolas, Monaco, Courier New"" IsReadOnly=""True"" IsDocumentEnabled=""True"" VerticalScrollBarVisibility=""Auto"" HorizontalScrollBarVisibility=""Disabled"" CaretBrush=""#888"" Padding=""0""/></Border>
                        <Border Grid.Row=""1"" x:Name=""CodeCanvasIssuesHost"" Background=""#1E1E1E"" CornerRadius=""0,0,16,0"" BorderBrush=""#2A2A2A"" BorderThickness=""0,1,0,0"" MinHeight=""120""><DockPanel Margin=""15,10,15,12"" LastChildFill=""True""><TextBlock DockPanel.Dock=""Top"" Text=""画布诊断"" Foreground=""#888"" FontSize=""11"" FontWeight=""SemiBold"" Margin=""0,0,0,8""/><ScrollViewer VerticalScrollBarVisibility=""Auto"" HorizontalScrollBarVisibility=""Disabled""><TextBox x:Name=""TxtCanvasIssues"" IsReadOnly=""True"" TextWrapping=""Wrap"" AcceptsReturn=""True"" Background=""Transparent"" Foreground=""#C8C8C8"" BorderThickness=""0"" FontSize=""12"" Padding=""0"" CaretBrush=""#888""/></ScrollViewer></DockPanel></Border>
                    </Grid>
                </Border>
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
                if (_ballWindow != null)
                {
                    _ballWindow.Close();
                    _ballWindow = null;
                }
                ShutdownPlugin();
                _window = null;
            };
            _window.SourceInitialized += (s, e) => AttachWindowResizeHitTest();
            InitializeFloatingScrollbars();

            _rootChromeBorder = (Border)_window.FindName("RootChromeBorder");

            var headerBorder = (Border)_window.FindName("HeaderBorder");
            if (headerBorder != null) headerBorder.MouseLeftButtonDown += (s, e) => BeginWindowHeaderDrag(e);

            var txtHeaderTitle = (TextBlock)_window.FindName("TxtHeaderTitle");
            if (txtHeaderTitle != null) txtHeaderTitle.MouseLeftButtonDown += (s, e) => { if (e.ClickCount >= 2) MinimizeToBall(); else BeginWindowHeaderDrag(e); };

            var btnMinimize = (Button)_window.FindName("BtnMinimize");
            if (btnMinimize != null)
            {
                btnMinimize.Click += (s, e) => _window.WindowState = WindowState.Minimized;
            }

            var btnMaxRestore = (Button)_window.FindName("BtnMaxRestore");
            Action updateMaxRestoreButton = () =>
            {
                if (btnMaxRestore != null)
                    btnMaxRestore.Content = _window.WindowState == WindowState.Maximized ? "❐" : "□";
                UpdateWindowChromeForState();
            };
            if (btnMaxRestore != null)
            {
                btnMaxRestore.Click += (s, e) =>
                {
                    _window.WindowState = _window.WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
                    updateMaxRestoreButton();
                };
                _window.StateChanged += (s, e) => updateMaxRestoreButton();
                updateMaxRestoreButton();
            }

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
            _codeHeaderTitle = (TextBlock)_window.FindName("CodeHeaderTitle");
            _btnToggleViewMode = (Button)_window.FindName("BtnToggleViewMode");
            _historyCol = (ColumnDefinition)_window.FindName("HistoryCol");
            _chatCol = (ColumnDefinition)_window.FindName("ChatCol");
            _codeCol = (ColumnDefinition)_window.FindName("CodeCol");
            var btnToggleCode = (Button)_window.FindName("BtnToggleCode");

            _inputAreaBorder = (Border)_window.FindName("InputAreaBorder");
            if (_inputAreaBorder != null)
                _inputAreaBorder.SizeChanged += (s, ev) => SyncCodeIssuesStripHeightToInputArea();

            if (btnToggleCode != null) {
            btnToggleCode.Click += (s, e) => {
                _isCodeVisible = !_isCodeVisible;
                if (_isCodeVisible) {
                        if (_chatCol != null) _chatCol.MinWidth = PaneMinWidth;
                        if (_codeCol != null) {
                            _codeCol.MinWidth = PaneMinWidth;
                            _codeCol.Width = new GridLength(2, GridUnitType.Star);
                        }
                    if (_codeHeaderTitle != null) _codeHeaderTitle.Visibility = Visibility.Visible;
                    if (_btnToggleViewMode != null) _btnToggleViewMode.Visibility = Visibility.Visible;
                    _widthBeforeCodeView = _window.ActualWidth > 0 ? _window.ActualWidth : _window.Width;
                    UpdateWindowMinWidthForVisiblePanes();
                    double desiredWidth = DefaultWindowWidth + CodeViewColumnWidth;
                    double maxWorkAreaWidth = SystemParameters.WorkArea.Width;
                    if (_window.Width < desiredWidth)
                        _window.Width = Math.Min(desiredWidth, Math.Max(_window.MinWidth, maxWorkAreaWidth));
                        if (_inputAreaBorder != null) _inputAreaBorder.CornerRadius = new CornerRadius(0, 0, 0, 16);
                    StartGrasshopperCodeSurfaceHooks();
                    SyncCodeIssuesStripHeightToInputArea();
                    UpdateCodeView();
                } else {
                        if (_codeCol != null) {
                            _codeCol.MinWidth = 0;
                            _codeCol.Width = new GridLength(0);
                        }
                    if (_codeHeaderTitle != null) _codeHeaderTitle.Visibility = Visibility.Collapsed;
                    if (_btnToggleViewMode != null) _btnToggleViewMode.Visibility = Visibility.Collapsed;
                    UpdateWindowMinWidthForVisiblePanes();
                    if (!double.IsNaN(_widthBeforeCodeView) && _widthBeforeCodeView >= _window.MinWidth)
                        _window.Width = _widthBeforeCodeView;
                    _widthBeforeCodeView = double.NaN;
                        if (_inputAreaBorder != null) _inputAreaBorder.CornerRadius = new CornerRadius(0, 0, 16, 16);
                }
            };
            }

            _window.Loaded += (s, ev) =>
            {
                StartGrasshopperCodeSurfaceHooks();
                SyncCodeIssuesStripHeightToInputArea();
            };

            if (_btnToggleViewMode != null) {
            _btnToggleViewMode.Click += (s, e) => {
                _isJsonMode = !_isJsonMode;
                _btnToggleViewMode.Content = _isJsonMode ? "JSON" : "RAW";
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
                if (_historyCol != null) _historyCol.Width = new GridLength(320);
                _historySidebar.Visibility = Visibility.Visible;
                _historySidebar.BeginAnimation(FrameworkElement.WidthProperty, null);
                _historySidebar.Width = double.NaN;
                _historySidebar.Height = double.NaN;
                _historySidebar.VerticalAlignment = VerticalAlignment.Stretch;
                RefreshHistorySidebar();
                UpdateWindowMinWidthForVisiblePanes();
            }
            else
            {
                _historySidebar.BeginAnimation(FrameworkElement.WidthProperty, null);
                _historySidebar.Visibility = Visibility.Collapsed;
                if (_historyCol != null) _historyCol.Width = new GridLength(0);
                UpdateWindowMinWidthForVisiblePanes();
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

            object[] toolDefinitions = BuildToolDefinitionsForCurrentMode();

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

                        var dispatch = ExecuteToolCall(
                            funcName,
                            argsObj,
                            argsJson,
                            callId,
                            fullContent,
                            fullReasoning,
                            operationCards);

                        if (dispatch.EndApiRoundAwaitingUser)
                        {
                            SyncActiveHistoryConversation();
                            return dispatch.EarlyResponse ?? new ApiResponse { Content = fullContent, Reasoning = fullReasoning };
                        }

                        string toolResult = dispatch.ToolResult ?? "";
                        addComp += dispatch.AddComp;
                        delComp += dispatch.DelComp;
                        addConn += dispatch.AddConn;
                        delConn += dispatch.DelConn;

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
                        ActivateMainWindow();
                    } else {
                        _ballWindow.DragMove();
                    }
                }
            };

            _ballWindow.Content = border;
            _ballWindow.Show();
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
