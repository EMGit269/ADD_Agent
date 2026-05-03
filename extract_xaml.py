with open('ADDGH/ChatWindow.cs', 'r', encoding='utf-8') as f:
    content = f.read()

start = content.find('string xaml = @"')
end = content.find('";\n            try', start)
xaml = content[start+16:end]

with open('ADDGH/ui.xaml', 'w', encoding='utf-8') as f:
    f.write(xaml.replace('""', '"'))
