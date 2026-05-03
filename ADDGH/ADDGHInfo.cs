using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace ADDGH
{
    public class ADDGHInfo : GH_AssemblyInfo
    {
        public override string Name => "Magpie";
        
        // 插件图标，这里暂为空
        public override Bitmap Icon => null;
        
        public override string Description => "在 Grasshopper 中直接接入 Magpie AI 大模型";
        
        // 插件的唯一标识符
        public override Guid Id => new Guid("A3B5882B-6C2E-4DE9-97CD-1B2F99C8D405");
        
        public override string AuthorName => "AI Agent";
        
        public override string AuthorContact => "auto-generated";
    }
}
