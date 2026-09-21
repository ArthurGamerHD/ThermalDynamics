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
/// <summary>return operation.</summary>
			get { return (float)GetOrSetMember(null, (int)SliderSettingsAccessors.Min); }
/// <summary>Returns the orsetmember.</summary>
			set { GetOrSetMember(value, (int)SliderSettingsAccessors.Min); }
		}

		public float Max
		{
/// <summary>return operation.</summary>
			get { return (float)GetOrSetMember(null, (int)SliderSettingsAccessors.Max); }
/// <summary>Returns the orsetmember.</summary>
			set { GetOrSetMember(value, (int)SliderSettingsAccessors.Max); }
		}

		public float Percent
		{
/// <summary>return operation.</summary>
			get { return (float)GetOrSetMember(null, (int)SliderSettingsAccessors.Percent); }
/// <summary>Returns the orsetmember.</summary>
			set { GetOrSetMember(value, (int)SliderSettingsAccessors.Percent); }
		}

		public string ValueText
		{
/// <summary>Returns the orsetmember.</summary>
			get { return GetOrSetMember(null, (int)SliderSettingsAccessors.ValueText) as string; }
/// <summary>Returns the orsetmember.</summary>
			set { GetOrSetMember(value, (int)SliderSettingsAccessors.ValueText); }
		}

/// <summary>TerminalSlider operation.</summary>
		public TerminalSlider() : base(MenuControls.SliderSetting)
		{ }
	}
}