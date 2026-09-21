using System;
using VRageMath;

namespace RichHudFramework.UI
{
	public class ColorPickerHSV : ColorPickerRGB, IValueControl<Vector3>
	{
/// <summary>Vector3 operation.</summary>
		protected static readonly Vector3 HSVScale = new Vector3(360f, 100f, 100f);
/// <summary>Vector3 operation.</summary>
		protected static readonly Vector3 RcpHSVScale = 1f / new Vector3(360f, 100f, 100f);

		public override Color Value
		{
			get { return _color; }
			set { ColorHSV = value.ColorToHSV() * HSVScale; }
		}

        Vector3 IValueControl<Vector3>.Value => ColorHSV;

        public Vector3 ColorHSV
		{
			get { return _hsvColor; }
			set
			{
				sliders[0].Value = value.X;
				sliders[1].Value = value.Y;
				sliders[2].Value = value.Z;
				_hsvColor = value;
			}
		}

		public Vector3 ColorHSVNorm
		{
			get { return _hsvColor * RcpHSVScale; }
			set { ColorHSV = value * HSVScale; }
		}

        protected Vector3 _hsvColor;

/// <summary>ColorPickerHSV operation.</summary>
		public ColorPickerHSV(HudParentBase parent = null) : base(parent)
		{
			sliders[0].Max = 360f;
			sliders[1].Max = 100f;
			sliders[2].Max = 100f;
		}

/// <summary>UpdateChannelR operation.</summary>
		protected override void UpdateChannelR(object sender, EventArgs args)
		{
			var slider = sender as SliderBox;
			_hsvColor.X = (float)Math.Round(slider.Value);
			sliderText[0].TextBoard.SetText($"H: {_hsvColor.X}");

			_color = (_hsvColor * RcpHSVScale).HSVtoColor();
			display.Color = _color;
		}

/// <summary>UpdateChannelG operation.</summary>
		protected override void UpdateChannelG(object sender, EventArgs args)
		{
			var slider = sender as SliderBox;
			_hsvColor.Y = (float)Math.Round(slider.Value);
			sliderText[1].TextBoard.SetText($"S: {_hsvColor.Y}");

			_color = (_hsvColor * RcpHSVScale).HSVtoColor();
			display.Color = _color;
		}

/// <summary>UpdateChannelB operation.</summary>
		protected override void UpdateChannelB(object sender, EventArgs args)
		{
			var slider = sender as SliderBox;
			_hsvColor.Z = (float)Math.Round(slider.Value);
			sliderText[2].TextBoard.SetText($"V: {_hsvColor.Z}");

			_color = (_hsvColor * RcpHSVScale).HSVtoColor();
			display.Color = _color;
		}
	}
}