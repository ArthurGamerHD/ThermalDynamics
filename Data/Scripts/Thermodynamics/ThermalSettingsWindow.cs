using System;
using System.Collections.Generic;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using RichHudFramework.UI.Rendering;
using Sandbox.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// The settings window, drawn by this mod rather than by the Rich HUD terminal.
    ///
    /// <para>
    /// **The terminal draws a panel around every control and this mod cannot reach it.** A page
    /// there is page, category, tile, control, and the tile is a fixed 300x250 box with a border
    /// and a scroll bar of its own, created and drawn inside Rich HUD Master — the client API
    /// offers `AddControl` and `Enabled` and nothing else, so neither the box nor the line under it
    /// can be turned off, and a control cannot be attached anywhere but inside one. The framework's
    /// HUD element library is a different matter: it compiles into this mod and draws from here,
    /// which is what the crosshair readout and the debug panel are already made of. So the window
    /// is built from those, and a setting sits on the page itself.
    /// </para>
    ///
    /// <para>
    /// It still needs Rich HUD Master installed — the HUD tree is rooted in it — but what the
    /// window looks like is this file's decision.
    /// </para>
    /// </summary>
    public sealed class ThermalSettingsWindow : WindowBase
    {
        /// <summary>Width of the page rail down the left.</summary>
        private const float NavWidth = 232f;

        /// <summary>Height of one setting's row, and of one entry in the rail.</summary>
        private const float RowHeight = 32f, NavRowHeight = 26f;

        /// <summary>Width of the control column, and of the value column right of it.</summary>
        private const float ControlWidth = 250f, ValueWidth = 86f;

        private const float Gap = 10f;

        private static readonly Color Body = new Color(24, 30, 36, 240);
        private static readonly Color Edge = new Color(72, 86, 98);
        private static readonly Color Selected = new Color(51, 66, 76);
        private static readonly Color Clear = new Color(0, 0, 0, 0);

        private static readonly GlyphFormat TitleFormat =
            new GlyphFormat(new Color(238, 244, 248), TextAlignment.Left, 1.2f);
        private static readonly GlyphFormat NoteFormat =
            new GlyphFormat(new Color(140, 156, 168), TextAlignment.Left, 0.95f);
        private static readonly GlyphFormat NameFormat =
            new GlyphFormat(new Color(210, 224, 232), TextAlignment.Left, 1.02f);
        private static readonly GlyphFormat DimFormat =
            new GlyphFormat(new Color(126, 138, 148), TextAlignment.Left, 1.02f);
        private static readonly GlyphFormat ValueFormat =
            new GlyphFormat(new Color(178, 196, 208), TextAlignment.Right, 1.02f);
        private static readonly GlyphFormat FolderFormat =
            new GlyphFormat(new Color(132, 148, 160), TextAlignment.Left, 0.95f);
        private static readonly GlyphFormat NavFormat =
            new GlyphFormat(new Color(206, 220, 230), TextAlignment.Left, 1.02f);
        private static readonly GlyphFormat StatFormat =
            new GlyphFormat(new Color(198, 214, 224), TextAlignment.Left, 0.98f);

        /// <summary>One page: what the rail calls it, and what it holds.</summary>
        private sealed class Page
        {
            public string Name;
            public string Note;
            public ScrollBox Box;
            public LabelBoxButton Button;
        }

        private readonly ScrollBox nav;
        private readonly Label title, note;
        private readonly TexturedBox divider;
        private readonly BorderedButton close;

        private readonly List<Page> pages = new List<Page>();
        private Page current;

        /// <summary>
        /// What each control does to bring itself back in step with the settings. A change made
        /// anywhere — another control, the Defaults button, the server pushing new values — is
        /// reflected in all of them rather than only in the one that was touched.
        /// </summary>
        private readonly List<Action> refreshers = new List<Action>();

        /// <summary>
        /// True while <see cref="Refresh"/> is writing values into controls, so a control's own
        /// change handler does not read that as the player moving it and write it back.
        /// </summary>
        private bool refreshing;

        /// <summary>
        /// What each slider does every frame: watch for the Ctrl that offers it a field, and for the
        /// Enter, the Escape or the lost focus that closes one. Per control rather than per window,
        /// because the state belongs to the control.
        /// </summary>
        private readonly List<Action> polls = new List<Action>();

        /// <summary>Whether Ctrl is down this frame, read once for every slider on the page.</summary>
        private bool CtrlHeld;

        /// <summary>The statistics page's lines, one label each, so the page scrolls.</summary>
        private readonly List<Label> statisticsLines = new List<Label>();
        private ScrollBox statisticsBox;

        public ThermalSettingsWindow(HudParentBase parent) : base(parent)
        {
            HeaderText = "Thermodynamics";
            HeaderBuilder.Format = new GlyphFormat(new Color(232, 240, 246), TextAlignment.Center, 1.1f);

            BodyColor = Body;
            BorderColor = Edge;

            Size = new Vector2(1080f, 680f);
            MinimumSize = new Vector2(720f, 400f);

            close = new BorderedButton(header)
            {
                Text = "close",
                Size = new Vector2(72f, 22f),
                ParentAlignment = ParentAlignments.InnerRight | ParentAlignments.InnerV,
                Offset = new Vector2(-8f, 0f),
                Format = new GlyphFormat(new Color(214, 226, 234), TextAlignment.Center, 0.9f),
                Color = Clear,
                BorderColor = Edge,
                ZOffset = 2,
            };
            close.MouseInput.LeftClicked += (sender, args) => Hide();

            nav = new ScrollBox(true, body)
            {
                ParentAlignment = ParentAlignments.InnerTopLeft,
                SizingMode = HudChainSizingModes.FitMembersOffAxis
                    | HudChainSizingModes.AlignMembersStart,
                Color = Clear,
                Width = NavWidth,
                Spacing = 2f,
            };

            divider = new TexturedBox(body)
            {
                ParentAlignment = ParentAlignments.InnerTopLeft,
                Color = new Color(56, 68, 78),
                Width = 1f,
            };

            title = new Label(body)
            {
                ParentAlignment = ParentAlignments.InnerTopLeft,
                AutoResize = false,
                VertCenterText = true,
                Format = TitleFormat,
                Height = 30f,
            };

            note = new Label(body)
            {
                ParentAlignment = ParentAlignments.InnerTopLeft,
                AutoResize = false,
                VertCenterText = true,
                Format = NoteFormat,
                Height = 22f,
            };

            Visible = false;
        }

        /// <summary>
        /// Sizes the rail, the heading and whichever page is showing to the window as it stands,
        /// which is what lets the window be dragged larger and the pages grow with it.
        /// </summary>
        protected override void Layout()
        {
            base.Layout();

            float height = body.Height, width = body.Width;

            nav.Width = NavWidth;
            nav.Height = Math.Max(height - 2f * Gap, 1f);
            nav.Offset = new Vector2(Gap, -Gap);

            divider.Height = Math.Max(height - 2f * Gap, 1f);
            divider.Offset = new Vector2(NavWidth + 1.5f * Gap, -Gap);

            float left = NavWidth + 2f * Gap;
            float contentWidth = Math.Max(width - left - Gap, 1f);

            title.Width = contentWidth;
            title.Offset = new Vector2(left, -Gap);

            note.Width = contentWidth;
            note.Offset = new Vector2(left, -(Gap + title.Height));

            float top = Gap + title.Height + note.Height + Gap;

            for (int i = 0; i < pages.Count; i++)
            {
                ScrollBox box = pages[i].Box;
                if (!box.Visible) continue;

                box.Width = contentWidth;
                box.Height = Math.Max(height - top - Gap, 1f);
                box.Offset = new Vector2(left, -top);
            }
        }

        /// <summary>Escape closes the window, as it does every other menu in the game.</summary>
        protected override void HandleInput(Vector2 cursorPos)
        {
            base.HandleInput(cursorPos);

            CtrlHeld = MyAPIGateway.Input != null && MyAPIGateway.Input.IsAnyCtrlKeyPressed();

            // Asked before the polls run, because a poll is what consumes an Escape pressed into an
            // open field — and without this the same keystroke would close the field and then the
            // window behind it.
            bool typing = TypingSomewhere();

            for (int i = 0; i < polls.Count; i++) polls[i]();

            if (SharedBinds.Escape.IsNewPressed && !typing) Hide();
        }

        /// <summary>Whether a slider on the page has been turned into a field that is open.</summary>
        private bool TypingSomewhere()
        {
            for (int i = 0; i < typingCells.Count; i++)
            {
                if (typingCells[i]()) return true;
            }

            return false;
        }

        /// <summary>One per slider: whether that slider is showing its field.</summary>
        private readonly List<Func<bool>> typingCells = new List<Func<bool>>();
        private readonly List<Action> closeTextInputs = new List<Action>();

        public void Show()
        {
            Visible = true;
            HudMain.EnableCursor = true;
            GetWindowFocus();
        }

        public void Hide()
        {
            // Hiding a parent does not clear a TextBox's explicit OpenInput state or focus.
            // Release only our own editors; never override another mod's input blacklist.
            foreach (Action close in closeTextInputs) close();
            Visible = false;
            HudMain.EnableCursor = false;
        }

        public bool IsOpen => Visible;

        // ---------------------------------------------------------------------------------------
        // Building
        // ---------------------------------------------------------------------------------------

        /// <summary>A heading in the rail, naming the group of pages under it.</summary>
        public void AddFolder(string name)
        {
            nav.Add(new Label
            {
                AutoResize = false,
                VertCenterText = true,
                Format = FolderFormat,
                Height = NavRowHeight,
                Padding = new Vector2(10f, 0f),
                Text = name.ToUpper(),
            });
        }

        /// <summary>
        /// A page of settings: one control to a row, on the window's own background.
        /// </summary>
        public void AddPage(string name, string subheader, IList<string> settings,
            IList<string> advanced, bool editable, string trailing, bool indented)
        {
            Page page = NewPage(name, subheader, indented);

            for (int i = 0; i < settings.Count; i++)
            {
                page.Box.Add(BuildRow(settings[i], editable));
            }

            // The line, and only when there is something below it: a page whose every setting is
            // ordinary should not look as though it is hiding one.
            if (advanced != null && advanced.Count > 0)
            {
                page.Box.Add(Spacer());
                page.Box.Add(Divider("Advanced"));

                for (int i = 0; i < advanced.Count; i++)
                {
                    page.Box.Add(BuildRow(advanced[i], editable));
                }
            }

            if (!string.IsNullOrEmpty(trailing))
            {
                page.Box.Add(Spacer());
                page.Box.Add(TextLine("The rest of this system: " + trailing, NoteFormat));
            }
        }

        /// <summary>The one bulk action, on a page of its own.</summary>
        public void AddDefaultsPage(bool local, Action restore)
        {
            Page page = NewPage("Defaults", local
                ? "Returns every world setting to the value a fresh install ships"
                : "Applied by the server; ask an administrator", false);

            page.Box.Add(TextLine(
                "A change applies as it is made and reaches the config file a second later, so"
                + " there is no Save button here. Starting over is the one case that needs an"
                + " action of its own.", NoteFormat));
            page.Box.Add(Spacer());

            BorderedButton button = new BorderedButton
            {
                Text = "Restore every world setting",
                Size = new Vector2(300f, 30f),
                Format = new GlyphFormat(new Color(226, 236, 242), TextAlignment.Center, 1.02f),
                Color = Clear,
                BorderColor = Edge,
            };

            if (local) button.MouseInput.LeftClicked += (sender, args) => restore();
            else button.UseCursor = false;

            HudChain row = new HudChain(false)
            {
                Height = 34f,
                Spacing = 10f,
                SizingMode = HudChainSizingModes.AlignMembersStart,
            };
            row.Add(button, 0f);

            page.Box.Add(row);
        }

        /// <summary>
        /// The statistics page: the one page that holds sentences rather than controls. A label to
        /// the line, so that it scrolls — a scroll box moves its members, and a single tall member
        /// has nothing to move.
        /// </summary>
        public void AddStatisticsPage()
        {
            Page page = NewPage("Statistics", "What this world is set to, and what it is doing", false);
            statisticsBox = page.Box;
        }

        /// <summary>Writes the statistics text into the page, a label to the line.</summary>
        public void SetStatistics(string text)
        {
            if (statisticsBox == null) return;

            string[] lines = text.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                if (i == statisticsLines.Count)
                {
                    Label line = new Label
                    {
                        AutoResize = false,
                        VertCenterText = true,
                        Format = StatFormat,
                        Height = 19f,
                    };

                    statisticsLines.Add(line);
                    statisticsBox.Add(line);
                }

                statisticsLines[i].Text = lines[i].Length == 0 ? " " : lines[i];
                statisticsLines[i].Visible = true;
            }

            for (int i = lines.Length; i < statisticsLines.Count; i++)
            {
                statisticsLines[i].Visible = false;
            }
        }

        /// <summary>Opens the window on the page it was last on, or on the first one built.</summary>
        public void OpenToFirst()
        {
            if (current == null && pages.Count > 0) Select(pages[0]);
        }

        private Page NewPage(string name, string subheader, bool indented)
        {
            ScrollBox box = new ScrollBox(true, body)
            {
                SizingMode = HudChainSizingModes.FitMembersOffAxis
                    | HudChainSizingModes.AlignMembersStart,
                ParentAlignment = ParentAlignments.InnerTopLeft,
                Color = Clear,
                Spacing = 6f,
                Visible = false,
            };

            LabelBoxButton button = new LabelBoxButton
            {
                AutoResize = false,
                VertCenterText = true,
                Format = NavFormat,
                Height = NavRowHeight,
                TextPadding = new Vector2(indented ? 26f : 12f, 0f),
                Text = name,
                Color = Clear,
                HighlightEnabled = true,
                HighlightColor = new Color(44, 56, 66),
            };

            Page page = new Page { Name = name, Note = subheader, Box = box, Button = button };
            button.MouseInput.LeftClicked += (sender, args) => Select(page);

            nav.Add(button);
            pages.Add(page);

            if (current == null) Select(page);

            return page;
        }

        private void Select(Page page)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                bool chosen = pages[i] == page;

                pages[i].Box.Visible = chosen;
                pages[i].Button.Color = chosen ? Selected : Clear;
            }

            current = page;
            title.Text = page.Name;
            note.Text = page.Note;
        }

        /// <summary>
        /// A heading across the page with a rule beside it, for the tier below the fold: the
        /// settings a world tunes once or never, on the page that owns them rather than moved
        /// somewhere a reader has to go looking.
        /// </summary>
        private static HudElementBase Divider(string text)
        {
            Label label = new Label
            {
                AutoResize = false,
                VertCenterText = true,
                Format = NoteFormat,
                Text = text,
                Width = 90f,
            };

            TexturedBox rule = new TexturedBox { Color = new Color(56, 68, 78), Height = 1f };

            HudChain row = new HudChain(false)
            {
                Height = 24f,
                Spacing = 8f,
                SizingMode = HudChainSizingModes.None,
            };

            row.Add(label, 0f);
            row.Add(rule, 1f);

            return row;
        }

        /// <summary>Blank vertical space between one thing and the next.</summary>
        private static HudElementBase Spacer()
        {
            return new EmptyHudElement { Height = 10f, Width = 10f };
        }

        /// <summary>A line of prose across the page, wrapped to whatever width the window is.</summary>
        private static HudElementBase TextLine(string text, GlyphFormat format)
        {
            return new Label
            {
                AutoResize = false,
                BuilderMode = TextBuilderModes.Wrapped,
                Format = format,
                Height = 46f,
                Text = text,
            };
        }

        /// <summary>
        /// One setting's row: its name across the left, its control in a column of one width, and
        /// the value it is at on the right. Three columns rather than each control carrying its own
        /// label, because a column that lines up is what makes a page of forty dials readable.
        /// </summary>
        private HudElementBase BuildRow(string name, bool editable)
        {
            ThermalSettingsMenu.Entry entry = ThermalSettingsMenu.EntryFor(name);
            bool enabled = ThermalSettingsMenu.MayOffer(name, editable);

            Label label = new Label
            {
                AutoResize = false,
                VertCenterText = true,
                Format = enabled ? NameFormat : DimFormat,
                Text = entry.Label,
            };

            Label value = new Label
            {
                AutoResize = false,
                VertCenterText = true,
                Format = ValueFormat,
                Width = ValueWidth,
                Text = "",
            };

            HudElementBase control = BuildControl(name, entry, enabled, label, value);

            HudChain row = new HudChain(false)
            {
                Height = RowHeight,
                Spacing = 10f,
                SizingMode = HudChainSizingModes.FitMembersOffAxis,
            };

            row.Add(label, 1f);
            row.Add(control, 0f);
            row.Add(value, 0f);

            return row;
        }

        /// <summary>
        /// The control a setting gets: a switch for a flag, a named choice where the values are
        /// distinct behaviours, a typed field where a slider cannot divide the range, and a slider
        /// otherwise. The same four the terminal menu offered, which is where these rules come from.
        /// </summary>
        private HudElementBase BuildControl(string name, ThermalSettingsMenu.Entry entry,
            bool enabled, Label label, Label value)
        {
            ToolTip tip = ThermalSettingsMenu.TipFor(name, entry);

            if (Settings.IsFlag(name)) return Switch(name, enabled, label, tip);

            if (name == "DebugBlockOverlay")
            {
                // Read from the view rather than from the setting: Ctrl+Shift+= cycles the overlay
                // without writing one, so the setting is what it was last set to and the view is
                // what is actually on screen.
                return Choice(name, enabled, label, tip, ThermalSettingsMenu.OverlayNames(),
                    () => (int)ThermalDebugView.Current);
            }

            if (name == "ShadowDetail")
            {
                return Choice(name, enabled, label, tip, ThermalSettingsMenu.ShadowDetailNames, null);
            }

            if (ThermalSettingsMenu.NeedsTyping(entry))
            {
                return Field(name, entry, enabled, label, tip);
            }

            return Slider(name, entry, enabled, label, value, tip);
        }

        private HudElementBase Switch(string name, bool enabled, Label label, ToolTip tip)
        {
            BorderedCheckBox box = new BorderedCheckBox
            {
                Size = new Vector2(26f, 26f),
                BorderColor = Edge,
                Value = Settings.Instance.GetValue(name) > 0.5f,
            };

            box.MouseInput.ToolTip = tip;
            box.UseCursor = enabled;

            box.ValueChanged += (sender, args) =>
            {
                if (refreshing) return;
                ThermalSettingsMenu.Write(name, box.Value ? 1f : 0f);
            };

            refreshers.Add(() =>
            {
                box.Value = Settings.Instance.GetValue(name) > 0.5f;
                Mark(label, name);
            });

            HudChain holder = new HudChain(false)
            {
                Width = ControlWidth,
                SizingMode = HudChainSizingModes.AlignMembersEnd,
            };
            holder.Add(box, 0f);

            return holder;
        }

        private HudElementBase Choice(string name, bool enabled, Label label, ToolTip tip,
            string[] labels, Func<int> live)
        {
            Func<int> selected = live ?? (() => (int)Settings.Instance.GetValue(name));

            Dropdown<int> dropdown = new Dropdown<int>
            {
                Width = ControlWidth,
                Height = 26f,
                Color = new Color(38, 48, 56),
            };

            for (int i = 0; i < labels.Length; i++) dropdown.Add(new RichText(labels[i]), i);

            dropdown.SetSelection(selected());
            dropdown.MouseInput.ToolTip = tip;
            dropdown.UseCursor = enabled;

            dropdown.ValueChanged += (sender, args) =>
            {
                if (refreshing) return;

                ListBoxEntry<int> selection = dropdown.Value;
                if (selection == null) return;

                ThermalSettingsMenu.Write(name, selection.AssocMember);
            };

            refreshers.Add(() =>
            {
                dropdown.SetSelection(selected());
                Mark(label, name);
            });

            return dropdown;
        }

        /// <summary>
        /// A setting typed rather than dragged, for the ranges a slider cannot divide: the step
        /// budget spans four million and the friction scale spans a hundredth.
        /// </summary>
        private HudElementBase Field(string name, ThermalSettingsMenu.Entry entry, bool enabled,
            Label label, ToolTip tip)
        {
            TextField field = new TextField
            {
                Width = ControlWidth,
                Height = 26f,
                Color = new Color(38, 48, 56),
                BorderColor = Edge,
                Format = new GlyphFormat(new Color(214, 228, 236), TextAlignment.Left, 1.02f),
                Text = ThermalSettingsMenu.Number(Settings.Instance.GetValue(name), entry),
                EnableEditing = enabled,
            };

            field.MouseInput.ToolTip = tip;
            closeTextInputs.Add(() => { field.FocusHandler.ReleaseFocus(); field.CloseInput(); });
            field.UseCursor = enabled;

            // Anything that cannot be part of a number never reaches the field, so a typo is
            // refused as it is made rather than on losing focus.
            field.CharFilterFunc = c =>
                (c >= '0' && c <= '9') || c == '.' || c == '-' || c == 'e' || c == 'E' || c == '+';

            field.ValueChanged += (sender, args) =>
            {
                if (refreshing) return;

                float typed;
                if (!ThermalSettingsMenu.TryParse(field.TextBoard.ToString(), out typed)) return;

                ThermalSettingsMenu.Write(name, entry.Integer ? (float)Math.Round(typed) : typed);
            };

            refreshers.Add(() =>
            {
                // Not while it is being typed into: putting the setting's value back mid-number
                // would fight whoever is typing it.
                if (!field.InputOpen)
                    field.Text = ThermalSettingsMenu.Number(Settings.Instance.GetValue(name), entry);

                Mark(label, name);
            });

            return field;
        }

        /// <summary>
        /// A slider, and the field it becomes when a value is held down on with **Ctrl**.
        ///
        /// <para>
        /// **A slider has about two hundred positions and some of these ranges have thousands of
        /// values a player means exactly.** The menu already types the ranges no slider can divide
        /// at all — the step budget, the friction scale — but a range that a slider can *nearly*
        /// divide is the worse case: it looks as though it reached 6.5 and it is at 6.47. Ctrl and a
        /// click puts the number in a field, Enter or a click elsewhere commits it, and Escape
        /// leaves the setting where it was.
        /// </para>
        ///
        /// <para>
        /// **The grab is what takes the click, not the slider.** A ctrl-click that reached the
        /// slider would move it to wherever it landed before the field opened, so the setting would
        /// change on the way to typing a different value. The grab sits over the cell with the
        /// window's own topmost offset and is only visible — and a hidden element takes no input —
        /// while Ctrl is down.
        /// </para>
        /// </summary>
        private HudElementBase Slider(string name, ThermalSettingsMenu.Entry entry, bool enabled,
            Label label, Label value, ToolTip tip)
        {
            HudElementBase cell = new EmptyHudElement { Width = ControlWidth };

            SliderBox slider = new SliderBox(cell)
            {
                DimAlignment = DimAlignments.Size,
                Min = entry.Min,
                Max = entry.Max,
                Value = Settings.Instance.GetValue(name),
                BorderColor = Edge,
                BackgroundColor = new Color(38, 48, 56),
            };

            TextField typed = new TextField(cell)
            {
                DimAlignment = DimAlignments.Size,
                Color = new Color(38, 48, 56),
                BorderColor = new Color(120, 168, 196),
                Format = new GlyphFormat(new Color(226, 238, 246), TextAlignment.Left, 1.02f),
                Visible = false,
            };

            typed.CharFilterFunc = c =>
                (c >= '0' && c <= '9') || c == '.' || c == '-' || c == 'e' || c == 'E' || c == '+';

            MouseInputElement grab = new MouseInputElement(cell)
            {
                DimAlignment = DimAlignments.Size,
                ZOffset = sbyte.MaxValue,
                CanIgnoreMasking = true,
                Visible = false,
            };

            slider.MouseInput.ToolTip = tip;
            grab.ToolTip = tip;
            slider.UseCursor = enabled;

            value.Text = ThermalSettingsMenu.ValueText(slider.Value, entry);

            // **A drag writes once, when it ends.** Every write applies the whole configuration:
            // it publishes to the network, bumps the solver revision every grid then notices, and
            // has each of them walk its nodes and rebuild its room air. A slider dragged for a
            // second used to do that sixty times, which on a fleet is far more work than a frame
            // can carry and reads as input lag rather than as a slow menu. The label follows the
            // handle live; the setting follows the mouse button.
            bool held = false;
            float pending = 0f;

            slider.ValueChanged += (sender, args) =>
            {
                if (refreshing || typed.Visible) return;

                float moved = entry.Integer ? (float)Math.Round(slider.Value) : slider.Value;
                value.Text = ThermalSettingsMenu.ValueText(moved, entry);

                if (slider.MouseInput.IsLeftClicked)
                {
                    held = true;
                    pending = moved;
                    return;
                }

                ThermalSettingsMenu.Write(name, moved);
            };

            grab.LeftClicked += (sender, args) =>
            {
                if (!enabled) return;

                typed.Text = ThermalSettingsMenu.Number(Settings.Instance.GetValue(name), entry);
                typed.Visible = true;
                slider.Visible = false;
                grab.Visible = false;
                typed.OpenInput();
            };

            // Closing hands the field's text to the same write path a slider uses, so a typed value
            // goes through the same clamp as the chat command and the mod API.
            Action close = () =>
            {
                if (!typed.Visible) return;

                typed.CloseInput();
                typed.Visible = false;
                slider.Visible = true;
            };

            closeTextInputs.Add(() => { typed.FocusHandler.ReleaseFocus(); typed.CloseInput(); close(); });

            Action commit = () =>
            {
                float parsed;
                if (ThermalSettingsMenu.TryParse(typed.TextBoard.ToString(), out parsed))
                {
                    ThermalSettingsMenu.Write(name, entry.Integer ? (float)Math.Round(parsed) : parsed);
                }

                close();
            };

            typingCells.Add(() => typed.Visible);

            polls.Add(() =>
            {
                // The end of a drag: the button is up and the last value the handle passed has not
                // been written yet.
                if (held && !slider.MouseInput.IsLeftClicked)
                {
                    held = false;
                    ThermalSettingsMenu.Write(name, pending);
                }

                if (typed.Visible)
                {
                    // Escape leaves the setting alone; Enter and losing the field's focus commit.
                    // Focus is what `InputOpen` follows, so clicking anywhere else is a commit.
                    if (SharedBinds.Escape.IsNewPressed) { close(); return; }
                    if (SharedBinds.Enter.IsNewPressed || !typed.InputOpen) commit();

                    return;
                }

                grab.Visible = enabled && CtrlHeld;
            });

            refreshers.Add(() =>
            {
                float set = Settings.Instance.GetValue(name);

                if (!slider.MouseInput.IsLeftClicked && !typed.Visible) slider.Value = set;
                if (!typed.Visible) value.Text = ThermalSettingsMenu.ValueText(set, entry);

                Mark(label, name);
            });

            return cell;
        }

        /// <summary>A setting moved away from what a fresh install ships is dotted.</summary>
        private static void Mark(Label label, string name)
        {
            label.Text = ThermalSettingsMenu.Label(name, ThermalSettingsMenu.Changed(name));
        }

        /// <summary>
        /// Brings every control back in step with the settings behind them.
        /// </summary>
        public void Refresh()
        {
            refreshing = true;

            try
            {
                for (int i = 0; i < refreshers.Count; i++) refreshers[i]();
            }
            finally
            {
                refreshing = false;
            }
        }
    }
}
