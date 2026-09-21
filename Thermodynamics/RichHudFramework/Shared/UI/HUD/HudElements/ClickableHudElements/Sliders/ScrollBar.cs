using VRageMath;

namespace RichHudFramework.UI
{
	public class ScrollBar : HudElementBase, IClickableElement, IValueControl<float>
    {
		public event EventHandler ValueChanged
		{
			add { SlideInput.ValueChanged += value; }
			remove { SlideInput.ValueChanged -= value; }
		}

		public EventHandler UpdateValueCallback
		{
			set { SlideInput.ValueChanged += value; }
		}

		public float Min
		{
			get { return SlideInput.Min; }
			set { SlideInput.Min = value; }
		}

		public float Max
		{
			get { return SlideInput.Max; }
			set { SlideInput.Max = value; }
		}

		public float Value { get { return SlideInput.Value; } set { SlideInput.Value = value; } }

		public float Percent { get { return SlideInput.Percent; } set { SlideInput.Percent = value; } }

		public float VisiblePercent { get; set; }

		public bool Vertical { get { return SlideInput.Vertical; } set { SlideInput.Vertical = value; SlideInput.Reverse = value; } }

		public override bool IsMousedOver => SlideInput.IsMousedOver;

		public IFocusHandler FocusHandler { get; }

		public IMouseInput MouseInput => SlideInput.MouseInput;

		public readonly SliderBar SlideInput;

/// <summary>ScrollBar operation.</summary>
		public ScrollBar(HudParentBase parent) : base(parent)
		{
/// <summary>InputFocusHandler operation.</summary>
			FocusHandler = new InputFocusHandler(this);
/// <summary>SliderBar operation.</summary>
			SlideInput = new SliderBar(this)
			{
				Reverse = true,
				Vertical = true,
				SliderWidth = 13f,
				BarWidth = 13f,

/// <summary>Color operation.</summary>
				SliderColor = new Color(78, 87, 101),
/// <summary>Color operation.</summary>
				SliderHighlight = new Color(136, 140, 148),

/// <summary>Color operation.</summary>
				BarColor = new Color(41, 51, 61),
			};

/// <summary>Vector2 operation.</summary>
			Size = new Vector2(13f, 300f);
/// <summary>Vector2 operation.</summary>
			Padding = new Vector2(30f, 10f);
			SlideInput.SliderVisible = false;
			VisiblePercent = 0.2f;
		}

/// <summary>ScrollBar operation.</summary>
		public ScrollBar() : this(null)
		{ }

/// <summary>Layout operation.</summary>
		protected override void Layout()
		{
			Vector2 size = UnpaddedSize;
			SlideInput.BarSize = size;

			if (Vertical)
			{
				SlideInput.SliderWidth = size.X;
				SlideInput.SliderHeight = size.Y * VisiblePercent;
				SlideInput.SliderVisible = SlideInput.SliderHeight < SlideInput.BarHeight;
			}
			else
			{
				SlideInput.SliderHeight = size.Y;
				SlideInput.SliderWidth = size.X * VisiblePercent;
				SlideInput.SliderVisible = SlideInput.SliderWidth < SlideInput.BarWidth;
			}
		}
	}
}