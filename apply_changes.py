import re

with open('ADDGH/ChatWindow.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Remove _btnStop field
content = re.sub(r'\s*private static Button _btnStop;', '', content)

# 2. Remove _btnStop FindName and Click
content = re.sub(r'\s*_btnStop = \(Button\)_window\.FindName\("BtnStop"\);', '', content)
content = re.sub(r'\s*if \(_btnStop != null\) \{\s*_btnStop\.Click \+= \(s, e\) => \{\s*_cts\?\.Cancel\(\);\s*_btnStop\.Visibility = Visibility\.Collapsed;\s*\};\s*\}', '', content)

# 3. Update BtnSend_Click logic
content = re.sub(r'if \(_isGenerating\) return;', r'if (_isGenerating) { _cts?.Cancel(); return; }', content)

content = re.sub(r'_isGenerating = true;\s*(?:if \(_btnSend != null\) )?_btnSend\.IsEnabled = false;\s*(?:if \(_btnStop != null\) )?_btnStop\.Visibility = Visibility\.Visible;', 
                 r'_isGenerating = true;\n            if (_btnSend != null) _btnSend.Content = "■";', content)

content = re.sub(r'finally\s*\{\s*HideThinkingAnimation\(\);\s*_isGenerating = false;\s*(?:if \(_btnSend != null\) )?_btnSend\.IsEnabled = true;\s*(?:if \(_btnStop != null\) )?_btnStop\.Visibility = Visibility\.Collapsed;\s*\}',
                 r'finally {\n                HideThinkingAnimation();\n                _isGenerating = false;\n                if (_btnSend != null) _btnSend.Content = "➤";\n            }', content)

# 4. Remove emoji from Expander Header in UpdateLibraryUI
content = content.replace('Header = $"📁 {group.Key}  ({group.Count()})"', 'Header = $"{group.Key}  ({group.Count()})"')

# 5. Modify XAML
start = content.find('string xaml = @"')
end = content.find('";\n            try', start)
xaml = content[start+16:end]

# Remove BtnStop from XAML
xaml = re.sub(r'<Button x:Name="BtnStop"[^\>]+/>\s*', '', xaml)

# Add Expander Style to Window.Resources
expander_style = '''
        <Style TargetType="Expander">
            <Setter Property="Foreground" Value="#EEE"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Expander">
                        <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
                            <DockPanel>
                                <ToggleButton x:Name="HeaderSite" DockPanel.Dock="Top" IsChecked="{Binding IsExpanded, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}" Content="{TemplateBinding Header}">
                                    <ToggleButton.Template>
                                        <ControlTemplate TargetType="ToggleButton">
                                            <Border Background="Transparent" Padding="5">
                                                <StackPanel Orientation="Horizontal">
                                                    <TextBlock x:Name="Icon" Text="▶" FontSize="10" Foreground="#888" Width="15" VerticalAlignment="Center"/>
                                                    <ContentPresenter VerticalAlignment="Center"/>
                                                </StackPanel>
                                            </Border>
                                            <ControlTemplate.Triggers>
                                                <Trigger Property="IsChecked" Value="True">
                                                    <Setter TargetName="Icon" Property="Text" Value="▼"/>
                                                </Trigger>
                                                <Trigger Property="IsMouseOver" Value="True">
                                                    <Setter TargetName="Icon" Property="Foreground" Value="#FFF"/>
                                                </Trigger>
                                            </ControlTemplate.Triggers>
                                        </ControlTemplate>
                                    </ToggleButton.Template>
                                </ToggleButton>
                                <ContentPresenter x:Name="ExpandSite" Visibility="Collapsed" DockPanel.Dock="Bottom"/>
                            </DockPanel>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsExpanded" Value="True">
                                <Setter TargetName="ExpandSite" Property="Visibility" Value="Visible"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>'''

if '<Style TargetType="Expander">' not in xaml:
    xaml = xaml.replace('</Window.Resources>', expander_style + '\n    </Window.Resources>')

content = content[:start+16] + xaml + content[end:]

with open('ADDGH/ChatWindow.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print('Modifications applied')
