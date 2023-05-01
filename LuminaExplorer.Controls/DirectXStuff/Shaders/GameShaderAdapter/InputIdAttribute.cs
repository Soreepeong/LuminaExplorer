using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter; 

[MeansImplicitUse]
[AttributeUsage(AttributeTargets.Struct)]
public class InputIdAttribute : Attribute {
    public InputIdAttribute(InputId id) => Id = id;

    public InputId Id { get; }

    public static IEnumerable<Type> FindAllImplementors() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(x => x.GetTypes())
            .Select(x => (x, x.GetCustomAttribute<InputIdAttribute>()))
            .Where(x => x.Item2 is not null)
            .Select(x => x.Item1);
}
