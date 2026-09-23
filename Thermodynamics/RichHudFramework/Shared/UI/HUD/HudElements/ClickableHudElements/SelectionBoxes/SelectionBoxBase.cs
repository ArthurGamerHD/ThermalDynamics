using RichHudFramework.UI.Rendering;
using System.Collections;
using System.Collections.Generic;
using VRage;
using VRageMath;

namespace RichHudFramework.UI
{
	public abstract class ChainSelectionBoxBase<TContainer, TElement>
		: SelectionBoxBase<HudChain<TContainer, TElement>, TContainer, TElement>

		where TContainer : class, ISelectionBoxEntry<TElement>, new()
		where TElement : HudElementBase, IMinLabelElement
	{

		public ChainSelectionBoxBase(HudParentBase parent = null) : base(parent) { }
	}

	public abstract class ScrollSelectionBoxBase<TContainer, TElement>
		: SelectionBoxBase<ScrollBox<TContainer, TElement>, TContainer, TElement>
		where TElement : HudElementBase, IMinLabelElement

		where TContainer : class, ISelectionBoxEntry<TElement>, new()
	{
		public Color Color { get { return EntryChain.Color; } set { EntryChain.Color = value; } }

		public virtual bool EnableScrolling { get { return EntryChain.EnableScrolling; } set { EntryChain.EnableScrolling = value; } }

		public virtual bool UseSmoothScrolling { get { return EntryChain.UseSmoothScrolling; } set { EntryChain.UseSmoothScrolling = value; } }

		protected override float HighlightWidth =>
			EntryChain.Size.X - Padding.X - EntryChain.ScrollBar.Width - EntryChain.Padding.X - HighlightPadding.X;


		public ScrollSelectionBoxBase(HudParentBase parent = null) : base(parent) { }


		protected override void HandleInput(Vector2 cursorPos)
		{
			if (listInput.KeyboardScroll)
			{
				if (listInput.HighlightIndex > EntryChain.End)
					EntryChain.End = listInput.HighlightIndex;

				else if (listInput.HighlightIndex < EntryChain.Start)
					EntryChain.Start = listInput.HighlightIndex;
			}
		}
	}

	public abstract class SelectionBoxBase<TChain, TContainer, TElement>
		: HudElementBase, IEntryBox<TContainer, TElement>, IClickableElement
		where TElement : HudElementBase, IMinLabelElement

		where TChain : HudChain<TContainer, TElement>, new()

		where TContainer : class, ISelectionBoxEntry<TElement>, new()
	{
		public event EventHandler ValueChanged
		{
			add { listInput.SelectionChanged += value; }
			remove { listInput.SelectionChanged -= value; }
		}

		public EventHandler UpdateValueCallback { set { listInput.SelectionChanged += value; } }

		public SelectionBoxBase<TChain, TContainer, TElement> ListContainer => this;

		public IReadOnlyList<TContainer> EntryList => EntryChain.Collection;

		public Color HighlightColor { get; set; }

		public Color FocusColor { get; set; }

		public Color TabColor
		{
			get { return selectionBox.TabColor; }
			set
			{
				selectionBox.TabColor = value;
				highlightBox.TabColor = value;
			}
		}

		public Vector2 HighlightPadding { get; set; }

		public GlyphFormat Format { get; set; }

		public Color FocusTextColor { get; set; }

		public TContainer Value => listInput.Selection;

		public int SelectionIndex => listInput.SelectionIndex;

		public int Count => EntryChain.Count;

		public IFocusHandler FocusHandler { get; }

		public IMouseInput MouseInput => listInput;

		public override bool IsMousedOver => listInput.IsMousedOver;


        protected virtual Vector2I ListRange => new Vector2I(0, EntryChain.Count - 1);

        protected virtual Vector2 ListSize => EntryChain.Size;

        protected virtual Vector2 ListPos => EntryChain.Position;

        protected virtual float HighlightWidth => EntryChain.Size.X - Padding.X - EntryChain.Padding.X - HighlightPadding.X;

        public readonly TChain EntryChain;

        protected readonly HighlightBox selectionBox, highlightBox;

        protected readonly ListInputElement<TContainer, TElement> listInput;

        protected readonly bool chainHidesDisabled;

        protected MyTuple<TContainer, GlyphFormat> lastSelection;


        protected SelectionBoxBase(HudParentBase parent = null) : base(parent)
		{
			EntryChain = new TChain
			{
				AlignVertical = true,
				SizingMode = HudChainSizingModes.FitMembersOffAxis,
				DimAlignment = DimAlignments.UnpaddedSize,
			};
			EntryChain.Register(this);

			chainHidesDisabled = EntryChain is ScrollBox<TContainer, TElement>;


			selectionBox = new HighlightBox(EntryChain) { Visible = false };

			highlightBox = new HighlightBox(EntryChain) { Visible = false, CanDrawTab = false };


			FocusHandler = new InputFocusHandler(this);
			listInput = new ListInputElement<TContainer, TElement>(this, EntryChain) { ZOffset = 1 };

			HighlightColor = TerminalFormatting.Atomic;
			FocusColor = TerminalFormatting.Mint;
			Format = TerminalFormatting.ControlFormat;
			FocusTextColor = TerminalFormatting.Charcoal;


			Size = new Vector2(335f, 203f);

			HighlightPadding = new Vector2(8f, 0f);
		}


		public IEnumerator<TContainer> GetEnumerator() => EntryChain.Collection.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();


		public void SetSelectionAt(int index) =>
			listInput.SetSelectionAt(index);


		public void OffsetSelectionIndex(int offset, bool wrap = false) =>
			listInput.OffsetSelectionIndex(offset, wrap);


		public void SetSelection(TContainer member) =>
			listInput.SetSelection(member);


		public void ClearSelection() =>
			listInput.ClearSelection();


		public virtual Vector2 GetRangeSize(int start = 0, int end = -1) => EntryChain.GetRangeSize(start, end);


        protected override void Layout()
		{
			if (!chainHidesDisabled)
			{
				foreach (TContainer entry in EntryChain)
					entry.Element.Visible = entry.Enabled;
			}

			highlightBox.Visible = false;
			selectionBox.Visible = false;

			if (EntryChain.Count > 0)
				UpdateSelection();

			listInput.ListSize = ListSize;
			listInput.ListPos = ListPos;
			listInput.ListRange = ListRange;
		}


        protected virtual void UpdateSelection()
		{
			UpdateSelectionPositions();
			UpdateSelectionFormatting();
		}


        protected virtual void UpdateSelectionPositions()
		{
			float entryWidth = HighlightWidth;

			if (Value != null && Value.Element.Visible)
			{
				Vector2 offset = Value.Element.Position - selectionBox.Origin;
				offset.X -= (ListSize.X - entryWidth - HighlightPadding.X) / 2f;

				selectionBox.Offset = offset;
				selectionBox.Height = Value.Element.Height - HighlightPadding.Y;
				selectionBox.Width = entryWidth;
				selectionBox.Visible = Value.Element.Visible && Value.AllowHighlighting;
			}

			if (listInput.HighlightIndex != listInput.SelectionIndex)
			{
				var entry = EntryChain[listInput.HighlightIndex];
				Vector2 offset = entry.Element.Position - highlightBox.Origin;
				offset.X -= (ListSize.X - entryWidth - HighlightPadding.X) / 2f;

				highlightBox.Visible = (listInput.IsMousedOver || FocusHandler.HasFocus)
					&& entry.Element.Visible && entry.AllowHighlighting;

				highlightBox.Height = entry.Element.Height - HighlightPadding.Y;
				highlightBox.Width = entryWidth;
				highlightBox.Offset = offset;
			}
		}


        protected virtual void UpdateSelectionFormatting()
		{
			if (lastSelection.Item1 != null)
			{
				ITextBoard textBoard = lastSelection.Item1.Element.TextBoard;
				textBoard.SetFormatting(lastSelection.Item2);
				lastSelection.Item1 = null;
			}

			if ((SelectionIndex == listInput.FocusIndex) && SelectionIndex != -1)
			{
				if (
					(listInput.KeyboardScroll ^ (SelectionIndex != listInput.HighlightIndex)) ||
					(!MouseInput.IsMousedOver && SelectionIndex == listInput.HighlightIndex)
				)
				{
					if (EntryChain[listInput.SelectionIndex].AllowHighlighting)
					{
						SetFocusFormat(listInput.SelectionIndex);
						selectionBox.Color = FocusColor;
					}
				}
				else
					selectionBox.Color = HighlightColor;

				highlightBox.Color = HighlightColor;
			}
			else
			{
				if (listInput.KeyboardScroll)
				{
					if (EntryChain[listInput.HighlightIndex].AllowHighlighting)
					{
						SetFocusFormat(listInput.HighlightIndex);
						highlightBox.Color = FocusColor;
					}
				}
				else
					highlightBox.Color = HighlightColor;

				selectionBox.Color = HighlightColor;
			}
		}


		protected void SetFocusFormat(int index)
		{
			var entry = EntryChain[index];
			var textBoard = entry.Element.TextBoard;

			lastSelection.Item1 = entry;
			lastSelection.Item2 = textBoard.Format;

			textBoard.SetFormatting(textBoard.Format.WithColor(FocusTextColor));
		}

		protected class HighlightBox : TexturedBox
		{
			public bool CanDrawTab { get; set; } = true;

			public Color TabColor { get { return tabBoard.Color; } set { tabBoard.Color = value; } }

			private readonly MatBoard tabBoard;


            public HighlightBox(HudParentBase parent = null) : base(parent)
			{

				tabBoard = new MatBoard() { Color = TerminalFormatting.Mercury };
				Color = TerminalFormatting.Atomic;
				IsSelectivelyMasked = true;
			}


            protected override void Draw()
			{

				var box = default(CroppedBox);
				Vector2 size = UnpaddedSize,
						halfSize = size * 0.5f;


				box.bounds = new BoundingBox2(Position - halfSize, Position + halfSize);
				box.mask = MaskingBox;

				if (hudBoard.Color.A > 0)
					hudBoard.Draw(ref box, HudSpace.PlaneToWorldRef);

				if (CanDrawTab && tabBoard.Color.A > 0)
				{
					Vector2 tabPos = Position;

					Vector2 tabSize = new Vector2(4f, size.Y - Padding.Y) * 0.5f;
					tabPos.X += (-size.X + tabSize.X) * 0.5f;


					box.bounds = new BoundingBox2(tabPos - tabSize, tabPos + tabSize);
					tabBoard.Draw(ref box, HudSpace.PlaneToWorldRef);
				}
			}
		}
	}
}