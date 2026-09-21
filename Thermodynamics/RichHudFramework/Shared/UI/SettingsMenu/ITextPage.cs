using RichHudFramework.UI.Rendering;

namespace RichHudFramework
{
	namespace UI
	{
		public enum TextPageAccessors : int
		{
			GetOrSetHeader = 10,

			GetOrSetSubheader = 11,

			GetOrSetText = 12,

			GetTextBuilder = 13,
		}

		public interface ITextPage : ITerminalPage
		{
			RichText HeaderText { get; set; }

			RichText SubHeaderText { get; set; }

			RichText Text { get; set; }

			ITextBuilder TextBuilder { get; }
		}
	}
}