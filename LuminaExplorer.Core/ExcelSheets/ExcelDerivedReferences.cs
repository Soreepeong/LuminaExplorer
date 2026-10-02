using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Lumina.Excel.Sheets;
using LuminaExplorer.Core.GameDataNames;
using LuminaExplorer.Core.SqPackPath;

namespace LuminaExplorer.Core.ExcelSheets;

/// <summary>A game path that a numeric cell refers to, such as the texture of an icon ID.</summary>
/// <param name="ColumnIndex">Index of the column in the header.</param>
/// <param name="SourceText">The value of the cell, as text, such as <c>26003</c> or <c>e0100 v1 (top)</c>.</param>
/// <param name="Path">Game path, without a leading slash; folders end with a slash.</param>
/// <param name="IsFolder">Whether <paramref name="Path"/> is a folder.</param>
public readonly record struct ExcelDerivedReference(int ColumnIndex, string SourceText, string Path, bool IsFolder);

/// <summary>The columns of a sheet that refer to game paths by number; see <see cref="ExcelDerivedReferences"/>.
/// </summary>
public sealed class ExcelDerivedReferencePlan {
    internal ExcelDerivedReferencePlan(
        ExcelSheetSource source,
        (int Column, ExcelDerivedReferences.ColumnKind Kind, string FieldName)[] columns,
        int equipSlotCategoryColumn)
    {
        this.Source = source;
        this.Columns = columns;
        this.EquipSlotCategoryColumn = equipSlotCategoryColumn;
        this.ColumnMask = new bool[source.Columns.Length];
        foreach (var c in columns)
            this.ColumnMask[c.Column] = true;
    }

    public ExcelSheetSource Source { get; }

    internal (int Column, ExcelDerivedReferences.ColumnKind Kind, string FieldName)[] Columns { get; }

    internal int EquipSlotCategoryColumn { get; }

    private bool[] ColumnMask { get; }

    /// <summary>Tests if a column may refer to game paths.</summary>
    public bool HasColumn(int columnIndex) =>
        columnIndex >= 0 && columnIndex < this.ColumnMask.Length && this.ColumnMask[columnIndex];
}

/// <summary>
/// Finds the game paths that numeric cells refer to, using the field kinds of EXDSchema: icons
/// (<c>ui/icon/026000/026003.tex</c>), model IDs (<c>chara/equipment/e0100/</c>,
/// <c>chara/weapon/w0201/obj/body/b0043/model/w0201b0043.mdl</c>), and links to <c>ModelChara</c>
/// (<c>chara/monster/m0361/obj/body/b0001/model/m0361b0001.mdl</c>). Only model paths that exist are given.
/// </summary>
/// <remarks>Thread safe; existence checks are cached.</remarks>
public sealed class ExcelDerivedReferences {
    private static readonly string[] DemihumanSlots = ["met", "top", "glv", "dwn", "sho"];

    private readonly GamePathResolver _paths;
    private readonly ConcurrentDictionary<string, string?> _modelPaths = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, string> _iconPaths = new();
    private readonly Lazy<Dictionary<uint, (string? Path, string Text)>> _modelChara;
    private readonly Lazy<Dictionary<uint, EquipSlot>> _equipSlots;

    private static readonly ConditionalWeakTable<GamePathResolver, ExcelDerivedReferences> Instances = new();

    public ExcelDerivedReferences(GamePathResolver paths)
    {
        this._paths = paths;
        this._modelChara = new(this.LoadModelChara, LazyThreadSafetyMode.ExecutionAndPublication);
        this._equipSlots = new(this.LoadEquipSlots, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public GamePathResolver Paths => this._paths;

    /// <summary>Gets the instance shared by the users of a resolver.</summary>
    public static ExcelDerivedReferences Get(GamePathResolver paths) => Instances.GetValue(paths, x => new(x));

    /// <summary>Gets whether the sheets used by <see cref="Derive"/> have been loaded.</summary>
    public bool IsPrepared =>
        this._modelChara.IsValueCreated && this._equipSlots.IsValueCreated && this._paths.IsFolderListLoaded;

    /// <summary>Loads the sheets and the folder list used by <see cref="Derive"/>, which may take a while; call from a
    /// background thread.</summary>
    public void Prepare()
    {
        this._paths.LoadFolderList();
        _ = this._modelChara.Value;
        _ = this._equipSlots.Value;
    }

    internal enum ColumnKind {
        Icon,
        Model,
        ModelCharaLink,

        /// <summary>The <c>Model</c> column of <c>ModelChara</c> itself.</summary>
        ModelCharaRow,
    }

    /// <summary>Finds the columns of a sheet that may refer to game paths.</summary>
    /// <returns>The plan, or <c>null</c> if no column does, or the schema does not match.</returns>
    public ExcelDerivedReferencePlan? CreatePlan(ExcelSheetSource source, ExcelSchemaBinding? binding)
    {
        if (binding is not { IsMatched: true })
            return null;

        var columns = new List<(int, ColumnKind, string)>();
        var isModelChara = string.Equals(source.SheetName, "ModelChara", StringComparison.OrdinalIgnoreCase);
        for (var i = 0; i < binding.Fields.Length; i++) {
            if (binding.Fields[i] is not { } field || !source.Columns[i].IsNumeric)
                continue;

            switch (field.Kind) {
                case ExcelSchemaFieldKind.Icon:
                    columns.Add((i, ColumnKind.Icon, field.Name));
                    break;
                case ExcelSchemaFieldKind.ModelId:
                    columns.Add((i, ColumnKind.Model, field.Name));
                    break;
                case ExcelSchemaFieldKind.Link
                    when field.Targets.Contains("ModelChara", StringComparer.OrdinalIgnoreCase):
                    columns.Add((i, ColumnKind.ModelCharaLink, field.Name));
                    break;
                case ExcelSchemaFieldKind.Scalar when isModelChara && field.Name == "Model":
                    columns.Add((i, ColumnKind.ModelCharaRow, field.Name));
                    break;
            }
        }

        if (columns.Count == 0)
            return null;

        var escColumn = binding.FindTopLevelColumn("EquipSlotCategory");
        if (escColumn != -1 &&
            (binding.Fields[escColumn] is not { Kind: ExcelSchemaFieldKind.Link } escField ||
                !escField.Targets.Contains("EquipSlotCategory", StringComparer.OrdinalIgnoreCase)))
            escColumn = -1;

        return new(source, columns.ToArray(), escColumn);
    }

    /// <summary>Finds the game paths that the cells of a row refer to.</summary>
    /// <param name="plan">Plan from <see cref="CreatePlan"/>.</param>
    /// <param name="page">Page containing the row.</param>
    /// <param name="row">Row.</param>
    /// <param name="output">List to add the references to.</param>
    /// <param name="onlyColumn">Column to look at, or <c>-1</c> for all.</param>
    public void Derive(
        ExcelDerivedReferencePlan plan,
        ExcelSheetPage page,
        in ExcelSheetRow row,
        List<ExcelDerivedReference> output,
        int onlyColumn = -1)
    {
        foreach (var (column, kind, fieldName) in plan.Columns) {
            if (onlyColumn != -1 && column != onlyColumn)
                continue;

            object? value;
            try {
                value = plan.Source.Columns[column].ReadValue(page, row);
            } catch (Exception) {
                continue;
            }

            switch (kind) {
                case ColumnKind.Icon:
                    if (ExcelSheetFormatter.TryGetInteger(value, out var iconId) && iconId is > 0 and < 1000000) {
                        output.Add(
                            new(column, iconId.ToString(CultureInfo.InvariantCulture), this.GetIconPath(iconId), false));
                    }

                    break;

                case ColumnKind.Model:
                    this.DeriveModel(plan, page, row, column, fieldName, value, output);
                    break;

                case ColumnKind.ModelCharaLink:
                    if (ExcelSheetFormatter.TryGetInteger(value, out var modelCharaId) &&
                        modelCharaId is > 0 and <= uint.MaxValue &&
                        this._modelChara.Value.TryGetValue((uint) modelCharaId, out var linked) &&
                        linked.Path is { } linkedPath) {
                        output.Add(
                            new(
                                column,
                                modelCharaId.ToString(CultureInfo.InvariantCulture),
                                linkedPath,
                                linkedPath.EndsWith('/')));
                    }

                    break;

                case ColumnKind.ModelCharaRow:
                    if (this._modelChara.Value.TryGetValue(row.RowId, out var self) && self.Path is { } selfPath)
                        output.Add(new(column, self.Text, selfPath, selfPath.EndsWith('/')));
                    break;
            }
        }
    }

    /// <summary>Gets the path of an icon; the language specific one if only that exists.</summary>
    public string GetIconPath(long iconId) =>
        this._iconPaths.GetOrAdd(
            iconId,
            static (id, self) => {
                var path = ExcelSheetFormatter.GetIconPath(id);
                if (self._paths.FileExists(path))
                    return path;

                var en = path.Insert(path.LastIndexOf('/') + 1, "en/");
                return self._paths.FileExists(en) ? en : path;
            },
            this);

    private void DeriveModel(
        ExcelDerivedReferencePlan plan,
        ExcelSheetPage page,
        in ExcelSheetRow row,
        int column,
        string fieldName,
        object? value,
        List<ExcelDerivedReference> output)
    {
        ulong v;
        bool is64;
        switch (value) {
            case uint u:
                (v, is64) = (u, false);
                break;
            case int i:
                (v, is64) = (unchecked((uint) i), false);
                break;
            case ulong u:
                (v, is64) = (u, true);
                break;
            case long l:
                (v, is64) = (unchecked((ulong) l), true);
                break;
            default:
                return;
        }

        var a = (int) (v & 0xFFFF);
        var b = (int) ((v >> 16) & 0xFFFF);
        var c = (int) ((v >> 32) & 0xFFFF);
        if (a == 0)
            return;

        var formatted = ExcelSheetFormatter.FormatModelId(value) ?? v.ToString(CultureInfo.InvariantCulture);

        // The equipment slot of the row tells weapons and gear apart.
        if (plan.EquipSlotCategoryColumn != -1 &&
            ExcelSheetFormatter.TryGetInteger(
                plan.Source.Columns[plan.EquipSlotCategoryColumn].ReadValue(page, row),
                out var escId) &&
            escId is > 0 and <= uint.MaxValue &&
            this._equipSlots.Value.TryGetValue((uint) escId, out var slot)) {
            if (slot.IsWeapon) {
                if (is64 && this.GetWeaponPath(a, b) is { } weaponPath) {
                    output.Add(
                        new(column, $"w{a:D4}b{b:D4} v{c}", weaponPath, weaponPath.EndsWith('/')));
                }
            } else if (slot.Kind is { } kind && this.GetGearPath(kind, a) is { } gearPath) {
                output.Add(new(column, $"{kind[0]}{a:D4} v{b} ({slot.Slot})", gearPath, true));
            }

            return;
        }

        // Otherwise, guess from the field name and the value, and use the first that exists.
        var name = fieldName.ToLowerInvariant();
        var weaponFirst = name.Contains("weapon") || (is64 && c != 0);
        var accessoryFirst = name.Contains("ear") || name.Contains("neck") || name.Contains("wrist") ||
            name.Contains("ring") || name.Contains("finger");
        string? path = null;
        if (is64 && weaponFirst)
            path = this.GetWeaponPath(a, b);
        path ??= accessoryFirst
            ? this.GetGearPath("accessory", a) ?? this.GetGearPath("equipment", a)
            : this.GetGearPath("equipment", a) ?? this.GetGearPath("accessory", a);
        if (path is null && is64 && !weaponFirst)
            path = this.GetWeaponPath(a, b);
        if (path is not null)
            output.Add(new(column, formatted, path, path.EndsWith('/')));
    }

    /// <summary>Gets the model of a weapon if it exists, or its folder if only that exists.</summary>
    public string? GetWeaponPath(int weapon, int body) =>
        this.GetIdBodyPath("weapon", 'w', weapon, "body", 'b', body);

    /// <summary>Gets the model of a monster if it exists, or its folder if only that exists.</summary>
    public string? GetMonsterPath(int monster, int body) =>
        this.GetIdBodyPath("monster", 'm', monster, "body", 'b', body);

    /// <summary>Gets the folder of a demihuman if it exists.</summary>
    public string? GetDemihumanPath(int demihuman, int equipment) =>
        this.GetIdBodyPath("demihuman", 'd', demihuman, "equipment", 'e', equipment);

    /// <summary>Gets the folder of an equipment (<c>equipment</c>) or accessory (<c>accessory</c>) set, if it
    /// exists.</summary>
    public string? GetGearPath(string kind, int setId) =>
        setId == 0
            ? null
            : this._modelPaths.GetOrAdd(
                $"{kind[0]}{setId:D4}",
                static (_, x) => {
                    var (self, kind, setId) = x;
                    var folder = $"chara/{kind}/{kind[0]}{setId:D4}/";
                    return self._paths.FileExists($"{folder}{kind[0]}{setId:D4}.imc") ||
                        self._paths.FolderExists(folder)
                            ? folder
                            : null;
                },
                (this, kind, setId));

    private string? GetIdBodyPath(string kind, char idPrefix, int id, string subKind, char subPrefix, int subId) =>
        id == 0
            ? null
            : this._modelPaths.GetOrAdd(
                $"{idPrefix}{id:D4}{subPrefix}{subId:D4}",
                static (stem, x) => {
                    var (self, kind, idPrefix, id, subKind, subPrefix, subId) = x;
                    var leaf = $"chara/{kind}/{idPrefix}{id:D4}/obj/{subKind}/{subPrefix}{subId:D4}/";
                    if (kind == "demihuman") {
                        return self._paths.FileExists($"{leaf}{subPrefix}{subId:D4}.imc") ||
                            DemihumanSlots.Any(s => self._paths.FileExists($"{leaf}model/{stem}_{s}.mdl"))
                                ? leaf
                                : null;
                    }

                    var model = $"{leaf}model/{stem}.mdl";
                    if (self._paths.FileExists(model))
                        return model;
                    return self._paths.FileExists($"{leaf}{subPrefix}{subId:D4}.imc") ? leaf : null;
                },
                (this, kind, idPrefix, id, subKind, subPrefix, subId));

    private Dictionary<uint, (string? Path, string Text)> LoadModelChara()
    {
        var result = new Dictionary<uint, (string?, string)>();
        try {
            foreach (var row in this._paths.GameData.GetExcelSheet<ModelChara>()!) {
                var path = row.Type switch {
                    2 => this.GetDemihumanPath(row.Model, row.Base),
                    3 => this.GetMonsterPath(row.Model, row.Base),
                    4 => this.GetWeaponPath(row.Model, row.Base),
                    _ => null,
                };
                if (path is null)
                    continue;

                var prefix = row.Type switch { 2 => "d", 3 => "m", _ => "w" };
                var sub = row.Type == 2 ? "e" : "b";
                result[row.RowId] = (path, $"{prefix}{row.Model:D4}{sub}{row.Base:D4} v{row.Variant}");
            }
        } catch (Exception) {
            // The sheet layout may not match this version of Lumina.Excel; no ModelChara references then.
        }

        return result;
    }

    private Dictionary<uint, EquipSlot> LoadEquipSlots()
    {
        var result = new Dictionary<uint, EquipSlot>();
        try {
            foreach (var row in this._paths.GameData.GetExcelSheet<EquipSlotCategory>()!) {
                if (row.MainHand > 0 || row.OffHand > 0)
                    result[row.RowId] = new(true, null, null);
                else if (CharaFolderNames.GetGearSlot(row) is ({ } kind, { } slot))
                    result[row.RowId] = new(false, kind, slot);
                else
                    result[row.RowId] = new(false, null, null);
            }
        } catch (Exception) {
            // Model IDs are guessed then.
        }

        return result;
    }

    private readonly record struct EquipSlot(bool IsWeapon, string? Kind, string? Slot);
}
