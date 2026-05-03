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
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Windows.Markup;
using Grasshopper.Kernel;

namespace ADDGH
{
    public static class ChatWindow
    {
        private static Window _window;
        private static StackPanel _chatPanel;
        private static ScrollViewer _chatScroll;
        private static TextBox _txtInput;
        private static Button _btnSend;
        private static System.Windows.Threading.DispatcherTimer _scrollHideTimer;
        
        private static Grid _settingsOverlay;
        private static TextBox _txtApiKey;
        private static ComboBox _comboProvider;
        private static TextBox _txtApiBaseUrl;
        private static TextBox _txtModel;
        private static System.Threading.CancellationTokenSource _cts;
        private static string _currentBase64Image = null;
        private static string _currentImagePath = null;
        private static TextBlock _txtImageAttached;
        private static Button _btnClearImage;

        private static Border _codeViewBorder;
        private static TextBox _txtCodeView;
        private static ColumnDefinition _codeCol;
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
        
        private static Border _warningBar;
        private static TextBlock _txtWarning;
        private static Button _btnCloseWarning;

        private const string SYSTEM_PROMPT = @"你是 GH 参数化专家。请遵循专业表达与风险管控规范：
1. 严禁技术术语：禁止说'沙箱'、'is_sandbox'、'工具'等。称实验区为'非破坏性实验方案'或'逻辑草案'。
2. 风险等级判定（决定是否开启 is_sandbox）：
   - 🔴 高风险（必开）：删除 8 个以上电池、重构主干逻辑、连接可能引发长时间计算的组件（如复杂网格/物理模拟）。
   - 🟡 中风险（自主判定）：添加 5-8 个电池的功能分支、修改密集型交叉连线、替换现有逻辑块。
   - 🟢 低风险（直接操作）：修改 Slider/Panel 数值、添加单个辅助电池、电池对齐或整理分组。
3. 命名规范：数值条 (Number Slider) 必须设 label。普通电池严禁改 label。
4. 最终总结请使用结构化 Markdown：短标题、列表、重点加粗；涉及代码、JSON、表达式或关键参数时使用 ``` 代码块，不要把大段技术内容挤在普通段落中。
5. 在开始建模、修改画布或设计 GH 逻辑前，应主动检查 reference_index.md；若有相关参考，先用 read_reference_json 读取对应 JSON，再复用或改造其中的建模逻辑。
优先批量处理，直接行动。";

        private static List<object> _messages = new List<object>();
        private static string _cachedCanvasState = null;  // 画布状态缓存
        private static bool _canvasChanged = true;  // 画布是否改变标记

        private static readonly HttpClient _httpClient = new HttpClient() { Timeout = TimeSpan.FromMinutes(5) };

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
                    Foreground = Brushes.Gray,
                    FontSize = 13,
                    Margin = new Thickness(5, 0, 0, 24),
                    VerticalAlignment = VerticalAlignment.Center
                };
                
                var breathingAnim = new DoubleAnimation {
                    From = 1.0, To = 0.3,
                    Duration = TimeSpan.FromSeconds(1),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };
                text.BeginAnimation(UIElement.OpacityProperty, breathingAnim);

                _thinkingBubble = new Border { Child = text };
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
                <Border Grid.Column=""1"" x:Name=""CodeViewBorder"" Background=""#0A0A0A"" CornerRadius=""0,16,16,0"" BorderBrush=""#222"" BorderThickness=""1,0,0,0"">
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height=""60""/>
                            <RowDefinition Height=""*""/>
                        </Grid.RowDefinitions>
                        <Border Grid.Row=""0"" Padding=""20,0"">
                            <Grid>
                                <TextBlock Text=""GRAPH LOGIC"" Foreground=""#555"" FontSize=""10"" FontWeight=""Bold"" VerticalAlignment=""Center""/>
                                <StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Right"" VerticalAlignment=""Center"">
                                    <Button x:Name=""BtnToggleViewMode"" Content=""JSON"" Foreground=""#555"" Background=""Transparent"" BorderThickness=""1"" BorderBrush=""#333"" FontSize=""9"" Padding=""8,2"" Cursor=""Hand"">
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
                        <TextBox Grid.Row=""1"" x:Name=""TxtCodeView"" Background=""Transparent"" Foreground=""#888"" BorderThickness=""0"" 
                                 FontSize=""12"" FontFamily=""Consolas, Monaco, 'Courier New'"" Padding=""20,0,20,20"" 
                                 IsReadOnly=""True"" TextWrapping=""Wrap"" AcceptsReturn=""True"" VerticalScrollBarVisibility=""Auto"" HorizontalScrollBarVisibility=""Disabled""/>
                    </Grid>
                </Border>

                <!-- Chat Area (Left) -->
                <Grid Grid.Column=""0"">
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
                
                        <StackPanel Orientation=""Horizontal"" Margin=""0,0,0,5"">
                            <TextBlock x:Name=""TxtImageAttached"" Text=""已选择图片"" Foreground=""#4CAF50"" FontSize=""11"" VerticalAlignment=""Center"" Visibility=""Collapsed""/>
                            <Button x:Name=""BtnClearImage"" Content=""✕"" Foreground=""#FF6B6B"" Background=""Transparent"" BorderThickness=""0"" FontSize=""11"" Cursor=""Hand"" Margin=""5,0,0,0"" Visibility=""Collapsed""/>
                        </StackPanel>
                        <Border Background=""#2A2A2A"" BorderBrush=""#333333"" BorderThickness=""1"" CornerRadius=""8"" Padding=""4"" Margin=""0,0,0,8"">
                            <TextBox x:Name=""TxtInput"" Background=""Transparent"" Foreground=""#FFF"" BorderThickness=""0"" Padding=""14,10,14,10"" FontSize=""14"" AcceptsReturn=""True"" VerticalScrollBarVisibility=""Auto"" TextWrapping=""Wrap"" MinHeight=""36"" MaxHeight=""116"" CaretBrush=""White""/>
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
                            </Grid.ColumnDefinitions>
                            
                            <Button x:Name=""BtnUploadImage"" Grid.Column=""0"" Style=""{StaticResource IconButtonStyle}"" Content=""+"" Foreground=""#A0A0A0"" Background=""Transparent"" BorderThickness=""0"" FontSize=""22"" FontWeight=""Medium"" Cursor=""Hand"" ToolTip=""上传图片"" Margin=""0,0,10,0""/>
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
                            
                            <Button x:Name=""BtnSend"" Grid.Column=""6"" Content=""➤"" Foreground=""Black"" FontSize=""18"" Margin=""0"" Width=""36"" Height=""36"" Cursor=""Hand"" VerticalAlignment=""Center"">
                                <Button.Template>
                                    <ControlTemplate TargetType=""Button"">
                                        <Border x:Name=""bg"" Background=""White"" CornerRadius=""18"">
                                        <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Margin=""2,0,0,0""/>
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
            <Grid x:Name=""SettingsOverlay"" Grid.ColumnSpan=""2"" Background=""#A5000000"" Visibility=""Collapsed"">
            <Border Background=""#1E1E1E"" CornerRadius=""12"" Width=""380"" Height=""550"" HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Padding=""20"">
                <StackPanel>
                    <TextBlock Text=""配置 API"" Foreground=""White"" FontSize=""16"" FontWeight=""SemiBold"" Margin=""0,0,0,15""/>
                    
                    <TextBlock Text=""提供商 (Provider)"" Foreground=""#A0A0A0"" FontSize=""12"" Margin=""0,0,0,5""/>
                    <ComboBox x:Name=""ComboProvider"" Height=""32"" Margin=""0,0,0,10"" Background=""#2A2A2A"" Foreground=""Black"">
                        <ComboBoxItem Content=""DeepSeek""/>
                        <ComboBoxItem Content=""Qwen (通义千问)""/>
                        <ComboBoxItem Content=""Seed (商汤)""/>
                        <ComboBoxItem Content=""Custom""/>
                    </ComboBox>

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

            _window.Closed += (s, e) => _window = null;
            InitializeFloatingScrollbars();
            
            var headerBorder = (Border)_window.FindName("HeaderBorder");
            if (headerBorder != null) headerBorder.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed && e.ClickCount == 1) _window.DragMove(); };

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
            
            var btnContinue = (Button)_window.FindName("BtnContinue");
            if (btnContinue != null) {
            btnContinue.Click += (s, e) => {
                if (_isGenerating) { _cts?.Cancel(); return; }
                    if (_txtInput != null) _txtInput.Text = "继续";
                BtnSend_Click(null, null);
            };
            }

            _codeViewBorder = (Border)_window.FindName("CodeViewBorder");
            _txtCodeView = (TextBox)_window.FindName("TxtCodeView");
            _codeCol = (ColumnDefinition)_window.FindName("CodeCol");
            var btnToggleCode = (Button)_window.FindName("BtnToggleCode");

            var inputAreaBorder = (Border)_window.FindName("InputAreaBorder");
            if (btnToggleCode != null) {
            btnToggleCode.Click += (s, e) => {
                _isCodeVisible = !_isCodeVisible;
                if (_isCodeVisible) {
                        if (_codeCol != null) _codeCol.Width = new GridLength(750);
                    _window.Width = 1200;
                        if (headerBorder != null) headerBorder.CornerRadius = new CornerRadius(16, 0, 0, 0);
                        if (inputAreaBorder != null) inputAreaBorder.CornerRadius = new CornerRadius(0, 0, 0, 16);
                    UpdateCodeView();
                } else {
                        if (_codeCol != null) _codeCol.Width = new GridLength(0);
                    _window.Width = 450;
                        if (headerBorder != null) headerBorder.CornerRadius = new CornerRadius(16, 16, 0, 0);
                        if (inputAreaBorder != null) inputAreaBorder.CornerRadius = new CornerRadius(0, 0, 16, 16);
                }
            };
            }

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
            _txtApiBaseUrl = (TextBox)_window.FindName("TxtApiBaseUrl");
            _txtModel = (TextBox)_window.FindName("TxtModel");
            var txtLibraryPath = (TextBox)_window.FindName("TxtLibraryPath");

            if (btnSettings != null) {
            btnSettings.Click += (s, e) => {
                    if (_txtApiKey != null) _txtApiKey.Text = Grasshopper.Instances.Settings.GetValue("AI_API_Key", "");
                    if (_txtApiBaseUrl != null) _txtApiBaseUrl.Text = Grasshopper.Instances.Settings.GetValue("AI_API_BaseUrl", "https://api.deepseek.com/chat/completions");
                    if (_txtModel != null) _txtModel.Text = Grasshopper.Instances.Settings.GetValue("AI_ModelName", "deepseek-reasoner");
                    if (txtLibraryPath != null) txtLibraryPath.Text = Grasshopper.Instances.Settings.GetValue("Library_Path", "");
                    if (_settingsOverlay != null) _settingsOverlay.Visibility = Visibility.Visible;
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
                    if (_txtApiKey != null) Grasshopper.Instances.Settings.SetValue("AI_API_Key", _txtApiKey.Text);
                    if (_txtApiBaseUrl != null) Grasshopper.Instances.Settings.SetValue("AI_API_BaseUrl", _txtApiBaseUrl.Text);
                    if (_txtModel != null) Grasshopper.Instances.Settings.SetValue("AI_ModelName", "deepseek-reasoner");
                    if (txtLibraryPath != null) Grasshopper.Instances.Settings.SetValue("Library_Path", txtLibraryPath.Text);
                    if (_settingsOverlay != null) _settingsOverlay.Visibility = Visibility.Collapsed;
                };
            }

            var btnCancelSettings = (Button)_window.FindName("BtnCancelSettings");
            if (btnCancelSettings != null) {
                btnCancelSettings.Click += (s, e) => {
                    if (_settingsOverlay != null) _settingsOverlay.Visibility = Visibility.Collapsed;
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
            }

            var btnUploadImage = (Button)_window.FindName("BtnUploadImage");
            _txtImageAttached = (TextBlock)_window.FindName("TxtImageAttached");
            _btnClearImage = (Button)_window.FindName("BtnClearImage");

            if (btnUploadImage != null) {
            btnUploadImage.Click += (s, e) => {
                var ofd = new Microsoft.Win32.OpenFileDialog { Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp" };
                if (ofd.ShowDialog() == true) {
                    _currentImagePath = ofd.FileName;
                    _currentBase64Image = Convert.ToBase64String(System.IO.File.ReadAllBytes(_currentImagePath));
                        if (_txtImageAttached != null) _txtImageAttached.Visibility = Visibility.Visible;
                        if (_btnClearImage != null) _btnClearImage.Visibility = Visibility.Visible;
                }
            };
            }

            if (_btnClearImage != null) {
            _btnClearImage.Click += (s, e) => {
                _currentImagePath = null;
                _currentBase64Image = null;
                    if (_txtImageAttached != null) _txtImageAttached.Visibility = Visibility.Collapsed;
                    if (_btnClearImage != null) _btnClearImage.Visibility = Visibility.Collapsed;
            };
            }

            var btnNewChat = (Button)_window.FindName("BtnNewChat");
            if (btnNewChat != null) {
            btnNewChat.Click += (s, e) => {
                _messages.Clear();
                _messages.Add(new { role = "system", content = SYSTEM_PROMPT });
                    if (_chatPanel != null) _chatPanel.Children.Clear();
                    _cachedCanvasState = null;
                    _canvasChanged = true;
                AppendSystemMessage("新对话已开启，历史已清空。");
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
                    _settingsOverlay.Visibility = Visibility.Collapsed;
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
                    string prompt = "请对当前画布内容进行总结，生成五个简短的画布描述（围绕当前画布什么典型建模操作，比如某种gh电池使用、基于某种建模逻辑的曲线生成方法等等），描述以卡片形式供我选择。请调用 show_reference_options 工具来展示选项。用户选择后，程序会把画布 JSON 保存到项目 reference 文件夹，并更新 skills/reference_index.md。";
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
            catch { }
            
            try
            {
                if (param.Access == GH_ParamAccess.list) return baseType + "[]";
                if (param.Access == GH_ParamAccess.tree) return baseType + "[][]";
            }
            catch { }
            
            return baseType;
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
                                try { typeHint = GetTypeHint(param); } catch { }
                                
                                string desc = "";
                                try { desc = param.Description ?? ""; } catch { }
                                
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
                                try { typeHint = GetTypeHint(param); } catch { }
                                
                                string desc = "";
                                try { desc = param.Description ?? ""; } catch { }
                                
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
                    catch { }
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
                AppendSystemMessage($"同步失败: {ex.Message}");
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

        private static async void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            if (_isGenerating) { _cts?.Cancel(); return; }
            string input = _txtInput.Text.Trim();
            if (string.IsNullOrEmpty(input) && _currentBase64Image == null) return;

            _isGenerating = true;
            if (_btnSend != null) {
                _btnSend.Content = "■";
                var bg = _btnSend.Template.FindName("bg", _btnSend) as Border;
                if (bg != null) bg.CornerRadius = new CornerRadius(8);
                var cp = _btnSend.Template.FindName("cp", _btnSend) as ContentPresenter;
                if (cp != null) cp.Margin = new Thickness(0);
            }
            _txtInput.Text = "";

            if (_messages.Count == 0) {
                string skillsSummary = GetSkillsSummary();
                _messages.Add(new { role = "system", content = SYSTEM_PROMPT + skillsSummary });
            }

            if (_currentBase64Image != null) {
                var contentArr = new List<object> { new { type = "text", text = input } };
                contentArr.Add(new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{_currentBase64Image}" } });
                _messages.Add(new { role = "user", content = contentArr });
                AppendBubble(string.IsNullOrEmpty(input) ? "发送了图片" : input, true);
            } else {
                _messages.Add(new { role = "user", content = input });
                AppendBubble(input, true);
            }

            _currentBase64Image = null;
            _txtImageAttached.Visibility = Visibility.Collapsed;
            _btnClearImage.Visibility = Visibility.Collapsed;

            _cts = new System.Threading.CancellationTokenSource();
            string apiKey = Grasshopper.Instances.Settings.GetValue("AI_API_Key", "");

            try {
            ShowThinkingAnimation();
                await CallLLMAPI(apiKey, 0, _cts.Token);
            } catch (OperationCanceledException) {
                AppendSystemMessage("已停止生成。");
            } catch (Exception ex) {
                AppendSystemMessage("Error: " + ex.Message, true);
            } finally {
                HideThinkingAnimation();
                _isGenerating = false;
                if (_btnSend != null) _btnSend.Content = "➤";
            }
        }

        private class ApiResponse { public string Content; public string Reasoning; }

        private static List<object> CompressMessages(List<object> fullMessages)
        {
            var compressed = new List<object>();
            int lastCanvasStateIndex = -1;

            // 找出最后一次获取完整画布状态的索引
            for (int i = fullMessages.Count - 1; i >= 0; i--)
            {
                var msg = fullMessages[i] as Newtonsoft.Json.Linq.JObject;
                if (msg == null)
                {
                    // 如果是匿名对象（我们自己构建的）
                    var type = fullMessages[i].GetType();
                    var roleProp = type.GetProperty("role");
                    var nameProp = type.GetProperty("name");
                    if (roleProp != null && nameProp != null)
                    {
                        string role = roleProp.GetValue(fullMessages[i])?.ToString();
                        string name = nameProp.GetValue(fullMessages[i])?.ToString();
                        if (role == "tool" && name == "get_gh_components")
                        {
                            lastCanvasStateIndex = i;
                            break;
                        }
                    }
                }
                else
                {
                    // 如果是 JObject（从 API 返回解析的）
                    string role = msg["role"]?.ToString();
                    string name = msg["name"]?.ToString();
                    if (role == "tool" && name == "get_gh_components")
                    {
                        lastCanvasStateIndex = i;
                        break;
                    }
                }
            }

            for (int i = 0; i < fullMessages.Count; i++)
            {
                var msg = fullMessages[i];
                bool isCanvasState = false;

                var jmsg = msg as Newtonsoft.Json.Linq.JObject;
                if (jmsg == null)
                {
                    var type = msg.GetType();
                    var roleProp = type.GetProperty("role");
                    var nameProp = type.GetProperty("name");
                    if (roleProp != null && nameProp != null)
                    {
                        string role = roleProp.GetValue(msg)?.ToString();
                        string name = nameProp.GetValue(msg)?.ToString();
                        if (role == "tool" && name == "get_gh_components") isCanvasState = true;
                    }
                }
                else
                {
                    string role = jmsg["role"]?.ToString();
                    string name = jmsg["name"]?.ToString();
                    if (role == "tool" && name == "get_gh_components") isCanvasState = true;
                }

                if (isCanvasState && i != lastCanvasStateIndex)
                {
                    // 替换旧的画布状态以节省 Token
                    compressed.Add(new { 
                        role = "tool", 
                        tool_call_id = jmsg != null ? jmsg["tool_call_id"]?.ToString() : msg.GetType().GetProperty("tool_call_id")?.GetValue(msg)?.ToString(),
                        name = "get_gh_components", 
                        content = "[历史画布状态已折叠以节省 Token]" 
                    });
                }
                else
                {
                    compressed.Add(msg);
                }
            }

            return compressed;
        }

        private static async Task<ApiResponse> CallLLMAPI(string apiKey, int depth = 0, System.Threading.CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            const int MAX_DEPTH = 50;
            if (depth >= MAX_DEPTH) 
            {
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

            string baseUrl = Grasshopper.Instances.Settings.GetValue("AI_API_BaseUrl", "https://api.deepseek.com/chat/completions");
            string modelName = Grasshopper.Instances.Settings.GetValue("AI_ModelName", "deepseek-reasoner");

            var request = new HttpRequestMessage(HttpMethod.Post, baseUrl);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            var messagesToSend = CompressMessages(_messages);

                    var requestBody = new
                    {
                    model = modelName,
                    messages = messagesToSend,
                    stream = false,
                    temperature = 0.3,
                    tools = new object[]
                    {
                        new {
                            type = "function",
                            function = new {
                                name = "ensure_gh_canvas",
                                description = "确保当前存在可用的 Grasshopper 画布。若未检测到可用画布，则新建一个空白 GH 画布并设为当前画布。"
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "get_gh_components",
                                description = "获取当前 Grasshopper 画布的完整 JSON 结构图谱，包含所有电池、ID、坐标、端口详情及精确的连线关系。"
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "add_gh_component",
                                description = "在画布上创建一个新的 Grasshopper 电池。请务必使用完整 'name'。对于 Slider/Panel，必须提供 'label' 参数进行命名。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        name = new { type = "string", description = "电池标准名称" },
                                        x = new { type = "number", description = "画布 X 坐标" },
                                        y = new { type = "number", description = "画布 Y 坐标" },
                                        label = new { type = "string", description = "仅限 Slider/Panel 的显示标签。普通电池严禁使用。" }
                                    },
                                    required = new[] { "name", "x", "y" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "connect_gh_components",
                                description = "在两个电池的端口之间建立连接。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        from_id = new { type = "string", description = "源电池的 GUID" },
                                        from_index = new { type = "integer", description = "源电池输出端口索引 (从0开始)" },
                                        to_id = new { type = "string", description = "目标电池的 GUID" },
                                        to_index = new { type = "integer", description = "目标电池输入端口索引 (从0开始)" }
                                    },
                                    required = new[] { "from_id", "from_index", "to_id", "to_index" }
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
                                        is_sandbox = new { type = "boolean", description = "可选：沙箱模式下不直接删除，而是禁用并放入回收站组" }
                                    },
                                    required = new[] { "id" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "set_gh_component_value",
                                description = "修改 Slider 的数值或 Panel 的文本内容。仅限 Slider 和 Panel 使用。对于 Slider，还可以同时设置最小值、最大值和小数精度。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        id = new { type = "string", description = "电池 GUID" },
                                        value = new { type = "string", description = "可选：要设置的值（数字字符串或文本）。对于 Panel 此参数是必需的" },
                                        min = new { type = "number", description = "可选：Slider 最小值" },
                                        max = new { type = "number", description = "可选：Slider 最大值" },
                                        decimals = new { type = "integer", description = "可选：Slider 小数位数（0-10）" }
                                    },
                                    required = new[] { "id" }
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
                                        to_index = new { type = "integer", description = "目标电池输入端口索引" }
                                    },
                                    required = new[] { "from_id", "from_index", "to_id", "to_index" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "create_component_graph",
                                description = "【推荐】一次性批量创建多个电池并建立它们之间的连线，适合构建复杂的几何逻辑。示例：{\"components\":[{\"alias_id\":\"pt1\",\"name\":\"Construct Point\",\"x\":0,\"y\":0},{\"alias_id\":\"crv1\",\"name\":\"Circle CNR\",\"x\":200,\"y\":0,\"value\":\"5\"}],\"connections\":[{\"from_alias\":\"pt1\",\"from_index\":0,\"to_alias\":\"crv1\",\"to_index\":0}]}",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        components = new {
                                            type = "array",
                                            items = new {
                                                type = "object",
                                                properties = new {
                                                    alias_id = new { type = "string", description = "临时代号(如 'pt1', 'crv1')，用于连线引用，必须唯一" },
                                                    name = new { type = "string", description = "电池标准名称" },
                                                    label = new { type = "string", description = "仅限 Slider/Panel 的显示标签。普通电池严禁使用。" },
                                                    x = new { type = "number", description = "画布 X 坐标" },
                                                    y = new { type = "number", description = "画布 Y 坐标" },
                                                    value = new { type = "string", description = "可选：如果是 Slider/Panel，设置初始值" },
                                                    min = new { type = "number", description = "可选：如果是 Slider，设置最小值" },
                                                    max = new { type = "number", description = "可选：如果是 Slider，设置最大值" },
                                                    decimals = new { type = "integer", description = "可选：如果是 Slider，设置小数位数" }
                                                },
                                                required = new[] { "alias_id", "name", "x", "y" }
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
                                        is_sandbox = new { type = "boolean", description = "可选：开启沙箱模式，电池将偏移并在橙色组中生成，不破坏原有逻辑" }
                                    },
                                    required = new[] { "components", "connections" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "check_gh_errors",
                                description = "检查当前画布是否存在运行时错误或警告。"
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
                                        enabled = new { type = "boolean", description = "是否启用电池 (true为启用, false为禁用)" }
                                    },
                                    required = new[] { "id" }
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
                                        action = new { type = "string", description = "'add' 或 'remove'" }
                                    },
                                    required = new[] { "id", "is_input", "action" }
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
                                        name = new { type = "string", description = "创建组时的显示名称，create 时需要" }
                                    },
                                    required = new[] { "action" }
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
                                        operation = new { type = "string", description = "操作类型: 'Flatten', 'Graft', 'Simplify', 'Reverse', 'None'" }
                                    },
                                    required = new[] { "id", "is_input", "index", "operation" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "search_component_library",
                                description = "根据关键词在 Grasshopper 电池库中搜索可用插件和电池名称。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        keyword = new { type = "string", description = "搜索关键词" }
                                    },
                                    required = new[] { "keyword" }
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
                                        content = new { type = "string", description = "技能的详细内容，包括使用的电池、连线逻辑、注意事项等（Markdown 格式）" }
                                    },
                                    required = new[] { "file_name", "name", "description", "content" }
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
                                        file_name = new { type = "string", description = "要读取的 skill 文件名，如 'general_modeling.md' 或 'workflow_optimization.md'" }
                                    },
                                    required = new[] { "file_name" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "read_reference_json",
                                description = "读取 reference 目录中的参考画布 JSON。通常先读取 reference_index.md，根据描述选定 file_name 后再调用本工具。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        file_name = new { type = "string", description = "reference 目录下的 JSON 文件名，如 'ref_20260503123000.json'" }
                                    },
                                    required = new[] { "file_name" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "show_reference_options",
                                description = "显示五个画布描述选项供用户选择，用于创建参考。",
                                parameters = new {
                                    type = "object",
                                    properties = new {
                                        options = new {
                                            type = "array",
                                            items = new { type = "string" },
                                            description = "5个简短的画布描述"
                                        }
                                    },
                                    required = new[] { "options" }
                                }
                            }
                        },
                        new {
                            type = "function",
                            function = new {
                                name = "apply_gh_sandbox",
                                description = "将沙箱模式中的修改应用到主画布。这会删除回收站中的电池，并将沙箱组中的电池移回原位并解除沙箱组。"
                            }
                        }
                    }
                };

                string jsonContent = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                request.Content = content;

                ShowThinkingAnimation("载入中...");
                DateTime startTime = DateTime.Now;
                
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                
                ShowThinkingAnimation("思考中...");

                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    return new ApiResponse { Content = "Error: " + response.StatusCode + "\n" + err };
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

                var json = JObject.Parse(responseJson);
                var messageNode = json["choices"]?[0]?["message"];
                if (messageNode == null) return new ApiResponse { Content = "Error: Invalid API response." };

                string fullContent = messageNode["content"]?.ToString() ?? "";
                string fullReasoning = messageNode["reasoning_content"]?.ToString() ?? "";
                var fullToolCalls = messageNode["tool_calls"] as JArray ?? new JArray();

                bool isSandboxOperation = false;
                foreach (var tc in fullToolCalls) {
                    string argsJson = tc["function"]?["arguments"]?.ToString();
                    if (!string.IsNullOrEmpty(argsJson) && argsJson.Contains("\"is_sandbox\"") && argsJson.Contains("true")) {
                        isSandboxOperation = true;
                        break;
                    }
                }

                await _window.Dispatcher.InvokeAsync(() => {
                    if (!string.IsNullOrEmpty(fullReasoning))
                    {
                        AppendCollapsibleBubble(fullReasoning, "已思考 " + Math.Round(durationSeconds, 1) + "s", "💭");
                    }
                    if (!string.IsNullOrEmpty(fullContent))
                    {
                        if (isSandboxOperation) {
                            AppendSandboxBubble(fullContent);
                        } else {
                            AppendBubble(fullContent, false, depth == 0); 
                        }
                    }
                });

                _messages.Add(messageNode);

                int addComp = 0, delComp = 0, addConn = 0, delConn = 0;

                if (fullToolCalls.Count > 0)
                {
                    ShowThinkingAnimation("工作中...");

                    foreach (var toolCall in fullToolCalls)
                    {
                        ct.ThrowIfCancellationRequested();
                        string funcName = toolCall["function"]?["name"]?.ToString();
                        string argsJson = toolCall["function"]?["arguments"]?.ToString();
                        string callId = toolCall["id"]?.ToString();

                        string toolResult = "";
                        try
                        {
                            var args = JsonConvert.DeserializeObject<Dictionary<string, object>>(argsJson);
                            if (funcName == "ensure_gh_canvas") toolResult = ExecuteEnsureGhCanvas();
                            else if (funcName == "get_gh_components") toolResult = ExecuteGetGhComponents();
                            else if (funcName == "add_gh_component") { 
                                string label = args.ContainsKey("label") ? args["label"].ToString() : null;
                                toolResult = ExecuteAddGhComponent(args["name"].ToString(), (float)Convert.ToDouble(args["x"]), (float)Convert.ToDouble(args["y"]), label); 
                                addComp++; 
                            }
                            else if (funcName == "connect_gh_components") { toolResult = ExecuteConnectGhComponents(args["from_id"].ToString(), Convert.ToInt32(args["from_index"]), args["to_id"].ToString(), Convert.ToInt32(args["to_index"])); addConn++; }
                            else if (funcName == "remove_gh_component") { 
                                bool isSandbox = args.ContainsKey("is_sandbox") && Convert.ToBoolean(args["is_sandbox"]);
                                toolResult = ExecuteRemoveGhComponent(args["id"].ToString(), isSandbox); 
                                delComp++; 
                            }
                            else if (funcName == "set_gh_component_value") {
                                string val = args.ContainsKey("value") ? args["value"].ToString() : null;
                                double? min = args.ContainsKey("min") ? (double?)Convert.ToDouble(args["min"]) : null;
                                double? max = args.ContainsKey("max") ? (double?)Convert.ToDouble(args["max"]) : null;
                                int? decimals = args.ContainsKey("decimals") ? (int?)Convert.ToInt32(args["decimals"]) : null;
                                toolResult = ExecuteSetGhComponentValue(args["id"].ToString(), val, min, max, decimals);
                            }
                            else if (funcName == "remove_gh_connection") { toolResult = ExecuteRemoveGhConnection(args["from_id"].ToString(), Convert.ToInt32(args["from_index"]), args["to_id"].ToString(), Convert.ToInt32(args["to_index"])); delConn++; }
                            else if (funcName == "create_component_graph") { 
                                bool autoG = args.ContainsKey("auto_group") && Convert.ToBoolean(args["auto_group"]);
                                bool isSandbox = args.ContainsKey("is_sandbox") && Convert.ToBoolean(args["is_sandbox"]);
                                string gName = args.ContainsKey("group_name") ? args["group_name"].ToString() : (autoG ? "AI Generated" : (isSandbox ? "🧪 SANDBOX" : null));
                                toolResult = ExecuteCreateComponentGraph(args["components"] as JArray, args["connections"] as JArray, gName, isSandbox); 
                                if (args["components"] is JArray comps) addComp += comps.Count;
                                if (args["connections"] is JArray conns) addConn += conns.Count;
                            }
                            else if (funcName == "check_gh_errors") toolResult = ExecuteCheckGhErrors();
                            else if (funcName == "search_component_library") toolResult = ExecuteSearchComponentLibrary(args["keyword"].ToString());
                            else if (funcName == "set_gh_component_status") {
                                bool? preview = args.ContainsKey("preview") ? (bool?)args["preview"] : null;
                                bool? enabled = args.ContainsKey("enabled") ? (bool?)args["enabled"] : null;
                                toolResult = ExecuteSetGhComponentStatus(args["id"].ToString(), preview, enabled);
                            }
                            else if (funcName == "modify_gh_component_ports") {
                                toolResult = ExecuteModifyGhComponentPorts(args["id"].ToString(), (bool)args["is_input"], args["action"].ToString());
                            }
                            else if (funcName == "modify_gh_port_data") {
                                toolResult = ExecuteModifyGhPortData(args["id"].ToString(), Convert.ToBoolean(args["is_input"]), Convert.ToInt32(args["index"]), args["operation"].ToString());
                            }
                            else if (funcName == "manage_gh_groups") {
                                string gId = args.ContainsKey("group_id") ? args["group_id"].ToString() : null;
                                string gName = args.ContainsKey("name") ? args["name"].ToString() : null;
                                JArray idsArray = args.ContainsKey("ids") ? args["ids"] as JArray : null;
                                List<string> idsList = idsArray?.Select(v => v.ToString()).ToList();
                                toolResult = ExecuteManageGhGroups(args["action"].ToString(), idsList, gId, gName);
                            }
                            else if (funcName == "read_skill_file") {
                                toolResult = ExecuteReadSkillFile(args["file_name"].ToString());
                            }
                            else if (funcName == "read_reference_json") {
                                toolResult = ExecuteReadReferenceJson(args["file_name"].ToString());
                            }
                            else if (funcName == "create_gh_skill") {
                                toolResult = ExecuteCreateGhSkill(args["file_name"].ToString(), args["name"].ToString(), args["description"].ToString(), args["content"].ToString());
                            }
                            else if (funcName == "show_reference_options") {
                                var options = args["options"] as Newtonsoft.Json.Linq.JArray;
                                System.Collections.Generic.List<string> optList = new System.Collections.Generic.List<string>();
                                if (options != null) {
                                    foreach(var opt in options) optList.Add(opt.ToString());
                                }
                                AppendReferenceOptionsBubble(optList);
                                toolResult = "已向用户展示参考选项卡片，等待用户选择。";
                                _messages.Add(new { role = "tool", tool_call_id = callId, name = funcName, content = toolResult });
                                return new ApiResponse { Content = fullContent, Reasoning = fullReasoning };
                            }
                            else if (funcName == "apply_gh_sandbox") {
                                toolResult = ExecuteApplyGhSandbox();
                            }
                        }
                        catch (Exception ex) { toolResult = "Error: " + ex.Message; }

                        _messages.Add(new { role = "tool", tool_call_id = callId, name = funcName, content = toolResult });
                    }

                    if (addComp > 0 || delComp > 0 || addConn > 0 || delConn > 0) {
                        AppendColoredStatsMessage(addComp, delComp, addConn, delConn);
                    }

                    ct.ThrowIfCancellationRequested();
                    return await CallLLMAPI(apiKey, depth + 1, ct);
                }

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
                    catch { }

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

        private static string ExecuteGetGhComponents()
        {
            if (!_canvasChanged && _cachedCanvasState != null) {
                return _cachedCanvasState;
            }

            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }

                var graph = new JObject();
                graph["timestamp"] = DateTime.Now.ToString("HH:mm:ss");
                
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
                    components.Add(compJson);
                }
                graph["canvas_errors"] = globalErrors;
                graph["components"] = components;
                graph["groups"] = groups;
                
                result = graph.ToString(Formatting.None); // 使用压缩格式节省 Token
                _cachedCanvasState = result;
                _canvasChanged = false;
                UpdateCodeView();
            }));
            return result;
        }

        // ── 共享序列化 helper（不改变任何字段结构）──────────────────────────
        private static JObject BuildComponentJson(Grasshopper.Kernel.IGH_DocumentObject obj)
        {
            var j = new JObject();
            j["name"]     = obj.Name;
            j["nickname"] = obj.NickName;
            j["id"]       = obj.InstanceGuid.ToString();
            j["pivot"]    = new JObject { { "x", Math.Round(obj.Attributes.Pivot.X) }, { "y", Math.Round(obj.Attributes.Pivot.Y) } };
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
            return j;
        }

        // ── 摘要：仅 id/name/pivot + 首条报错，不含端口 ──────────────────────
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
                result = new JObject { ["components"] = arr, ["groups"] = groups }.ToString(Formatting.None);
            }));
            return result;
        }

        // ── 上下文：目标 + 前后各 depth 层邻居（完整详情）───────────────────
        private static string ExecuteGetComponentContext(string id, int depth = 1)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }
                if (!Guid.TryParse(id, out Guid guid)) { result = "Error: ID 格式错误。"; return; }
                var target = doc.FindObject(guid, true);
                if (target == null) { result = "Error: 找不到该电池。"; return; }

                var visited = new HashSet<Guid> { guid };
                void Traverse(Grasshopper.Kernel.IGH_DocumentObject o, int rem)
                {
                    if (rem <= 0) return;
                    if (o is Grasshopper.Kernel.IGH_Component c) {
                        foreach (var p in c.Params.Input)  foreach (var s in p.Sources)    { var nb = s.Attributes.GetTopLevel.DocObject; if (visited.Add(nb.InstanceGuid)) Traverse(nb, rem - 1); }
                        foreach (var p in c.Params.Output) foreach (var r in p.Recipients) { var nb = r.Attributes.GetTopLevel.DocObject; if (visited.Add(nb.InstanceGuid)) Traverse(nb, rem - 1); }
                    }
                    else if (o is Grasshopper.Kernel.IGH_Param pm) {
                        foreach (var s in pm.Sources)    { var nb = s.Attributes.GetTopLevel.DocObject; if (visited.Add(nb.InstanceGuid)) Traverse(nb, rem - 1); }
                        foreach (var r in pm.Recipients) { var nb = r.Attributes.GetTopLevel.DocObject; if (visited.Add(nb.InstanceGuid)) Traverse(nb, rem - 1); }
                    }
                }
                Traverse(target, depth);

                var arr = new JArray();
                foreach (var vid in visited) { var o = doc.FindObject(vid, true); if (o != null) arr.Add(BuildComponentJson(o)); }
                result = new JObject { ["context_components"] = arr }.ToString(Formatting.None);
            }));
            return result;
        }

        private static void UpdateCodeView()
        {
            if (!_isCodeVisible || _txtCodeView == null) return;

            if (!_isJsonMode)
            {
                string raw = ExecuteGetGhComponents();
                Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                    try {
                        // 尝试在 UI 上进行格式化展示，即使 AI 接收的是压缩版
                        var obj = JsonConvert.DeserializeObject(raw);
                        _txtCodeView.Text = JsonConvert.SerializeObject(obj, Formatting.Indented);
                    } catch {
                        _txtCodeView.Text = raw;
                    }
                }));
                return;
            }

            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) {
                    _txtCodeView.Text = "// 没有激活的画布";
                    return;
                }

                var graph = new JObject();
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

                _txtCodeView.Text = graph.ToString(Formatting.Indented);
            }));
        }

        private static string ExecuteAddGhComponent(string name, float x, float y, string label = null)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }

                var proxy = FindComponentProxy(name);

                if (proxy == null) { result = "Error: 找不到电池 '" + name + "'。"; return; }

                var obj = proxy.CreateInstance() as Grasshopper.Kernel.IGH_DocumentObject;
                obj.CreateAttributes();
                obj.Attributes.Pivot = new System.Drawing.PointF(x, y);
                if (!string.IsNullOrEmpty(label)) obj.NickName = label;
                obj.Attributes.ExpireLayout();
                
                                        doc.AddObject(obj, false);
                doc.NewSolution(false);
                result = "已添加 " + name + " (ID: " + obj.InstanceGuid + ")。";
                _canvasChanged = true;
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
                doc.NewSolution(false);
                result = "连线成功。";
                result += GetCanvasErrors(doc);
                _canvasChanged = true;
            }));
            return result;
        }

        private static string ExecuteRemoveGhComponent(string id, bool isSandbox = false)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }
                if (!Guid.TryParse(id, out Guid guid)) { result = "Error: ID 格式错误。"; return; }
                var obj = doc.FindObject(guid, true);
                if (obj == null) { result = "Error: 找不到电池。"; return; }

                if (isSandbox)
                {
                    if (obj is Grasshopper.Kernel.IGH_ActiveObject ao) ao.Locked = true;
                    if (obj is Grasshopper.Kernel.IGH_PreviewObject po) po.Hidden = true;
                    var recycleGroup = doc.Objects.OfType<Grasshopper.Kernel.Special.GH_Group>().FirstOrDefault(g => g.NickName == "♻️ RECYCLE");
                    if (recycleGroup == null) {
                        recycleGroup = new Grasshopper.Kernel.Special.GH_Group { NickName = "♻️ RECYCLE", Colour = System.Drawing.Color.FromArgb(100, 100, 100, 100) };
                        doc.AddObject(recycleGroup, false);
                    }
                    recycleGroup.AddObject(obj.InstanceGuid);
                    result = "沙箱模式：已禁用并移至回收站。";
                }
                else
                {
                    doc.RemoveObject(obj, false);
                    result = "删除成功。";
                }
                doc.NewSolution(false);
                _canvasChanged = true;
            }));
            return result;
        }

        private static string ExecuteApplyGhSandbox()
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }

                int deletedCount = 0;
                int appliedCount = 0;

                // 1. 处理回收站
                var recycleGroup = doc.Objects.OfType<Grasshopper.Kernel.Special.GH_Group>().FirstOrDefault(g => g.NickName == "♻️ RECYCLE");
                if (recycleGroup != null)
                {
                    var toDelete = new List<Grasshopper.Kernel.IGH_DocumentObject>();
                    foreach (var guid in recycleGroup.ObjectIDs)
                    {
                        var obj = doc.FindObject(guid, true);
                        if (obj != null) toDelete.Add(obj);
                    }
                    foreach (var obj in toDelete)
                    {
                        doc.RemoveObject(obj, false);
                        deletedCount++;
                    }
                    doc.RemoveObject(recycleGroup, false);
                }

                // 2. 处理沙箱组
                var sandboxGroups = doc.Objects.OfType<Grasshopper.Kernel.Special.GH_Group>().Where(g => g.NickName == "🧪 SANDBOX").ToList();
                foreach (var group in sandboxGroups)
                {
                    var toMove = new List<Grasshopper.Kernel.IGH_DocumentObject>();
                    foreach (var guid in group.ObjectIDs)
                    {
                        var obj = doc.FindObject(guid, true);
                        if (obj != null) toMove.Add(obj);
                    }
                    foreach (var obj in toMove)
                    {
                        obj.Attributes.Pivot = new System.Drawing.PointF(obj.Attributes.Pivot.X - 1500f, obj.Attributes.Pivot.Y);
                        obj.Attributes.ExpireLayout();
                        appliedCount++;
                    }
                    // 移除沙箱组
                    doc.RemoveObject(group, false);
                }

                if (deletedCount == 0 && appliedCount == 0)
                {
                    result = "没有找到沙箱修改。";
                }
                else
                {
                    result = $"沙箱应用成功！删除了 {deletedCount} 个回收站电池，应用了 {appliedCount} 个新电池。";
                    doc.NewSolution(false);
                    _canvasChanged = true;
                }
            }));
            return result;
        }

        private static string ExecuteSetGhComponentValue(string id, string value, double? min, double? max, int? decimals)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }
                if (!Guid.TryParse(id, out Guid guid)) { result = "Error: ID 格式错误。"; return; }
                var obj = doc.FindObject(guid, true);
                if (obj == null) { result = "Error: 找不到电池。"; return; }

                if (obj is Grasshopper.Kernel.Special.GH_NumberSlider slider) {
                    List<string> changes = new List<string>();
                    
                    if (value != null) {
                        if (decimal.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal decVal)) {
                            slider.Slider.Value = decVal;
                            changes.Add("值=" + decVal);
                        } else { result = "Error: 数值解析失败。"; return; }
                    }
                    
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
                    
                    if (changes.Count > 0) {
            doc.NewSolution(false);
                        result = "Slider 设置成功：" + string.Join("，", changes);
                        _canvasChanged = true;
                    } else {
                        result = "未指定任何属性更改。";
                    }
                } else if (obj is Grasshopper.Kernel.Special.GH_Panel panel) {
                    if (value == null) {
                        result = "Error: Panel 必须提供 value 参数。"; return;
                    }
                    panel.UserText = value;
                    doc.NewSolution(false);
                    result = "Panel 设置成功。";
                    _canvasChanged = true;
                } else result = "Error: 不是 Slider 或 Panel。";
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
                doc.NewSolution(false);
                result = "端口数据操作成功。";
                _canvasChanged = true;
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
                doc.NewSolution(false);
                result = "连线已断开。";
                result += GetCanvasErrors(doc);
                _canvasChanged = true;
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

        private static string ExecuteCreateComponentGraph(JArray components, JArray connections, string groupName = null, bool isSandbox = false)
        {
            string result = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) { result = "Error: 没有打开的画布。"; return; }

                float offsetX = isSandbox ? 1500f : 0f; // 沙箱模式偏移

                Dictionary<string, Grasshopper.Kernel.IGH_DocumentObject> createdObjs = new Dictionary<string, Grasshopper.Kernel.IGH_DocumentObject>();

                if (components != null) {
                    foreach (var c in components) {
                        string name = c["name"]?.ToString();
                        string label = c["label"]?.ToString();
                        float x = (c["x"]?.ToObject<float>() ?? 0) + offsetX;
                        float y = c["y"]?.ToObject<float>() ?? 0;
                        string val = c["value"]?.ToString();
                        double? min = c["min"]?.ToObject<double>();
                        double? max = c["max"]?.ToObject<double>();
                        int? decimals = c["decimals"]?.ToObject<int>();
                        string alias = c["alias_id"]?.ToString();

                        var proxy = FindComponentProxy(name);

                        if (proxy != null) {
                            var obj = proxy.CreateInstance() as Grasshopper.Kernel.IGH_DocumentObject;
                            obj.CreateAttributes();
                            obj.Attributes.Pivot = new System.Drawing.PointF(x, y);
                            if (!string.IsNullOrEmpty(label)) obj.NickName = label;
                            doc.AddObject(obj, false);

                            if (obj is Grasshopper.Kernel.Special.GH_NumberSlider s) {
                                if (min.HasValue) s.Slider.Minimum = (decimal)min.Value;
                                if (max.HasValue) s.Slider.Maximum = (decimal)max.Value;
                                if (decimals.HasValue) s.Slider.DecimalPlaces = Math.Max(0, Math.Min(10, decimals.Value));
                                if (!string.IsNullOrEmpty(val) && decimal.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal d)) 
                                    s.Slider.Value = d;
                            }
                            else if (obj is Grasshopper.Kernel.Special.GH_Panel p && !string.IsNullOrEmpty(val)) {
                                p.UserText = val;
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
                    group.Colour = isSandbox ? System.Drawing.Color.FromArgb(120, 255, 165, 0) : System.Drawing.Color.FromArgb(80, 100, 150, 250);
                    foreach (var obj in createdObjs.Values) group.AddObject(obj.InstanceGuid);
                    doc.AddObject(group, false);
                    group.ExpireSolution(true);
                }

                doc.NewSolution(false);
                result = "图谱构建完成。";
                result += GetCanvasErrors(doc);
                _canvasChanged = true;
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
                doc.NewSolution(false);
                result = "状态更新成功。";
                _canvasChanged = true;
            }));
            return result;
        }

        private static string ExecuteModifyGhComponentPorts(string id, bool isInput, string action)
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
                        var param = list[list.Count - 1];
                        if (vpc.CanRemoveParameter(isInput ? Grasshopper.Kernel.GH_ParameterSide.Input : Grasshopper.Kernel.GH_ParameterSide.Output, list.Count - 1)) {
                            comp.Params.UnregisterParameter(param);
                        } else { result = "Error: 无法删除该端口。"; return; }
                    }
                }
                
                vpc.VariableParameterMaintenance();
                comp.Params.OnParametersChanged();
                obj.ExpireSolution(true);
                doc.NewSolution(false);
                result = "端口修改成功。";
                _canvasChanged = true;
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
                doc.NewSolution(false);
                _canvasChanged = true;
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
            }));
        }

        private static void AppendSandboxBubble(string text)
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var container = new StackPanel { Margin = new Thickness(0, 0, 0, 20), HorizontalAlignment = HorizontalAlignment.Stretch };
                
                var header = new TextBlock { 
                    Text = "🧪 SANDBOX MODE", 
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0)),
                    FontSize = 11, 
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                container.Children.Add(header);

                var scroll = new ScrollViewer {
                    MaxHeight = 150,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var bubble = new Border {
                    Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(255, 165, 0)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10)
                };

                var tb = new TextBlock { 
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)), 
                    TextWrapping = TextWrapping.Wrap, 
                    FontSize = 12,
                    LineHeight = 18
                };
                ParseMarkdown(tb, text);
                bubble.Child = tb;
                scroll.Content = bubble;
                container.Children.Add(scroll);

                var btnApply = new Button {
                    Content = "确认应用修改",
                    Background = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                    Foreground = Brushes.White,
                    Padding = new Thickness(10, 5, 10, 5),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    FontWeight = FontWeights.Bold
                };
                btnApply.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
                    <ControlTemplate TargetType=""Button"" xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                        <Border Background=""{TemplateBinding Background}"" CornerRadius=""4"">
                            <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Margin=""{TemplateBinding Padding}""/>
                        </Border>
                    </ControlTemplate>");
                
                btnApply.Click += (s, e) => {
                    btnApply.IsEnabled = false;
                    btnApply.Content = "已应用";
                    btnApply.Background = new SolidColorBrush(Color.FromRgb(100, 100, 100));
                    if (_txtInput != null) _txtInput.Text = "确认应用修改";
                    BtnSend_Click(null, null);
                };

                container.Children.Add(btnApply);

                if (_thinkingBubble != null) {
                    _chatPanel.Children.Remove(_thinkingBubble);
                    _chatPanel.Children.Add(container);
                    _chatPanel.Children.Add(_thinkingBubble);
                } else {
                    _chatPanel.Children.Add(container);
                }
                _chatScroll.ScrollToEnd();
            }));
        }

        private static void AppendBubble(string text, bool isUser, bool showHeader = true)
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var container = new StackPanel { Margin = new Thickness(0, 0, 0, 20), HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left };
                
                if (showHeader)
                {
                    var header = new TextBlock { 
                        Text = isUser ? "YOU" : "KREITA", 
                        Foreground = isUser ? new SolidColorBrush(Color.FromRgb(150, 150, 150)) : new SolidColorBrush(Color.FromRgb(255, 200, 100)),
                        FontSize = 11, 
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 0, 0, 6),
                        HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left
                    };
                    container.Children.Add(header);
                }

                var bubble = new Border {
                    Padding = new Thickness(0, 5, 0, 10),
                    MaxWidth = 380
                };

                bubble.Child = BuildMarkdownPanel(text);
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

                var logPanel = new StackPanel { Margin = new Thickness(24, 4, 0, 0), Visibility = Visibility.Collapsed };
                
                var contentBorder = new Border {
                    Background = new SolidColorBrush(Color.FromRgb(25, 25, 25)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12)
                };

                var tb = new TextBlock { 
                    Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)), 
                    TextWrapping = TextWrapping.Wrap, 
                    FontSize = 13, 
                    LineHeight = 20
                };
                ParseMarkdown(tb, text);

                var scroll = new ScrollViewer { 
                    MaxHeight = 300, 
                    Content = tb, 
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    PanningMode = PanningMode.VerticalOnly
                };
                
                contentBorder.Child = scroll;
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

        private static void ParseMarkdown(TextBlock tb, string text)
        {
            tb.Inlines.Clear();
            string[] parts = System.Text.RegularExpressions.Regex.Split(text, @"(\*\*.*?\*\*|`.*?`|\*.*?\*)");
            foreach (var part in parts) {
                if (string.IsNullOrEmpty(part)) continue;
                if (part.StartsWith("**") && part.EndsWith("**")) {
                    tb.Inlines.Add(new System.Windows.Documents.Run(part.Substring(2, part.Length - 4)) { FontWeight = FontWeights.Bold });
                } else if (part.StartsWith("*") && part.EndsWith("*")) {
                    tb.Inlines.Add(new System.Windows.Documents.Run(part.Substring(1, part.Length - 2)) { FontStyle = FontStyles.Italic });
                } else if (part.StartsWith("`") && part.EndsWith("`")) {
                    var border = new Border { 
                        Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(3, 0, 3, 0),
                        Child = new TextBlock { Text = part.Substring(1, part.Length - 2), Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 100)), FontSize = 12 }
                    };
                    tb.Inlines.Add(new System.Windows.Documents.InlineUIContainer(border));
                } else {
                    tb.Inlines.Add(new System.Windows.Documents.Run(part));
                }
            }
        }

        private static StackPanel BuildMarkdownPanel(string text)
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical };
            if (string.IsNullOrEmpty(text)) return panel;

            var lines = text.Replace("\r\n", "\n").Split('\n');
            bool inCode = false;
            string codeLang = "";
            var code = new StringBuilder();

            Action flushCode = () => {
                var codeText = code.ToString().TrimEnd('\n');
                code.Clear();

                var header = new TextBlock {
                    Text = string.IsNullOrWhiteSpace(codeLang) ? "CODE" : codeLang.ToUpperInvariant(),
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 6)
                };

                var codeBlock = new TextBox {
                    Text = codeText,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.NoWrap,
                    AcceptsReturn = true,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 220
                };

                var inner = new StackPanel();
                inner.Children.Add(header);
                inner.Children.Add(codeBlock);

                panel.Children.Add(new Border {
                    Background = new SolidColorBrush(Color.FromRgb(22, 22, 22)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(48, 48, 48)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 8, 0, 10),
                    Child = inner
                });
            };

            foreach (string rawLine in lines) {
                string line = rawLine;
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

                var tb = new TextBlock {
                    Foreground = new SolidColorBrush(Color.FromRgb(235, 235, 235)),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14,
                    LineHeight = 22,
                    Margin = new Thickness(0, 2, 0, 2)
                };

                string trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) {
                    panel.Children.Add(new Border { Height = 6, Opacity = 0 });
                    continue;
                }

                if (trimmed.StartsWith("### ")) {
                    tb.FontSize = 15;
                    tb.FontWeight = FontWeights.SemiBold;
                    tb.Foreground = new SolidColorBrush(Color.FromRgb(255, 220, 150));
                    ParseMarkdown(tb, trimmed.Substring(4));
                } else if (trimmed.StartsWith("## ")) {
                    tb.FontSize = 16;
                    tb.FontWeight = FontWeights.SemiBold;
                    tb.Foreground = new SolidColorBrush(Color.FromRgb(255, 220, 150));
                    tb.Margin = new Thickness(0, 8, 0, 4);
                    ParseMarkdown(tb, trimmed.Substring(3));
                } else if (trimmed.StartsWith("# ")) {
                    tb.FontSize = 17;
                    tb.FontWeight = FontWeights.Bold;
                    tb.Foreground = new SolidColorBrush(Color.FromRgb(255, 220, 150));
                    tb.Margin = new Thickness(0, 8, 0, 4);
                    ParseMarkdown(tb, trimmed.Substring(2));
                } else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ")) {
                    tb.Inlines.Add(new System.Windows.Documents.Run("• ") { Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 100)) });
                    var inline = new TextBlock();
                    ParseMarkdown(inline, trimmed.Substring(2));
                    foreach (var item in inline.Inlines.ToList()) {
                        inline.Inlines.Remove(item);
                        tb.Inlines.Add(item);
                    }
                } else if (trimmed.StartsWith("> ")) {
                    tb.Foreground = new SolidColorBrush(Color.FromRgb(190, 190, 190));
                    ParseMarkdown(tb, trimmed.Substring(2));
                    panel.Children.Add(new Border {
                        BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                        BorderThickness = new Thickness(2, 0, 0, 0),
                        Padding = new Thickness(10, 2, 0, 2),
                        Margin = new Thickness(0, 4, 0, 4),
                        Child = tb
                    });
                    continue;
                } else {
                    ParseMarkdown(tb, line);
                }

                panel.Children.Add(tb);
            }

            if (inCode) flushCode();
            return panel;
        }

        private static void AppendReferenceOptionsBubble(System.Collections.Generic.List<string> options)
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var container = new StackPanel { Margin = new Thickness(0, 0, 0, 20), HorizontalAlignment = HorizontalAlignment.Left };
                
                var header = new TextBlock { 
                    Text = "选择参考描述", 
                    Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(2, 0, 0, 8)
                };
                container.Children.Add(header);

                var optionsPanel = new StackPanel { Orientation = Orientation.Vertical };
                
                foreach (var opt in options) {
                    var btn = new Button {
                        Content = opt,
                        Background = Brushes.Transparent,
                        Foreground = new SolidColorBrush(Color.FromRgb(235, 235, 235)),
                        BorderThickness = new Thickness(1),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(54, 54, 54)),
                        Padding = new Thickness(12, 10, 12, 10),
                        Margin = new Thickness(0, 0, 0, 7),
                        Cursor = Cursors.Hand,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        FontSize = 13
                    };
                    btn.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
                        <ControlTemplate TargetType=""Button"" xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                            <Border x:Name=""Bd"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}"" CornerRadius=""9"">
                                <ContentPresenter HorizontalAlignment=""{TemplateBinding HorizontalContentAlignment}"" VerticalAlignment=""Center"" Margin=""{TemplateBinding Padding}""/>
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property=""IsMouseOver"" Value=""True"">
                                    <Setter TargetName=""Bd"" Property=""Background"" Value=""#242424""/>
                                    <Setter TargetName=""Bd"" Property=""BorderBrush"" Value=""#666666""/>
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>");
                    btn.Click += (s, e) => {
                        SaveReference(opt);
                        AppendBubble($"已选择: {opt}", true);
                        container.IsEnabled = false;
                    };
                    optionsPanel.Children.Add(btn);
                }

                var customPanel = new Grid { Margin = new Thickness(0, 6, 0, 0) };
                customPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                customPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                
                var txtCustom = new TextBox {
                    Background = new SolidColorBrush(Color.FromRgb(22, 22, 22)),
                    Foreground = new SolidColorBrush(Color.FromRgb(235, 235, 235)),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(54, 54, 54)),
                    Padding = new Thickness(10, 8, 10, 8),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    CaretBrush = Brushes.White
                };
                Grid.SetColumn(txtCustom, 0);
                customPanel.Children.Add(txtCustom);

                var btnCustom = new Button {
                    Content = "确定",
                    Background = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                    Foreground = Brushes.Black,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(16, 0, 16, 0),
                    Margin = new Thickness(5, 0, 0, 0),
                    Cursor = Cursors.Hand,
                    FontWeight = FontWeights.SemiBold
                };
                Grid.SetColumn(btnCustom, 1);
                btnCustom.Click += (s, e) => {
                    string customText = txtCustom.Text.Trim();
                    if (!string.IsNullOrEmpty(customText)) {
                        SaveReference(customText);
                        AppendBubble($"已选择: {customText}", true);
                        container.IsEnabled = false;
                    }
                };
                customPanel.Children.Add(btnCustom);
                
                optionsPanel.Children.Add(customPanel);

                var bubble = new Border {
                    Background = new SolidColorBrush(Color.FromRgb(18, 18, 18)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(14),
                    MaxWidth = 380,
                    Child = optionsPanel
                };
                
                container.Children.Add(bubble);

                if (_thinkingBubble != null) {
                    _chatPanel.Children.Remove(_thinkingBubble);
                    _chatPanel.Children.Add(container);
                    _chatPanel.Children.Add(_thinkingBubble);
                } else {
                    _chatPanel.Children.Add(container);
                }
                _chatScroll.ScrollToEnd();
            }));
        }

        private static void SaveReference(string description)
        {
            string canvasJson = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                canvasJson = ExecuteGetGhComponents();
            }));

            System.Threading.Tasks.Task.Run(() => {
                try {
                    if (string.IsNullOrEmpty(canvasJson)) {
                        AppendSystemMessage("保存参考失败: 无法获取画布内容", true);
                        return;
                    }
                    
                    string refPath = GetReferenceDirectory();
                    if (!System.IO.Directory.Exists(refPath)) System.IO.Directory.CreateDirectory(refPath);
                    string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                    string fileName = "ref_" + timestamp + ".json";
                    string filePath = System.IO.Path.Combine(refPath, fileName);
                    System.IO.File.WriteAllText(filePath, canvasJson, System.Text.Encoding.UTF8);

                    string result = UpdateReferenceIndexSkill(description, fileName);
                    
                    AppendSystemMessage($"参考已保存：{fileName}\n{result}");
                } catch (Exception ex) {
                    AppendSystemMessage($"保存参考失败: {ex.Message}", true);
                }
            });
        }

        private static string GetProjectRootDirectory()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "ADDGH.csproj")))
                {
                    return System.IO.Directory.GetParent(dir)?.FullName ?? dir;
                }
                dir = System.IO.Directory.GetParent(dir)?.FullName;
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
                "description: 在处理建模、修改画布、生成 GH 逻辑或判断可复用方案时应主动检查；若条目描述与当前任务相关，调用 read_reference_json 读取对应 JSON。\n" +
                "---\n\n" +
                "# Reference Index\n\n" +
                "使用流程：\n" +
                "1. 在开始建模、修改画布或设计 GH 逻辑前，主动浏览下面的参考条目。\n" +
                "2. 如果某个描述与当前任务相关，调用 `read_reference_json`，传入对应 `file_name`。\n" +
                "3. 读取 JSON 后，基于其中的电池、连线和建模逻辑复用或改造方案。\n\n" +
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

            string content = System.IO.File.Exists(indexPath)
                ? System.IO.File.ReadAllText(indexPath, Encoding.UTF8)
                : GetReferenceIndexTemplate();

            if (!content.Contains($"reference/{safeFileName}"))
            {
                if (!content.Contains("## References")) content = GetReferenceIndexTemplate() + content;
                if (!content.EndsWith("\n")) content += "\n";
                content += FormatReferenceEntry(description, safeFileName);
                System.IO.File.WriteAllText(indexPath, content, Encoding.UTF8);
            }

            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                UpdateSkillLibraryUI();
            }));

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
                    _txtInput.Text = $"请参考 reference_index.md 中的 {entry.FileName}，读取对应参考 JSON 后复用建模逻辑。";
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
                AppendSystemMessage($"删除参考失败: {ex.Message}", true);
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
            if (_btnSend != null) {
                _btnSend.Content = "■";
                var bg = _btnSend.Template.FindName("bg", _btnSend) as Border;
                if (bg != null) bg.CornerRadius = new CornerRadius(8);
                var cp = _btnSend.Template.FindName("cp", _btnSend) as ContentPresenter;
                if (cp != null) cp.Margin = new Thickness(0);
            }
            _txtInput.Text = "";

            if (_messages.Count == 0) {
                string skillsSummary = GetSkillsSummary();
                _messages.Add(new { role = "system", content = SYSTEM_PROMPT + skillsSummary });
            }

            _messages.Add(new { role = "user", content = actualPrompt });
            AppendBubble(displayText, true);

            _currentBase64Image = null;
            _txtImageAttached.Visibility = Visibility.Collapsed;
            _btnClearImage.Visibility = Visibility.Collapsed;

            _cts = new System.Threading.CancellationTokenSource();
            string apiKey = Grasshopper.Instances.Settings.GetValue("AI_API_Key", "");

            try {
                ShowThinkingAnimation();
                await CallLLMAPI(apiKey, 0, _cts.Token);
            } catch (OperationCanceledException) {
                AppendSystemMessage("已停止生成。");
            } catch (Exception ex) {
                AppendSystemMessage("Error: " + ex.Message, true);
            } finally {
                HideThinkingAnimation();
                _isGenerating = false;
                if (_btnSend != null) _btnSend.Content = "➤";
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

        private static void AppendSystemMessage(string text, bool isError = false)
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var tb = new TextBlock { 
                    Text = text, 
                    Foreground = isError ? Brushes.Tomato : Brushes.Gray, 
                    FontSize = 12, 
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 15)
                };
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
            } catch { }
            return "";
        }
    }
}
