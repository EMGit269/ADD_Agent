with open('ADDGH/ChatWindow.cs', 'r', encoding='utf-8') as f:
    content = f.read()

start_idx = content.find('<Style TargetType="Expander">')
end_idx = content.find('</Style>', start_idx) + 8

if start_idx != -1:
    old_style = content[start_idx:end_idx]
    new_style = old_style.replace('"', '""')
    content = content.replace(old_style, new_style)
    with open('ADDGH/ChatWindow.cs', 'w', encoding='utf-8') as f:
        f.write(content)
    print('Fixed quotes in Expander style')
