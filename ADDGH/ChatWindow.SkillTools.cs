using System;
using System.Text;

namespace ADDGH
{
    public static partial class ChatWindow
    {
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
    }
}
