using System;
using System.Collections.Generic;
using System.Linq;
using ADDGH.Agent;
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
                GetWebResearchToolDefinition(),
                GetQueryGhComponentsToolDefinition(),
                GetComponentContextToolDefinition(),
                GetReadComponentScriptToolDefinition(),
                GetCreateGhSkillToolDefinition(),
                GetReadSkillFileToolDefinition(),
                GetReadReferenceJsonToolDefinition(),
                GetImportReferenceGhToolDefinition(),
                ShowReferenceOptionsTool.GetApiToolDefinition(),
                ShowPlanStepsTool.GetApiToolDefinition()
            };
            return BuildAgentToolSurface(toolDefinitions);
        }

        private static object GetCreateScriptComponentGraphToolDefinition()
        {
            var portSchema = ToolSchemaFactory.Object(
                new
                {
                    name = ToolSchemaFactory.String("C# input variable name. Must be a valid identifier and must not collide with reserved/output variables."),
                    type_hint = ToolSchemaFactory.String("Optional input type hint. Prefer Rhino C# Script menu names for real port hints: bool, int, string, double, Point3d, Point3dList, Vector3d, Plane, Interval, Line, Circle, Arc, Curve, Polyline, Rectangle3d, Mesh, Surface, Brep, GeometryBase, TextDot, TextEntity. ADD Agent also accepts conversion-only helper hints such as curve[], circle[], double[], int[]; these are not native Rhino port hints and only drive defensive alias injection.")
                },
                new[] { "name" });

            var scriptSchema = ToolSchemaFactory.Object(
                new
                {
                    alias_id = ToolSchemaFactory.String("Temporary unique script alias used by connections in this batch."),
                    label = ToolSchemaFactory.String("Optional display nickname for the script component."),
                    x = ToolSchemaFactory.Number("Canvas X coordinate."),
                    y = ToolSchemaFactory.Number("Canvas Y coordinate."),
                    source = ToolSchemaFactory.String("Script source/body."),
                    inputs = ToolSchemaFactory.Array(portSchema),
                    output_count = ToolSchemaFactory.Integer("Number of output ports when explicit outputs are not supplied."),
                    outputs = ToolSchemaFactory.Array(portSchema, "Explicit output port definitions.")
                },
                new[] { "alias_id", "x", "y", "source" });

            var helperSchema = ToolSchemaFactory.Object(
                new
                {
                    alias_id = ToolSchemaFactory.String("Temporary unique helper alias used by connections in this batch."),
                    name = ToolSchemaFactory.String("Grasshopper helper component name or type."),
                    component_guid = ToolSchemaFactory.String("Exact Grasshopper component GUID when known."),
                    label = ToolSchemaFactory.String("Optional display nickname."),
                    x = ToolSchemaFactory.Number("Canvas X coordinate."),
                    y = ToolSchemaFactory.Number("Canvas Y coordinate."),
                    value = ToolSchemaFactory.String("Optional initial helper value."),
                    min = ToolSchemaFactory.Number("Optional slider minimum."),
                    max = ToolSchemaFactory.Number("Optional slider maximum."),
                    decimals = ToolSchemaFactory.Integer("Optional numeric precision.")
                },
                new[] { "alias_id", "x", "y" });

            var connectionSchema = ToolSchemaFactory.Object(
                new
                {
                    from_alias = ToolSchemaFactory.String("Source alias_id from scripts or helper components in this batch."),
                    from_index = ToolSchemaFactory.Integer("Source output port index."),
                    to_alias = ToolSchemaFactory.String("Target alias_id from scripts or helper components in this batch."),
                    to_index = ToolSchemaFactory.Integer("Target input port index.")
                },
                new[] { "from_alias", "from_index", "to_alias", "to_index" });

            return ToolSchemaFactory.Function(
                "create_script_component_graph",
                "Legacy batch tool that creates script components, helper components, and connections together. Prefer create_csharp_script_component for new C# Script workflows unless this compatibility path is specifically needed.",
                new
                {
                    mode = ToolSchemaFactory.String("Creation mode for the legacy graph path."),
                    scripts = ToolSchemaFactory.Array(scriptSchema),
                    components = ToolSchemaFactory.Array(helperSchema),
                    connections = ToolSchemaFactory.Array(connectionSchema),
                    group_name = ToolSchemaFactory.String("Optional group name for created scripts and helpers."),
                    summary = ToolSchemaFactory.String(ToolSummaryDescription),
                    summary_detail = ToolSchemaFactory.String(ToolSummaryDetailDescription)
                },
                new[] { "mode", "scripts", "connections", "summary" });
        }

        private static object GetWebResearchToolDefinition()
        {
            return ToolSchemaFactory.Function(
                "web_research",
                "Search or open local mirrored documentation when local knowledge is insufficient. This tool does not access the public internet. For RhinoCommon/Grasshopper API signature lookup, prefer mode=api_pipeline before local search.",
                new
                {
                    mode = ToolSchemaFactory.String("Research mode: api_pipeline for RhinoCommon/Grasshopper API lookup, search for local documentation search, or fetch for a known mirrored documentation URL."),
                    query = ToolSchemaFactory.String("Search/API query. For api_pipeline include candidate type/method names and concept words, for example Brep.CreateFromRevolution surface of revolution."),
                    url = ToolSchemaFactory.String("Mirrored documentation URL when mode=fetch. Only URLs available in the local documentation mirror can be fetched."),
                    allowed_domains = ToolSchemaFactory.StringArray("Optional domain allowlist for focused local documentation lookup, such as developer.rhino3d.com or mcneel.github.io."),
                    max_results = ToolSchemaFactory.Integer("Maximum search results to retrieve."),
                    max_chars = ToolSchemaFactory.Integer("Maximum returned text characters; keep modest unless detailed source context is required."),
                    summary = ToolSchemaFactory.String(ToolSummaryDescription),
                    summary_detail = ToolSchemaFactory.String(ToolSummaryDetailDescription)
                },
                new[] { "summary" });
        }

        private static object GetQueryGhComponentsToolDefinition()
        {
            return ToolSchemaFactory.Function(
                "query_gh_components",
                "Query a focused subset of current Grasshopper components by id, name, error state, script status, connection state, or port name.",
                new
                {
                    id = ToolSchemaFactory.String("Optional exact public id or GUID to query."),
                    name_contains = ToolSchemaFactory.String("Optional substring filter for component name/nickname."),
                    has_errors = ToolSchemaFactory.Boolean("Optional filter for components with runtime errors."),
                    is_script = ToolSchemaFactory.Boolean("Optional filter for script components."),
                    has_connections = ToolSchemaFactory.Boolean("Optional filter for components with any wires."),
                    port_name_contains = ToolSchemaFactory.String("Optional substring filter for input/output port names."),
                    max_results = ToolSchemaFactory.Integer("Maximum matching components to return."),
                    neighbor_depth = ToolSchemaFactory.Integer("Optional neighbor traversal depth around matches."),
                    summary = ToolSchemaFactory.String(ToolSummaryDescription),
                    summary_detail = ToolSchemaFactory.String(ToolSummaryDetailDescription)
                },
                new[] { "summary" });
        }

        private static object GetComponentContextToolDefinition()
        {
            return ToolSchemaFactory.Function(
                "get_component_context",
                "Read focused context around one component, including nearby components, ports, connections, and optionally script bodies.",
                new
                {
                    id = ToolSchemaFactory.String("Target component public id or GUID."),
                    depth = ToolSchemaFactory.Integer("Neighbor traversal depth. Use 1 for focused repair unless more graph context is needed."),
                    include_script_bodies = ToolSchemaFactory.Boolean("Whether to include full script bodies. Set true only when code content is needed."),
                    summary = ToolSchemaFactory.String(ToolSummaryDescription),
                    summary_detail = ToolSchemaFactory.String(ToolSummaryDetailDescription)
                },
                new[] { "id", "summary" });
        }

        private static object GetReadComponentScriptToolDefinition()
        {
            return ToolSchemaFactory.Function(
                "read_component_script",
                "Read the source/body of an existing script component. Use before editing or debugging an existing C# Script.",
                new
                {
                    id = ToolSchemaFactory.String("Target script component public id or GUID."),
                    summary = ToolSchemaFactory.String(ToolSummaryDescription),
                    summary_detail = ToolSchemaFactory.String(ToolSummaryDetailDescription)
                },
                new[] { "id", "summary" });
        }

        private static object GetReadSkillFileToolDefinition()
        {
            return ToolSchemaFactory.Function(
                "read_skill_file",
                "Load the full body of one relevant skill by file name or skill id. Use only after the skill summary indicates relevance; do not bulk-read unrelated skills.",
                new
                {
                    file_name = ToolSchemaFactory.String("Skill file name or id from the skill summary, for example official_x.md or trained_example.md."),
                    summary = ToolSchemaFactory.String(ToolSummaryDescription),
                    summary_detail = ToolSchemaFactory.String(ToolSummaryDetailDescription)
                },
                new[] { "file_name", "summary" });
        }

        private static object GetReadReferenceJsonToolDefinition()
        {
            return ToolSchemaFactory.Function(
                "read_reference_json",
                "Read one saved reference JSON from the reference directory after deciding it is relevant from the reference index or user selection.",
                new
                {
                    file_name = ToolSchemaFactory.String("Reference JSON file name under reference/. .json is optional."),
                    summary = ToolSchemaFactory.String(ToolSummaryDescription),
                    summary_detail = ToolSchemaFactory.String(ToolSummaryDetailDescription)
                },
                new[] { "file_name", "summary" });
        }

        private static object GetCreateGhSkillToolDefinition()
        {
            return ToolSchemaFactory.Function(
                "create_gh_skill",
                "Create a new reusable skill markdown file. Use only in explicit skill-authoring or self-training workflows after behavior is validated.",
                new
                {
                    file_name = ToolSchemaFactory.String("Markdown file name under skills/. Must be a safe file name; .md is optional."),
                    name = ToolSchemaFactory.String("Skill name for YAML frontmatter."),
                    description = ToolSchemaFactory.String("Short trigger description explaining when the skill should be used."),
                    content = ToolSchemaFactory.String("Skill markdown body with concrete procedure, constraints, and verification guidance."),
                    summary = ToolSchemaFactory.String(ToolSummaryDescription),
                    summary_detail = ToolSchemaFactory.String(ToolSummaryDetailDescription)
                },
                new[] { "file_name", "name", "description", "content", "summary" });
        }

        private static object GetImportReferenceGhToolDefinition()
        {
            return ToolSchemaFactory.Function(
                "import_reference_gh",
                "Import a saved .gh or .ghx reference file into the active Grasshopper canvas. Use only when the user wants to reuse/reference an existing saved canvas.",
                new
                {
                    file_name = ToolSchemaFactory.String("Reference .gh or .ghx file name under reference/."),
                    offset_x = ToolSchemaFactory.Number("Optional X offset applied to imported objects."),
                    offset_y = ToolSchemaFactory.Number("Optional Y offset applied to imported objects."),
                    group_name = ToolSchemaFactory.String("Optional group name for imported objects."),
                    summary = ToolSchemaFactory.String(ToolSummaryDescription),
                    summary_detail = ToolSchemaFactory.String(ToolSummaryDetailDescription)
                },
                new[] { "file_name", "summary" });
        }

        private static object GetCreateCSharpScriptComponentToolDefinition()
        {
            var inputPortSchema = ToolSchemaFactory.Object(
                new
                {
                    name = ToolSchemaFactory.String("C# input variable name. Must be a valid identifier."),
                    type_hint = ToolSchemaFactory.String("Optional C# Script input type hint, for example double, int, bool, Point3d, Curve, Brep, Mesh, or GeometryBase.")
                },
                new[] { "name" });

            var outputPortSchema = ToolSchemaFactory.Object(
                new
                {
                    label = ToolSchemaFactory.String("Optional display label for the output port."),
                    name = ToolSchemaFactory.String("C# output variable name. Must be a valid identifier."),
                    type_hint = ToolSchemaFactory.String("Optional output type hint for documentation/aliasing.")
                });

            var helperComponentSchema = ToolSchemaFactory.Object(
                new
                {
                    alias_id = ToolSchemaFactory.String("Temporary unique helper alias used by local connections."),
                    name = ToolSchemaFactory.String("Grasshopper helper component name or type."),
                    component_guid = ToolSchemaFactory.String("Exact helper component GUID when known."),
                    label = ToolSchemaFactory.String("Optional helper nickname."),
                    x = ToolSchemaFactory.Number("Canvas X coordinate."),
                    y = ToolSchemaFactory.Number("Canvas Y coordinate."),
                    value = ToolSchemaFactory.String("Optional initial helper value."),
                    min = ToolSchemaFactory.Number("Optional slider minimum."),
                    max = ToolSchemaFactory.Number("Optional slider maximum."),
                    decimals = ToolSchemaFactory.Integer("Optional numeric precision.")
                },
                new[] { "alias_id", "x", "y" });

            var connectionSchema = ToolSchemaFactory.Object(
                new
                {
                    from_alias = ToolSchemaFactory.String("Source alias_id from script or helper components."),
                    from_index = ToolSchemaFactory.Integer("Source output port index."),
                    to_alias = ToolSchemaFactory.String("Target alias_id from script or helper components."),
                    to_index = ToolSchemaFactory.Integer("Target input port index.")
                },
                new[] { "from_alias", "from_index", "to_alias", "to_index" });

            return ToolSchemaFactory.Function(
                "create_csharp_script_component",
                "Create a new C# Script component with ports, body, optional helper components, and optional connections. Use this as the primary tool for C#-first modeling.",
                new
                {
                    alias_id = ToolSchemaFactory.String("Optional temporary alias for connecting helpers in this tool call."),
                    name = ToolSchemaFactory.String("Script component name/nickname."),
                    label = ToolSchemaFactory.String("Optional display label; name is preferred when both are present."),
                    x = ToolSchemaFactory.Number("Canvas X coordinate."),
                    y = ToolSchemaFactory.Number("Canvas Y coordinate."),
                    inputs = ToolSchemaFactory.Array(inputPortSchema),
                    outputs = ToolSchemaFactory.Array(outputPortSchema, "Output port definitions exposed by the script."),
                    body = ToolSchemaFactory.String("C# script body. Include only the code that belongs in the component body, not markdown."),
                    components = ToolSchemaFactory.Array(helperComponentSchema),
                    connections = ToolSchemaFactory.Array(connectionSchema, "Optional local connections among the new script and helper components."),
                    group_name = ToolSchemaFactory.String("Optional group name for created script/helpers."),
                    summary = ToolSchemaFactory.String(ToolSummaryDescription),
                    summary_detail = ToolSchemaFactory.String(ToolSummaryDetailDescription)
                },
                new[] { "x", "y", "body", "summary" });
        }

        private static object GetEditCSharpScriptComponentToolDefinition()
        {
            return ToolSchemaFactory.Function(
                "edit_csharp_script_component",
                "Edit an existing C# Script component body. Use after read_component_script or get_component_context when repairing or improving an existing script.",
                new
                {
                    id = ToolSchemaFactory.String("Target C# Script component public id or GUID."),
                    mode = ToolSchemaFactory.String("Edit mode, normally set_body when replacing the script body."),
                    body = ToolSchemaFactory.String("Replacement C# script body when mode writes code."),
                    summary = ToolSchemaFactory.String(ToolSummaryDescription),
                    summary_detail = ToolSchemaFactory.String(ToolSummaryDetailDescription)
                },
                new[] { "id", "mode", "summary" });
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

        private static object[] FilterToolsForVisionContext(object[] toolDefinitions)
        {
            return toolDefinitions;
        }
    }
}
