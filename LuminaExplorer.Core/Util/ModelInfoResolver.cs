using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using LuminaExplorer.Core.ExtraFormats;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;

namespace LuminaExplorer.Core.Util;

public class ModelInfoResolver {
    private readonly PbdFile? _pbd;
    private readonly EstFile? _faceTable;
    private readonly EstFile? _hairTable;
    private readonly EstFile? _metTable;
    private readonly EstFile? _topTable;

    public ModelInfoResolver(PbdFile? pbd, EstFile? faceTable, EstFile? hairTable, EstFile? metTable, EstFile? topTable) {
        _pbd = pbd;
        _faceTable = faceTable;
        _hairTable = hairTable;
        _metTable = metTable;
        _topTable = topTable;
    }

    public IEnumerable<string> FindSklbPath(string mdlPath) {
        var pathComponents = mdlPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        switch (pathComponents[1]) {
            case "human": {
                // chara/human/c????/*/<part>/.????/*/...
                var part = pathComponents[4];
                var raceId = Convert.ToInt32(pathComponents[2][1..], 10);
                var setId = Convert.ToInt32(pathComponents[5][1..], 10);
                yield return GetBaseSklbPath(raceId);
                if (TryFindSklbPathForHumanRace(part, raceId, setId, out var sklbPath))
                    yield return sklbPath;
                break;
            }
            case "equipment": {
                // chara/equipment/e????/model/c????e????_<part>.mdl
                var part = Path.GetFileNameWithoutExtension(pathComponents[^1]).Split('_', 2)[1];
                var raceId = Convert.ToInt32(pathComponents[^1][1..5], 10);
                var setId = Convert.ToInt32(pathComponents[^1][6..10], 10);
                yield return GetBaseSklbPath(raceId);
                if (TryFindSklbPathForHumanRace(part, raceId, setId, out var sklbPath))
                    yield return sklbPath;
                break;
            }
            case "demihuman":
            case "monster":
            case "weapon":
                yield return string.Format(
                    "chara/{0}/{1}{2:D4}/skeleton/base/b0001/skl_{1}{2:D4}b0001.sklb",
                    pathComponents[1], pathComponents[1][0], int.Parse(pathComponents[2][1..])
                );
                break;
        }
    }

    public string GetBaseSklbPath(int raceId) => 
        string.Format("chara/human/c{0:D4}/skeleton/base/b0001/skl_c{0:D4}b0001.sklb", raceId);

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

        var skeletonId = table.GetSkeletonId(raceId, setId);
        while (skeletonId is null && raceId != (int)XivHumanSkeletonId.DefaultHuman) {
            if (_pbd?.TryGetDeformerBySkeletonId((XivHumanSkeletonId) raceId, out var deformer) is not true)
                return false;
            if (deformer.Parent is not { } parent)
                return false;
            
            raceId = (int) parent.SkeletonId;
            skeletonId = table.GetSkeletonId(raceId, setId);
        }
        sklbPath = string.Format(
            "chara/human/c{0:D4}/skeleton/{1}/{2}{3:D4}/skl_c{0:D4}{2}{3:D4}.sklb",
            raceId, part, part[0], skeletonId
        );
        return true;
    }

    public static Task<ModelInfoResolver> GetResolver(
        Func<string, Task<EstFile?>> estFileFetcher,
        Func<string, Task<PbdFile?>> pbdFileFetcher) {
        var pbdTable = pbdFileFetcher("chara/xls/boneDeformer/human.pbd");
        var faceTable = estFileFetcher("chara/xls/charadb/faceSkeletonTemplate.est");
        var hairTable = estFileFetcher("chara/xls/charadb/hairSkeletonTemplate.est");
        var metTable = estFileFetcher("chara/xls/charadb/extra_met.est");
        var topTable = estFileFetcher("chara/xls/charadb/extra_top.est");
        return Task.WhenAll(pbdTable, faceTable, hairTable, metTable, topTable)
            .ContinueWith(_ => new ModelInfoResolver(
                pbdTable.Result,
                faceTable.Result,
                hairTable.Result,
                metTable.Result,
                topTable.Result));
    }
}
