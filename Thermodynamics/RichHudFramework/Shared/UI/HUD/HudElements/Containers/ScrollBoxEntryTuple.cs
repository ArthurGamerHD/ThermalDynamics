namespace RichHudFramework.UI
{
	public class ScrollBoxEntryTuple<TElement, TData>
		: ScrollBoxEntry<TElement>, IScrollBoxEntryTuple<TElement, TData>
		where TElement : HudElementBase
	{
		public virtual TData AssocMember { get; set; }


		public ScrollBoxEntryTuple()
		{ }
	}

	public class ScrollBoxEntryTuple<TData> : ScrollBoxEntryTuple<HudElementBase, TData>
	{ }
}