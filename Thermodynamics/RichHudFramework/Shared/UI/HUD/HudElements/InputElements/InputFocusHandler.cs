using System;

namespace RichHudFramework
{
	namespace UI
	{
		using Client;
		using Server;

		public class InputFocusHandler : IFocusHandler
		{
			public IFocusableElement InputOwner { get; set; }

			public bool HasFocus { get; private set; }

			public event EventHandler GainedInputFocus;

			public event EventHandler LostInputFocus;

			public EventHandler GainedInputFocusCallback { set { GainedInputFocus += value; } }

			public EventHandler LostInputFocusCallback { set { LostInputFocus += value; } }


			public InputFocusHandler(IFocusableElement inputOwner)
			{
				InputOwner = inputOwner;
			}


			public virtual void GetInputFocus()
			{
				if (!HasFocus)
				{
					HudMain.GetInputFocus(this);
					HasFocus = true;
					GainedInputFocus?.Invoke(InputOwner, EventArgs.Empty);
				}
			}


			public virtual void ReleaseFocus()
			{
				if (HasFocus)
				{
					HasFocus = false;
					LostInputFocus?.Invoke(InputOwner, EventArgs.Empty);
				}
			}
		}
	}
}