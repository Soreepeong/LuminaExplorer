using System.Collections.Generic;
using Lumina.Data;
using Lumina.Data.Attributes;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra.Tmb;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra;

[FileExtension(".tmb")]
public class TmbFile : FileResource {
    /// <summary>"TMLB".</summary>
    public const uint MagicValue = TmbTimeline.MagicValue;

    public TmbTimeline Timeline = null!;

    public IReadOnlyList<TmbEntry> Entries => this.Timeline.Entries;

    public IEnumerable<string> Paths => this.Timeline.Paths;

    public override void LoadFile()
    {
        this.Timeline = new(this.Data);
    }
}