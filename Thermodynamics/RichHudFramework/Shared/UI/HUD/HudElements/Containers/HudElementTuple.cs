namespace RichHudFramework.UI
{
	public class HudElementTuple<TElement, TData> : HudElementContainer<TElement> where TElement : HudElementBase
	{
		public virtual TData AssocData { get; set; }

/// <summary>HudElementTuple operation.</summary>
		public HudElementTuple()
		{ }
	}

	public class HudElementTuple<TData> : HudElementTuple<HudElementBase, TData>
	{ }
}
