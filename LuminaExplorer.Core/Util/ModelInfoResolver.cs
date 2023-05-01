using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;

namespace LuminaExplorer.Core.Util; 

public class ModelInfoResolver {
    private readonly EstFile? _faceTable;
    private readonly EstFile? _hairTable;
    private readonly EstFile? _metTable;
    private readonly EstFile? _topTable;

    public ModelInfoResolver(EstFile? faceTable, EstFile? hairTable, EstFile? metTable, EstFile? topTable) {
        _faceTable = faceTable;
        _hairTable = hairTable;
        _metTable = metTable;
        _topTable = topTable;
    }

    public bool TryFindSklbPath(string mdlPath, [MaybeNullWhen(false)] out string sklbPath) {
        try {
            var pathComponents = mdlPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            switch (pathComponents[1]) {
                case "human":
                    // chara/human/c????/*/<part>/.????/*/...
                    return TryFindSklbPathForHumanRace(
                        part: pathComponents[4],
                        raceId: Convert.ToInt32(pathComponents[2][1..], 10),
                        setId: Convert.ToInt32(pathComponents[5][1..], 10),
                        out sklbPath);
                case "equipment":
                    // chara/equipment/e????/model/c????e????_<part>.mdl
                    return TryFindSklbPathForHumanRace(
                        part: Path.GetFileNameWithoutExtension(pathComponents[^1]).Split('_', 2)[1],
                        raceId: Convert.ToInt32(pathComponents[^1][1..5], 10),
                        setId: Convert.ToInt32(pathComponents[^1][6..10], 10),
                        out sklbPath);
                case "demihuman":
                case "monster":
                case "weapon":
                    sklbPath = string.Format(
                        "chara/{0}/{1}{2:D4}/skeleton/base/b0001/skl_{1}{2:D4}b0001.sklb",
                        pathComponents[1], pathComponents[1][0], int.Parse(pathComponents[2][1..])
                    );
                    return true;
            }
        } catch (Exception) {
            // swallow
        }

        sklbPath = null;
        return false;
    }

    public bool TryFindSklbPathForHumanRace(
        string part,
        int raceId,
        int setId,
        [MaybeNullWhen(false)] out string sklbPath) {
        sklbPath = null;

        // use default
        if (part == "body")
            return false;

        var table = part switch {
            "face" => _faceTable,
            "hair" => _hairTable,
            "met" => _metTable,
            "top" => _topTable,
            _ => null,
        };
        if (table is null)
            return false;

        sklbPath = string.Format(
            "chara/human/c{0:D4}/skeleton/{1}/{2}{3:D4}/skl_c{0:D4}{2}{3:D4}.sklb",
            raceId, part, part[0], table.GetSkeletonId(raceId, setId)
        );
        return true;
    }
    
    public static Task<ModelInfoResolver> GetResolver(Func<string, Task<EstFile?>> estFileFetcher) {
        var faceTable = estFileFetcher("chara/xls/charadb/faceSkeletonTemplate.est");
        var hairTable = estFileFetcher("chara/xls/charadb/hairSkeletonTemplate.est");
        var metTable = estFileFetcher("chara/xls/charadb/extra_met.est");
        var topTable = estFileFetcher("chara/xls/charadb/extra_top.est");
        return Task.WhenAll(faceTable, hairTable, metTable, topTable)
            .ContinueWith(_ => new ModelInfoResolver(
                faceTable.Result,
                hairTable.Result,
                metTable.Result,
                topTable.Result));
    }
}
