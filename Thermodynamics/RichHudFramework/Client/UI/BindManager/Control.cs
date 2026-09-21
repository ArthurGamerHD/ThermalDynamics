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

/// <summary>Control operation.</summary>
				public Control(int index)
				{
					this.Index = index;
				}

/// <summary>Equals operation.</summary>
				public override bool Equals(object obj)
				{
					return (obj as Control).Index == Index;
				}

/// <summary>Returns the hashcode.</summary>
				public override int GetHashCode()
				{
					return Index.GetHashCode();
				}
			}
		}
	}
}