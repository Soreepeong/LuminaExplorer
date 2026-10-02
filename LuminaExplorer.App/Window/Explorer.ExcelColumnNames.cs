using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LuminaExplorer.Core.ExcelSheets;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    // Sheet name -> EXDSchema field name per column index; empty if the sheet has no matching schema.
    private readonly ConcurrentDictionary<string, string?[]> _excelColumnNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Looks up the EXDSchema column names of the given sheets, so that <see cref="ResolveExcelColumnName"/> can
    /// answer without blocking.
    /// </summary>
    public async Task PrepareExcelColumnNames(IReadOnlyCollection<string> sheetNames, CancellationToken cancellationToken)
    {
        if (this._vfs is not { } vfs)
            return;

        var missing = sheetNames.Where(x => !this._excelColumnNames.ContainsKey(x)).ToArray();
        if (missing.Length == 0)
            return;

        var set = (await this.ExcelSchemaProvider.GetSchemaSetAsync().WaitAsync(cancellationToken)).Set;
        foreach (var sheetName in missing) {
            cancellationToken.ThrowIfCancellationRequested();
            string?[] names = [];
            if (set is not null) {
                try {
                    if (await ExcelSheetSource.OpenByName(vfs, vfs.RootFolder, sheetName, cancellationToken)
                        is { } source) {
                        var binding = ExcelSchemaBinding.Create(source, set);
                        if (binding.IsMatched)
                            names = binding.Fields.Select(x => x?.Name).ToArray();
                    }
                } catch (Exception e) when (e is not OperationCanceledException) {
                    // Labels fall back to column indices.
                }
            }

            this._excelColumnNames[sheetName] = names;
        }
    }

    private string? ResolveExcelColumnName(string sheetName, int columnIndex) =>
        this._excelColumnNames.TryGetValue(sheetName, out var names) && columnIndex >= 0 && columnIndex < names.Length
            ? names[columnIndex]
            : null;
}
