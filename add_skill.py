import re

with open('ADDGH/ChatWindow.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Update XAML RowDefinitions
row_defs_pattern = r'(<RowDefinition Height="0" x:Name="LibraryRow"/>\s*</Grid\.RowDefinitions>)'
row_defs_replacement = r'<RowDefinition Height="0" x:Name="LibraryRow"/>\n                        <RowDefinition Height="0" x:Name="SkillRow"/>\n                    </Grid.RowDefinitions>'
content = re.sub(row_defs_pattern, row_defs_replacement, content)

# 2. Update Input Area Grid Columns
col_defs_pattern = r'(<ColumnDefinition Width="Auto"/>\s*<ColumnDefinition Width="\*"/>\s*<ColumnDefinition Width="Auto"/>\s*</Grid\.ColumnDefinitions>)'
col_defs_replacement = r'<ColumnDefinition Width="Auto"/>\n                            <ColumnDefinition Width="Auto"/>\n                            <ColumnDefinition Width="*"/>\n                            <ColumnDefinition Width="Auto"/>\n                        </Grid.ColumnDefinitions>'
content = re.sub(col_defs_pattern, col_defs_replacement, content)

# 3. Add BtnToggleSkill
btn_lib_pattern = r'(<Button x:Name="BtnToggleLibrary".*?/>)'
btn_skill = r'\1\n                        <Button x:Name="BtnToggleSkill" Grid.Column="4" Content="技能库" Foreground="#A0A0A0" Background="Transparent" BorderThickness="0" FontSize="14" Cursor="Hand" ToolTip="展开/收起技能库" Margin="8,0,0,0"/>'
content = re.sub(btn_lib_pattern, btn_skill, content)

# 4. Update BtnSend Grid.Column
btn_send_pattern = r'(<Button x:Name="BtnSend" Grid\.Column=")5(".*?>)'
content = re.sub(btn_send_pattern, r'\g<1>6\2', content)

# 5. Add SkillPanel XAML
lib_panel_pattern = r'(</Border>\s*<!-- End Chat Area Grid -->)'
skill_panel = r'''            </Border>

            <!-- 技能库扩展区 -->
            <Border Grid.Row="5" Background="#111111" BorderBrush="#333333" BorderThickness="0,1,0,0" x:Name="SkillPanel" CornerRadius="0,0,16,16" Visibility="Collapsed">
                <Grid Margin="15">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*" />
                    </Grid.RowDefinitions>
                    
                    <Grid Margin="0,0,0,12">
                        <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                            <TextBlock Text="技能库" Foreground="#EEE" FontSize="15" FontWeight="Bold"/>
                            <TextBlock x:Name="TxtSkillCount" Text="" Foreground="#555" FontSize="11" Margin="8,0,0,0" VerticalAlignment="Bottom"/>
                        </StackPanel>
                        <Button x:Name="BtnRefreshSkill" Content="刷新" HorizontalAlignment="Right" Foreground="#A0A0A0" Background="Transparent" BorderThickness="0" FontSize="14" Cursor="Hand" ToolTip="重新加载技能库"/>
                    </Grid>

                    <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto" Height="350">
                        <StackPanel x:Name="SkillContent" />
                    </ScrollViewer>
                </Grid>
\1'''
content = re.sub(lib_panel_pattern, skill_panel, content)

# 6. Add C# UI Fields
fields_pattern = r'(private static StackPanel _libraryContent;\s*private static TextBlock _txtLibCount;)'
fields_replacement = r'\1\n        private static RowDefinition _skillRow;\n        private static StackPanel _skillContent;\n        private static TextBlock _txtSkillCount;\n        private static bool _isSkillVisible = false;'
content = re.sub(fields_pattern, fields_replacement, content)

# 7. Add C# UI Bindings
bindings_pattern = r'(var btnRefreshLib = \(Button\)_window\.FindName\("BtnRefreshLib"\);)'
bindings_replacement = r'''\1
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
            }'''
content = re.sub(bindings_pattern, bindings_replacement, content)

# 8. Add C# Tool Definition
tool_def_pattern = r'(new \{\s*type = "function",\s*function = new \{\s*name = "read_skill_file",)'
tool_def_replacement = r'''new {
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
                        \1'''
content = re.sub(tool_def_pattern, tool_def_replacement, content)

# 9. Add C# Tool Execution
tool_exec_pattern = r'(else if \(funcName == "read_skill_file"\) \{\s*toolResult = ExecuteReadSkillFile\(args\["file_name"\]\.ToString\(\)\);\s*\})'
tool_exec_replacement = r'''\1
                            else if (funcName == "create_gh_skill") {
                                toolResult = ExecuteCreateGhSkill(args["file_name"].ToString(), args["name"].ToString(), args["description"].ToString(), args["content"].ToString());
                            }'''
content = re.sub(tool_exec_pattern, tool_exec_replacement, content)

# 10. Add C# Methods UpdateSkillLibraryUI and ExecuteCreateGhSkill
methods_pattern = r'(private static string ExecuteReadSkillFile\(string fileName\)\s*\{.*?\n        \})'
methods_replacement = r'''\1

        private static string ExecuteCreateGhSkill(string fileName, string name, string description, string content)
        {
            try {
                string skillsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Grasshopper", "Libraries", "skills");
                if (!System.IO.Directory.Exists(skillsPath)) {
                    skillsPath = System.IO.Path.Combine(Environment.CurrentDirectory, "skills");
                    if (!System.IO.Directory.Exists(skillsPath)) System.IO.Directory.CreateDirectory(skillsPath);
                }
                if (!fileName.EndsWith(".md")) fileName += ".md";
                string filePath = System.IO.Path.Combine(skillsPath, fileName);
                
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
                string skillsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Grasshopper", "Libraries", "skills");
                if (!System.IO.Directory.Exists(skillsPath)) skillsPath = System.IO.Path.Combine(Environment.CurrentDirectory, "skills");
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
        }'''
# Escape backslashes in replacement string so re.sub doesn't complain about \s
methods_replacement = methods_replacement.replace('\\', '\\\\')
content = re.sub(methods_pattern, methods_replacement, content, flags=re.DOTALL)

with open('ADDGH/ChatWindow.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print('Skill feature added successfully')
