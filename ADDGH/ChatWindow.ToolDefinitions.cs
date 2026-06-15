using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ADDGH
{
    public static partial class ChatWindow
    {
        private const string ToolSummaryDescription = "Short UI operation summary in Chinese. Keep it concise and action-oriented; do not put large data, code, JSON, or analysis here.";
        private const string ToolSummaryDetailDescription = "Optional UI detail for the operation card. Use only when a short clarification helps the user understand the tool action.";

        private static object[] BuildToolDefinitionsForCurrentMode()
        {
            object[] toolDefinitions = new object[]
            {
                new {
                    type = "function",
                    function = new {
                        name = "create_ai_image",
                        description = "Generate or edit an AI image from the user's prompt and optional uploaded images. Use only for image creation/editing tasks, not for Grasshopper canvas modeling.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                prompt = new { type = "string", description = "Image generation/editing prompt. Include subject, style, constraints, and what to preserve from uploaded images when relevant." },
                                intent = new { type = "string", description = "Task intent, for example generate, edit, variation, reference, or background." },
                                use_uploaded_images = new { type = "boolean", description = "Whether uploaded images should be used as visual references or edit sources. Default true when images are attached." },
                                aspect_ratio = new { type = "string", description = "Optional output aspect ratio such as 1:1, 16:9, 4:3, 3:2, or 9:16." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "prompt", "intent", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "ensure_gh_canvas",
                        description = "Ensure an active Grasshopper canvas/document exists before creating or modifying GH objects. Call before mutating tools when canvas availability is uncertain.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
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
                        description = "Inspect the current Grasshopper canvas: component ids, names, ports, connections, errors, groups, and script summaries. Use before modifying existing canvas objects.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "add_gh_component",
                        description = "Add one Grasshopper component or value helper. Prefer create_component_graph for creating multiple related components and connections in one step.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                name = new { type = "string", description = "Grasshopper component name or ADD helper type. Use when component_guid is unknown." },
                                component_guid = new { type = "string", description = "Exact Grasshopper component GUID when known. Prefer this over ambiguous names." },
                                x = new { type = "number", description = "Canvas X coordinate." },
                                y = new { type = "number", description = "Canvas Y coordinate." },
                                label = new { type = "string", description = "Optional display nickname for the created component/helper." },
                                graph_mapper_type = new { type = "string", description = "Optional Graph Mapper type when creating or configuring a graph mapper." },
                                value = new { type = "string", description = "Optional initial value for sliders, panels, value lists, or other value helpers." },
                                min = new { type = "number", description = "Optional slider or graph mapper minimum." },
                                max = new { type = "number", description = "Optional slider or graph mapper maximum." },
                                decimals = new { type = "integer", description = "Optional numeric precision for slider-like helpers." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "x", "y", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "connect_gh_components",
                        description = "Connect output and input ports between existing Grasshopper components. Use ids from get_gh_components or aliases from a batch creation result.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                from_id = new { type = "string", description = "Source component public id or GUID." },
                                from_index = new { type = "integer", description = "Source output port index. Defaults to 0 when omitted." },
                                from_port_label = new { type = "string", description = "Optional source port label to resolve when index is ambiguous." },
                                to_id = new { type = "string", description = "Target component public id or GUID." },
                                to_index = new { type = "integer", description = "Target input port index. Defaults to 0 when omitted." },
                                to_port_label = new { type = "string", description = "Optional target port label to resolve when index is ambiguous." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "from_id", "to_id", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "remove_gh_component",
                        description = "Remove one existing Grasshopper component or group by id. Inspect first when the target id is uncertain.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "Component/group public id or GUID to remove." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "id", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "set_gh_component_value",
                        description = "Set value/configuration on an existing GH helper component such as Slider, Panel, Value List, Boolean Toggle, or Graph Mapper. Do not use to edit C# Script source.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "Target component public id or GUID." },
                                value = new { type = "string", description = "Value to apply. Format depends on target helper type." },
                                property = new { type = "string", description = "Optional property name when changing a specific setting instead of the main value." },
                                graph_mapper_type = new { type = "string", description = "Optional Graph Mapper type to apply." },
                                min = new { type = "number", description = "Optional slider/mapper minimum." },
                                max = new { type = "number", description = "Optional slider/mapper maximum." },
                                decimals = new { type = "integer", description = "Optional numeric precision for slider-like helpers." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "id", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "remove_gh_connection",
                        description = "Remove one wire between two existing Grasshopper component ports.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                from_id = new { type = "string", description = "Source component public id or GUID." },
                                from_index = new { type = "integer", description = "Source output port index." },
                                to_id = new { type = "string", description = "Target component public id or GUID." },
                                to_index = new { type = "integer", description = "Target input port index." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "from_id", "from_index", "to_id", "to_index", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "create_component_graph",
                        description = "Batch-create a Grasshopper graph from component definitions and connections. Prefer this over repeated add/connect calls for new multi-component workflows.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                components = new {
                                    type = "array",
                                    items = new {
                                        type = "object",
                                        properties = new {
                                            alias_id = new { type = "string", description = "Temporary unique id used by connections in this same batch." },
                                            name = new { type = "string", description = "Grasshopper component name or helper type." },
                                            component_guid = new { type = "string", description = "Exact Grasshopper component GUID when known." },
                                            label = new { type = "string", description = "Optional display nickname." },
                                            x = new { type = "number", description = "Canvas X coordinate." },
                                            y = new { type = "number", description = "Canvas Y coordinate." },
                                            value = new { type = "string", description = "Optional initial helper value." },
                                            graph_mapper_type = new { type = "string", description = "Optional Graph Mapper type." },
                                            min = new { type = "number", description = "Optional slider/mapper minimum." },
                                            max = new { type = "number", description = "Optional slider/mapper maximum." },
                                            decimals = new { type = "integer", description = "Optional numeric precision." }
                                        },
                                        required = new[] { "alias_id", "x", "y" }
                                    }
                                },
                                connections = new {
                                    type = "array",
                                    items = new {
                                        type = "object",
                                        properties = new {
                                            from_alias = new { type = "string", description = "Source component alias_id from this batch." },
                                            from_index = new { type = "integer", description = "Source output port index." },
                                            to_alias = new { type = "string", description = "Target component alias_id from this batch." },
                                            to_index = new { type = "integer", description = "Target input port index." }
                                        },
                                        required = new[] { "from_alias", "from_index", "to_alias", "to_index" }
                                    }
                                },
                                group_name = new { type = "string", description = "Optional group name for created components." },
                                auto_group = new { type = "boolean", description = "Whether to place created components into an automatic group." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "components", "connections", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "recompute_gh_canvas",
                        description = "Recompute the active Grasshopper document after edits and update runtime state/errors.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "gh_native_script_editor",
                        description = "Fallback native script editor integration for an existing script component. Prefer create_csharp_script_component for new scripts and edit_csharp_script_component for normal C# body edits.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "Target script component public id or GUID." },
                                mode = new { type = "string", description = "Editor action/mode requested." },
                                code = new { type = "string", description = "Script source when the selected mode writes code." },
                                language = new { type = "string", description = "Script language, normally csharp." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "id", "mode", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "check_gh_errors",
                        description = "Check Grasshopper runtime errors, warnings, invalid components, and script compilation/runtime issues. Use after creating or editing canvas logic.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "set_gh_component_status",
                        description = "Set preview and/or enabled state for one Grasshopper component.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "Target component public id or GUID." },
                                preview = new { type = "boolean", description = "Optional preview visibility state." },
                                enabled = new { type = "boolean", description = "Optional enabled/disabled state." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "id", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "set_all_csharp_script_previews",
                        description = "Set preview state for all C# Script components. Useful before visual review to show or hide script-generated geometry.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                preview = new { type = "boolean", description = "Preview state to apply to all C# Script components." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "preview", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "modify_gh_component_ports",
                        description = "Fallback tool to add, remove, or rename dynamic component ports. For normal C# creation, prefer create_csharp_script_component; use this only to repair an existing variable-parameter component.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "Target component public id or GUID." },
                                is_input = new { type = "boolean", description = "True for input ports, false for output ports." },
                                action = new { type = "string", description = "Port operation, such as add, remove, rename, or set_type." },
                                port_name = new { type = "string", description = "Port name involved in the operation." },
                                type_hint = new { type = "string", description = "Optional when adding a C# Script port. Prefer Rhino C# Script menu names such as bool, int, string, double, Point3d, Point3dList, Vector3d, Plane, Line, Circle, Arc, Curve, Mesh, Surface, Brep, GeometryBase. Conversion-only helper hints such as curve[], circle[], double[], int[] only refresh ADD Agent aliases and are not native Rhino port hints." },
                                index = new { type = "integer", description = "Optional target port index." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "id", "is_input", "action", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "manage_gh_groups",
                        description = "Create, update, or ungroup Grasshopper Groups. Use action=create to group component ids, add_to_group/remove_from_group to edit members, and ungroup to delete one or more group objects while leaving their member components on the canvas.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                action = new { type = "string", @enum = new[] { "create", "add_to_group", "remove_from_group", "ungroup" }, description = "Group operation to perform." },
                                ids = new { type = "array", items = new { type = "string" }, description = "For create/add_to_group/remove_from_group: component ids. For ungroup: optional group ids when ungrouping multiple groups." },
                                group_id = new { type = "string", description = "Target group id for add_to_group/remove_from_group/ungroup. Public ids such as G01 are accepted." },
                                name = new { type = "string", description = "Group name when action=create." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "action", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "modify_gh_port_data",
                        description = "Modify Grasshopper port data access/tree settings on an existing component. Use only when data matching or list/tree access needs repair.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "Target component public id or GUID." },
                                is_input = new { type = "boolean", description = "True for input port, false for output port." },
                                index = new { type = "integer", description = "Target port index." },
                                operation = new { type = "string", description = "Data operation/access setting to apply." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "id", "is_input", "index", "operation", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "search_component_library",
                        description = "Search ADD Agent's local component library by keyword when choosing a Grasshopper component name or category.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                keyword = new { type = "string", description = "Component name, category, or modeling concept to search." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "keyword", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "search_gh_component_catalog",
                        description = "Search the Grasshopper component catalog when exact component names or GUIDs are unknown. Use before add/create graph when component identity is uncertain.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                query = new { type = "string", description = "Component name, category, keyword, or modeling operation to search." },
                                max_results = new { type = "integer", description = "Maximum results to return. Use a small value for focused searches." },
                                category_contains = new { type = "string", description = "Optional category substring filter." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "query", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "web_research",
                        description = "Search or open web documentation/current information when local knowledge is insufficient or the user asks for latest/external information. For RhinoCommon/Grasshopper API signature lookup, prefer mode=api_pipeline before generic search.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                mode = new { type = "string", description = "Research mode: api_pipeline for RhinoCommon/Grasshopper API lookup, search for general web search, or fetch for a known URL." },
                                query = new { type = "string", description = "Search/API query. For api_pipeline include candidate type/method names and concept words, for example Brep.CreateFromRevolution surface of revolution." },
                                url = new { type = "string", description = "Direct URL when mode=fetch." },
                                allowed_domains = new { type = "array", items = new { type = "string" }, description = "Optional domain allowlist for focused/official-source research." },
                                max_results = new { type = "integer", description = "Maximum search results to retrieve." },
                                max_chars = new { type = "integer", description = "Maximum returned text characters; keep modest unless detailed source context is required." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "query_gh_components",
                        description = "Query a focused subset of current Grasshopper components by id, name, error state, script status, connection state, or port name.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "Optional exact public id or GUID to query." },
                                name_contains = new { type = "string", description = "Optional substring filter for component name/nickname." },
                                has_errors = new { type = "boolean", description = "Optional filter for components with runtime errors." },
                                is_script = new { type = "boolean", description = "Optional filter for script components." },
                                has_connections = new { type = "boolean", description = "Optional filter for components with any wires." },
                                port_name_contains = new { type = "string", description = "Optional substring filter for input/output port names." },
                                max_results = new { type = "integer", description = "Maximum matching components to return." },
                                neighbor_depth = new { type = "integer", description = "Optional neighbor traversal depth around matches." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "get_component_context",
                        description = "Read focused context around one component, including nearby components, ports, connections, and optionally script bodies.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "Target component public id or GUID." },
                                depth = new { type = "integer", description = "Neighbor traversal depth. Use 1 for focused repair unless more graph context is needed." },
                                include_script_bodies = new { type = "boolean", description = "Whether to include full script bodies. Set true only when code content is needed." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "id", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "read_component_script",
                        description = "Read the source/body of an existing script component. Use before editing or debugging an existing C# Script.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "Target script component public id or GUID." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "id", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "create_gh_skill",
                        description = "Create a new reusable skill markdown file. Use only in explicit skill-authoring or self-training workflows after behavior is validated.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                file_name = new { type = "string", description = "Markdown file name under skills/. Must be a safe file name; .md is optional." },
                                name = new { type = "string", description = "Skill name for YAML frontmatter." },
                                description = new { type = "string", description = "Short trigger description explaining when the skill should be used." },
                                content = new { type = "string", description = "Skill markdown body with concrete procedure, constraints, and verification guidance." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "file_name", "name", "description", "content", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "read_skill_file",
                        description = "Load the full body of one relevant skill by file name or skill id. Use only after the skill summary indicates relevance; do not bulk-read unrelated skills.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                file_name = new { type = "string", description = "Skill file name or id from the skill summary, for example official_x.md or trained_example.md." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "file_name", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "read_reference_json",
                        description = "Read one saved reference JSON from the reference directory after deciding it is relevant from the reference index or user selection.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                file_name = new { type = "string", description = "Reference JSON file name under reference/. .json is optional." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "file_name", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "import_reference_gh",
                        description = "Import a saved .gh or .ghx reference file into the active Grasshopper canvas. Use only when the user wants to reuse/reference an existing saved canvas.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                file_name = new { type = "string", description = "Reference .gh or .ghx file name under reference/." },
                                offset_x = new { type = "number", description = "Optional X offset applied to imported objects." },
                                offset_y = new { type = "number", description = "Optional Y offset applied to imported objects." },
                                group_name = new { type = "string", description = "Optional group name for imported objects." },
                                summary = new { type = "string", description = ToolSummaryDescription },
                                summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
                            },
                            required = new[] { "file_name", "summary" }
                        }
                    }
                },
                ShowReferenceOptionsTool.GetApiToolDefinition(),
                ShowPlanStepsTool.GetApiToolDefinition()
            };
            return ApplyWorkflowToolSurfacePolicy(FilterToolsForVisionContext(FilterToolsForAgentMode(FilterToolsForLayoutMode(toolDefinitions))));
        }

        private static object GetCreateScriptComponentGraphToolDefinition()
        {
            var portSchema = new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", description = "C# input variable name. Must be a valid identifier and must not collide with reserved/output variables." },
                    type_hint = new { type = "string", description = "Optional input type hint. Prefer Rhino C# Script menu names for real port hints: bool, int, string, double, Point3d, Point3dList, Vector3d, Plane, Interval, Line, Circle, Arc, Curve, Polyline, Rectangle3d, Mesh, Surface, Brep, GeometryBase, TextDot, TextEntity. ADD Agent also accepts conversion-only helper hints such as curve[], circle[], double[], int[]; these are not native Rhino port hints and only drive defensive alias injection." }
                },
                required = new[] { "name" }
            };

            return new
            {
                type = "function",
                function = new
                {
                    name = "create_script_component_graph",
                    description = "Legacy batch tool that creates script components, helper components, and connections together. Prefer create_csharp_script_component for new C# Script workflows unless this compatibility path is specifically needed.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            mode = new { type = "string", description = "Creation mode for the legacy graph path." },
                            scripts = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        alias_id = new { type = "string", description = "Temporary unique script alias used by connections in this batch." },
                                        label = new { type = "string", description = "Optional display nickname for the script component." },
                                        x = new { type = "number", description = "Canvas X coordinate." },
                                        y = new { type = "number", description = "Canvas Y coordinate." },
                                        source = new { type = "string", description = "Script source/body." },
                                        inputs = new { type = "array", items = portSchema },
                                        output_count = new { type = "integer", description = "Number of output ports when explicit outputs are not supplied." },
                                        outputs = new { type = "array", items = portSchema, description = "Explicit output port definitions." }
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
                                        alias_id = new { type = "string", description = "Temporary unique helper alias used by connections in this batch." },
                                        name = new { type = "string", description = "Grasshopper helper component name or type." },
                                        component_guid = new { type = "string", description = "Exact Grasshopper component GUID when known." },
                                        label = new { type = "string", description = "Optional display nickname." },
                                        x = new { type = "number", description = "Canvas X coordinate." },
                                        y = new { type = "number", description = "Canvas Y coordinate." },
                                        value = new { type = "string", description = "Optional initial helper value." },
                                        min = new { type = "number", description = "Optional slider minimum." },
                                        max = new { type = "number", description = "Optional slider maximum." },
                                        decimals = new { type = "integer", description = "Optional numeric precision." }
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
                                        from_alias = new { type = "string", description = "Source alias_id from scripts or helper components in this batch." },
                                        from_index = new { type = "integer", description = "Source output port index." },
                                        to_alias = new { type = "string", description = "Target alias_id from scripts or helper components in this batch." },
                                        to_index = new { type = "integer", description = "Target input port index." }
                                    },
                                    required = new[] { "from_alias", "from_index", "to_alias", "to_index" }
                                }
                            },
                            group_name = new { type = "string", description = "Optional group name for created scripts and helpers." },
                            summary = new { type = "string", description = ToolSummaryDescription },
                            summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
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
                    name = new { type = "string", description = "C# input variable name. Must be a valid identifier." },
                    type_hint = new { type = "string", description = "Optional C# Script input type hint, for example double, int, bool, Point3d, Curve, Brep, Mesh, or GeometryBase." }
                },
                required = new[] { "name" }
            };

            var outputPortSchema = new
            {
                type = "object",
                properties = new
                {
                    label = new { type = "string", description = "Optional display label for the output port." },
                    name = new { type = "string", description = "C# output variable name. Must be a valid identifier." },
                    type_hint = new { type = "string", description = "Optional output type hint for documentation/aliasing." }
                }
            };

            var helperComponentSchema = new
            {
                type = "object",
                properties = new
                {
                    alias_id = new { type = "string", description = "Temporary unique helper alias used by local connections." },
                    name = new { type = "string", description = "Grasshopper helper component name or type." },
                    component_guid = new { type = "string", description = "Exact helper component GUID when known." },
                    label = new { type = "string", description = "Optional helper nickname." },
                    x = new { type = "number", description = "Canvas X coordinate." },
                    y = new { type = "number", description = "Canvas Y coordinate." },
                    value = new { type = "string", description = "Optional initial helper value." },
                    min = new { type = "number", description = "Optional slider minimum." },
                    max = new { type = "number", description = "Optional slider maximum." },
                    decimals = new { type = "integer", description = "Optional numeric precision." }
                },
                required = new[] { "alias_id", "x", "y" }
            };

            var connectionSchema = new
            {
                type = "object",
                properties = new
                {
                    from_alias = new { type = "string", description = "Source alias_id from script or helper components." },
                    from_index = new { type = "integer", description = "Source output port index." },
                    to_alias = new { type = "string", description = "Target alias_id from script or helper components." },
                    to_index = new { type = "integer", description = "Target input port index." }
                },
                required = new[] { "from_alias", "from_index", "to_alias", "to_index" }
            };

            return new
            {
                type = "function",
                function = new
                {
                    name = "create_csharp_script_component",
                    description = "Create a new C# Script component with ports, body, optional helper components, and optional connections. Use this as the primary tool for C#-first modeling.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            alias_id = new { type = "string", description = "Optional temporary alias for connecting helpers in this tool call." },
                            name = new { type = "string", description = "Script component name/nickname." },
                            label = new { type = "string", description = "Optional display label; name is preferred when both are present." },
                            x = new { type = "number", description = "Canvas X coordinate." },
                            y = new { type = "number", description = "Canvas Y coordinate." },
                            inputs = new { type = "array", items = inputPortSchema },
                            outputs = new { type = "array", items = outputPortSchema, description = "Output port definitions exposed by the script." },
                            body = new { type = "string", description = "C# script body. Include only the code that belongs in the component body, not markdown." },
                            components = new { type = "array", items = helperComponentSchema },
                            connections = new { type = "array", items = connectionSchema, description = "Optional local connections among the new script and helper components." },
                            group_name = new { type = "string", description = "Optional group name for created script/helpers." },
                            summary = new { type = "string", description = ToolSummaryDescription },
                            summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
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
                    description = "Edit an existing C# Script component body. Use after read_component_script or get_component_context when repairing or improving an existing script.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            id = new { type = "string", description = "Target C# Script component public id or GUID." },
                            mode = new { type = "string", description = "Edit mode, normally set_body when replacing the script body." },
                            body = new { type = "string", description = "Replacement C# script body when mode writes code." },
                            summary = new { type = "string", description = ToolSummaryDescription },
                            summary_detail = new { type = "string", description = ToolSummaryDetailDescription }
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
            blocked.Add("capture_rhino_viewport");
            blocked.Add("prepare_visual_review_preview");
            if (_agentMode == AgentMode.SelfTrain)
            {
                blocked.Add("create_gh_skill");
            }
            if (_layoutMode == LayoutMode.Battery)
            {
                blocked.Add("create_csharp_script_component");
            }
            else if (_layoutMode == LayoutMode.CSharpFirst)
            {
                blocked.Add("create_component_graph");
                blocked.Add("create_script_component_graph");
                blocked.Add("gh_native_script_editor");
                blocked.Add("modify_gh_component_ports");
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
                "check_gh_errors",
                "search_component_library",
                "search_gh_component_catalog",
                "query_gh_components",
                "get_component_context",
                "read_component_script",
                "read_skill_file",
                "read_reference_json",
                "import_reference_gh",
                "web_research",
                ShowPlanStepsTool.FunctionName
            };

            return toolDefinitions
                .Where(t => allowed.Contains(GetToolDefinitionName(t) ?? ""))
                .ToArray();
        }

        private static object[] FilterToolsForVisionContext(object[] toolDefinitions)
        {
            return toolDefinitions;
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
                        setFn["description"] = "C# priority helper value tool. Use only for non-script helper values such as Slider or Panel. Do not use it to edit C# Script source; use edit_csharp_script_component for C# body edits.";
                    }
                    return setJo;
                }
                if (string.Equals(name, "modify_gh_component_ports", StringComparison.OrdinalIgnoreCase) && _layoutMode == LayoutMode.CSharpFirst)
                {
                    JObject portJo = JObject.FromObject(toolDefinition);
                    var portFn = portJo["function"] as JObject;
                    if (portFn != null)
                    {
                        portFn["description"] = "C# priority fallback repair tool for dynamic ports. Do not use this as the normal way to change C# Script inputs or outputs; prefer create_csharp_script_component for new scripts and edit_csharp_script_component for existing script logic. Use only when a C# Script or other variable-parameter component is visibly out of sync and a direct port repair is required.";
                    }
                    return portJo;
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
