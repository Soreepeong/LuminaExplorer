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

    public TabbedTextViewerControl() {
        SplitContainer splitter;
        Controls.Add(splitter = new() {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 160,
        });
        splitter.Panel1.Controls.Add(_listBox = new() {
            Dock = DockStyle.Fill,
        });
        splitter.Panel2.Controls.Add(_tabControl = new() {
            Dock = DockStyle.Fill,
            Multiline = true,
        });

        _listBox.SelectedIndexChanged += ListBoxOnSelectedIndexChanged;
        _listBox.KeyDown += ListBoxOnKeyDown;
        _listBox.DoubleClick += ListBoxOnDoubleClick;
    }

    private void ListBoxOnKeyDown(object? sender, KeyEventArgs e) {
        if (e.KeyCode == Keys.Enter)
            _tabControl.SelectedTab?.Controls.Cast<Control>().FirstOrDefault()?.Focus();
    }

    private void ListBoxOnDoubleClick(object? sender, EventArgs e) {
        _tabControl.SelectedTab?.Controls.Cast<Control>().FirstOrDefault()?.Focus();
    }

    protected override void Dispose(bool disposing) {
        if (disposing)
            _tabControl.Dispose();

        base.Dispose(disposing);
    }

    private void ListBoxOnSelectedIndexChanged(object? sender, EventArgs e) {
        if (_listBox.SelectedItem is not string selectedItem)
            return;

        using (_tabControl.DisableRedrawScoped()) {
            var tab = _tabControl.TabPages.Cast<TabPage>()
                .Select((x, i) => (x, i)).FirstOrDefault(x => x.x.Name == selectedItem, (null!, -1)).i;
            if (tab != -1) {
                var tabPage = _tabControl.TabPages[tab];
                _tabControl.TabPages.RemoveAt(tab);
                _tabControl.TabPages.Insert(0, tabPage);
                _tabControl.SelectedIndex = 0;
            } else {
                var tabPage = NewPage(_listBox.SelectedIndex);
                _tabControl.TabPages.Insert(0, tabPage);
                _tabControl.SelectedIndex = 0;
            }

            while (_tabControl.TabPages.Count > 8)
                _tabControl.TabPages.RemoveAt(_tabControl.TabPages.Count - 1);

            _listBox.Focus();
        }
    }

    private TabPage NewPage(int pageIndex) {
        var page = new TabPage(_listBox.Items[pageIndex].ToString());
        var scintilla = new Scintilla {Dock = DockStyle.Fill};

        scintilla.StyleResetDefault();
        scintilla.Styles[Style.Default].Font = FontFamily.GenericMonospace.Name;
        scintilla.Styles[Style.Default].Size = (int) base.Font.Size;
        scintilla.StyleClearAll();
        scintilla.Text = _contents[pageIndex];
        scintilla.ReadOnly = true;
        scintilla.BorderStyle = BorderStyle.None;

        page.Controls.Add(scintilla);
        return page;
    }

    public void SetTexts(IEnumerable<string?>? names, IEnumerable<string> contents) {
        Clear();
        AppendTexts(names, contents);
    }

    public void AppendTexts(IEnumerable<string?>? names, IEnumerable<string> contents) {
        _contents.AddRange(contents);
        using (_listBox.DisableRedrawScoped())
        using (_tabControl.DisableRedrawScoped()) {
            if (names is not null)
                _listBox.Items.AddRange(names.Cast<object>().Take(_contents.Count - _listBox.Items.Count).ToArray());
            if (_listBox.Items.Count < _contents.Count) {
                _listBox.Items.AddRange(Enumerable.Range(_listBox.Items.Count, _contents.Count - _listBox.Items.Count)
                    .Select(i => (object) $"Item {i}")
                    .ToArray());
            }

            var ll = _tabControl.TabPages.Count;
            var ul = Math.Min(_listBox.Items.Count, 8);
            if (ll < ul)
                _tabControl.TabPages.AddRange(Enumerable.Range(ll, ul).Select(NewPage).ToArray());

            _tabControl.SelectedIndex = 0;
        }
    }

    public override Size GetPreferredSize(Size proposedSize) {
        if (_tabControl.TabPages.Cast<TabPage>().FirstOrDefault() is not { } tabPage ||
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
            width + DeviceDpi * 2 +
            _tabControl.Padding.X + _tabControl.Margin.Horizontal +
            tabPage.Padding.Horizontal + tabPage.Margin.Horizontal +
            scintilla.Padding.Horizontal + scintilla.Margin.Horizontal,
            proposedSize.Width);
        height = Math.Min(
            height + DeviceDpi / 3 +
            _tabControl.Height - tabPage.Height +
            tabPage.Padding.Vertical + tabPage.Margin.Vertical +
            scintilla.Padding.Vertical + scintilla.Margin.Vertical,
            proposedSize.Width);
        return new(width, height);
    }

    public void Clear() {
        while (_tabControl.TabPages.Count > 0) {
            var page = _tabControl.TabPages[^1];
            _tabControl.TabPages.RemoveAt(_tabControl.TabPages.Count - 1);
            page.Dispose();
        }

        _listBox.Items.Clear();
        _contents.Clear();
    }
}
