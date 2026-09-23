using System;
using System.Collections;
using System.Collections.Generic;
using VRageMath;

namespace RichHudFramework.UI
{
	using Rendering;

	public abstract class TreeBoxBase<TContainer, TElement> : TreeBoxBase<
		TreeBoxBase<TContainer, TElement>.TreeChainSelectionBox,
		HudChain<TContainer, TElement>,
		TContainer,
		TElement>

		where TContainer : class, ISelectionBoxEntry<TElement>, new()
		where TElement : HudElementBase, IMinLabelElement
	{

		public TreeBoxBase(HudParentBase parent) : base(parent) { }

		public TreeBoxBase() : base(null) { }

		public class TreeChainSelectionBox : ChainSelectionBoxBase<TContainer, TElement>
		{ }
	}

	public abstract class TreeBoxBase<TSelectionBox, TChain, TContainer, TElement>
		: LabelElementBase, IEntryBox<TContainer, TElement>, IClickableElement
		where TElement : HudElementBase, IMinLabelElement

		where TContainer : class, ISelectionBoxEntry<TElement>, new()

		where TChain : HudChain<TContainer, TElement>, new()

		where TSelectionBox : SelectionBoxBase<TChain, TContainer, TElement>, new()
	{
		public event EventHandler ValueChanged
		{
			add { selectionBox.ValueChanged += value; }
			remove { selectionBox.ValueChanged -= value; }
		}

		public EventHandler UpdateValueCallback { set { selectionBox.ValueChanged += value; } }

		public IReadOnlyList<TContainer> EntryList => selectionBox.EntryList;

		public TreeBoxBase<TSelectionBox, TChain, TContainer, TElement> ListContainer => this;

		public bool ListOpen { get; protected set; }

		public float DropdownHeight
		{
			get { return selectionBox.Height; }
			set { selectionBox.Height = value; }
		}

		public float IndentSize { get; set; }

		public RichText Name
		{
			get { return labelButton.Name; }
			set { labelButton.Name = value; }
		}

		public override ITextBoard TextBoard => labelButton.nameLabel.TextBoard;

		public GlyphFormat Format
		{
			get { return labelButton.Format; }
			set
			{
				labelButton.Format = value;
				selectionBox.Format = value;
			}
		}

		public float LabelHeight
		{
			get { return labelButton.Height; }
			set { labelButton.Height = value; }
		}

		public Color FocusTextColor
		{
			get { return selectionBox.FocusTextColor; }
			set { selectionBox.FocusTextColor = value; }
		}

		public Color HeaderColor
		{
			get { return labelButton.Color; }
			set { labelButton.Color = value; }
		}

		public Color HighlightColor
		{
			get { return selectionBox.HighlightColor; }
			set { selectionBox.HighlightColor = value; }
		}

		public Color FocusColor
		{
			get { return selectionBox.FocusColor; }
			set { selectionBox.FocusColor = value; }
		}

		public Color TabColor
		{
			get { return selectionBox.TabColor; }
			set { selectionBox.TabColor = value; }
		}

		public Vector2 HighlightPadding
		{
			get { return selectionBox.HighlightPadding; }
			set { selectionBox.HighlightPadding = value; }
		}

		public TContainer Value => selectionBox.Value;

		public int Count => selectionBox.Count;

		public IFocusHandler FocusHandler => labelButton.FocusHandler;

		public IMouseInput MouseInput => labelButton.MouseInput;

		public readonly TSelectionBox selectionBox;

		protected readonly TreeBoxDisplay labelButton;


        protected TreeBoxBase(HudParentBase parent) : base(parent)
		{

			labelButton = new TreeBoxDisplay(this)
			{
				ParentAlignment = ParentAlignments.PaddedInnerTop,
				DimAlignment = DimAlignments.UnpaddedWidth
			};


			selectionBox = new TSelectionBox()
			{
				Visible = false,
				ParentAlignment = ParentAlignments.Bottom,
				HighlightPadding = Vector2.Zero
			};

			selectionBox.Register(labelButton);
			selectionBox.FocusHandler.InputOwner = this;
			selectionBox.EntryChain.SizingMode = HudChainSizingModes.FitMembersOffAxis;

			Width = 200f;
			LabelHeight = 34f;
			IndentSize = 20f;
			DropdownHeight = 100f;
			FocusHandler.InputOwner = this;
			Format = GlyphFormat.Blueish;
			labelButton.Name = "NewTreeBox";

			labelButton.MouseInput.LeftClicked += ToggleList;
		}


        protected TreeBoxBase() : this(null) { }


		public void SetSelection(TContainer member) => selectionBox.SetSelection(member);


		public void SetSelectionAt(int index) => selectionBox.SetSelectionAt(index);


		public void ClearSelection() => selectionBox.ClearSelection();


        protected virtual void ToggleList(object sender, EventArgs args)
		{
			if (!ListOpen)
				OpenList();
			else
				CloseList();
		}


		public void OpenList()
		{
			labelButton.Open = true;
			ListOpen = true;
		}


		public void CloseList()
		{
			labelButton.Open = false;
			ListOpen = false;
		}


        protected override void Measure()
		{
			selectionBox.Visible = ListOpen;

			if (ListOpen)
			{
				Height = selectionBox.GetRangeSize().Y + labelButton.Height + Padding.Y;
				selectionBox.Width = Size.X - Padding.X - 2f * IndentSize;

				selectionBox.Offset = new Vector2(IndentSize, 0f);
				selectionBox.Height = Size.Y - labelButton.Height - Padding.Y;
			}
			else
			{
				Height = labelButton.Height + Padding.Y;
			}
		}


		public IEnumerator<TContainer> GetEnumerator() => selectionBox.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => selectionBox.GetEnumerator();

        protected class TreeBoxDisplay : HudElementBase, IClickableElement
		{
			public RichText Name
			{
				get { return nameLabel.Text; }
				set { nameLabel.Text = value; }
			}

			public GlyphFormat Format
			{
				get { return nameLabel.Format; }
				set { nameLabel.Format = value; }
			}

			public Color Color
			{
				get { return background.Color; }
				set { background.Color = value; }
			}

			public IFocusHandler FocusHandler { get; }
			public IMouseInput MouseInput => mouseInput;

			public bool Open
			{
				get { return _open; }
				set
				{
					_open = value;
					arrow.Material = _open ? downArrow : rightArrow;
				}
			}

			private bool _open;

			public readonly Label nameLabel;
			private readonly TexturedBox arrow, divider, background;
			private readonly MouseInputElement mouseInput;


			private static readonly Material downArrow = new Material("RichHudDownArrow", new Vector2(64f, 64f));

			private static readonly Material rightArrow = new Material("RichHudRightArrow", new Vector2(64f, 64f));


			public TreeBoxDisplay(HudParentBase parent) : base(parent)
			{

				background = new TexturedBox(this)
				{
					Color = TerminalFormatting.EbonyClay,
					DimAlignment = DimAlignments.Size,
				};


				nameLabel = new Label()
				{
					AutoResize = false,

					Padding = new Vector2(10f, 0f),
					Format = GlyphFormat.Blueish.WithSize(1.1f),
				};


				divider = new TexturedBox()
				{

					Padding = new Vector2(2f, 6f),

					Size = new Vector2(2f, 39f),

					Color = new Color(104, 113, 120),
				};


				arrow = new TexturedBox()
				{
					Width = 20f,

					Padding = new Vector2(8f, 0f),
					MatAlignment = MaterialAlignment.FitHorizontal,

					Color = new Color(227, 230, 233),
					Material = rightArrow,
				};


				var layout = new HudChain(false, this)
				{
					SizingMode = HudChainSizingModes.FitMembersOffAxis,
					DimAlignment = DimAlignments.UnpaddedSize,
					CollectionContainer = { arrow, divider, { nameLabel, 1f } }
				};


				FocusHandler = new InputFocusHandler(this);

				mouseInput = new MouseInputElement(this)
				{
					DimAlignment = DimAlignments.Size
				};
			}
		}
	}
}