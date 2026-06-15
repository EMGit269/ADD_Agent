using System;

namespace ADDGH.Agent
{
    public sealed class WorkflowRouter
    {
        public WorkflowRoute Route(AgentTurnContext context)
        {
            if (context == null) return WorkflowRoute.Fallback();

            string text = (context.UserText ?? "").Trim();
            string lower = text.ToLowerInvariant();

            if (IsSelfTraining(context))
                return SelfTrainingRoute();

            if (context.HasImageAttachments)
                return RouteImageTurn(context, lower);

            if (ContainsAny(lower, "联网", "网页", "搜索", "最新", "官方文档", "api 查询", "api查证", "web", "url", "http"))
                return WebResearchRoute();

            if (ContainsAny(lower, "导入参考", "复用参考", "import reference", "import_reference", "参考画布"))
                return ReferenceImportRoute();

            if (ContainsAny(lower, "查参考", "读取参考", "reference", "参考"))
                return ReferenceLookupRoute();

            if (ContainsAny(lower, "skill", "技能", "经验", "沉淀", "训练"))
                return SkillLookupRoute();

            if (ContainsAny(lower, "c#", "csharp", "script", "脚本", "编译", "报错", "error", "exception"))
            {
                if (ContainsAny(lower, "新建", "创建", "生成", "create", "add"))
                    return CSharpCreateRoute();
                return CSharpFixRoute();
            }

            if (ContainsAny(lower, "修改", "调整", "修复", "优化", "继续", "改", "delete", "remove", "edit"))
                return GrasshopperModifyRoute(context);

            if (ContainsAny(lower, "建模", "生成", "创建", "做一个", "画一个", "参数化", "grasshopper", "gh"))
                return GrasshopperCreateRoute(context);

            return GeneralRoute(context);
        }

        private static WorkflowRoute RouteImageTurn(AgentTurnContext context, string lower)
        {
            if (ContainsAny(lower, "生成图片", "创作图片", "改图", "图生图", "ai 图片", "ai图片", "image generation"))
            {
                var route = WorkflowRoute.Create(WorkflowIntent.AiImageGeneration, 0.92, "User attached image(s) and asked for AI image creation or editing.");
                route.RequiredTools.Add("create_ai_image");
                route.ContextPacks.Add("image-input");
                return route;
            }

            if (ContainsAny(lower, "建模", "还原", "复刻", "做成", "生成gh", "生成 gh", "grasshopper", "参数化"))
            {
                var route = WorkflowRoute.Create(WorkflowIntent.VisualModeling, 0.86, "User attached image(s) and requested Grasshopper/Rhino modeling from visual reference.");
                route.AllowsCanvasMutation = true;
                route.RequiresVisualReview = true;
                route.RequiredTools.Add("ensure_gh_canvas");
                route.RequiredTools.Add("get_gh_components");
                route.OptionalTools.Add("create_component_graph");
                route.OptionalTools.Add("create_csharp_script_component");
                route.ContextPacks.Add("image-input");
                route.ContextPacks.Add("canvas-state");
                return route;
            }

            var unclear = string.IsNullOrWhiteSpace(context.UserText);
            var visual = WorkflowRoute.Create(
                WorkflowIntent.VisualUnderstanding,
                unclear ? 0.55 : 0.76,
                unclear
                    ? "Image attachment without a clear text goal; ask for intent before mutating canvas."
                    : "Image attachment appears to need explanation, diagnosis, or discussion rather than canvas mutation.");
            visual.ContextPacks.Add("image-input");
            visual.ShouldAskClarifyingQuestion = unclear;
            return visual;
        }

        private static WorkflowRoute SelfTrainingRoute()
        {
            var route = WorkflowRoute.Create(WorkflowIntent.SelfTraining, 0.95, "Current agent mode is self-training.");
            route.AllowsCanvasMutation = true;
            route.AllowsSkillWrite = true;
            route.RequiresVisualReview = true;
            route.RequiredTools.Add("ensure_gh_canvas");
            route.RequiredTools.Add("get_gh_components");
            route.OptionalTools.Add("create_component_graph");
            route.OptionalTools.Add("create_csharp_script_component");
            route.OptionalTools.Add("edit_csharp_script_component");
            route.OptionalTools.Add("create_gh_skill");
            route.ContextPacks.Add("self-training");
            route.ContextPacks.Add("skills-index");
            return route;
        }

        private static WorkflowRoute WebResearchRoute()
        {
            var route = WorkflowRoute.Create(WorkflowIntent.WebResearch, 0.82, "User requested web/latest/API verification.");
            route.RequiredTools.Add("web_research");
            route.ContextPacks.Add("web-research");
            return route;
        }

        private static WorkflowRoute ReferenceImportRoute()
        {
            var route = WorkflowRoute.Create(WorkflowIntent.ReferenceImport, 0.82, "User requested importing or reusing a saved reference canvas.");
            route.AllowsCanvasMutation = true;
            route.RequiredTools.Add("read_reference_json");
            route.RequiredTools.Add("import_reference_gh");
            route.ContextPacks.Add("reference-index");
            route.ContextPacks.Add("canvas-state");
            return route;
        }

        private static WorkflowRoute ReferenceLookupRoute()
        {
            var route = WorkflowRoute.Create(WorkflowIntent.ReferenceLookup, 0.74, "User mentioned references; inspect relevant reference entries before importing.");
            route.RequiredTools.Add("read_reference_json");
            route.OptionalTools.Add("show_reference_options");
            route.ContextPacks.Add("reference-index");
            return route;
        }

        private static WorkflowRoute SkillLookupRoute()
        {
            var route = WorkflowRoute.Create(WorkflowIntent.SkillLookup, 0.70, "User mentioned skills or reusable experience.");
            route.RequiredTools.Add("read_skill_file");
            route.ContextPacks.Add("skills-index");
            return route;
        }

        private static WorkflowRoute CSharpCreateRoute()
        {
            var route = WorkflowRoute.Create(WorkflowIntent.CSharpScriptCreate, 0.78, "User request likely needs a new C# Script component.");
            route.AllowsCanvasMutation = true;
            route.RequiredTools.Add("create_csharp_script_component");
            route.OptionalTools.Add("get_gh_components");
            route.OptionalTools.Add("recompute_gh_canvas");
            route.ContextPacks.Add("canvas-state");
            route.ContextPacks.Add("skills-index");
            return route;
        }

        private static WorkflowRoute CSharpFixRoute()
        {
            var route = WorkflowRoute.Create(WorkflowIntent.CSharpScriptFix, 0.78, "User request mentions C# Script, errors, or script repair.");
            route.AllowsCanvasMutation = true;
            route.RequiredTools.Add("get_gh_components");
            route.RequiredTools.Add("edit_csharp_script_component");
            route.OptionalTools.Add("recompute_gh_canvas");
            route.ContextPacks.Add("canvas-state");
            route.ContextPacks.Add("skills-index");
            return route;
        }

        private static WorkflowRoute GrasshopperModifyRoute(AgentTurnContext context)
        {
            var route = WorkflowRoute.Create(WorkflowIntent.GrasshopperModify, 0.72, "User request appears to modify an existing Grasshopper canvas.");
            route.AllowsCanvasMutation = true;
            route.RequiredTools.Add("get_gh_components");
            route.OptionalTools.Add("create_component_graph");
            route.OptionalTools.Add("set_gh_component_value");
            route.OptionalTools.Add("connect_gh_components");
            route.OptionalTools.Add("remove_gh_component");
            route.ContextPacks.Add("canvas-state");
            route.ContextPacks.Add("skills-index");
            if (!context.CanvasAvailable || context.CanvasLikelyEmpty)
                route.Reason += " Canvas may be empty; verify before assuming existing components.";
            return route;
        }

        private static WorkflowRoute GrasshopperCreateRoute(AgentTurnContext context)
        {
            var route = WorkflowRoute.Create(WorkflowIntent.GrasshopperCreate, 0.70, "User request appears to create a Grasshopper definition.");
            route.AllowsCanvasMutation = true;
            route.RequiredTools.Add("ensure_gh_canvas");
            route.OptionalTools.Add("create_component_graph");
            route.OptionalTools.Add("create_csharp_script_component");
            route.OptionalTools.Add("get_gh_components");
            route.ContextPacks.Add("canvas-state");
            route.ContextPacks.Add("skills-index");
            return route;
        }

        private static WorkflowRoute GeneralRoute(AgentTurnContext context)
        {
            var route = WorkflowRoute.Create(WorkflowIntent.GeneralChat, 0.45, "No high-confidence specialized workflow matched.");
            route.ContextPacks.Add("general");
            if (context.CanvasAvailable && !context.CanvasLikelyEmpty)
                route.OptionalTools.Add("get_gh_components");
            route.OptionalTools.Add("read_skill_file");
            return route;
        }

        private static bool IsSelfTraining(AgentTurnContext context)
        {
            return string.Equals(context.AgentMode, "SelfTrain", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsAny(string lower, params string[] needles)
        {
            if (string.IsNullOrEmpty(lower) || needles == null) return false;
            foreach (var needle in needles)
            {
                if (!string.IsNullOrEmpty(needle) && lower.IndexOf(needle.ToLowerInvariant(), StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }
    }
}
