using System;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;

namespace ADDGH
{
    // 利用 GH_AssemblyPriority 在 Grasshopper 启动时执行代码
    public class MenuIntegration : GH_AssemblyPriority
    {
        public override GH_LoadingInstruction PriorityLoad()
        {
            if (Grasshopper.Instances.DocumentEditor != null)
            {
                AddADDMenu(Grasshopper.Instances.DocumentEditor);
            }
            else
            {
                Grasshopper.Instances.CanvasCreated += Instances_CanvasCreated;
            }
            return GH_LoadingInstruction.Proceed;
        }

        private void Instances_CanvasCreated(GH_Canvas canvas)
        {
            Grasshopper.Instances.CanvasCreated -= Instances_CanvasCreated;
            AddADDMenu(Grasshopper.Instances.DocumentEditor);
        }

        private void AddADDMenu(GH_DocumentEditor editor)
        {
            if (editor == null) return;
            
            foreach(ToolStripItem item in editor.MainMenuStrip.Items)
            {
                if (item.Text == "Magpie") return;
            }
            
            ToolStripMenuItem aiMenu = new ToolStripMenuItem("Magpie");
            aiMenu.Click += (s, e) => {
                ChatWindow.Show();
            };
            
            editor.MainMenuStrip.Items.Add(aiMenu);
        }
    }
}
