using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ADDGH
{
    public static partial class ChatWindow
    {
        private static object[] BuildToolDefinitionsForCurrentMode()
        {
            object[] toolDefinitions = new object[]
            {
                new {
                    type = "function",
                    function = new {
                        name = "create_ai_image",
                        description = "当用户明确要求文生图、参考图生成、图片风格改写或编辑现有图片时，调用图片创作工具。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                prompt = new { type = "string", description = "图片创作提示词" },
                                intent = new { type = "string", description = "generate 或 edit" },
                                use_uploaded_images = new { type = "boolean", description = "是否使用当前轮上传的图片作为参考或编辑输入" },
                                aspect_ratio = new { type = "string", description = "可选宽高比，例如 1:1、16:9、3:4" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语" }
                            },
                            required = new[] { "prompt", "intent", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "ensure_gh_canvas",
                        description = "确保当前存在可用的 Grasshopper 画布。若未检测到可用画布，则新建一个空白 GH 画布并设为当前画布。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "summary" }
                        }
                    }
                },
                GetCreateCSharpScriptComponentToolDefinition(),
                GetEditCSharpScriptComponentToolDefinition(),
                GetCreateScriptComponentGraphToolDefinition(),
                new {
                    type = "function",
                    function = new {
                        name = "get_gh_components",
                        description = "获取当前 Grasshopper 画布的完整 JSON：rhino_units 当前 Rhino 模型单位/公差、电池、端口、连线、运行时错误；对脚本/表达式类实例尽可能附带 script_bodies（截断后文本，含属性/字段名）。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "add_gh_component",
                        description = "在画布上创建**单个** Grasshopper 电池。适合只落一颗、占位/定位，或必须先看清画布反馈再决定下一步；**多颗电池且要带连线请优先用 create_component_graph 一次完成**。必须提供 name 或 component_guid 之一。默认只用 **name**。**不要**为普通电池先查 catalog；仅当已确认同名歧义或必须放置脚本/表达式类且 name 无法区分时，再用 search_gh_component_catalog 的 **component_guid**。Slider/Panel 可在同一次创建里直接提供 value/min/max/decimals，工具会在对象落盘后按顺序一次性写入，避免先后冲突；仍然必须提供 label。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                name = new { type = "string", description = "电池标准名称（与 component_guid 二选一）" },
                                component_guid = new { type = "string", description = "可选：组件库**类型** GUID（与 name 二选一）。多数情况不必填；同名冲突或脚本电池时再取自 search_gh_component_catalog 的 guid。" },
                                x = new { type = "number", description = "画布 X 坐标" },
                                y = new { type = "number", description = "画布 Y 坐标" },
                                label = new { type = "string", description = "仅限 Slider/Panel 的显示标签。普通电池严禁使用。" },
                                graph_mapper_type = new { type = "string", description = "可选：Graph Mapper 曲线类型。Graph Mapper 未指定时默认 Bezier；可填 Bezier、Linear、Parabola、Sine、Gaussian、Power、Square Root 等。" },
                                value = new { type = "string", description = "可选：Slider/Panel 初值；Slider 会自动按 min/max 夹紧后写入。" },
                                min = new { type = "number", description = "可选：Slider 最小值。" },
                                max = new { type = "number", description = "可选：Slider 最大值。" },
                                decimals = new { type = "integer", description = "可选：Slider 小数位数（0-10）。" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "x", "y", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "connect_gh_components",
                        description = "在两个**已有**电池的端口之间建立一条连接；常用于补连、改线或接入已有实例 id。若在同一次任务里**新建多颗电池及它们之间的多条连线**，优先在同一次 create_component_graph 的 connections 里完成，避免多轮少量 add 再少量 connect。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                from_id = new { type = "string", description = "源电池的 GUID" },
                                from_index = new { type = "integer", description = "源电池输出端口索引 (从0开始)" },
                                to_id = new { type = "string", description = "目标电池的 GUID" },
                                to_index = new { type = "integer", description = "目标电池输入端口索引 (从0开始)" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "from_id", "from_index", "to_id", "to_index", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "remove_gh_component",
                        description = "从画布上删除指定的电池。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "要删除的电池 GUID" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "id", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "set_gh_component_value",
                        description = "用于： Slider/Panel 的数值或显示文本； **仅当**用户明确要求或方案必需时，向 Evaluate/Expression/C#/Python/VB 等**脚本或表达式电池实例**写入代码/公式。**GhPython、Rhino Python 3 Script：可执行源码在 `Text`，严禁用 `Description`（摘要/元数据）；未指定 property 时会优先匹配 `Text`。** 默认按成员名启发式匹配可写 string 属性或字段；若失败，错误信息会列出候选名。可用 property 精确指定成员名。写完后会触发求解与延迟再算以尽量使脚本执行。**读代码**请用 get_gh_components（含 script_bodies 字段）。Slider 可同时设置 min/max/decimals；若只设置 value，工具会按当前 slider 最小/最大值自动夹紧。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "电池 GUID" },
                                value = new { type = "string", description = "脚本/表达式/Panel：要写入的完整文本或代码（多行可用 \\n）。Panel 必填；Slider 填数字字符串。" },
                                property = new { type = "string", description = "可选：精确写入的成员名（属性或字段，大小写不敏感）。Python/GhPython/Python 3 Script 填 **Text**；勿填 Description。其它如 Code、Script、PythonCode 等以 get_gh_components 提示为准；不填则启发式自动选（会优先 Text）。" },
                                graph_mapper_type = new { type = "string", description = "可选：当 id 是 Graph Mapper 时设置曲线类型；未指定时默认 Bezier。也可把类型写在 value 中。" },
                                min = new { type = "number", description = "可选：Slider 最小值" },
                                max = new { type = "number", description = "可选：Slider 最大值" },
                                decimals = new { type = "integer", description = "可选：Slider 小数位数（0-10）" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "id", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "remove_gh_connection",
                        description = "断开两个电池之间的连线。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                from_id = new { type = "string", description = "源电池 GUID" },
                                from_index = new { type = "integer", description = "源电池输出端口索引" },
                                to_id = new { type = "string", description = "目标电池 GUID" },
                                to_index = new { type = "integer", description = "目标电池输入端口索引" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "from_id", "from_index", "to_id", "to_index", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "create_component_graph",
                        description = "【推荐】**新建一整块逻辑时优先**：一次性提交多个电池与它们之间的连线，优于多轮「少量 add_gh_component ↔ 少量 connect_gh_components」。适合构建复杂或局部的几何逻辑。每个 component 须提供 alias_id、x、y，以及 **name**（常用）；**不要**默认填 component_guid。仅同名或脚本类 name 无法区分时才用 guid。示例：{\"components\":[{\"alias_id\":\"pt1\",\"name\":\"Construct Point\",\"x\":0,\"y\":0},{\"alias_id\":\"crv1\",\"name\":\"Circle CNR\",\"x\":200,\"y\":0,\"value\":\"5\"}],\"connections\":[{\"from_alias\":\"pt1\",\"from_index\":0,\"to_alias\":\"crv1\",\"to_index\":0}]}",
                        parameters = new {
                            type = "object",
                            properties = new {
                                components = new {
                                    type = "array",
                                    items = new {
                                        type = "object",
                                        properties = new {
                                            alias_id = new { type = "string", description = "临时代号(如 'pt1', 'crv1')，用于连线引用，必须唯一" },
                                            name = new { type = "string", description = "电池标准名称（与 component_guid 二选一）" },
                                            component_guid = new { type = "string", description = "可选：类型 GUID（与 name 二选一）。一般只写 name；同名或脚本电池再填 guid。" },
                                            label = new { type = "string", description = "仅限 Slider/Panel 的显示标签。普通电池严禁使用。" },
                                            x = new { type = "number", description = "画布 X 坐标" },
                                            y = new { type = "number", description = "画布 Y 坐标" },
                                            value = new { type = "string", description = "可选：Slider/Panel 初值；**脚本类电池（含 Python 3 Script）的源码**（写入 **Text**，勿当 Description）。" },
                                            graph_mapper_type = new { type = "string", description = "可选：Graph Mapper 曲线类型。未指定时默认 Bezier；也可把类型写在 value 中。" },
                                            min = new { type = "number", description = "可选：如果是 Slider，设置最小值" },
                                            max = new { type = "number", description = "可选：如果是 Slider，设置最大值" },
                                            decimals = new { type = "integer", description = "可选：如果是 Slider，设置小数位数" }
                                        },
                                        required = new[] { "alias_id", "x", "y" }
                                    }
                                },
                                connections = new {
                                    type = "array",
                                    items = new {
                                        type = "object",
                                        properties = new {
                                            from_alias = new { type = "string", description = "源电池的临时代号" },
                                            from_index = new { type = "integer", description = "源电池输出端口索引，从0开始" },
                                            to_alias = new { type = "string", description = "目标电池的临时代号" },
                                            to_index = new { type = "integer", description = "目标电池输入端口索引，从0开始" }
                                        },
                                        required = new[] { "from_alias", "from_index", "to_alias", "to_index" }
                                    }
                                },
                                group_name = new { type = "string", description = "可选：提供名称将自动为这些电池打组" },
                                auto_group = new { type = "boolean", description = "可选：是否自动根据任务成组 (默认 false)" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "components", "connections", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "recompute_gh_canvas",
                        description = "手动触发当前 Grasshopper 文档重新求解（写脚本/公式后若未执行或预览未更新可先试），不修改任何电池。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "capture_rhino_viewport",
                        description = "截取当前 Rhino 视口 PNG。默认会先自动取景：优先根据当前 Grasshopper 预览几何的包围盒缩放到可见范围，解决模型不在视窗内、太小或太大导致看不见的问题；若拿不到有效 GH 预览范围，则退回 Rhino 文档对象范围，最后才保留当前视图直接截图。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                framing = new { type = "string", description = "可选：auto（默认，优先 GH 预览范围）| gh_preview（强制按 GH 预览取景）| rhino_doc（按 Rhino 文档对象取景）| current_view（不自动缩放，直接截当前视图）。" },
                                width = new { type = "integer", description = "可选：输出图片宽度，默认 1600。" },
                                height = new { type = "integer", description = "可选：输出图片高度，默认 900。" },
                                padding_ratio = new { type = "number", description = "可选：自动取景时的留白比例，默认 0.12，建议 0.05-0.3。" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "gh_native_script_editor",
                        description = "针对 **Grasshopper 内置 C#/VB Script**：`open_focus` 打开/聚焦 GH_ScriptEditor；`read_source` **只从电池实例反射可读脚本正文**（与 get_gh_components.script_bodies 同源，**不调用**编辑器 GetSourceCode，避免读取崩溃）；`set_source_commit` 走编辑器并仅替换首个可编辑块。**read_source→改返回的 primary_for_edit→set_source_commit** 为推荐流程。**GhPython / Python 3 Script** 请用 set_gh_component_value，源码属性为 **Text**（勿写 Description）。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "电池 InstanceGuid" },
                                mode = new { type = "string", description = "open_focus | read_source（反射读，非编辑器） | set_source_commit" },
                                code = new { type = "string", description = "仅在 set_source_commit 必填：要写入**首个可编辑区**的正文（多行可）；通常对应 RunScript 内逻辑。**不要**默认为整文件含 using/模板，除非已与 read_source 对照确认。" },
                                language = new { type = "string", description = "可选：auto（默认）| cs | vb。仅在无法从电池反射到 GH_ScriptLanguage 时使用。" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "id", "mode", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "check_gh_errors",
                        description = "检查当前画布是否存在运行时错误或警告。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "set_gh_component_status",
                        description = "控制电池的显示(Preview)和启用(Enabled)状态。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "电池 GUID" },
                                preview = new { type = "boolean", description = "是否显示预览 (true为显示, false为隐藏)" },
                                enabled = new { type = "boolean", description = "是否启用电池 (true为启用, false为禁用)" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "id", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "set_all_csharp_script_previews",
                        description = "批量控制当前画布上所有 C# Script 电池的预览状态。适合截图前一次性关闭所有 C# Script 预览，避免过程线、点、调试几何干扰视觉检查；也可在需要时重新开启。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                preview = new { type = "boolean", description = "true 为开启所有 C# Script 预览，false 为关闭所有 C# Script 预览。" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "preview", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "prepare_visual_review_preview",
                        description = "为视觉截图检查准备一个干净的预览出口。该工具会创建或重建一个 Geometry 参数预览电池，把指定输出连接到它，并硬编码关闭所有 C# Script 预览，减少过程线、点和调试几何对截图的干扰。视觉检查前优先使用它，而不是依赖脚本过程预览。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                source_id = new { type = "string", description = "最终视觉检查目标的源组件 GUID。" },
                                source_output_index = new { type = "integer", description = "源组件输出端口索引，从 0 开始。" },
                                label = new { type = "string", description = "可选：预览电池显示名称，默认 VisualReviewPreview。" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "source_id", "source_output_index", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "modify_gh_component_ports",
                        description = "针对支持动态端口的电池增加或删除端口。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "电池 GUID" },
                                is_input = new { type = "boolean", description = "true 为输入端口，false 为输出端口" },
                                action = new { type = "string", description = "'add' 或 'remove'" },
                                port_name = new { type = "string", description = "remove 时优先按此名称删除端口，匹配 Name / NickName（忽略大小写）" },
                                index = new { type = "integer", description = "remove 的可选兜底索引；未提供 port_name 时使用" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "id", "is_input", "action", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "manage_gh_groups",
                        description = "管理画布上的电池组(Group)。action 说明：'create'=创建新组（需要 ids 和 name），'ungroup'=解散组（需要 group_id），'add_to_group'=添加电池到组（需要 group_id 和 ids），'remove_from_group'=从组中移除电池（需要 group_id 和 ids）。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                action = new { type = "string", description = "'create', 'ungroup', 'add_to_group', 'remove_from_group'" },
                                ids = new { type = "array", items = new { type = "string" }, description = "涉及的电池 GUID 列表，create/add_to_group/remove_from_group 时需要" },
                                group_id = new { type = "string", description = "操作已有组时的组 GUID，ungroup/add_to_group/remove_from_group 时需要" },
                                name = new { type = "string", description = "创建组时的显示名称，create 时需要" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "action", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "modify_gh_port_data",
                        description = "修改电池端口的数据处理方式（如：拍平 Flatten, 成组 Graft, 精简 Simplify, 反转 Reverse）。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "电池 GUID" },
                                is_input = new { type = "boolean", description = "true为输入端口，false为输出端口" },
                                index = new { type = "integer", description = "端口索引 (从0开始)" },
                                operation = new { type = "string", description = "操作类型: 'Flatten', 'Graft', 'Simplify', 'Reverse', 'None'" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "id", "is_input", "index", "operation", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "search_component_library",
                        description = "根据关键词在 Grasshopper 电池库中搜索可用插件和电池名称（简短文本列表，最多约 15 条）。**日常找电池名首选**；不要用 search_gh_component_catalog 代替本接口做常规检索。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                keyword = new { type = "string", description = "搜索关键词" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "keyword", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "search_gh_component_catalog",
                        description = "【非必要勿调用】仅在：① 要放置/核对**脚本或表达式类**电池类型；② 已确认 **add_gh_component 因同名失败** 必须用 guid 时。日常找电池名请优先 **search_component_library**（更轻）。返回 JSON（含 guid）；**不要**把本接口当作每次建模的前置步骤。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                query = new { type = "string", description = "匹配 name、nickname、category 或 subcategory 的关键词；脚本场景才用 Evaluate、C# 等，日常用 Point、Curve 等标准名即可" },
                                max_results = new { type = "integer", description = "最大条数，默认 30，上限 200" },
                                category_contains = new { type = "string", description = "可选：仅保留 category 或 subcategory 含此子串的项（如 Script）" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "query", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "query_gh_components",
                        description = "按 id、组件名、报错状态、脚本组件、是否有连线、端口名等条件搜索当前 Grasshopper 画布，并仅返回命中的组件片段；可选附带一阶或二阶邻居摘要，适合先搜索再局部展开，不必每次读取整张画布 JSON。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "可选：组件 GUID，精确匹配。" },
                                name_contains = new { type = "string", description = "可选：组件 Name 或 NickName 包含的关键字。" },
                                has_errors = new { type = "boolean", description = "可选：是否只看有 Error/Warning 的组件。" },
                                is_script = new { type = "boolean", description = "可选：是否只看脚本/表达式类组件。" },
                                has_connections = new { type = "boolean", description = "可选：是否只看存在输入来源或输出接收者的组件。" },
                                port_name_contains = new { type = "string", description = "可选：端口 Name/NickName 包含的关键字。" },
                                max_results = new { type = "integer", description = "可选：最多返回多少个命中，默认 8，上限 50。" },
                                neighbor_depth = new { type = "integer", description = "可选：返回命中组件邻居摘要的层数，0/1/2，默认 1。" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "get_component_context",
                        description = "按组件 id 读取该组件及其邻居的完整局部上下文。默认不展开脚本体，只返回结构、端口、连线、运行时消息；需要脚本正文时再单独调用 read_component_script。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "组件 GUID。" },
                                depth = new { type = "integer", description = "可选：邻居层数，默认 1。" },
                                include_script_bodies = new { type = "boolean", description = "可选：是否在局部上下文中直接附带 script_bodies；默认 false。" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "id", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "read_component_script",
                        description = "单独读取某个脚本/表达式类组件可反射到的脚本正文。适合在 query_gh_components 或 get_component_context 定位到目标后，再递进展开脚本体。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "组件 GUID。" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "id", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "create_gh_skill",
                        description = "将当前画布内容或特定逻辑总结为一个可复用的技能(Skill)并保存。文件开头必须包含 YAML Frontmatter 格式的 name 和 description。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                file_name = new { type = "string", description = "保存的文件名（英文，以 .md 结尾，如 'connect_points.md'）" },
                                name = new { type = "string", description = "技能名称（如 '连点成线'）" },
                                description = new { type = "string", description = "简短的技能描述" },
                                content = new { type = "string", description = "技能的详细内容，包括使用的电池、连线逻辑、注意事项等（Markdown 格式）" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "file_name", "name", "description", "content", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "read_skill_file",
                        description = "读取 skills 目录中的 skill 文件，获取详细的操作指南和最佳实践。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                file_name = new { type = "string", description = "要读取的 skill 文件名，如 'general_modeling.md' 或 'workflow_optimization.md'" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "file_name", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "read_reference_json",
                        description = "读取 reference 目录中的参考画布 JSON。应在建模/连线逻辑已规划清楚之后再调用；可先 read_skill_file 读取 reference_index.md 选定 file_name。不要在尚未形成方案时抢先读参考。",
                        parameters = new {
                            type = "object",
                            properties = new {
                                file_name = new { type = "string", description = "reference 目录下的 JSON 文件名，如 'ref_20260503123000.json'" },
                                summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                                summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                            },
                            required = new[] { "file_name", "summary" }
                        }
                    }
                },
                ShowReferenceOptionsTool.GetApiToolDefinition(),
                ShowPlanStepsTool.GetApiToolDefinition()
            };
            return FilterToolsForVisionContext(FilterToolsForAgentMode(FilterToolsForLayoutMode(toolDefinitions)));
        }

        private static object GetCreateScriptComponentGraphToolDefinition()
        {
            var portSchema = new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", description = "Port name; also applied to Name and NickName." },
                    type_hint = new { type = "string", description = "可选：端口类型提示，仅写入 Description，不做强类型约束。" }
                },
                required = new[] { "name" }
            };

            return new
            {
                type = "function",
                function = new
                {
                    name = "create_script_component_graph",
                    description = "Create one or more Python 3 Script components with ports, source, helper components, connections, and group. Use this in mixed mode only when Python scripting is clearly better than native GH components. Python uses Rhino 8 Python 3 Script only; do not fall back to GhPython. For C# Script use create_csharp_script_component instead.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            mode = new { type = "string", description = "csharp | python。csharp 创建 C# Script；python 创建 Python 3 Script。" },
                            scripts = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        alias_id = new { type = "string", description = "脚本电池临时代号，供 connections 引用，必须唯一。" },
                                        label = new { type = "string", description = "可选：脚本电池 NickName。" },
                                        x = new { type = "number", description = "画布 X 坐标。" },
                                        y = new { type = "number", description = "画布 Y 坐标。" },
                                        source = new { type = "string", description = "脚本源码。C# 只提供 RunScript 方法内部语句，不要包含 using、Script_Instance 类或 RunScript 签名；Python 3 写入 Text。" },
                                        inputs = new { type = "array", items = portSchema },
                                        output_count = new { type = "integer", description = "已弃用：C# Script 请改用 create_csharp_script_component。" },
                                        outputs = new { type = "array", items = portSchema, description = "输出端口。C# 模式下不要填写，会被忽略；Python 模式可填写。" }
                                    },
                                    required = new[] { "alias_id", "x", "y", "source" }
                                }
                            },
                            components = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        alias_id = new { type = "string", description = "辅助电池临时代号，供 connections 引用，必须唯一。" },
                                        name = new { type = "string", description = "Helper component standard name; alternative to component_guid." },
                                        component_guid = new { type = "string", description = "Optional type GUID; alternative to name." },
                                        label = new { type = "string", description = "仅限 Slider/Panel 的显示标签。" },
                                        x = new { type = "number", description = "画布 X 坐标。" },
                                        y = new { type = "number", description = "画布 Y 坐标。" },
                                        value = new { type = "string", description = "Slider/Panel 初值。" },
                                        min = new { type = "number", description = "Slider 最小值。" },
                                        max = new { type = "number", description = "Slider 最大值。" },
                                        decimals = new { type = "integer", description = "Slider 小数位数。" }
                                    },
                                    required = new[] { "alias_id", "x", "y" }
                                }
                            },
                            connections = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        from_alias = new { type = "string", description = "源电池临时代号。" },
                                        from_index = new { type = "integer", description = "源输出端口索引，从 0 开始。" },
                                        to_alias = new { type = "string", description = "目标电池临时代号。" },
                                        to_index = new { type = "integer", description = "目标输入端口索引，从 0 开始。" }
                                    },
                                    required = new[] { "from_alias", "from_index", "to_alias", "to_index" }
                                }
                            },
                            group_name = new { type = "string", description = "可选：自动为这些电池打组。" },
                            summary = new { type = "string", description = "必填：一句中文说明本次操作，用于界面小卡片；勿写工具函数名或英文 API。" },
                            summary_detail = new { type = "string", description = "可选：卡片右侧次要短语（勿写函数名）。" }
                        },
                        required = new[] { "mode", "scripts", "connections", "summary" }
                    }
                }
            };
        }

        private static object GetCreateCSharpScriptComponentToolDefinition()
        {
            var inputPortSchema = new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", description = "C# input variable name. Must be a valid identifier and must not collide with reserved/output variables a,b,c..." },
                    type_hint = new { type = "string", description = "Optional input type hint. Common hints such as double/number, int/integer, bool, string, point3d, vector3d, curve, brep, mesh, plane will be used by the tool to auto-inject typed local aliases into the RunScript body, so the body can use the input name directly without repeatedly converting from object." }
                },
                required = new[] { "name" }
            };

            var outputPortSchema = new
            {
                type = "object",
                properties = new
                {
                    label = new { type = "string", description = "Business label for this output. The actual C# variable name is forced to b,c,d... and this label is written to the port description." },
                    name = new { type = "string", description = "Optional alias for label. It is not used as the C# variable name." },
                    type_hint = new { type = "string", description = "Optional output type hint written to the port description only." }
                }
            };

            var helperComponentSchema = new
            {
                type = "object",
                properties = new
                {
                    alias_id = new { type = "string", description = "Temporary alias used by connections." },
                    name = new { type = "string", description = "Grasshopper component name. In C# priority mode only Params and Display categories are allowed." },
                    component_guid = new { type = "string", description = "Optional component type GUID. In C# priority mode only Params and Display categories are allowed." },
                    label = new { type = "string", description = "Optional label, mainly for Slider/Panel." },
                    x = new { type = "number", description = "Canvas X coordinate." },
                    y = new { type = "number", description = "Canvas Y coordinate." },
                    value = new { type = "string", description = "Initial Slider/Panel value." },
                    min = new { type = "number", description = "Slider minimum." },
                    max = new { type = "number", description = "Slider maximum." },
                    decimals = new { type = "integer", description = "Slider decimal places." }
                },
                required = new[] { "alias_id", "x", "y" }
            };

            var connectionSchema = new
            {
                type = "object",
                properties = new
                {
                    from_alias = new { type = "string", description = "Source alias." },
                    from_index = new { type = "integer", description = "Source output index, zero based." },
                    to_alias = new { type = "string", description = "Target alias." },
                    to_index = new { type = "integer", description = "Target input index, zero based." }
                },
                required = new[] { "from_alias", "from_index", "to_alias", "to_index" }
            };

            return new
            {
                type = "function",
                function = new
                {
                    name = "create_csharp_script_component",
                    description = "Dedicated C# Script layout tool. It first creates a default C# Script component, waits briefly for Grasshopper/Rhino 8 to finish initializing it, then applies the requested component name, input ports, extra output ports, and RunScript body. Default C# outputs such as out/a are preserved; requested business outputs are added as b,c,d... and those are the variables to assign. For common input type hints, the tool auto-injects typed local aliases into the body so the body can use the input names directly instead of repeatedly converting object values. After the body is written, the tool automatically triggers a short delayed two-pass recompute so the agent usually does not need a separate recompute step. It intentionally skips connections during creation; connect components later after the script component is stable. Use this instead of create_script_component_graph for C# priority modeling.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            alias_id = new { type = "string", description = "Temporary alias for the C# Script component. Defaults to core when omitted." },
                            name = new { type = "string", description = "Optional C# Script component nickname applied after the default component has initialized." },
                            label = new { type = "string", description = "Deprecated alias for name. Prefer name." },
                            x = new { type = "number", description = "Canvas X coordinate." },
                            y = new { type = "number", description = "Canvas Y coordinate." },
                            inputs = new { type = "array", items = inputPortSchema },
                            outputs = new { type = "array", items = outputPortSchema, description = "Business output labels. Default out/a ports are preserved; requested outputs are added as b,c,d... in this order. Do not assign to a." },
                            body = new { type = "string", description = "Only the RunScript method body. No using statements, no class declaration, no RunScript signature, no template. When inputs carry common type hints, write the body as if those inputs were already strongly typed; the tool will inject local aliases automatically." },
                            components = new { type = "array", items = helperComponentSchema },
                            connections = new { type = "array", items = connectionSchema, description = "Optional. Currently skipped during C# creation for stability; use a later connection tool call after creation." },
                            group_name = new { type = "string", description = "Optional group name." },
                            summary = new { type = "string", description = "Required short Chinese summary for the UI operation card. Do not write the function name." },
                            summary_detail = new { type = "string", description = "Optional short secondary phrase for the UI operation card." }
                        },
                        required = new[] { "x", "y", "body", "summary" }
                    }
                }
            };
        }

        private static object GetEditCSharpScriptComponentToolDefinition()
        {
            return new
            {
                type = "function",
                function = new
                {
                    name = "edit_csharp_script_component",
                    description = "Dedicated safe editor for existing Grasshopper C# Script components. read_body returns only the editable RunScript body when available. set_body replaces only the editable RunScript body while preserving the built-in C# Script template, using statements, class declaration, and GH-managed RunScript signature. When current input ports carry common type hints, set_body auto-injects typed local aliases so the body can use input names directly. After writing the body, the tool automatically triggers a short delayed two-pass recompute so a separate recompute step is usually unnecessary. Never pass a full C# file, class, using block, or RunScript signature.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            id = new { type = "string", description = "C# Script component InstanceGuid." },
                            mode = new { type = "string", description = "read_body | set_body" },
                            body = new { type = "string", description = "Required for set_body: only the RunScript method body. No using statements, no class declaration, no RunScript signature, no template. For common input type hints, write the body as if the inputs were already strongly typed." },
                            summary = new { type = "string", description = "Required short Chinese summary for the UI operation card. Do not write the function name." },
                            summary_detail = new { type = "string", description = "Optional short secondary phrase for the UI operation card." }
                        },
                        required = new[] { "id", "mode", "summary" }
                    }
                }
            };
        }

        private static string GetToolDefinitionName(object toolDefinition)
        {
            try
            {
                JObject jo = JObject.FromObject(toolDefinition);
                return jo["function"]?["name"]?.ToString();
            }
            catch (Exception ex)
            {
                AddGhLog.Debug("GetToolDefinitionName failed: " + ex.Message);
                return null;
            }
        }

        private static object[] FilterToolsForLayoutMode(object[] toolDefinitions)
        {
            if (toolDefinitions == null) return toolDefinitions;

            var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_layoutMode == LayoutMode.Battery)
            {
                blocked.Add("create_csharp_script_component");
            }
            else if (_layoutMode == LayoutMode.CSharpFirst)
            {
                blocked.Add("create_component_graph");
                blocked.Add("create_script_component_graph");
                blocked.Add("gh_native_script_editor");
                if (_agentMode == AgentMode.Create)
                {
                    blocked.Add("read_reference_json");
                    blocked.Add("create_gh_skill");
                    blocked.Add(ShowReferenceOptionsTool.FunctionName);
                }
            }

            return toolDefinitions
                .Where(t => !blocked.Contains(GetToolDefinitionName(t) ?? ""))
                .Select(RestrictAddComponentToolForScriptMode)
                .ToArray();
        }

        private static object[] FilterToolsForAgentMode(object[] toolDefinitions)
        {
            if (toolDefinitions == null) return toolDefinitions;

            if (_agentMode != AgentMode.Plan)
            {
                return toolDefinitions
                    .Where(t => !string.Equals(GetToolDefinitionName(t), ShowPlanStepsTool.FunctionName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "get_gh_components",
                "capture_rhino_viewport",
                "check_gh_errors",
                "search_component_library",
                "search_gh_component_catalog",
                "query_gh_components",
                "get_component_context",
                "read_component_script",
                "read_skill_file",
                "read_reference_json",
                ShowPlanStepsTool.FunctionName
            };

            return toolDefinitions
                .Where(t => allowed.Contains(GetToolDefinitionName(t) ?? ""))
                .ToArray();
        }

        private static object[] FilterToolsForVisionContext(object[] toolDefinitions)
        {
            if (toolDefinitions == null || IsVisionToolContextActive()) return toolDefinitions;

            var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "capture_rhino_viewport",
                "prepare_visual_review_preview",
                "set_all_csharp_script_previews"
            };

            return toolDefinitions
                .Where(t => !blocked.Contains(GetToolDefinitionName(t) ?? ""))
                .ToArray();
        }

        private static object RestrictAddComponentToolForScriptMode(object toolDefinition)
        {
            string name = GetToolDefinitionName(toolDefinition);
            if (_layoutMode != LayoutMode.CSharpFirst || !string.Equals(name, "add_gh_component", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(name, "set_gh_component_value", StringComparison.OrdinalIgnoreCase) && _layoutMode == LayoutMode.CSharpFirst)
                {
                    JObject setJo = JObject.FromObject(toolDefinition);
                    var setFn = setJo["function"] as JObject;
                    if (setFn != null)
                    {
                        setFn["description"] = "C# 优先模式下的受限辅助值设置工具：仅用于 Slider、Panel 等非脚本辅助电池的数值或显示文本。严禁用它写入 C# Script 源码；修改 C# Script 方法体必须使用 edit_csharp_script_component。";
                    }
                    return setJo;
                }
                return toolDefinition;
            }

            JObject jo = JObject.FromObject(toolDefinition);
            var fn = jo["function"] as JObject;
            if (fn != null)
            {
                fn["description"] = "C# priority mode helper tool: only add Params or Display components as inputs, outputs, preview, or debugging helpers for C# Script. Do not create core modeling components here; put core logic in create_csharp_script_component.";
            }
            return jo;
        }
    }
}
