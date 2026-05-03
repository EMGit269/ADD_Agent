with open('ADDGH/ChatWindow.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# Fix the syntax error in ChatWindow.cs
# Find the end of the file and check for syntax errors
lines = content.split('\n')
for i, line in enumerate(lines):
    if 'private static string ExecuteCreateGhSkill' in line:
        print(f'Found ExecuteCreateGhSkill at line {i+1}')
        # Check surrounding lines
        for j in range(max(0, i-5), min(len(lines), i+5)):
            print(f'{j+1}: {lines[j]}')
        break
