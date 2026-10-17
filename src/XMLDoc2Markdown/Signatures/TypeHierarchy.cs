namespace XMLDoc2Markdown.Signatures;
using System;
using System.Collections.Generic;

internal static class TypeHierarchy
{
    internal static IEnumerable<Type> GetInheritanceHierarchy(this Type type)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            yield return current;
        }
    }
}
