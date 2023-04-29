using System;
using System.Runtime.InteropServices;
using System.Text;
using Lumina.Data;
using Lumina.Data.Attributes;
using LuminaExplorer.Core.Util;
using Microsoft.Extensions.Primitives;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;

[FileExtension(".fdt")]
public class FdtFile : FileResource {
    /// <summary>
    /// Gets the header of this file.
    /// </summary>
    public FdtHeader FileHeader;

    /// <summary>
    /// Gets the font header of this file.
    /// </summary>
    public FontTableHeader FontHeader;

    /// <summary>
    /// Gets the kerning table header of this file.
    /// </summary>
    public KerningTableHeader KerningHeader;

    /// <summary>
    /// Gets all the glyphs defined in this file.
    /// </summary>
    public FontTableEntry[] Glyphs = null!;

    /// <summary>
    /// Gets all the kerning entries defined in this file.
    /// </summary>
    public KerningTableEntry[] Distances = null!;

    public override void LoadFile() {
        FileHeader = Reader.ReadStructure<FdtHeader>();

        FontHeader = Reader.WithSeek(FileHeader.FontTableHeaderOffset).ReadStructure<FontTableHeader>();
        Glyphs = Reader.ReadStructuresAsArray<FontTableEntry>(FontHeader.FontTableEntryCount);

        KerningHeader = Reader.WithSeek(FileHeader.KerningTableHeaderOffset).ReadStructure<KerningTableHeader>();
        Distances = Reader.ReadStructuresAsArray<KerningTableEntry>(
            Math.Min(FontHeader.KerningTableEntryCount, KerningHeader.Count));
    }

    /// <summary>
    /// Header of game font file format.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FdtHeader {
        public const uint MagicValue = 0x76736366;

        public uint Magic;
        public uint Version;

        /// <summary>
        /// Offset to FontTableHeader.
        /// </summary>
        public int FontTableHeaderOffset;

        /// <summary>
        /// Offset to KerningTableHeader.
        /// </summary>
        public int KerningTableHeaderOffset;

        public uint Padding1;
        public uint Padding2;
        public uint Padding3;
        public uint Padding4;
    }

    /// <summary>
    /// Header of glyph table.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FontTableHeader {
        public const uint MagicValue = 0x64687466;

        public uint Magic;

        /// <summary>
        /// Number of glyphs defined in this file.
        /// </summary>
        public int FontTableEntryCount;

        /// <summary>
        /// Number of kerning informations defined in this file.
        /// </summary>
        public int KerningTableEntryCount;

        public uint Padding1;

        /// <summary>
        /// Width of backing texture.
        /// </summary>
        public ushort TextureWidth;

        /// <summary>
        /// Height of backing texture.
        /// </summary>
        public ushort TextureHeight;

        /// <summary>
        /// Size of the font defined from this file, in points unit.
        /// </summary>
        public float Size;

        /// <summary>
        /// Line height of the font defined forom this file, in pixels unit.
        /// </summary>
        public int LineHeight;

        /// <summary>
        /// Ascent of the font defined from this file, in pixels unit.
        /// </summary>
        public int Ascent;

        /// <summary>
        /// Gets descent of the font defined from this file, in pixels unit.
        /// </summary>
        public int Descent => LineHeight - Ascent;

        public override string ToString() => $"{Size}pt ({FontTableEntryCount}, {KerningTableEntryCount})";
    }

    /// <summary>
    /// Glyph table entry.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FontTableEntry : IComparable<FontTableEntry> {
        /// <summary>
        /// Mapping of texture channel index to byte index.
        /// </summary>
        public static readonly int[] TextureChannelOrder = {2, 1, 0, 3};

        /// <summary>
        /// Integer representation of a Unicode character in UTF-8 in reverse order, read in little endian.
        /// </summary>
        public int CharUtf8;

        /// <summary>
        /// Integer representation of a Shift_JIS character in reverse order, read in little endian.
        /// </summary>
        public ushort CharSjis;

        /// <summary>
        /// Index of backing texture.
        /// </summary>
        public ushort TextureIndex;

        /// <summary>
        /// Horizontal offset of glyph image in the backing texture.
        /// </summary>
        public ushort TextureOffsetX;

        /// <summary>
        /// Vertical offset of glyph image in the backing texture.
        /// </summary>
        public ushort TextureOffsetY;

        /// <summary>
        /// Bounding width of this glyph.
        /// </summary>
        public byte BoundingWidth;

        /// <summary>
        /// Bounding height of this glyph.
        /// </summary>
        public byte BoundingHeight;

        /// <summary>
        /// Distance adjustment for drawing next character.
        /// </summary>
        public sbyte NextOffsetX;

        /// <summary>
        /// Distance adjustment for drawing current character.
        /// </summary>
        public sbyte CurrentOffsetY;

        /// <summary>
        /// Gets the index of the file among all the backing texture files.
        /// </summary>
        public int TextureFileIndex => TextureIndex / 4;

        /// <summary>
        /// Gets the channel index in the backing texture file.
        /// </summary>
        public int TextureChannelIndex => TextureIndex % 4;

        /// <summary>
        /// Gets the byte index in a multichannel pixel corresponding to the channel.
        /// </summary>
        public int TextureChannelByteIndex => TextureChannelOrder[TextureChannelIndex];

        /// <summary>
        /// Gets the advance width of this character.
        /// </summary>
        public int AdvanceWidth => BoundingWidth + NextOffsetX;

        /// <summary>
        /// Gets the Unicode codepoint of the character for this entry in int type.
        /// </summary>
        public int CharInt => Utf8Uint32ToCodePoint(CharUtf8);

        /// <summary>
        /// Gets the Unicode codepoint of the character for this entry in char type.
        /// </summary>
        public char Char => (char) Utf8Uint32ToCodePoint(CharUtf8);

        /// <inheritdoc/>
        public int CompareTo(FontTableEntry other) {
            return CharUtf8 - other.CharUtf8;
        }

        public string StringFromUtf8 {
            get {
                var c = CharInt;
                return c == 0 ? string.Empty : char.ConvertFromUtf32(c);
            }
        }

        public string StringFromSjis {
            get {
                if (CharSjis == 0)
                    return string.Empty;
                Span<byte> bytes = stackalloc byte[2];
                var len = 1;
                if (CharSjis < 0x100)
                    bytes[0] = unchecked((byte) CharSjis);
                else {
                    bytes[0] = unchecked((byte) (CharSjis >> 8));
                    bytes[1] = unchecked((byte) (CharSjis >> 0));
                    len = 2;
                }

                return CodePagesEncodingProvider.Instance.GetEncoding(932)!.GetString(bytes[..len]);
            }
        }

        public override string ToString() {
            var sb = new StringBuilder();
            sb.Append(CharInt switch {
                var r and <= 0xFF => $"\\x{r:X02}",
                var r and <= 0xFFFF => $"\\u{r:X04}",
                var r => $"\\U{r:X08}",
            });
            var u8s = StringFromUtf8;
            var sjs = StringFromSjis;
            if (u8s == sjs)
                sb.Append($" {u8s}");
            else
                sb.Append($" {u8s}({sjs})");
            sb.Append($" ({BoundingWidth}x{BoundingHeight}) @{TextureIndex}:{TextureOffsetX}:{TextureOffsetY}");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Header of kerning table.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct KerningTableHeader {
        public const uint MagicValue = 0x64686e6b;

        public uint Magic;

        /// <summary>
        /// Number of kerning entries in this table.
        /// </summary>
        public int Count;

        public uint Padding1;
        public uint Padding2;

        public override string ToString() => $"K={Count}";
    }

    /// <summary>
    /// Kerning table entry.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct KerningTableEntry : IComparable<KerningTableEntry> {
        /// <summary>
        /// Integer representation of a Unicode character in UTF-8 in reverse order, read in little endian, for the left character.
        /// </summary>
        public int LeftUtf8;

        /// <summary>
        /// Integer representation of a Unicode character in UTF-8 in reverse order, read in little endian, for the right character.
        /// </summary>
        public int RightUtf8;

        /// <summary>
        /// Integer representation of a Shift_JIS character in reverse order, read in little endian, for the left character.
        /// </summary>
        public ushort LeftSjis;

        /// <summary>
        /// Integer representation of a Shift_JIS character in reverse order, read in little endian, for the right character.
        /// </summary>
        public ushort RightSjis;

        /// <summary>
        /// Horizontal offset adjustment for the right character.
        /// </summary>
        public int RightOffset;

        /// <summary>
        /// Gets the Unicode codepoint of the character for this entry in int type.
        /// </summary>
        public int LeftInt => Utf8Uint32ToCodePoint(LeftUtf8);

        /// <summary>
        /// Gets the Unicode codepoint of the character for this entry in char type.
        /// </summary>
        public char Left => (char) Utf8Uint32ToCodePoint(LeftUtf8);

        /// <summary>
        /// Gets the Unicode codepoint of the character for this entry in int type.
        /// </summary>
        public int RightInt => Utf8Uint32ToCodePoint(RightUtf8);

        /// <summary>
        /// Gets the Unicode codepoint of the character for this entry in char type.
        /// </summary>
        public char Right => (char) Utf8Uint32ToCodePoint(RightUtf8);

        /// <inheritdoc/>
        public int CompareTo(KerningTableEntry other) {
            if (LeftUtf8 == other.LeftUtf8)
                return RightUtf8 - other.RightUtf8;
            else
                return LeftUtf8 - other.LeftUtf8;
        }

        public string StringFromLeftUtf8 => char.ConvertFromUtf32(LeftUtf8);

        public string StringFromRightUtf8 => char.ConvertFromUtf32(RightUtf8);

        public override string ToString() => $"K[{StringFromLeftUtf8}, {StringFromRightUtf8}] = {RightOffset}";
    }

    /// <summary>
    /// Finds glyph definition for corresponding codepoint.
    /// </summary>
    /// <param name="codepoint">Unicode codepoint (UTF-32 value).</param>
    /// <returns>Corresponding FontTableEntry, or null if not found.</returns>
    public FontTableEntry? FindGlyph(int codepoint) {
        var i = Array.BinarySearch(Glyphs, new() {CharUtf8 = CodePointToUtf8Int32(codepoint)});
        if (i < 0 || i == Glyphs.Length)
            return null;
        return Glyphs[i];
    }

    /// <summary>
    /// Returns glyph definition for corresponding codepoint.
    /// </summary>
    /// <param name="codepoint">Unicode codepoint (UTF-32 value).</param>
    /// <returns>Corresponding FontTableEntry, or that of a fallback character.</returns>
    public FontTableEntry GetGlyph(int codepoint) {
        return (FindGlyph(codepoint)
            ?? FindGlyph('〓')
            ?? FindGlyph('?')
            ?? FindGlyph('='))!.Value;
    }

    /// <summary>
    /// Returns distance adjustment between two adjacent characters.
    /// </summary>
    /// <param name="codepoint1">Left character.</param>
    /// <param name="codepoint2">Right character.</param>
    /// <returns>Supposed distance adjustment between given characters.</returns>
    public int GetDistance(int codepoint1, int codepoint2) {
        var i = Array.BinarySearch(Distances, new() {
            LeftUtf8 = CodePointToUtf8Int32(codepoint1),
            RightUtf8 = CodePointToUtf8Int32(codepoint2),
        });
        if (i < 0 || i == Distances.Length)
            return 0;
        return Distances[i].RightOffset;
    }

    public static int CodePointToUtf8Int32(int codepoint) {
        return codepoint switch {
            <= 0x7F => codepoint,
            <= 0x7FF =>
                ((0xC0 | (codepoint >> 6)) << 8) |
                ((0x80 | ((codepoint >> 0) & 0x3F)) << 0),
            <= 0xFFFF =>
                ((0xE0 | (codepoint >> 12)) << 16) |
                ((0x80 | ((codepoint >> 6) & 0x3F)) << 8) |
                ((0x80 | ((codepoint >> 0) & 0x3F)) << 0),
            <= 0x10FFFF =>
                ((0xF0 | (codepoint >> 18)) << 24) |
                ((0x80 | ((codepoint >> 12) & 0x3F)) << 16) |
                ((0x80 | ((codepoint >> 6) & 0x3F)) << 8) |
                ((0x80 | ((codepoint >> 0) & 0x3F)) << 0),
            _ => 0xFFFE
        };
    }

    public static int Utf8Uint32ToCodePoint(int n) {
        if ((n & 0xFFFFFF80) == 0)
            return n & 0x7F;

        if ((n & 0xFFFFE0C0) == 0xC080)
            return
                (((n >> 0x08) & 0x1F) << 6) |
                (((n >> 0x00) & 0x3F) << 0);

        if ((n & 0xF0C0C0) == 0xE08080)
            return
                (((n >> 0x10) & 0x0F) << 12) |
                (((n >> 0x08) & 0x3F) << 6) |
                (((n >> 0x00) & 0x3F) << 0);

        if ((n & 0xF8C0C0C0) == 0xF0808080)
            return
                (((n >> 0x18) & 0x07) << 18) |
                (((n >> 0x10) & 0x3F) << 12) |
                (((n >> 0x08) & 0x3F) << 6) |
                (((n >> 0x00) & 0x3F) << 0);

        return 0xFFFF; // Guaranteed non-unicode
    }
}
