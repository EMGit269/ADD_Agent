import re
with open('ADDGH/ChatWindow.cs', 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('⏹', '停止')

with open('ADDGH/ChatWindow.cs', 'w', encoding='utf-8') as f:
    f.write(content)
print("Replaced stop icon")
