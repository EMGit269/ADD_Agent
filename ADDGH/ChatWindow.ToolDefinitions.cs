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
                        description = "Modify dynamic component ports. In C# priority mode this is a fallback repair tool only; normal C# Script interface changes should use C# script creation/editing tools.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                prompt = new { type = "string", description = "Description" },
                                intent = new { type = "string", description = "Description" },
                                use_uploaded_images = new { type = "boolean", description = "Description" },
                                aspect_ratio = new { type = "string", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "prompt", "intent", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "ensure_gh_canvas",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
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
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "add_gh_component",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                name = new { type = "string", description = "Description" },
                                component_guid = new { type = "string", description = "Description" },
                                x = new { type = "number", description = "Description" },
                                y = new { type = "number", description = "Description" },
                                label = new { type = "string", description = "Description" },
                                graph_mapper_type = new { type = "string", description = "Description" },
                                value = new { type = "string", description = "Description" },
                                min = new { type = "number", description = "Description" },
                                max = new { type = "number", description = "Description" },
                                decimals = new { type = "integer", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "x", "y", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "connect_gh_components",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                from_id = new { type = "string", description = "Description" },
                                from_index = new { type = "integer", description = "Description" },
                                from_port_label = new { type = "string", description = "Description" },
                                to_id = new { type = "string", description = "Description" },
                                to_index = new { type = "integer", description = "Description" },
                                to_port_label = new { type = "string", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "from_id", "to_id", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "remove_gh_component",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "id", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "set_gh_component_value",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "Description" },
                                value = new { type = "string", description = "Description" },
                                property = new { type = "string", description = "Description" },
                                graph_mapper_type = new { type = "string", description = "Description" },
                                min = new { type = "number", description = "Description" },
                                max = new { type = "number", description = "Description" },
                                decimals = new { type = "integer", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "id", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "remove_gh_connection",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                from_id = new { type = "string", description = "Description" },
                                from_index = new { type = "integer", description = "Description" },
                                to_id = new { type = "string", description = "Description" },
                                to_index = new { type = "integer", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "from_id", "from_index", "to_id", "to_index", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "create_component_graph",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                components = new {
                                    type = "array",
                                    items = new {
                                        type = "object",
                                        properties = new {
                                            alias_id = new { type = "string", description = "Description" },
                                            name = new { type = "string", description = "Description" },
                                            component_guid = new { type = "string", description = "Description" },
                                            label = new { type = "string", description = "Description" },
                                            x = new { type = "number", description = "Description" },
                                            y = new { type = "number", description = "Description" },
                                            value = new { type = "string", description = "Description" },
                                            graph_mapper_type = new { type = "string", description = "Description" },
                                            min = new { type = "number", description = "Description" },
                                            max = new { type = "number", description = "Description" },
                                            decimals = new { type = "integer", description = "Description" }
                                        },
                                        required = new[] { "alias_id", "x", "y" }
                                    }
                                },
                                connections = new {
                                    type = "array",
                                    items = new {
                                        type = "object",
                                        properties = new {
                                            from_alias = new { type = "string", description = "Description" },
                                            from_index = new { type = "integer", description = "Description" },
                                            to_alias = new { type = "string", description = "Description" },
                                            to_index = new { type = "integer", description = "Description" }
                                        },
                                        required = new[] { "from_alias", "from_index", "to_alias", "to_index" }
                                    }
                                },
                                group_name = new { type = "string", description = "Description" },
                                auto_group = new { type = "boolean", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "components", "connections", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "recompute_gh_canvas",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "gh_native_script_editor",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "Description" },
                                mode = new { type = "string", description = "Description" },
                                code = new { type = "string", description = "Description" },
                                language = new { type = "string", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "id", "mode", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "check_gh_errors",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "set_gh_component_status",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "Description" },
                                preview = new { type = "boolean", description = "Description" },
                                enabled = new { type = "boolean", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "id", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "set_all_csharp_script_previews",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                preview = new { type = "boolean", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "preview", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "modify_gh_component_ports",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "Description" },
                                is_input = new { type = "boolean", description = "Description" },
                                action = new { type = "string", description = "Description" },
                                port_name = new { type = "string", description = "Description" },
                                type_hint = new { type = "string", description = "Optional when adding a C# Script port. Prefer Rhino C# Script menu names such as bool, int, string, double, Point3d, Point3dList, Vector3d, Plane, Line, Circle, Arc, Curve, Mesh, Surface, Brep, GeometryBase. Conversion-only helper hints such as curve[], circle[], double[], int[] only refresh ADD Agent aliases and are not native Rhino port hints." },
                                index = new { type = "integer", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
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
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "action", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "modify_gh_port_data",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "Description" },
                                is_input = new { type = "boolean", description = "Description" },
                                index = new { type = "integer", description = "Description" },
                                operation = new { type = "string", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "id", "is_input", "index", "operation", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "search_component_library",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                keyword = new { type = "string", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "keyword", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "search_gh_component_catalog",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                query = new { type = "string", description = "Description" },
                                max_results = new { type = "integer", description = "Description" },
                                category_contains = new { type = "string", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "query", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "web_research",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                mode = new { type = "string", description = "Description" },
                                query = new { type = "string", description = "Description" },
                                url = new { type = "string", description = "Description" },
                                allowed_domains = new { type = "array", items = new { type = "string" }, description = "Description" },
                                max_results = new { type = "integer", description = "Description" },
                                max_chars = new { type = "integer", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "query_gh_components",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "Description" },
                                name_contains = new { type = "string", description = "Description" },
                                has_errors = new { type = "boolean", description = "Description" },
                                is_script = new { type = "boolean", description = "Description" },
                                has_connections = new { type = "boolean", description = "Description" },
                                port_name_contains = new { type = "string", description = "Description" },
                                max_results = new { type = "integer", description = "Description" },
                                neighbor_depth = new { type = "integer", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "get_component_context",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "Description" },
                                depth = new { type = "integer", description = "Description" },
                                include_script_bodies = new { type = "boolean", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "id", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "read_component_script",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "id", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "create_gh_skill",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                file_name = new { type = "string", description = "Description" },
                                name = new { type = "string", description = "Description" },
                                description = new { type = "string", description = "Description" },
                                content = new { type = "string", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "file_name", "name", "description", "content", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "read_skill_file",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                file_name = new { type = "string", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "file_name", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "read_reference_json",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                file_name = new { type = "string", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
                            },
                            required = new[] { "file_name", "summary" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "import_reference_gh",
                        description = "Description",
                        parameters = new {
                            type = "object",
                            properties = new {
                                file_name = new { type = "string", description = "Description" },
                                offset_x = new { type = "number", description = "Description" },
                                offset_y = new { type = "number", description = "Description" },
                                group_name = new { type = "string", description = "Description" },
                                summary = new { type = "string", description = "Description" },
                                summary_detail = new { type = "string", description = "Description" }
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
                    description = "Description",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            mode = new { type = "string", description = "Description" },
                            scripts = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        alias_id = new { type = "string", description = "Description" },
                                        label = new { type = "string", description = "Description" },
                                        x = new { type = "number", description = "Description" },
                                        y = new { type = "number", description = "Description" },
                                        source = new { type = "string", description = "Description" },
                                        inputs = new { type = "array", items = portSchema },
                                        output_count = new { type = "integer", description = "Description" },
                                        outputs = new { type = "array", items = portSchema, description = "Description" }
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
                                        alias_id = new { type = "string", description = "Description" },
                                        name = new { type = "string", description = "Description" },
                                        component_guid = new { type = "string", description = "Description" },
                                        label = new { type = "string", description = "Description" },
                                        x = new { type = "number", description = "Description" },
                                        y = new { type = "number", description = "Description" },
                                        value = new { type = "string", description = "Description" },
                                        min = new { type = "number", description = "Description" },
                                        max = new { type = "number", description = "Description" },
                                        decimals = new { type = "integer", description = "Description" }
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
                                        from_alias = new { type = "string", description = "Description" },
                                        from_index = new { type = "integer", description = "Description" },
                                        to_alias = new { type = "string", description = "Description" },
                                        to_index = new { type = "integer", description = "Description" }
                                    },
                                    required = new[] { "from_alias", "from_index", "to_alias", "to_index" }
                                }
                            },
                            group_name = new { type = "string", description = "Description" },
                            summary = new { type = "string", description = "Description" },
                            summary_detail = new { type = "string", description = "Description" }
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
                    name = new { type = "string", description = "Description" },
                    type_hint = new { type = "string", description = "Description" }
                },
                required = new[] { "name" }
            };

            var outputPortSchema = new
            {
                type = "object",
                properties = new
                {
                    label = new { type = "string", description = "Description" },
                    name = new { type = "string", description = "Description" },
                    type_hint = new { type = "string", description = "Description" }
                }
            };

            var helperComponentSchema = new
            {
                type = "object",
                properties = new
                {
                    alias_id = new { type = "string", description = "Description" },
                    name = new { type = "string", description = "Description" },
                    component_guid = new { type = "string", description = "Description" },
                    label = new { type = "string", description = "Description" },
                    x = new { type = "number", description = "Description" },
                    y = new { type = "number", description = "Description" },
                    value = new { type = "string", description = "Description" },
                    min = new { type = "number", description = "Description" },
                    max = new { type = "number", description = "Description" },
                    decimals = new { type = "integer", description = "Description" }
                },
                required = new[] { "alias_id", "x", "y" }
            };

            var connectionSchema = new
            {
                type = "object",
                properties = new
                {
                    from_alias = new { type = "string", description = "Description" },
                    from_index = new { type = "integer", description = "Description" },
                    to_alias = new { type = "string", description = "Description" },
                    to_index = new { type = "integer", description = "Description" }
                },
                required = new[] { "from_alias", "from_index", "to_alias", "to_index" }
            };

            return new
            {
                type = "function",
                function = new
                {
                    name = "create_csharp_script_component",
                    description = "Description",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            alias_id = new { type = "string", description = "Description" },
                            name = new { type = "string", description = "Description" },
                            label = new { type = "string", description = "Description" },
                            x = new { type = "number", description = "Description" },
                            y = new { type = "number", description = "Description" },
                            inputs = new { type = "array", items = inputPortSchema },
                            outputs = new { type = "array", items = outputPortSchema, description = "Description" },
                            body = new { type = "string", description = "Description" },
                            components = new { type = "array", items = helperComponentSchema },
                            connections = new { type = "array", items = connectionSchema, description = "Description" },
                            group_name = new { type = "string", description = "Description" },
                            summary = new { type = "string", description = "Description" },
                            summary_detail = new { type = "string", description = "Description" }
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
                    description = "Description",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            id = new { type = "string", description = "Description" },
                            mode = new { type = "string", description = "Description" },
                            body = new { type = "string", description = "Description" },
                            summary = new { type = "string", description = "Description" },
                            summary_detail = new { type = "string", description = "Description" }
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
