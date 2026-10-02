using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra.Tmb;

/// <summary>A parsed TMB (TMLB) timeline. Used by standalone .tmb files, and embedded at the end of .pap files.
/// </summary>
public sealed class TmbTimeline {
    /// <summary>"TMLB".</summary>
    public const uint MagicValue = TmbMagic.Tmlb;

    /// <summary>Size of the timeline data, as declared in the header.</summary>
    public readonly int FileSize;

    /// <summary>Number of entries, as declared in the header.</summary>
    public readonly int EntryCount;

    /// <summary>All entries, in file order.</summary>
    public readonly IReadOnlyList<TmbEntry> Entries;

    /// <summary>Entries that have an ID (TMAC, TMTR, and C### events), keyed by their ID.</summary>
    public readonly IReadOnlyDictionary<short, TmbIdentifiedEntry> EntriesById;

    /// <summary>Parses a timeline.</summary>
    /// <param name="data">Timeline data. May contain trailing bytes after the declared size.</param>
    public TmbTimeline(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
            throw new InvalidDataException("TMB has not enough data for the basic header.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != MagicValue)
            throw new InvalidDataException("Invalid TMB magic.");

        this.FileSize = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        this.EntryCount = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        if (this.FileSize < 12 || this.FileSize > data.Length)
            throw new InvalidDataException(
                $"TMB declares size {this.FileSize}, but {data.Length} bytes are available.");
        if (this.EntryCount < 0)
            throw new InvalidDataException("TMB declares a negative entry count.");

        data = data[..this.FileSize];

        var entries = new List<TmbEntry>(Math.Min(this.EntryCount, this.FileSize / 8));
        var offset = 12;
        for (var i = 0; i < this.EntryCount; i++) {
            if (offset + 8 > data.Length)
                throw new InvalidDataException($"TMB entry #{i} has not enough space for its magic and size.");

            var size = BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 4)..]);
            if (size < 8 || size > data.Length - offset)
                throw new InvalidDataException($"TMB entry #{i} has an invalid size {size}.");

            entries.Add(TmbEntry.Parse(data, offset));
            offset += size;
        }

        this.Entries = entries;

        var byId = new Dictionary<short, TmbIdentifiedEntry>();
        foreach (var e in entries.OfType<TmbIdentifiedEntry>())
            byId.TryAdd(e.Id, e);
        this.EntriesById = byId;
    }

    /// <summary>Tests whether the given data starts with a TMB header.</summary>
    public static bool IsTimeline(ReadOnlySpan<byte> data) =>
        data.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(data) == MagicValue;

    /// <summary>The TMDH entry, if any.</summary>
    public TmbHeaderEntry? Header => this.Entries.OfType<TmbHeaderEntry>().FirstOrDefault();

    /// <summary>The TMPP entry, if any.</summary>
    public TmbPapPathEntry? PapPath => this.Entries.OfType<TmbPapPathEntry>().FirstOrDefault();

    /// <summary>The TMAL entry, if any.</summary>
    public TmbActorListEntry? ActorList => this.Entries.OfType<TmbActorListEntry>().FirstOrDefault();

    /// <summary>All TMAC entries, in file order.</summary>
    public IEnumerable<TmbActorEntry> Actors => this.Entries.OfType<TmbActorEntry>();

    /// <summary>All TMTR entries, in file order.</summary>
    public IEnumerable<TmbTrackEntry> Tracks => this.Entries.OfType<TmbTrackEntry>();

    /// <summary>All C### entries, in file order.</summary>
    public IEnumerable<TmbEventEntry> Events => this.Entries.OfType<TmbEventEntry>();

    /// <summary>All game paths referenced from this timeline, with extensions, in file order.</summary>
    public IEnumerable<string> Paths => this.Entries.Select(x => x.GamePath).OfType<string>();

    /// <summary>Resolves the actors listed in <see cref="ActorList"/>.</summary>
    public IEnumerable<TmbActorEntry> GetListedActors() =>
        this.Resolve<TmbActorEntry>(this.ActorList?.ActorIds ?? []);

    /// <summary>Resolves the tracks of an actor.</summary>
    public IEnumerable<TmbTrackEntry> GetTracks(TmbActorEntry actor) => this.Resolve<TmbTrackEntry>(actor.TrackIds);

    /// <summary>Resolves the entries of a track. Usually <see cref="TmbEventEntry"/>, but may be other entries.
    /// </summary>
    public IEnumerable<TmbIdentifiedEntry> GetEvents(TmbTrackEntry track) =>
        this.Resolve<TmbIdentifiedEntry>(track.EventIds);

    private IEnumerable<T> Resolve<T>(IEnumerable<short> ids) where T : TmbIdentifiedEntry
    {
        foreach (var id in ids) {
            if (this.EntriesById.GetValueOrDefault(id) is T entry)
                yield return entry;
        }
    }
}