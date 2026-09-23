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

			get { return GetOrSetMember(null, (int)TextFieldAccessors.CharFilterFunc) as Func<char, bool>; }

			set { GetOrSetMember(value, (int)TextFieldAccessors.CharFilterFunc); }
		}


		public TerminalTextField() : base(MenuControls.TextField)
		{ }
	}
}