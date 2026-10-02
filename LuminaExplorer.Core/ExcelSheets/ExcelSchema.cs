using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LuminaExplorer.Core.ExcelSheets;

/// <summary>Kind of a field in an EXDSchema sheet definition.</summary>
public enum ExcelSchemaFieldKind {
    Scalar,
    Link,
    Icon,
    ModelId,
    Color,
}

/// <summary>Conditional link: the target sheets depend on the value of another field in the same row.</summary>
/// <param name="SwitchField">Name of the top-level field to switch on.</param>
/// <param name="Cases">Target sheets by the value of the switch field.</param>
public sealed record ExcelSchemaLinkCondition(string SwitchField, IReadOnlyDictionary<long, string[]> Cases);

/// <summary>A single column of a sheet, as described by an EXDSchema definition (after flattening arrays).</summary>
public sealed class ExcelSchemaColumn {
    public ExcelSchemaColumn(
        string name,
        string topLevelName,
        ExcelSchemaFieldKind kind,
        string[] targets,
        ExcelSchemaLinkCondition? condition,
        string? comment)
    {
        this.Name = name;
        this.TopLevelName = topLevelName;
        this.Kind = kind;
        this.Targets = targets;
        this.Condition = condition;
        this.Comment = comment;
    }

    /// <summary>Gets the display name, e.g. <c>Name</c>, <c>BaseParam[2]</c>, or <c>Item[3].ReceiveCount[1]</c>.</summary>
    public string Name { get; }

    /// <summary>Gets the name of the top-level field containing this column, e.g. <c>Item</c>.</summary>
    public string TopLevelName { get; }

    public ExcelSchemaFieldKind Kind { get; }

    /// <summary>Gets the target sheets of an unconditional link; empty otherwise.</summary>
    public string[] Targets { get; }

    public ExcelSchemaLinkCondition? Condition { get; }

    /// <summary>Gets the comments of the field and its containing arrays, if any.</summary>
    public string? Comment { get; }

    /// <summary>Gets all sheets this column may link to.</summary>
    public IEnumerable<string> AllTargets =>
        this.Condition is { } c ? this.Targets.Concat(c.Cases.Values.SelectMany(x => x)).Distinct() : this.Targets;

    public string KindName => this.Kind switch {
        ExcelSchemaFieldKind.Scalar => "scalar",
        ExcelSchemaFieldKind.Link => "link",
        ExcelSchemaFieldKind.Icon => "icon",
        ExcelSchemaFieldKind.ModelId => "modelId",
        ExcelSchemaFieldKind.Color => "color",
        _ => this.Kind.ToString(),
    };

    /// <summary>Gets a multi-line description of the field, for tooltips.</summary>
    public string Description {
        get {
            var lines = new List<string> { $"Field: {this.Name}", $"Kind: {this.KindName}" };
            if (this.Targets.Length > 0)
                lines.Add($"Links to: {string.Join(", ", this.Targets)}");
            if (this.Condition is { } c) {
                lines.Add($"Links by {c.SwitchField}:");
                lines.AddRange(c.Cases.Select(x => $"  {x.Key}: {string.Join(", ", x.Value)}"));
            }

            if (!string.IsNullOrWhiteSpace(this.Comment))
                lines.Add(this.Comment.Trim());
            return string.Join('\n', lines);
        }
    }
}

/// <summary>An EXDSchema sheet definition, with its fields flattened into columns.</summary>
public sealed class ExcelSchemaSheet {
    private ExcelSchemaSheet(string name, string? displayField, ExcelSchemaColumn[] columns)
    {
        this.Name = name;
        this.DisplayField = displayField;
        this.Columns = columns;
    }

    public string Name { get; }

    /// <summary>Gets the name of the top-level field that best represents a row, if specified.</summary>
    public string? DisplayField { get; }

    /// <summary>Gets the columns, in the order of the definition (which is the order of offsets).</summary>
    public ExcelSchemaColumn[] Columns { get; }

    /// <summary>Parses an EXDSchema sheet definition.</summary>
    /// <exception cref="InvalidDataException">If the definition is invalid.</exception>
    public static ExcelSchemaSheet Parse(TextReader reader, string fallbackName)
    {
        var dto = Deserializer.Deserialize<SheetDto>(reader)
            ?? throw new InvalidDataException("The schema is empty.");
        if (dto.Fields is not { Count: > 0 } fields)
            throw new InvalidDataException("The schema has no fields.");

        var columns = new List<ExcelSchemaColumn>();
        var unknownIndex = 0;
        foreach (var field in fields)
            Flatten(columns, field, field.Name ?? $"Unknown{unknownIndex++}", null, null);

        return new(string.IsNullOrEmpty(dto.Name) ? fallbackName : dto.Name, dto.DisplayField, columns.ToArray());
    }

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static void Flatten(
        List<ExcelSchemaColumn> columns,
        FieldDto field,
        string path,
        string? topLevelName,
        string? parentComment)
    {
        topLevelName ??= path;
        var comment = string.IsNullOrWhiteSpace(field.Comment)
            ? parentComment
            : string.IsNullOrWhiteSpace(parentComment)
                ? field.Comment.Trim()
                : $"{parentComment}\n{field.Comment.Trim()}";

        var type = field.Type?.Trim() ?? "scalar";
        if (string.Equals(type, "array", StringComparison.OrdinalIgnoreCase)) {
            var count = field.Count ?? throw new InvalidDataException($"Array \"{path}\" has no count.");
            if (count is < 0 or > 65536)
                throw new InvalidDataException($"Array \"{path}\" has an invalid count {count}.");

            var subfields = field.Fields ?? [];
            for (var i = 0; i < count; i++) {
                var elementPath = $"{path}[{i}]";
                switch (subfields.Count) {
                    case 0:
                        columns.Add(new(elementPath, topLevelName, ExcelSchemaFieldKind.Scalar, [], null, comment));
                        break;
                    case 1:
                        Flatten(
                            columns,
                            subfields[0],
                            subfields[0].Name is { } n ? $"{elementPath}.{n}" : elementPath,
                            topLevelName,
                            comment);
                        break;
                    default:
                        var unknownIndex = 0;
                        foreach (var sub in subfields) {
                            Flatten(
                                columns,
                                sub,
                                $"{elementPath}.{sub.Name ?? $"Unknown{unknownIndex++}"}",
                                topLevelName,
                                comment);
                        }

                        break;
                }
            }

            return;
        }

        var kind = type.ToLowerInvariant() switch {
            "link" => ExcelSchemaFieldKind.Link,
            "icon" => ExcelSchemaFieldKind.Icon,
            "modelid" => ExcelSchemaFieldKind.ModelId,
            "color" => ExcelSchemaFieldKind.Color,
            _ => ExcelSchemaFieldKind.Scalar,
        };

        var targets = field.Targets?.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray() ?? [];
        ExcelSchemaLinkCondition? condition = null;
        if (field.Condition is { Switch: { } sw, Cases: { } cases } && !string.IsNullOrWhiteSpace(sw)) {
            var parsedCases = new SortedDictionary<long, string[]>();
            foreach (var (key, value) in cases) {
                if (long.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var k) &&
                    value is { Count: > 0 })
                    parsedCases[k] = value.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            }

            condition = new(sw, parsedCases);
        }

        if (kind != ExcelSchemaFieldKind.Link) {
            targets = [];
            condition = null;
        }

        columns.Add(new(path, topLevelName, kind, targets, condition, comment));
    }

    // ReSharper disable ClassNeverInstantiated.Local, UnusedAutoPropertyAccessor.Local
    private sealed class SheetDto {
        public string? Name { get; set; }
        public string? DisplayField { get; set; }
        public List<FieldDto>? Fields { get; set; }
    }

    private sealed class FieldDto {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public int? Count { get; set; }
        public string? Comment { get; set; }
        public List<FieldDto>? Fields { get; set; }
        public List<string>? Targets { get; set; }
        public ConditionDto? Condition { get; set; }
    }

    private sealed class ConditionDto {
        public string? Switch { get; set; }
        public Dictionary<string, List<string>>? Cases { get; set; }
    }
    // ReSharper restore ClassNeverInstantiated.Local, UnusedAutoPropertyAccessor.Local
}
