using System;
using System.Collections.Generic;
using VRage;
using VRageMath;

namespace RichHudFramework.UI
{
	using Rendering;
	using System.Collections;

	public class Dropdown<TValue> : Dropdown<ListBoxEntry<TValue>, Label, TValue>
	{
/// <summary>Dropdown operation.</summary>
		public Dropdown(HudParentBase parent) : base(parent)
		{ }

/// <summary>Dropdown operation.</summary>
		public Dropdown() : base(null)
		{ }
	}

	public class Dropdown<TElement, TValue> : Dropdown<ListBoxEntry<TElement, TValue>, TElement, TValue>
/// <summary>new operation.</summary>
		where TElement : HudElementBase, IMinLabelElement, new()
	{
/// <summary>Dropdown operation.</summary>
		public Dropdown(HudParentBase parent) : base(parent)
		{ }

/// <summary>Dropdown operation.</summary>
		public Dropdown() : base(null)
		{ }
	}

	public class Dropdown<TContainer, TElement, TValue>
		: HudElementBase, IClickableElement, IEntryBox<TContainer, TElement>
/// <summary>new operation.</summary>
		where TContainer : class, IListBoxEntry<TElement, TValue>, new()
		where TElement : HudElementBase, IMinLabelElement
	{
		public event EventHandler ValueChanged
		{
			add { listBox.ValueChanged += value; }
			remove { listBox.ValueChanged -= value; }
		}

		public EventHandler UpdateValueCallback { set { listBox.ValueChanged += value; } }

		public IReadOnlyList<TContainer> EntryList => listBox.EntryList;

		public IReadOnlyHudCollection<TContainer, TElement> HudCollection => listBox.EntryChain;

		public Dropdown<TContainer, TElement, TValue> ListContainer => this;

		public float DropdownHeight { get { return listBox.Height; } set { listBox.Height = value; } }

		public Vector2 MemberPadding { get { return listBox.MemberPadding; } set { listBox.MemberPadding = value; } }

		public float LineHeight { get { return listBox.LineHeight; } set { listBox.LineHeight = value; } }

		public GlyphFormat Format { get { return listBox.Format; } set { listBox.Format = value; display.Format = value; } }

		public Color Color { get { return listBox.Color; } set { listBox.Color = value; } }

		public Color BarColor { get { return listBox.BarColor; } set { listBox.BarColor = value; } }

		public Color BarHighlight { get { return listBox.BarHighlight; } set { listBox.BarHighlight = value; } }

		public Color SliderColor { get { return listBox.SliderColor; } set { listBox.SliderColor = value; } }

		public Color SliderHighlight { get { return listBox.SliderHighlight; } set { listBox.SliderHighlight = value; } }

		public Color HighlightColor { get { return listBox.HighlightColor; } set { listBox.HighlightColor = value; } }

		public Color TabColor { get { return listBox.TabColor; } set { listBox.TabColor = value; } }

		public Vector2 HighlightPadding { get { return listBox.HighlightPadding; } set { listBox.HighlightPadding = value; } }

		public int MinVisibleCount { get { return listBox.MinVisibleCount; } set { listBox.MinVisibleCount = value; } }

		public TContainer Value => listBox.Value;

		public int SelectionIndex => listBox.SelectionIndex;

		public IFocusHandler FocusHandler => display.FocusHandler;

		public IMouseInput MouseInput => display.MouseInput;

		public override bool IsMousedOver => display.IsMousedOver || listBox.IsMousedOver;

		public bool Open => listBox.Visible;

		protected readonly ListBox<TContainer, TElement, TValue> listBox;

		protected readonly DropdownDisplay display;

		protected bool getDispFocus;

/// <summary>Dropdown operation.</summary>
		public Dropdown(HudParentBase parent) : base(parent)
		{
/// <summary>DropdownDisplay operation.</summary>
			display = new DropdownDisplay(this)
			{
				DimAlignment = DimAlignments.UnpaddedSize,
				Text = "None"
			};
			
			listBox = new ListBox<TContainer, TElement, TValue>(this)
			{
				Visible = false,
				CanIgnoreMasking = true,
				ZOffset = 3,
				DimAlignment = DimAlignments.Width,
				ParentAlignment = ParentAlignments.Bottom,
/// <summary>Color operation.</summary>
				TabColor = new Color(0, 0, 0, 0),
			};
			listBox.FocusHandler.InputOwner = this;

/// <summary>Vector2 operation.</summary>
			Size = new Vector2(300f, 43f);
			DropdownHeight = 100f;

			display.MouseInput.LeftClicked += ClickDisplay;
			ValueChanged += UpdateDisplay;
		}

/// <summary>Dropdown operation.</summary>
		public Dropdown() : this(null)
		{ }

/// <summary>HandleInput operation.</summary>
		protected override void HandleInput(Vector2 cursorPos)
		{
			if (SharedBinds.LeftButton.IsNewPressed && !(display.IsMousedOver || listBox.IsMousedOver))
				CloseList();

			if (getDispFocus)
			{
				display.FocusHandler.GetInputFocus();
				getDispFocus = false;
			}
		}

/// <summary>UpdateDisplay operation.</summary>
		protected virtual void UpdateDisplay(object sender, EventArgs args)
		{
			if (Value != null)
			{
				var fmt = display.FocusHandler.HasFocus ? Format.WithColor(listBox.FocusTextColor) : Format;
				display.name.TextBoard.SetText(Value.Element.TextBoard.ToString(), fmt);
				CloseList();
			}
		}

/// <summary>ClickDisplay operation.</summary>
		protected virtual void ClickDisplay(object sender, EventArgs args)
		{
			if (!listBox.Visible)
				OpenList();
			else
				CloseList();
		}

/// <summary>OpenList operation.</summary>
		public void OpenList()
		{
			if (!listBox.Visible)
			{
				listBox.Visible = true;
				listBox.FocusHandler.GetInputFocus();
			}
		}

/// <summary>CloseList operation.</summary>
		public void CloseList()
		{
			if (listBox.Visible)
			{
				listBox.Visible = false;
				getDispFocus = true;
			}
		}

/// <summary>Adds a .</summary>
		public TContainer Add(RichText name, TValue assocMember, bool enabled = true) =>
			listBox.Add(name, assocMember, enabled);

/// <summary>Adds a range.</summary>
		public void AddRange(IReadOnlyList<MyTuple<RichText, TValue, bool>> entries) =>
			listBox.AddRange(entries);

/// <summary>Insert operation.</summary>
		public void Insert(int index, RichText name, TValue assocMember, bool enabled = true) =>
			listBox.Insert(index, name, assocMember, enabled);

/// <summary>Removes the at.</summary>
		public void RemoveAt(int index) =>
			listBox.RemoveAt(index);

/// <summary>Removes the .</summary>
		public bool Remove(TContainer entry) =>
			listBox.Remove(entry);

/// <summary>Removes the range.</summary>
		public void RemoveRange(int index, int count) =>
			listBox.RemoveRange(index, count);

/// <summary>ClearEntries operation.</summary>
		public void ClearEntries() =>
			listBox.ClearEntries();

/// <summary>Sets the selectionat.</summary>
		public void SetSelectionAt(int index) =>
			listBox.SetSelectionAt(index);

/// <summary>Sets the selection.</summary>
		public void SetSelection(TValue assocMember) =>
			listBox.SetSelection(assocMember);

/// <summary>Sets the selection.</summary>
		public void SetSelection(TContainer member) =>
			listBox.SetSelection(member);

/// <summary>Returns the orsetmember.</summary>
		public object GetOrSetMember(object data, int memberEnum) =>
		 listBox.GetOrSetMember(data, memberEnum);

/// <summary>Returns the enumerator.</summary>
		public IEnumerator<TContainer> GetEnumerator() =>
			listBox.EntryList.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() =>
			GetEnumerator();

		protected class DropdownDisplay : Button
		{
/// <summary>Material operation.</summary>
			private static readonly Material arrowMat = new Material("RichHudDownArrow", new Vector2(64f, 64f));

			public RichText Text { get { return name.Text; } set { name.Text = value; } }

			public GlyphFormat Format
			{
				get { return name.Format; }
				set { name.Format = value; }
			}

			public Color BorderColor { get { return border.Color; } set { border.Color = value; } }

			public float BorderThickness { get { return border.Thickness; } set { border.Thickness = value; } }

			public Color FocusTextColor { get; set; }

			public Color FocusColor { get; set; }

			public bool UseFocusFormatting { get; set; }

			public readonly Label name;
			public readonly TexturedBox arrow, divider;

			private readonly BorderBox border;
			private Color lastTextColor;

/// <summary>DropdownDisplay operation.</summary>
			public DropdownDisplay(HudParentBase parent = null) : base(parent)
			{
/// <summary>BorderBox operation.</summary>
				border = new BorderBox(this)
				{
					Thickness = 1f,
					DimAlignment = DimAlignments.UnpaddedSize,
				};

/// <summary>Label operation.</summary>
				name = new Label()
				{
					AutoResize = false,
/// <summary>Vector2 operation.</summary>
					Padding = new Vector2(10f, 0f)
				};

/// <summary>TexturedBox operation.</summary>
				divider = new TexturedBox()
				{
/// <summary>Vector2 operation.</summary>
					Padding = new Vector2(4f, 17f),
					Width = 2f,
/// <summary>Color operation.</summary>
					Color = new Color(104, 113, 120),
				};

/// <summary>TexturedBox operation.</summary>
				arrow = new TexturedBox()
				{
					Width = 38f,
					MatAlignment = MaterialAlignment.FitVertical,
					Material = arrowMat,
				};

/// <summary>HudChain operation.</summary>
				var layout = new HudChain(false, this)
				{
					SizingMode = HudChainSizingModes.FitMembersOffAxis,
					DimAlignment = DimAlignments.UnpaddedSize,
					CollectionContainer = { { name, 1f }, divider, arrow }
				};

				Format = TerminalFormatting.ControlFormat;
				FocusTextColor = TerminalFormatting.Charcoal;

				Color = TerminalFormatting.OuterSpace;
				HighlightColor = TerminalFormatting.Atomic;
				FocusColor = TerminalFormatting.Mint;
				BorderColor = TerminalFormatting.LimedSpruce;

				HighlightEnabled = true;
				UseFocusFormatting = true;

				FocusHandler.GainedInputFocus += GainFocus;
				FocusHandler.LostInputFocus += LoseFocus;
			}

/// <summary>HandleInput operation.</summary>
			protected override void HandleInput(Vector2 cursorPos)
			{
				if (FocusHandler.HasFocus)
				{
					if (SharedBinds.Space.IsNewPressed)
					{
						_mouseInput.LeftClick();
					}
				}
/// <summary>if operation.</summary>
				else if (!MouseInput.IsMousedOver)
				{
					lastBackgroundColor = Color;
					lastTextColor = name.Format.Color;
				}
			}

/// <summary>CursorEnter operation.</summary>
			protected override void CursorEnter(object sender, EventArgs args)
			{
				if (HighlightEnabled)
				{
					if (!UseFocusFormatting || !FocusHandler.HasFocus)
						lastBackgroundColor = Color;

					if (UseFocusFormatting)
					{
						if (!FocusHandler.HasFocus)
							lastTextColor = name.Format.Color;

						name.TextBoard.SetFormatting(name.Format.WithColor(lastTextColor));
					}

					Color = HighlightColor;
					divider.Color = lastTextColor.SetAlphaPct(0.8f);
					arrow.Color = lastTextColor;
				}
			}

/// <summary>CursorExit operation.</summary>
			protected override void CursorExit(object sender, EventArgs args)
			{
				if (HighlightEnabled)
				{
					if (UseFocusFormatting && FocusHandler.HasFocus)
					{
						Color = FocusColor;
						name.TextBoard.SetFormatting(name.Format.WithColor(FocusTextColor));

						divider.Color = FocusTextColor.SetAlphaPct(0.8f);
						arrow.Color = FocusTextColor;
					}
					else
					{
						Color = lastBackgroundColor;

						if (UseFocusFormatting)
							name.TextBoard.SetFormatting(name.Format.WithColor(lastTextColor));

						divider.Color = lastTextColor.SetAlphaPct(0.8f);
						arrow.Color = lastTextColor;
					}
				}
			}

/// <summary>GainFocus operation.</summary>
			private void GainFocus(object sender, EventArgs args)
			{
				if (UseFocusFormatting)
				{
					if (!MouseInput.IsMousedOver)
					{
						lastBackgroundColor = Color;
						lastTextColor = name.Format.Color;
					}

					Color = FocusColor;
					name.TextBoard.SetFormatting(name.Format.WithColor(FocusTextColor));

					divider.Color = FocusTextColor.SetAlphaPct(0.8f);
					arrow.Color = FocusTextColor;
				}
			}

/// <summary>LoseFocus operation.</summary>
			private void LoseFocus(object sender, EventArgs args)
			{
				if (UseFocusFormatting)
				{
					Color = lastBackgroundColor;
					name.TextBoard.SetFormatting(name.Format.WithColor(lastTextColor));

					divider.Color = lastTextColor.SetAlphaPct(0.8f);
					arrow.Color = lastTextColor;
				}
			}
		}
	}
}