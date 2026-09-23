namespace RichHudFramework
{
	namespace UI.Client
	{
		public sealed partial class BindManager
		{
			private class Control : IControl
			{
				public string Name => _instance.GetControlMember(Index, (int)ControlAccessors.Name) as string;

				public string DisplayName => _instance.GetControlMember(Index, (int)ControlAccessors.DisplayName) as string;

				public int Index { get; }

				public bool IsPressed => (bool)(_instance.GetControlMember(Index, (int)ControlAccessors.IsPressed) ?? false);

				public bool IsNewPressed => (bool)(_instance.GetControlMember(Index, (int)ControlAccessors.IsNewPressed) ?? false);

				public bool IsReleased => (bool)(_instance.GetControlMember(Index, (int)ControlAccessors.IsReleased) ?? false);

				public bool Analog => (bool)(_instance.GetControlMember(Index, (int)ControlAccessors.Analog) ?? false);

				public float AnalogValue => (float)(_instance.GetControlMember(Index, (int)ControlAccessors.AnalogValue) ?? 0f);


				public Control(int index)
				{
					this.Index = index;
				}


				public override bool Equals(object obj)
				{
					return (obj as Control).Index == Index;
				}


				public override int GetHashCode()
				{
					return Index.GetHashCode();
				}
			}
		}
	}
}