using System;
using System.Linq;
using System.Windows.Forms;
using LuminaExplorer.Controls.DirectXStuff.Shaders;

namespace LuminaExplorer.App.Window.FileViewers;

public partial class TextureViewer {
    private MenuStrip _menu = null!;
    private ToolStripMenuItem _menuFullScreen = null!;
    private ToolStripMenuItem _menuPropertyPanel = null!;
    private ToolStripMenuItem _menuUseAlphaChannel = null!;
    private ToolStripMenuItem _menuTransparencyGrid = null!;
    private ToolStripMenuItem[] _menuChannels = null!;

    // Whether Alt has been pressed alone, without any other key in between.
    private bool _menuKeyPending;

    private void InitializeMenu()
    {
        var viewer = this.TexViewer;

        this._menuChannels = [
            Item("&All channels", null, () => viewer.ChannelFilter = DirectXTexRendererShader.VisibleColorChannelTypes.All),
            Item("&Red channel only", "R", () => viewer.PerformHotkey(Keys.R)),
            Item("&Green channel only", "G", () => viewer.PerformHotkey(Keys.G)),
            Item("&Blue channel only", "B", () => viewer.PerformHotkey(Keys.B)),
            Item("Al&pha channel only", "A", () => viewer.PerformHotkey(Keys.A)),
        ];

        this._menu = new() {
            Visible = false,
            Dock = DockStyle.Top,
            Items = {
                Menu(
                    "&File",
                    Item("&Save as...", "Ctrl+S", this.ShowSaveDialog),
                    Item("&Copy image", "Ctrl+C", () => viewer.PerformHotkey(Keys.C | Keys.Control)),
                    new ToolStripSeparator(),
                    Item("&Close", "Esc", this.Close)),
                Menu(
                    "&Go",
                    Item("&Previous file", "PgUp", this.NavigateToPrevFile),
                    Item("&Next file", "PgDn", this.NavigateToNextFile),
                    Item("&First file", "Home", this.NavigateToFirstFile),
                    Item("&Last file", "End", this.NavigateToLastFile),
                    new ToolStripSeparator(),
                    Item("Previous &image, or previous folder", "[", () => viewer.PerformHotkey(Keys.OemOpenBrackets)),
                    Item("Next i&mage, or next folder", "]", () => viewer.PerformHotkey(Keys.OemCloseBrackets)),
                    Item("Pre&vious mipmap", ",", () => viewer.PerformHotkey(Keys.Oemcomma)),
                    Item("Ne&xt mipmap", ".", () => viewer.PerformHotkey(Keys.OemPeriod)),
                    new ToolStripSeparator(),
                    Info("Pan; previous or next file at the edge", "Arrow keys")),
                Menu(
                    "&View",
                    this._menuFullScreen = Item("&Full screen", "F, Enter", () => this.ToggleFullScreen("F")),
                    this._menuPropertyPanel = Item("&Properties", "Tab", this.TogglePropertyGrid),
                    new ToolStripSeparator(),
                    Item("Zoom &in", "+", () => viewer.PerformHotkey(Keys.Add)),
                    Item("Zoom &out", "-", () => viewer.PerformHotkey(Keys.Subtract)),
                    Item("Zoom in by 1%", "Ctrl++", () => viewer.PerformHotkey(Keys.Add | Keys.Control)),
                    Item("Zoom out by 1%", "Ctrl+-", () => viewer.PerformHotkey(Keys.Subtract | Keys.Control)),
                    Item("&Toggle actual size", "*", () => viewer.PerformHotkey(Keys.Multiply)),
                    new ToolStripSeparator(),
                    Menu(
                        "&Default zoom",
                        Item("Fit in &window", "1, 9", () => viewer.PerformHotkey(Keys.D1)),
                        Item("Fit &width", "8", () => viewer.PerformHotkey(Keys.D8)),
                        Item("Fit &height", "7", () => viewer.PerformHotkey(Keys.D7)),
                        Item("&Actual size", "0", () => viewer.PerformHotkey(Keys.D0)),
                        new ToolStripSeparator(),
                        Item("Toggle &zooming in to fit", "Z", () => viewer.PerformHotkey(Keys.Z))),
                    Menu(
                        "&Rotate",
                        Item("&None", "Alt+Up", () => viewer.PerformHotkey(Keys.Up | Keys.Alt)),
                        Item("90° &clockwise", "Alt+Right", () => viewer.PerformHotkey(Keys.Right | Keys.Alt)),
                        Item("&180°", "Alt+Down", () => viewer.PerformHotkey(Keys.Down | Keys.Alt)),
                        Item(
                            "90° c&ounterclockwise",
                            "Alt+Left",
                            () => viewer.PerformHotkey(Keys.Left | Keys.Alt)))),
                Menu(
                    "&Channels",
                    this._menuChannels
                        .Cast<ToolStripItem>()
                        .Append(new ToolStripSeparator())
                        .Append(
                            this._menuUseAlphaChannel = Item(
                                "&Use alpha channel",
                                "T",
                                () => viewer.UseAlphaChannel = !viewer.UseAlphaChannel))
                        .Append(this._menuTransparencyGrid = Item("Transparency &grid", "C", () => viewer.PerformHotkey(Keys.C)))
                        .ToArray()),
                Menu(
                    "&Help",
                    Info("Show this menu", "Alt"),
                    new ToolStripSeparator(),
                    Info("Pan", "Drag"),
                    Info("Zoom", "Ctrl+Wheel"),
                    Info("Zoom", "Double-click, then drag"),
                    Info("Toggle fit in window and actual size", "Double-click"),
                    Info("Full screen", "Middle click")),
            },
        };

        foreach (var item in this._menu.Items.OfType<ToolStripMenuItem>()) {
            item.DropDownOpening += (_, _) => this.UpdateMenuCheckedStates();
            item.DropDownClosed += (_, _) => this.BeginInvoke(this.HideMenuIfInactive);
        }

        // Added last so that it is docked first, spanning the full width above the other docked controls.
        this.Controls.Add(this._menu);
        this.KeyPreview = true;

        return;

        static ToolStripMenuItem Menu(string text, params ToolStripItem[] items)
        {
            var menu = new ToolStripMenuItem(text);
            menu.DropDownItems.AddRange(items);
            return menu;
        }

        static ToolStripMenuItem Item(string text, string? shortcut, Action onClick) =>
            new(text, null, (_, _) => onClick()) { ShortcutKeyDisplayString = shortcut };

        static ToolStripMenuItem Info(string text, string shortcut) =>
            new(text) { ShortcutKeyDisplayString = shortcut, Enabled = false };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        this._menuKeyPending = e.KeyCode == Keys.Menu && e.Modifiers == Keys.Alt;
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Menu && this._menuKeyPending) {
            this._menuKeyPending = false;

            // Handling the key release keeps Windows from activating the system menu.
            e.Handled = true;
            this.ShowMenu();
            return;
        }

        base.OnKeyUp(e);
    }

    private void ShowMenu()
    {
        if (this._menu.Visible) {
            this.HideMenu();
            return;
        }

        this._menu.Visible = true;
        var first = (ToolStripMenuItem) this._menu.Items[0];
        first.ShowDropDown();
        first.DropDownItems[0].Select();
    }

    private void HideMenuIfInactive()
    {
        // When moving between menus, the next drop down opens after the previous one closes.
        if (this._menu.Visible && !this._menu.Items.OfType<ToolStripMenuItem>().Any(x => x.DropDown.Visible))
            this.HideMenu();
    }

    private void HideMenu()
    {
        this._menu.Visible = false;
        this.TexViewer.Focus();
    }

    private void UpdateMenuCheckedStates()
    {
        var viewer = this.TexViewer;
        this._menuFullScreen.Checked = this.IsFullScreen;
        this._menuPropertyPanel.Checked = this.PropertyPanel.Visible;
        this._menuUseAlphaChannel.Checked = viewer.UseAlphaChannel;
        this._menuTransparencyGrid.Checked = viewer.TransparencyCellSize > 0;
        for (var i = 0; i < this._menuChannels.Length; i++)
            this._menuChannels[i].Checked = (int) viewer.ChannelFilter == i;
    }
}
