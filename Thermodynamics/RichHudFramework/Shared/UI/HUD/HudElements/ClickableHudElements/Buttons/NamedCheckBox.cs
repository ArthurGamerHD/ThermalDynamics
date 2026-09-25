using RichHudFramework.UI.Rendering;
using VRageMath;

namespace RichHudFramework.UI
{
	public class NamedCheckBox : HudElementBase, IClickableElement, IValueControl<bool>
    {
		public event EventHandler ValueChanged
		{
			add { checkbox.ValueChanged += value; }
			remove { checkbox.ValueChanged -= value; }
		}

		public EventHandler UpdateValueCallback
		{
			set { checkbox.ValueChanged += value; }
		}

		public RichText Name { get { return name.TextBoard.GetText(); } set { name.TextBoard.SetText(value); } }

        public GlyphFormat Format { get { return name.TextBoard.Format; } set { name.TextBoard.Format = value; } }

        public Vector2 TextSize { get { return name.Size; } set { name.Size = value; } }

        public Vector2 TextPadding { get { return name.Padding; } set { name.Padding = value; } }

        public bool AutoResize 
        { 
            get { return name.AutoResize; } 
            set 
            { 
                name.AutoResize = value;
                layout[0].AlignAxisScale = value ? 0f : 1f;
            } 
        }

        public TextBuilderModes BuilderMode { get { return name.BuilderMode; } set { name.BuilderMode = value; } }

        public bool VertCenterText { get { return name.VertCenterText; } set { name.VertCenterText = value; } }

        public ITextBuilder NameBuilder => name.TextBoard;

		public IFocusHandler FocusHandler => checkbox.FocusHandler;

		public IMouseInput MouseInput => checkbox.MouseInput;

        public bool Value { get { return checkbox.Value; } set { checkbox.Value = value; } }

        protected readonly Label name;

		protected readonly BorderedCheckBox checkbox;

		protected readonly HudChain layout;


        public NamedCheckBox(HudParentBase parent) : base(parent)
        {

            name = new Label()
            {
                Format = TerminalFormatting.ControlFormat.WithAlignment(TextAlignment.Right),
                Text = "NewCheckbox"
            };


            checkbox = new BorderedCheckBox();


            layout = new HudChain(false, this)
            {
                DimAlignment = DimAlignments.UnpaddedSize,
                Spacing = 17f,
                SizingMode = HudChainSizingModes.FitMembersOffAxis | HudChainSizingModes.AlignMembersCenter,
                CollectionContainer = { { name, 0f }, { checkbox, 0f } }
            };

            FocusHandler.InputOwner = this;
            AutoResize = true;

            Size = new Vector2(250f, 37f);
        }


		public NamedCheckBox() : this(null)
		{ }


		protected override void Measure()
        {
            if (AutoResize)
                UnpaddedSize = layout.UnpaddedSize + layout.Padding;
        }
    }
}