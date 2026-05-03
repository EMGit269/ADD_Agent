import re

with open('ADDGH/ChatWindow.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Update XAML to name ContentPresenter
content = content.replace(
    '<ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" Margin="2,0,0,0"/>',
    '<ContentPresenter x:Name="cp" HorizontalAlignment="Center" VerticalAlignment="Center" Margin="2,0,0,0"/>'
)

# 2. Update BtnSend_Click start
old_start = '''            _isGenerating = true;
            if (_btnSend != null) _btnSend.Content = "■";'''
new_start = '''            _isGenerating = true;
            if (_btnSend != null) {
                _btnSend.Content = "■";
                var bg = _btnSend.Template.FindName("bg", _btnSend) as Border;
                if (bg != null) bg.CornerRadius = new CornerRadius(8);
                var cp = _btnSend.Template.FindName("cp", _btnSend) as ContentPresenter;
                if (cp != null) cp.Margin = new Thickness(0);
            }'''
content = content.replace(old_start, new_start)

# 3. Update BtnSend_Click finally
old_finally = '''            finally {
                HideThinkingAnimation();
                _isGenerating = false;
                if (_btnSend != null) _btnSend.Content = "➤";
            }'''
new_finally = '''            finally {
                HideThinkingAnimation();
                _isGenerating = false;
                if (_btnSend != null) {
                    _btnSend.Content = "➤";
                    var bg = _btnSend.Template.FindName("bg", _btnSend) as Border;
                    if (bg != null) bg.CornerRadius = new CornerRadius(18);
                    var cp = _btnSend.Template.FindName("cp", _btnSend) as ContentPresenter;
                    if (cp != null) cp.Margin = new Thickness(2, 0, 0, 0);
                }
            }'''
content = content.replace(old_finally, new_finally)

with open('ADDGH/ChatWindow.cs', 'w', encoding='utf-8') as f:
    f.write(content)
print('Updated BtnSend logic')
