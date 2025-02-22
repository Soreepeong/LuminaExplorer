using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LuminaExplorer.Controls.Util;
using ScintillaNET;
using BorderStyle = ScintillaNET.BorderStyle;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public class TabbedTextViewerControl : AbstractFileResourceViewerControl {
    private readonly ListBox _listBox;
    private readonly TabControl _tabControl;
    private readonly List<string> _contents = new();

    public TabbedTextViewerControl()
    {
        SplitContainer splitter;
        this.Controls.Add(
            splitter = new() {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                SplitterDistance = 160,
            });
        splitter.Panel1.Controls.Add(
            this._listBox = new() {
                Dock = DockStyle.Fill,
            });
        splitter.Panel2.Controls.Add(
            this._tabControl = new() {
                Dock = DockStyle.Fill,
                Multiline = true,
            });

        this._listBox.SelectedIndexChanged += this.ListBoxOnSelectedIndexChanged;
        this._listBox.KeyDown += this.ListBoxOnKeyDown;
        this._listBox.DoubleClick += this.ListBoxOnDoubleClick;
    }

    private void ListBoxOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter) this._tabControl.SelectedTab?.Controls.Cast<Control>().FirstOrDefault()?.Focus();
    }

    private void ListBoxOnDoubleClick(object? sender, EventArgs e)
    {
        this._tabControl.SelectedTab?.Controls.Cast<Control>().FirstOrDefault()?.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) this._tabControl.Dispose();

        base.Dispose(disposing);
    }

    private void ListBoxOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (this._listBox.SelectedItem is not string selectedItem)
            return;

        using (this._tabControl.DisableRedrawScoped()) {
            var tab = this._tabControl.TabPages.Cast<TabPage>()
                .Select((x, i) => (x, i)).FirstOrDefault(x => x.x.Name == selectedItem, (null!, -1)).i;
            if (tab != -1) {
                var tabPage = this._tabControl.TabPages[tab];
                this._tabControl.TabPages.RemoveAt(tab);
                this._tabControl.TabPages.Insert(0, tabPage);
                this._tabControl.SelectedIndex = 0;
            } else {
                var tabPage = this.NewPage(this._listBox.SelectedIndex);
                this._tabControl.TabPages.Insert(0, tabPage);
                this._tabControl.SelectedIndex = 0;
            }

            while (this._tabControl.TabPages.Count > 8)
                this._tabControl.TabPages.RemoveAt(this._tabControl.TabPages.Count - 1);

            this._listBox.Focus();
        }
    }

    private TabPage NewPage(int pageIndex)
    {
        var page = new TabPage(this._listBox.Items[pageIndex].ToString());
        var scintilla = new Scintilla { Dock = DockStyle.Fill };

        scintilla.StyleResetDefault();
        scintilla.Styles[Style.Default].Font = FontFamily.GenericMonospace.Name;
        scintilla.Styles[Style.Default].Size = (int) base.Font.Size;
        scintilla.StyleClearAll();
        scintilla.Text = this._contents[pageIndex];
        scintilla.ReadOnly = true;
        scintilla.BorderStyle = BorderStyle.None;

        page.Controls.Add(scintilla);
        return page;
    }

    public void SetTexts(IEnumerable<string?>? names, IEnumerable<string> contents)
    {
        this.Clear();
        this.AppendTexts(names, contents);
    }

    public void AppendTexts(IEnumerable<string?>? names, IEnumerable<string> contents)
    {
        this._contents.AddRange(contents);
        using (this._listBox.DisableRedrawScoped())
        using (this._tabControl.DisableRedrawScoped()) {
            if (names is not null)
                this._listBox.Items.AddRange(
                    names.Cast<object>().Take(this._contents.Count - this._listBox.Items.Count).ToArray());
            if (this._listBox.Items.Count < this._contents.Count) {
                this._listBox.Items.AddRange(
                    Enumerable.Range(this._listBox.Items.Count, this._contents.Count - this._listBox.Items.Count)
                        .Select(i => (object) $"Item {i}")
                        .ToArray());
            }

            var ll = this._tabControl.TabPages.Count;
            var ul = Math.Min(this._listBox.Items.Count, 8);
            if (ll < ul) this._tabControl.TabPages.AddRange(Enumerable.Range(ll, ul).Select(this.NewPage).ToArray());

            this._tabControl.SelectedIndex = 0;
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        if (this._tabControl.TabPages.Cast<TabPage>().FirstOrDefault() is not { } tabPage ||
            tabPage.Controls.Cast<Control>().FirstOrDefault() is not Scintilla scintilla)
            return new(640, 480);

        var height = 0;
        var width = 640;
        foreach (var line in scintilla.Lines) {
            height += line.Height;
            width = Math.Max(width, scintilla.TextWidth(Style.Default, line.Text));
            if (height > proposedSize.Height)
                break;
        }

        width = Math.Min(
            width + this.DeviceDpi * 2 + this._tabControl.Padding.X + this._tabControl.Margin.Horizontal +
            tabPage.Padding.Horizontal + tabPage.Margin.Horizontal +
            scintilla.Padding.Horizontal + scintilla.Margin.Horizontal,
            proposedSize.Width);
        height = Math.Min(
            height + this.DeviceDpi / 3 + this._tabControl.Height - tabPage.Height +
            tabPage.Padding.Vertical + tabPage.Margin.Vertical +
            scintilla.Padding.Vertical + scintilla.Margin.Vertical,
            proposedSize.Width);
        return new(width, height);
    }

    public void Clear()
    {
        while (this._tabControl.TabPages.Count > 0) {
            var page = this._tabControl.TabPages[^1];
            this._tabControl.TabPages.RemoveAt(this._tabControl.TabPages.Count - 1);
            page.Dispose();
        }

        this._listBox.Items.Clear();
        this._contents.Clear();
    }
}
