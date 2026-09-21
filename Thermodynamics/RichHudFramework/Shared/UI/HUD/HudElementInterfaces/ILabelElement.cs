namespace RichHudFramework.UI
{
    using Rendering;

    public interface IMinLabelElement
    {
		ITextBoard TextBoard { get; }
    }

	public interface ILabelElement : IMinLabelElement
    {
		RichText Text { get; set; }

		GlyphFormat Format { get; set; }

		TextBuilderModes BuilderMode { get; set; }

		bool AutoResize { get; set; }

		bool VertCenterText { get; set; }
    }
}
