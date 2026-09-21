using System;
using VRageMath;

namespace RichHudFramework.UI
{
	public class SliderBar : MouseInputElement, IClickableElement, IValueControl<float>
    {
		public event EventHandler ValueChanged;

		public EventHandler UpdateValueCallback
		{
			set { ValueChanged += value; }
		}

		public float Min
		{
			get { return _min; }
			set
			{
				_min = value;

				if (_max > _min)
					Percent = (_current - _min) / (_max - _min);
				else
					Percent = 0;
			}
		}

		public float Max
		{
			get { return _max; }
			set
			{
				_max = value;

				if (_max > _min)
					Percent = (_current - _min) / (_max - _min);
				else
					Percent = 0;
			}
		}

		public float Value
		{
			get { return _current; }
			set
			{
				if (_max > _min)
					Percent = (value - _min) / (_max - _min);
				else
					Percent = 0f;
			}
		}

		public float Percent
		{
			get { return _percent; }
			set
			{
				_percent = MathHelper.Clamp(value, 0f, 1f);
				_current = _percent * (_max - _min) + _min;
			}
		}

		public bool EnableHighlight { get; set; }

		public Color BarColor { get; set; }

		public Color BarHighlight { get; set; }

		public Color SliderColor { get; set; }

		public Color SliderHighlight { get; set; }

		public Vector2 BarSize
		{
			get { return _barSize; }
			set
			{
				_barSize = value;
				UnpaddedSize = Vector2.Max(_barSize, _sliderSize);
			}
		}

		public float BarWidth
		{
			get { return _barSize.X; }
			set
			{
				_barSize.X = value;
				value = Math.Max(_barSize.X, _sliderSize.X);
/// <summary>Vector2 operation.</summary>
				UnpaddedSize = new Vector2(value, UnpaddedSize.Y);
			}
		}

		public float BarHeight
		{
			get { return _barSize.Y; }
			set
			{
				_barSize.Y = value;
				value = Math.Max(_barSize.Y, _sliderSize.Y);
/// <summary>Vector2 operation.</summary>
				UnpaddedSize = new Vector2(UnpaddedSize.X, value);
			}
		}

		public Vector2 SliderSize
		{
			get { return _sliderSize; }
			set
			{
				_sliderSize = value;
				UnpaddedSize = Vector2.Max(_barSize, _sliderSize);
			}
		}

		public float SliderWidth
		{
			get { return _sliderSize.X; }
			set
			{
				_sliderSize.X = value;
				value = Math.Max(_barSize.X, _sliderSize.X);
/// <summary>Vector2 operation.</summary>
				UnpaddedSize = new Vector2(value, UnpaddedSize.Y);
			}
		}

		public float SliderHeight
		{
			get { return _sliderSize.Y; }
			set
			{
				_sliderSize.Y = value;
				value = Math.Max(_barSize.Y, _sliderSize.Y);
/// <summary>Vector2 operation.</summary>
				UnpaddedSize = new Vector2(UnpaddedSize.X, value);
			}
		}

		public bool SliderVisible { get; set; }

		public bool Vertical { get; set; }

		public bool Reverse { get; set; }

		public IMouseInput MouseInput { get; }

		protected readonly TexturedBox slider, bar;

		protected Vector2 _barSize, _sliderSize;

		protected Vector2 startCursorOffset;

		protected Vector2 lastPos;

		protected float _min, _max, _current, _percent, lastValue;

		protected bool canMoveSlider;

/// <summary>SliderBar operation.</summary>
		public SliderBar(HudParentBase parent) : base(parent)
		{
/// <summary>TexturedBox operation.</summary>
			bar = new TexturedBox(this);
/// <summary>TexturedBox operation.</summary>
			slider = new TexturedBox(bar) { UseCursor = true, ShareCursor = true };
			MouseInput = this;

/// <summary>Vector2 operation.</summary>
			_barSize = new Vector2(100f, 12f);
/// <summary>Vector2 operation.</summary>
			_sliderSize = new Vector2(6f, 12f);
			UnpaddedSize = _barSize;
			SliderVisible = true;

			bar.Size = _barSize;
			slider.Size = _sliderSize;

/// <summary>Color operation.</summary>
			SliderColor = new Color(180, 180, 180, 255);
/// <summary>Color operation.</summary>
			BarColor = new Color(140, 140, 140, 255);
/// <summary>Color operation.</summary>
			SliderHighlight = new Color(200, 200, 200, 255);
			EnableHighlight = true;

			_min = 0f;
			_max = 1f;

			lastValue = float.PositiveInfinity;
			Value = 0f;
			Percent = 0f;

			ShareCursor = false;
			UseCursor = true;
			DimAlignment = DimAlignments.None;
		}

/// <summary>SliderBar operation.</summary>
		public SliderBar() : this(null)
		{ }

/// <summary>HandleInput operation.</summary>
		protected override void HandleInput(Vector2 cursorPos)
		{
			base.HandleInput(cursorPos);

			ShareCursor = Min == Max;

			if (!canMoveSlider && IsNewLeftClicked)
			{
				canMoveSlider = true;

				if (slider.IsMousedOver)
					startCursorOffset = cursorPos - slider.Position;
				else
					startCursorOffset = Vector2.Zero;
			}
/// <summary>if operation.</summary>
			else if (canMoveSlider && !SharedBinds.LeftButton.IsPressed)
				canMoveSlider = false;

			if (canMoveSlider && (cursorPos - lastPos).LengthSquared() > 4f)
			{
				float minOffset, maxOffset, pos;
				lastPos = cursorPos;
				cursorPos -= startCursorOffset;

				if (Vertical)
				{
					minOffset = -((_barSize.Y - _sliderSize.Y) * .5f);
					maxOffset = -minOffset;
					pos = MathHelper.Clamp(cursorPos.Y - Origin.Y, minOffset, maxOffset);
				}
				else
				{
					minOffset = -((_barSize.X - _sliderSize.X) * .5f);
					maxOffset = -minOffset;
					pos = MathHelper.Clamp(cursorPos.X - Origin.X, minOffset, maxOffset);
				}

				if (Reverse)
					Percent = 1f - ((pos - minOffset) / (maxOffset - minOffset));
				else
					Percent = (pos - minOffset) / (maxOffset - minOffset);
			}

			_current = (float)Math.Round(_current, 6);

			if (Math.Abs(_current - lastValue) > 1e-6f)
			{
				ValueChanged?.Invoke(FocusHandler?.InputOwner ?? this, EventArgs.Empty);
				lastValue = _current;
			}
		}

/// <summary>Layout operation.</summary>
		protected override void Layout()
		{
			slider.Visible = SliderVisible;

			if (EnableHighlight && (IsMousedOver || canMoveSlider))
			{
				slider.Color = SliderHighlight;

				if (BarHighlight != default(Color))
					bar.Color = BarHighlight;
			}
			else
			{
				slider.Color = SliderColor;
				bar.Color = BarColor;
			}

			Vector2 size = UnpaddedSize;

			if (_barSize.X >= _sliderSize.X)
			{
				_barSize.X = size.X;
				_sliderSize.X = Math.Min(_sliderSize.X, _barSize.X);
			}
			else
			{
				_sliderSize.X = size.X;
				_barSize.X = Math.Min(_sliderSize.X, _barSize.X);
			}

			if (_barSize.Y >= _sliderSize.Y)
			{
				_barSize.Y = size.Y;
				_sliderSize.Y = Math.Min(_sliderSize.Y, _barSize.Y);
			}
			else
			{
				_sliderSize.Y = size.Y;
				_barSize.Y = Math.Min(_sliderSize.Y, _barSize.Y);
			}

			bar.UnpaddedSize = _barSize;
			slider.UnpaddedSize = _sliderSize;

			UpdateButtonOffset();
		}

/// <summary>UpdateButtonOffset operation.</summary>
		private void UpdateButtonOffset()
		{
			if (Vertical)
			{
				if (Reverse)
/// <summary>Vector2 operation.</summary>
					slider.Offset = new Vector2(0f, -(Percent - .5f) * (_barSize.Y - _sliderSize.Y));
				else
/// <summary>Vector2 operation.</summary>
					slider.Offset = new Vector2(0f, (Percent - .5f) * (_barSize.Y - _sliderSize.Y));
			}
			else
			{
				if (Reverse)
/// <summary>Vector2 operation.</summary>
					slider.Offset = new Vector2(-(Percent - .5f) * (_barSize.X - _sliderSize.X), 0f);
				else
/// <summary>Vector2 operation.</summary>
					slider.Offset = new Vector2((Percent - .5f) * (_barSize.X - _sliderSize.X), 0f);
			}
		}
	}
}