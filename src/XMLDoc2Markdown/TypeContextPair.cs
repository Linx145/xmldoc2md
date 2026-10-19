using System;
using XMLDoc2Markdown.Rendering;

namespace XMLDoc2Markdown;

internal class TypeContextPair
{
    public Type type;
    public RenderingContext context;

    public TypeContextPair(Type type, RenderingContext context)
    {
        this.type = type;
        this.context = context;
    }
}