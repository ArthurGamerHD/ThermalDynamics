using RichHudFramework.UI.Rendering;

namespace RichHudFramework
{
	namespace UI
	{
		public abstract class LabelElementBase : HudElementBase, IMinLabelElement
		{
			public abstract ITextBoard TextBoard { get; }

/// <summary>LabelElementBase operation.</summary>
			public LabelElementBase(HudParentBase parent = null) : base(parent)
			{ }
		}
	}
}