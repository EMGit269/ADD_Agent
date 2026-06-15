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

        public static object String(string description)
        {
            return new { type = "string", description = description ?? "" };
        }

        public static object Integer(string description)
        {
            return new { type = "integer", description = description ?? "" };
        }

        public static object Number(string description)
        {
            return new { type = "number", description = description ?? "" };
        }

        public static object Boolean(string description)
        {
            return new { type = "boolean", description = description ?? "" };
        }

        public static object StringArray(string description)
        {
            return new
            {
                type = "array",
                items = new { type = "string" },
                description = description ?? ""
            };
        }
    }
}
