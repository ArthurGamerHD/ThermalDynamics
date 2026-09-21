namespace RichHudFramework
{
	namespace UI
	{
		public interface IFocusHandler
		{
			IFocusableElement InputOwner { get; set; }

			event EventHandler GainedInputFocus;

			event EventHandler LostInputFocus;

			EventHandler GainedInputFocusCallback { set; }

			EventHandler LostInputFocusCallback { set; }

			bool HasFocus { get; }

/// <summary>Returns the inputfocus.</summary>
			void GetInputFocus();

/// <summary>ReleaseFocus operation.</summary>
			void ReleaseFocus();
		}

		public interface IFocusableElement
		{
			IFocusHandler FocusHandler { get; }
		}
	}
}