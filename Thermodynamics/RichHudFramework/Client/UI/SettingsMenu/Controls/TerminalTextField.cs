using System;

namespace RichHudFramework.UI.Client
{
	public enum TextFieldAccessors : int
	{
		CharFilterFunc = 16,
	}

	public class TerminalTextField : TerminalValue<string>
	{
		public Func<char, bool> CharFilterFunc
		{
/// <summary>Returns the orsetmember.</summary>
			get { return GetOrSetMember(null, (int)TextFieldAccessors.CharFilterFunc) as Func<char, bool>; }
/// <summary>Returns the orsetmember.</summary>
			set { GetOrSetMember(value, (int)TextFieldAccessors.CharFilterFunc); }
		}

/// <summary>TerminalTextField operation.</summary>
		public TerminalTextField() : base(MenuControls.TextField)
		{ }
	}
}