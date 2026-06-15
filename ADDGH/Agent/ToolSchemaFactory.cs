namespace ADDGH.Agent
{
    public static class ToolSchemaFactory
    {
        public static object Function(string name, string description, object properties, string[] required)
        {
            return new
            {
                type = "function",
                function = new
                {
                    name,
                    description,
                    parameters = new
                    {
                        type = "object",
                        properties,
                        required = required ?? new string[0],
                        additionalProperties = false
                    }
                }
            };
        }
    }
}
