with open('ADDGH/ChatWindow.cs', 'r', encoding='utf-8') as f:
    content = f.read()

read_skill_method = '''        private static string ExecuteReadSkillFile(string fileName)
        {
            try {
                string skillsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Grasshopper", "Libraries", "skills");
                if (!System.IO.Directory.Exists(skillsPath)) {
                    skillsPath = System.IO.Path.Combine(Environment.CurrentDirectory, "skills");
                }
                if (!fileName.EndsWith(".md")) fileName += ".md";
                string filePath = System.IO.Path.Combine(skillsPath, fileName);
                
                if (System.IO.File.Exists(filePath)) {
                    return System.IO.File.ReadAllText(filePath);
                }
                return $"Error: 找不到技能文件 {fileName}";
            } catch (Exception ex) {
                return "Error: " + ex.Message;
            }
        }'''

content = content.replace('        private static string ExecuteCreateGhSkill', read_skill_method + '\n\n        private static string ExecuteCreateGhSkill')

with open('ADDGH/ChatWindow.cs', 'w', encoding='utf-8') as f:
    f.write(content)
