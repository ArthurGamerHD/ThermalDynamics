using RichHudFramework.UI.Rendering;
using System;
using VRageMath;

namespace RichHudFramework.UI
{
	public class ColorPickerRGB : HudElementBase, IValueControl<Color>
	{
		public event EventHandler ValueChanged;

		public EventHandler UpdateValueCallback
		{
			set { ValueChanged += value; }
		}

		public RichText Name { get { return name.TextBoard.GetText(); } set { name.TextBoard.SetText(value); } }

		public ITextBuilder NameBuilder => name.TextBoard;

		public GlyphFormat NameFormat { get { return name.TextBoard.Format; } set { name.TextBoard.SetFormatting(value); } }

		public GlyphFormat ValueFormat
		{
			get { return sliderText[0].Format; }
			set
			{
				foreach (Label label in sliderText)
					label.TextBoard.SetFormatting(value);
			}
		}

		public virtual Color Value
		{
			get { return _color; }
			set
			{
				sliders[0].Value = value.R;
				sliders[1].Value = value.G;
				sliders[2].Value = value.B;
				_color = value;
			}
		}


		protected readonly Label name;

		protected readonly TexturedBox display;

		protected readonly HudChain headerChain;


		protected readonly Label[] sliderText;

		protected readonly HudChain<HudElementContainer<Label>, Label> colorNameColumn;


		public readonly SliderBox[] sliders;

		protected readonly HudChain<HudElementContainer<SliderBox>, SliderBox> colorSliderColumn;

		protected readonly HudChain colorChain;

		protected Color _color, lastColor;

		protected int focusedChannel;

/// <summary>ColorPickerRGB operation.</summary>
		public ColorPickerRGB(HudParentBase parent) : base(parent)
		{
/// <summary>Label operation.</summary>
			name = new Label()
			{
				Format = GlyphFormat.Blueish.WithSize(1.08f),
				Text = "NewColorPicker",
				AutoResize = false,
/// <summary>Vector2 operation.</summary>
				Size = new Vector2(88f, 22f)
			};

/// <summary>TexturedBox operation.</summary>
			display = new TexturedBox()
			{
				Width = 231f,
				Color = Color.Black
			};

/// <summary>BorderBox operation.</summary>
			var dispBorder = new BorderBox(display)
			{
				Color = Color.White,
				Thickness = 1f,
				DimAlignment = DimAlignments.Size,
			};

/// <summary>HudChain operation.</summary>
			headerChain = new HudChain(false)
			{
				Height = 22f,
				SizingMode = HudChainSizingModes.FitMembersOffAxis,
				CollectionContainer = { name, { display, 1f } }
			};

			sliderText = new Label[]
			{
/// <summary>Label operation.</summary>
				new Label() { AutoResize = false, Format = TerminalFormatting.ControlFormat, Height = 47f },
/// <summary>Label operation.</summary>
				new Label() { AutoResize = false, Format = TerminalFormatting.ControlFormat, Height = 47f },
/// <summary>Label operation.</summary>
				new Label() { AutoResize = false, Format = TerminalFormatting.ControlFormat, Height = 47f }
			};

			colorNameColumn = new HudChain<HudElementContainer<Label>, Label>(true)
			{
				SizingMode = HudChainSizingModes.FitMembersOffAxis,
				Width = 87f,
				Spacing = 5f,
				CollectionContainer =
				{
					{ sliderText[0], 1f },
					{ sliderText[1], 1f },
					{ sliderText[2], 1f }
				}
			};
			
			sliders = new SliderBox[]
			{
/// <summary>SliderBox operation.</summary>
				new SliderBox()
				{
					Min = 0f, Max = 255f, Height = 47f,
					UpdateValueCallback = UpdateChannelR
				},
/// <summary>SliderBox operation.</summary>
				new SliderBox()
				{
					Min = 0f, Max = 255f, Height = 47f,
					UpdateValueCallback = UpdateChannelG
				},
/// <summary>SliderBox operation.</summary>
				new SliderBox()
				{
					Min = 0f, Max = 255f, Height = 47f,
					UpdateValueCallback = UpdateChannelB
				}
			};

			colorSliderColumn = new HudChain<HudElementContainer<SliderBox>, SliderBox>(true)
			{
				SizingMode = HudChainSizingModes.FitMembersOffAxis,
				Width = 231f,
				Spacing = 5f,
				CollectionContainer =
				{
					{ sliders[0], 1f },
					{ sliders[1], 1f },
					{ sliders[2], 1f }
				}
			};

/// <summary>HudChain operation.</summary>
			colorChain = new HudChain(false)
			{
				SizingMode = HudChainSizingModes.FitMembersOffAxis,
				CollectionContainer = { { colorNameColumn, 0f }, { colorSliderColumn, 1f } }
			};

/// <summary>HudChain operation.</summary>
			var mainChain = new HudChain(true, this)
			{
				DimAlignment = DimAlignments.UnpaddedSize,
				SizingMode = HudChainSizingModes.FitMembersOffAxis,
				Spacing = 5f,
				CollectionContainer =
				{
					{ headerChain, 0f },
					{ colorChain, 1f },
				}
			};

/// <summary>Vector2 operation.</summary>
			Size = new Vector2(318f, 163f);
			UseCursor = true;
			ShareCursor = true;
			focusedChannel = -1;
			Value = Color.White;
			lastColor = _color;
		}

/// <summary>ColorPickerRGB operation.</summary>
		public ColorPickerRGB() : this(null)
		{ }

/// <summary>Sets the channelfocused.</summary>
		public void SetChannelFocused(int channel)
		{
			channel = MathHelper.Clamp(channel, 0, 2);

			if (!sliders[channel].FocusHandler.HasFocus)
				focusedChannel = channel;
		}

/// <summary>UpdateChannelR operation.</summary>
		protected virtual void UpdateChannelR(object sender, EventArgs args)
		{
			var slider = sender as SliderBox;
			_color.R = (byte)Math.Round(slider.Value);
			sliderText[0].TextBoard.SetText($"R: {_color.R}");
			display.Color = _color;
		}

/// <summary>UpdateChannelG operation.</summary>
		protected virtual void UpdateChannelG(object sender, EventArgs args)
		{
			var slider = sender as SliderBox;
			_color.G = (byte)Math.Round(slider.Value);
			sliderText[1].TextBoard.SetText($"G: {_color.G}");
			display.Color = _color;
		}

/// <summary>UpdateChannelB operation.</summary>
		protected virtual void UpdateChannelB(object sender, EventArgs args)
		{
			var slider = sender as SliderBox;
			_color.B = (byte)Math.Round(slider.Value);
			sliderText[2].TextBoard.SetText($"B: {_color.B}");
			display.Color = _color;
		}

/// <summary>HandleInput operation.</summary>
		protected override void HandleInput(Vector2 cursorPos)
		{
			if (_color != lastColor)
			{
				ValueChanged?.Invoke(this, EventArgs.Empty);
				lastColor = _color;
			}

			if (focusedChannel != -1)
			{
				sliders[focusedChannel].FocusHandler.GetInputFocus();
				focusedChannel = -1;
			}

			for (int i = 0; i < sliders.Length; i++)
			{
				if (sliders[i].FocusHandler.HasFocus)
				{
					if (SharedBinds.UpArrow.IsNewPressed)
					{
						i = MathHelper.Clamp(i - 1, 0, sliders.Length - 1);
						sliders[i].FocusHandler.GetInputFocus();
					}
/// <summary>if operation.</summary>
					else if (SharedBinds.DownArrow.IsNewPressed)
					{
						i = MathHelper.Clamp(i + 1, 0, sliders.Length - 1);
						sliders[i].FocusHandler.GetInputFocus();
					}

					break;
				}
			}
		}
	}
}