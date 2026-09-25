namespace RichHudFramework.UI.Client
{
	public enum SliderSettingsAccessors : int
	{
		Min = 16,

		Max = 17,

		Percent = 18,

		ValueText = 19,
	}

	public class TerminalSlider : TerminalValue<float>
	{
		public float Min
		{

			get { return (float)GetOrSetMember(null, (int)SliderSettingsAccessors.Min); }

			set { GetOrSetMember(value, (int)SliderSettingsAccessors.Min); }
		}

		public float Max
		{

			get { return (float)GetOrSetMember(null, (int)SliderSettingsAccessors.Max); }

			set { GetOrSetMember(value, (int)SliderSettingsAccessors.Max); }
		}

		public float Percent
		{

			get { return (float)GetOrSetMember(null, (int)SliderSettingsAccessors.Percent); }

			set { GetOrSetMember(value, (int)SliderSettingsAccessors.Percent); }
		}

		public string ValueText
		{

			get { return GetOrSetMember(null, (int)SliderSettingsAccessors.ValueText) as string; }

			set { GetOrSetMember(value, (int)SliderSettingsAccessors.ValueText); }
		}


		public TerminalSlider() : base(MenuControls.SliderSetting)
		{ }
	}
}