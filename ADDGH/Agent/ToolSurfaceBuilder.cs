using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDGH.Agent
{
    public sealed class ToolSurfaceRequest
    {
        public object[] ToolDefinitions { get; set; }
        public List<Func<object[], object[]>> PreFilters { get; private set; }
        public Func<object[], object[]> WorkflowFilter { get; set; }
        public bool UseWorkflowFilter { get; set; }
        public WorkflowRoute Route { get; set; }
        public Action<string> LogDebug { get; set; }

        public ToolSurfaceRequest()
        {
            PreFilters = new List<Func<object[], object[]>>();
        }

        public ToolSurfaceRequest AddPreFilter(Func<object[], object[]> filter)
        {
            if (filter != null)
                PreFilters.Add(filter);
            return this;
        }
    }

    public sealed class ToolSurfaceBuilder
    {
        public object[] Build(ToolSurfaceRequest request)
        {
            if (request == null) return null;
            object[] current = request.ToolDefinitions;
            if (current == null) return null;

            foreach (var filter in request.PreFilters ?? new List<Func<object[], object[]>>())
            {
                if (filter == null) continue;
                current = filter(current) ?? current;
            }

            int beforeWorkflow = current.Length;
            if (request.UseWorkflowFilter && request.WorkflowFilter != null)
                current = request.WorkflowFilter(current) ?? current;

            request.LogDebug?.Invoke(
                "Tool surface built: "
                + (request.ToolDefinitions?.Length ?? 0)
                + " -> "
                + beforeWorkflow
                + " -> "
                + (current?.Length ?? 0)
                + " for "
                + (request.Route?.Intent.ToString() ?? "unknown"));

            return current?.ToArray();
        }
    }
}
